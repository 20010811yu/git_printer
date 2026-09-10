using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Common.VmBridge;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// 真实视觉检测服务（VisionMaster 桥接进程版）：
    /// 主程序（net10.0）无法直接引用 VM SDK（.NET Framework 程序集），由本服务管理
    /// VmVisionBridge 子进程（net48，承载 VmSolution SDK），经命名管道按 VmBridgeProtocol 收发：
    /// 懒启动桥接进程 → Load 方案 → Run 检测（PNG 结果图）→ Close 释放。
    /// SDK 原始对象全部封装在桥接进程内，本服务只见状态与 PNG 字节（符合"SDK 不外泄"规则）。
    /// 桥接进程异常退出或 Load 失败（含加密狗授权失败，ERR-027）→ 清理桥接进程，下次调用自动以全新进程重试；
    /// 全部失败走 Result.Fail 不抛 UI 异常。
    /// </summary>
    public class VisionMasterBridgeInspectionService : IImageInspectionService
    {
        // ══════════════ 常量 ══════════════

        /// <summary>命名管道名（与桥接进程 Program.PipeName 一致）</summary>
        private const string PipeName = "UiTopMachine.VmBridge";

        /// <summary>等待桥接进程就绪（管道可连）超时</summary>
        private const int BridgeStartTimeoutMs = 20000;

        /// <summary>方案加载命令超时（VM 方案加载较慢）</summary>
        private const int LoadTimeoutMs = 120000;

        /// <summary>检测运行命令超时</summary>
        private const int RunTimeoutMs = 30000;

        // ══════════════ 依赖 ══════════════

        private readonly string _bridgeExePath;
        private readonly string _solutionPath;
        private readonly string _procedureName;
        private readonly ILogService _logService;
        private readonly object _lock = new object();

        // ══════════════ 状态字段 ══════════════

        private Process? _bridgeProcess;
        private NamedPipeClientStream? _pipe;
        private bool _solutionLoaded;
        private int _sequence;
        private bool _disposed;
        private bool _bridgeUnavailableLogged;

        /// <inheritdoc />
        public event EventHandler? SolutionLoaded;

        /// <inheritdoc />
        public bool IsSolutionLoaded => Volatile.Read(ref _solutionLoaded);

        /// <inheritdoc />
        public string ProcedureName => _procedureName;

        // ══════════════ 构造 ══════════════

        /// <summary>
        /// 构造：注入桥接 exe 路径、方案路径、流程名与日志服务（DI 单例，Program.cs 显式传参）
        /// </summary>
        public VisionMasterBridgeInspectionService(
            string bridgeExePath,
            string solutionPath,
            string procedureName,
            ILogService logService)
        {
            _bridgeExePath = bridgeExePath ?? throw new ArgumentNullException(nameof(bridgeExePath));
            _solutionPath = solutionPath ?? throw new ArgumentNullException(nameof(solutionPath));
            _procedureName = procedureName ?? throw new ArgumentNullException(nameof(procedureName));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
        }

        // ══════════════ 接口实现 ══════════════

        /// <inheritdoc />
        public Task<Result<bool>> LoadSolutionAsync(string? solutionPath)
        {
            var path = string.IsNullOrWhiteSpace(solutionPath) ? _solutionPath : solutionPath;
            return Task.Run(() => LoadCore(path));
        }

        /// <inheritdoc />
        public Task<Result<ImageInspectionResult>> RunInspectionAsync()
        {
            return Task.Run(() => RunCore());
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            lock (_lock)
            {
                _disposed = true;
                CleanupBridgeNoLock(sendClose: true);
            }
        }

        // ══════════════ 业务方法（线程池执行） ══════════════

        /// <summary>
        /// 加载方案核心：确保桥接就绪 → Load → 置位并触发 SolutionLoaded 事件（后台线程）
        /// </summary>
        private Result<bool> LoadCore(string path)
        {
            if (Volatile.Read(ref _solutionLoaded))
            {
                return Result<bool>.OK(true); // 幂等：方案已加载
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return Result<bool>.Fail("方案路径为空，无法加载");
            }

            lock (_lock)
            {
                if (_disposed)
                {
                    return Result<bool>.Fail("视觉服务已停止");
                }

                if (_solutionLoaded)
                {
                    return Result<bool>.OK(true);
                }

                if (!File.Exists(path))
                {
                    return Result<bool>.Fail($"方案文件不存在：{path}");
                }

                try
                {
                    EnsureBridgeReadyNoLock();
                    var response = SendCommand(VmBridgeProtocol.Command.Load,
                        VmBridgeProtocol.EncodeRequestText(path), LoadTimeoutMs);
                    if (!response.Ok)
                    {
                        // Load 业务失败（含加密狗授权失败 ERR-027）：SDK 授权初始化每进程仅一次，
                        // 失败进程内重试无法自愈 → 立即清理桥接进程，下次调用以全新进程重试
                        CleanupBridgeNoLock(sendClose: false);
                        _logService.Warn("视觉方案加载失败，桥接进程已重启以便重试");
                        return Result<bool>.Fail(DescribeLoadError(response.Error));
                    }

                    _solutionLoaded = true;
                    SolutionLoaded?.Invoke(this, EventArgs.Empty);
                    _logService.Info($"视觉方案加载成功（{Path.GetFileName(path)}）");
                    return Result<bool>.OK(true);
                }
                catch (Exception ex)
                {
                    // 桥接进程/管道异常 → 清理现场，下次调用自动重试
                    CleanupBridgeNoLock(sendClose: false);
                    return Result<bool>.Fail($"视觉方案加载失败：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 检测核心：确保方案已加载 → Run → PNG 组装 Bitmap 返回
        /// </summary>
        private Result<ImageInspectionResult> RunCore()
        {
            if (!Volatile.Read(ref _solutionLoaded))
            {
                return Result<ImageInspectionResult>.Fail("检测方案未加载，请先加载方案");
            }

            lock (_lock)
            {
                if (_disposed || !_solutionLoaded)
                {
                    return Result<ImageInspectionResult>.Fail("检测方案未加载，请先加载方案");
                }

                try
                {
                    var response = SendCommand(VmBridgeProtocol.Command.Run,
                        VmBridgeProtocol.EncodeRequestText(_procedureName), RunTimeoutMs);
                    if (!response.Ok)
                    {
                        return Result<ImageInspectionResult>.Fail(response.Error ?? "视觉检测失败（桥接无响应）");
                    }

                    if (response.PngBytes == null || response.PngBytes.Length == 0)
                    {
                        if (response.Ok)
                        {
                            // 流程执行成功但本运行无新帧（低速图像源常态，ERR-028）：
                            // 返回无图结果，上层按跳过处理（保留上一张图、不计数、不报错）
                            return Result<ImageInspectionResult>.OK(new ImageInspectionResult
                            {
                                Image = null,
                                IsOk = response.IsOk,
                                Sequence = Interlocked.Increment(ref _sequence),
                                Elapsed = TimeSpan.FromMilliseconds(response.ElapsedMs)
                            });
                        }

                        return Result<ImageInspectionResult>.Fail(response.Error ?? "视觉检测无结果图");
                    }

                    Bitmap image;
                    using (var ms = new MemoryStream(response.PngBytes))
                    {
                        image = new Bitmap(ms);
                    }

                    var result = new ImageInspectionResult
                    {
                        Image = image,
                        IsOk = response.IsOk,
                        Sequence = Interlocked.Increment(ref _sequence),
                        Elapsed = TimeSpan.FromMilliseconds(response.ElapsedMs)
                    };
                    return Result<ImageInspectionResult>.OK(result);
                }
                catch (Exception ex)
                {
                    // 桥接异常 → 清理现场并复位加载态（下次自动重载）
                    CleanupBridgeNoLock(sendClose: false);
                    _solutionLoaded = false;
                    return Result<ImageInspectionResult>.Fail($"视觉检测失败：{ex.Message}");
                }
            }
        }

        // ══════════════ 桥接进程/管道管理（内部 _lock 已持有） ══════════════

        /// <summary>
        /// Load 失败文案：加密狗授权类错误附加用户指引，其余原样透传
        /// </summary>
        private static string DescribeLoadError(string? error)
        {
            var message = string.IsNullOrWhiteSpace(error) ? "方案加载失败（桥接无响应）" : error;
            return VmBridgeProtocol.IsDongleLicenseError(message)
                ? $"{message}——{VmBridgeProtocol.DongleLicenseHint}"
                : message;
        }


        /// <summary>
        /// 确保桥接进程与管道就绪（懒启动；进程死了自动重启）
        /// </summary>
        private void EnsureBridgeReadyNoLock()
        {
            if (_pipe != null && _bridgeProcess != null && !_bridgeProcess.HasExited)
            {
                return;
            }

            if (_pipe != null || _bridgeProcess != null)
            {
                CleanupBridgeNoLock(sendClose: false);
            }

            if (!File.Exists(_bridgeExePath))
            {
                if (!_bridgeUnavailableLogged)
                {
                    _bridgeUnavailableLogged = true;
                    _logService.Error($"视觉桥接进程不存在：{_bridgeExePath}（请先构建 tools/VmVisionBridge）");
                }

                throw new InvalidOperationException($"桥接进程不存在：{_bridgeExePath}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = _bridgeExePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            _bridgeProcess = Process.Start(psi) ?? throw new InvalidOperationException("视觉桥接进程启动失败");
            _logService.Info("视觉桥接进程已启动");

            _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            // 阻塞等待连接（当前在线程池线程，非 UI 线程；带超时防死等）
            var connectTask = _pipe.ConnectAsync(BridgeStartTimeoutMs);
            if (!connectTask.Wait(BridgeStartTimeoutMs + 1000))
            {
                throw new TimeoutException($"等待视觉桥接进程就绪超时（{BridgeStartTimeoutMs / 1000}s）");
            }
        }

        /// <summary>
        /// 发送命令并阻塞等待响应（调用方已在线程池线程）
        /// </summary>
        private VmBridgeProtocol.VmBridgeResponse SendCommand(byte command, byte[] payload, int timeoutMs)
        {
            var pipe = _pipe ?? throw new InvalidOperationException("管道未就绪");
            var frame = VmBridgeProtocol.EncodeFrame(command, payload);
            pipe.Write(frame, 0, frame.Length);
            pipe.Flush();

            // 读帧头（5 字节：命令 1B + 长度 4B）
            var header = new byte[VmBridgeProtocol.HeaderLength];
            ReadExactWithTimeout(pipe, header, timeoutMs);

            int length = VmBridgeProtocol.TryGetPayloadLength(header, header.Length);
            var payloadBuffer = new byte[length];
            if (length > 0)
            {
                ReadExactWithTimeout(pipe, payloadBuffer, timeoutMs);
            }

            return VmBridgeProtocol.ParseResponse(payloadBuffer);
        }

        /// <summary>
        /// 带超时精确读取填满缓冲（不足/超时/断开即抛异常）
        /// </summary>
        private void ReadExactWithTimeout(NamedPipeClientStream pipe, byte[] buffer, int timeoutMs)
        {
            int offset = 0;
            var deadline = Environment.TickCount + timeoutMs;
            while (offset < buffer.Length)
            {
                int remaining = deadline - Environment.TickCount;
                if (remaining <= 0)
                {
                    throw new TimeoutException($"桥接响应超时（已读 {offset}/{buffer.Length} 字节）");
                }

                var readTask = pipe.ReadAsync(buffer, offset, buffer.Length - offset);
                if (!readTask.Wait(remaining))
                {
                    throw new TimeoutException($"桥接响应超时（读 payload {offset}/{buffer.Length} 字节处）");
                }

                int n = readTask.Result;
                if (n <= 0)
                {
                    throw new IOException("桥接进程连接已断开");
                }

                offset += n;
            }
        }

        /// <summary>
        /// 清理桥接进程与管道；sendClose=true 时尽力发 Close 命令（优雅关闭方案）
        /// </summary>
        private void CleanupBridgeNoLock(bool sendClose)
        {
            if (sendClose && _pipe != null && _pipe.IsConnected)
            {
                try
                {
                    var frame = VmBridgeProtocol.EncodeFrame(VmBridgeProtocol.Command.Close, new byte[0]);
                    _pipe.Write(frame, 0, frame.Length);
                    _pipe.Flush();
                }
                catch
                {
                    // 优雅关闭尽力而为
                }
            }

            _pipe?.Dispose();
            _pipe = null;

            if (_bridgeProcess != null)
            {
                try
                {
                    if (!_bridgeProcess.HasExited)
                    {
                        // Close 已发过；给 2 秒优雅退出窗口，超时强杀兜底
                        if (!_bridgeProcess.WaitForExit(2000))
                        {
                            _bridgeProcess.Kill();
                        }
                    }
                }
                catch
                {
                    // 进程可能已退出
                }

                _bridgeProcess.Dispose();
                _bridgeProcess = null;
            }
        }
    }
}