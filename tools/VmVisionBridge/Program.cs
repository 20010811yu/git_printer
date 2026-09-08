using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Common.VmBridge;
using VM.Core;
using VM.PlatformSDKCS;

namespace VmVisionBridge
{
    /// <summary>
    /// 海康 VisionMaster 桥接进程（.NET Framework 4.8，x64）：
    /// 主程序（net10.0）无法直接引用 VM SDK（.NET Framework 程序集），由本进程承载 SDK——
    /// 监听命名管道 UiTopMachine.VmBridge，收二进制帧命令（协议见 VmBridgeProtocol），
    /// 调用 VmSolution/VmProcedure 完成加载、运行、取图（PNG），结果回帧。
    ///
    /// 用法：
    ///   VmVisionBridge                     —— 服务模式（等待主程序连接）
    ///   VmVisionBridge --probe "sol路径"    —— 探测模式：加载方案打印全部流程名后退出
    ///
    /// SDK 原始对象（VmSolution/VmProcedure/ImageBaseData）全部封装在本进程内，不跨进程外泄。
    /// </summary>
    internal static class Program
    {
        // ══════════════ 常量 ══════════════

        /// <summary>命名管道名（与主程序 VisionMasterBridgeInspectionService 一致）</summary>
        private const string PipeName = "UiTopMachine.VmBridge";

        /// <summary>命令处理超时（毫秒）：方案加载可能较慢，给足时间</summary>
        private const int LoadTimeoutMs = 120_000;

        /// <summary>检测运行超时（毫秒）</summary>
        private const int RunTimeoutMs = 30_000;

        // ══════════════ 状态字段 ══════════════

        private static bool _solutionLoaded;
        private static readonly object SolutionLock = new object();
        private static readonly string DiagLogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", "VmBridge-diag.log");

        /// <summary>
        /// 诊断日志追加落盘（取图回退/输出清单等关键排障信息；stderr 无重定向会丢失）
        /// </summary>
        private static void DiagLog(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(DiagLogPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(DiagLogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch
            {
                // 诊断日志尽力而为
            }
        }

        // ══════════════ 入口 ══════════════

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            try
            {
                // 探测模式：--probe "方案路径" → 加载 → 打印流程名 → 退出
                if (args.Length >= 2 && args[0] == "--probe")
                {
                    return RunProbe(args[1]);
                }

                // 调试模式：--grab "sol路径" [次数] —— 加载后连续运行，PNG 落盘检查像素内容
                if (args.Length >= 2 && args[0] == "--grab")
                {
                    return RunGrab(args[1], args.Length >= 3 ? int.Parse(args[2]) : 5);
                }

                return RunServer();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[VmBridge] 致命异常：" + ex.Message);
                return -1;
            }
        }

        // ══════════════ 探测模式 ══════════════

        /// <summary>
        /// 探测模式：加载方案，枚举打印全部流程名（首次接入时确定流程名用）
        /// </summary>
        private static int RunProbe(string solutionPath)
        {
            if (!File.Exists(solutionPath))
            {
                Console.WriteLine("PROBE_FAIL 方案文件不存在：" + solutionPath);
                return 2;
            }

            Console.WriteLine("[VmBridge] 探测模式，加载方案：" + solutionPath);
            VmSolution.Load(solutionPath, string.Empty, false);
            _solutionLoaded = true;

            var procedures = EnumerateProcedureNames();
            Console.WriteLine("PROBE_OK procedures=" + string.Join(",", procedures));
            foreach (var name in procedures)
            {
                Console.WriteLine("  流程：" + name);
            }

            ShutdownSolution();
            return 0;
        }

        // ══════════════ 服务模式 ══════════════

        /// <summary>
        /// 调试模式：加载方案后连续运行 N 次，把取到的图存 PNG（含全黑检测），用于离线排查出图链路
        /// </summary>
        private static int RunGrab(string solutionPath, int count)
        {
            if (!File.Exists(solutionPath))
            {
                Console.WriteLine("GRAB_FAIL 方案文件不存在：" + solutionPath);
                return 2;
            }

            VmSolution.Load(solutionPath, string.Empty, false);
            _solutionLoaded = true;
            var procedure = VmSolution.Instance["流程1"] as VmProcedure;
            if (procedure == null)
            {
                Console.WriteLine("GRAB_FAIL 流程1 不存在");
                return 2;
            }

            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "grab");
            Directory.CreateDirectory(dir);

            for (int i = 1; i <= count; i++)
            {
                var sw = Stopwatch.StartNew();
                procedure.SyncRun();
                sw.Stop();

                var image = TryGetOutputImage(procedure, "ImageData");
                if (image == null)
                {
                    Console.WriteLine($"GRAB[{i}] 无输出图 ErrorCode={procedure.ModuResult.ErrorCode} {sw.ElapsedMilliseconds}ms");
                    continue;
                }

                using var bitmap = image.ToBitmap();
                var path = Path.Combine(dir, $"grab_{i}.png");
                bitmap.Save(path, ImageFormat.Png);

                // 采样像素：全黑判定（中心 3×3 区域平均亮度）
                double lum = SampleLuminance(bitmap);
                Console.WriteLine($"GRAB[{i}] {bitmap.Width}x{bitmap.Height} 亮度均值={lum:F1} {sw.ElapsedMilliseconds}ms → {path}");
            }

            ShutdownSolution();
            return 0;
        }

        /// <summary>采样位图中心区域平均亮度（0~255；≈0 即全黑图）</summary>
        private static double SampleLuminance(Bitmap bitmap)
        {
            using var bmp = new Bitmap(bitmap);
            int cx = bitmap.Width / 2, cy = bitmap.Height / 2;
            double sum = 0;
            int n = 0;
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int x = Math.Min(Math.Max(cx + dx, 0), bitmap.Width - 1);
                    int y = Math.Min(Math.Max(cy + dy, 0), bitmap.Height - 1);
                    var p = bmp.GetPixel(x, y);
                    sum += (p.R + p.G + p.B) / 3.0;
                    n++;
                }
            }

            return n > 0 ? sum / n : 0;
        }

        /// <summary>
        /// 服务模式：循环接受管道连接，逐命令处理直到 Close
        /// </summary>
        private static int RunServer()
        {
            Console.WriteLine("[VmBridge] 服务启动，管道：" + PipeName);
            while (true)
            {
                using (var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    // 等待主程序连接（无超时，进程由主程序管理生命周期）
                    server.WaitForConnection();

                    Console.WriteLine("[VmBridge] 客户端已连接");
                    bool clientClosed = false;
                    while (!clientClosed)
                    {
                        var request = ReadRequest(server);
                        if (request == null)
                        {
                            // 客户端断开
                            clientClosed = true;
                            break;
                        }

                        byte command = request.Value.Key;
                        byte[] payload = request.Value.Value;

                        // 处理命令（内部全捕获，失败以 ERR 响应回帧，不断连接）
                        var responsePayload = HandleCommand(command, payload);
                        WriteFrame(server, command, responsePayload);

                        if (command == VmBridgeProtocol.Command.Close)
                        {
                            Console.WriteLine("[VmBridge] 收到 Close，正常退出");
                            return 0;
                        }
                    }

                    Console.WriteLine("[VmBridge] 客户端断开，继续等待新连接");
                }
            }
        }

        // ══════════════ 命令处理 ══════════════

        /// <summary>
        /// 处理单条命令，返回响应 payload（永不抛异常——失败封装为 ERR 响应）
        /// </summary>
        private static byte[] HandleCommand(byte command, byte[] payload)
        {
            try
            {
                switch (command)
                {
                    case VmBridgeProtocol.Command.Ping:
                        return VmBridgeProtocol.BuildResponse(true, null, null, false, 0, 0, 0, null);

                    case VmBridgeProtocol.Command.Load:
                    {
                        var path = Encoding.UTF8.GetString(payload ?? new byte[0]);
                        return HandleLoad(path);
                    }

                    case VmBridgeProtocol.Command.ListProcedures:
                    {
                        EnsureLoaded();
                        return VmBridgeProtocol.BuildResponse(true, null, EnumerateProcedureNames(), false, 0, 0, 0, null);
                    }

                    case VmBridgeProtocol.Command.Run:
                    {
                        var procedureName = Encoding.UTF8.GetString(payload ?? new byte[0]);
                        return HandleRun(procedureName);
                    }

                    case VmBridgeProtocol.Command.Close:
                        ShutdownSolution();
                        return VmBridgeProtocol.BuildResponse(true, null, null, false, 0, 0, 0, null);

                    default:
                        return VmBridgeProtocol.BuildResponse(false, "未知命令：" + command, null, false, 0, 0, 0, null);
                }
            }
            catch (VmException vex)
            {
                var message = $"VmException 0x{vex.errorCode:X}: {vex.errorMessage}";
                Console.Error.WriteLine("[VmBridge] " + message);
                return VmBridgeProtocol.BuildResponse(false, message, null, false, 0, 0, 0, null);
            }
            catch (Exception ex)
            {
                var message = ex.GetType().Name + ": " + ex.Message;
                Console.Error.WriteLine("[VmBridge] " + message);
                return VmBridgeProtocol.BuildResponse(false, message, null, false, 0, 0, 0, null);
            }
        }

        /// <summary>
        /// 加载方案（幂等；已加载直接成功）
        /// </summary>
        private static byte[] HandleLoad(string solutionPath)
        {
            lock (SolutionLock)
            {
                if (_solutionLoaded)
                {
                    return VmBridgeProtocol.BuildResponse(true, null, null, false, 0, 0, 0, null);
                }

                if (string.IsNullOrWhiteSpace(solutionPath) || !File.Exists(solutionPath))
                {
                    return VmBridgeProtocol.BuildResponse(false, "方案文件不存在：" + solutionPath, null, false, 0, 0, 0, null);
                }

                Console.WriteLine("[VmBridge] VmSolution.Load：" + solutionPath);
                var sw = Stopwatch.StartNew();
                VmSolution.Load(solutionPath, string.Empty, false);
                _solutionLoaded = true;
                sw.Stop();
                Console.WriteLine($"[VmBridge] 加载成功，耗时 {sw.ElapsedMilliseconds} ms");

                // 无新帧时保留模块上次结果（ERR-028：图像源帧率低于轮询频率时输出图会被清空）；
                // 对流程内全部模块启用，输出图稳定可取
                try
                {
                    foreach (var module in VmSolution.Instance.Modules)
                    {
                        if (module is VmProcedure proc)
                        {
                            proc.KeepModuleLastResult(true);
                            foreach (var mod in proc.Modules)
                            {
                                mod.KeepModuleLastResult(true);
                            }
                        }
                    }

                    Console.WriteLine("[VmBridge] KeepModuleLastResult 已启用");
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[VmBridge] KeepModuleLastResult 设置失败：" + ex.Message);
                }
                return VmBridgeProtocol.BuildResponse(true, null, null, false, 0, 0, 0, null);
            }
        }

        /// <summary>
        /// 运行一次检测：指定流程 SyncRun → 取输出图 → PNG 回帧
        /// </summary>
        private static byte[] HandleRun(string procedureName)
        {
            lock (SolutionLock)
            {
                EnsureLoaded();

                var procedure = VmSolution.Instance[procedureName] as VmProcedure;
                if (procedure == null)
                {
                    var available = string.Join(",", EnumerateProcedureNames());
                    return VmBridgeProtocol.BuildResponse(
                        false, $"方案内不存在流程「{procedureName}」；可用流程：{available}", null, false, 0, 0, 0, null);
                }

                var sw = Stopwatch.StartNew();
                procedure.SyncRun(); // 同步执行：阻塞至流程完成（ERR-028 根因——Run() 为异步触发立即返回，结果未出即取图为空；与参考程序 SyncRun 一致）
                sw.Stop();

                // 取流程输出图：优先官方约定输出键 "ImageData"，为空时枚举其余图像输出键回退；
                // 全部为空且流程成功（ErrorCode=0）视为「本运行无新帧」——低速图像源下属常态，
                // 返回成功无图（主程序按跳过处理），不算错误
                var imageData = TryGetOutputImage(procedure, "ImageData");

                if (imageData == null)
                {
                    var isOkNoFrame = procedure.ModuResult.ErrorCode == 0;
                    DiagLog($"Run 无输出图，ErrorCode={procedure.ModuResult.ErrorCode}，按{(isOkNoFrame ? "无新帧跳过" : "执行失败")}返回");
                    return VmBridgeProtocol.BuildResponse(isOkNoFrame,
                        isOkNoFrame ? null : $"流程执行失败，ErrorCode={procedure.ModuResult.ErrorCode}（无输出图）",
                        null, isOkNoFrame, sw.Elapsed.TotalMilliseconds, 0, 0, null);
                }

                using (var bitmap = imageData.ToBitmap())
                using (var pngStream = new MemoryStream())
                {
                    bitmap.Save(pngStream, ImageFormat.Png);
                    var isOk = procedure.ModuResult.ErrorCode == 0;
                    Console.WriteLine($"[VmBridge] Run {procedureName} → {bitmap.Width}x{bitmap.Height} " +
                                      $"{sw.Elapsed.TotalMilliseconds:F0} ms isOk={isOk}");
                    return VmBridgeProtocol.BuildResponse(true, null, null, isOk,
                        sw.Elapsed.TotalMilliseconds, bitmap.Width, bitmap.Height, pngStream.ToArray());
                }
            }
        }

        /// <summary>
        /// 按输出键取流程结果图：指定键为空时枚举全部输出回退尝试其余图像输出键，
        /// 并打印流程实际输出清单（诊断输出键名与间歇为空问题）
        /// </summary>
        private static ImageBaseData TryGetOutputImage(VmProcedure procedure, string preferredKey)
        {
            ImageBaseData TryRead(string key)
            {
                try
                {
                    return procedure.ModuResult.GetOutputImageV2(key);
                }
                catch
                {
                    return null; // 非图像类型输出键会抛类型不匹配异常，属预期
                }
            }

            var image = TryRead(preferredKey);
            if (image != null)
            {
                return image;
            }

            // 首选键为空：枚举全部输出名回退尝试（跳过首选键本身），同时打印清单供排障
            List<string> allKeys = new List<string>();
            try
            {
                foreach (var info in procedure.ModuResult.GetAllOutputNameInfo())
                {
                    if (!string.IsNullOrEmpty(info.Name))
                    {
                        allKeys.Add(info.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[VmBridge] GetAllOutputNameInfo 失败：" + ex.Message);
            }

            Console.Error.WriteLine("[VmBridge] 输出键「" + preferredKey + "」为空，流程输出清单：" +
                                    (allKeys.Count > 0 ? string.Join(",", allKeys) : "（枚举为空）"));
            DiagLog($"Run 输出键「{preferredKey}」为空，流程输出清单：{(allKeys.Count > 0 ? string.Join(",", allKeys) : "（枚举为空）")}");

            foreach (var key in allKeys)
            {
                if (key == preferredKey)
                {
                    continue;
                }

                image = TryRead(key);
                if (image != null)
                {
                    Console.Error.WriteLine("[VmBridge] 回退输出键「" + key + "」取图成功");
                    DiagLog($"回退输出键「{key}」取图成功");
                    return image;
                }
            }

            return null;
        }

        /// <summary>
        /// 枚举方案内全部流程名（探测/错误提示用）
        /// </summary>
        private static string[] EnumerateProcedureNames()
        {
            EnsureLoaded();
            var names = new System.Collections.Generic.List<string>();
            var modules = VmSolution.Instance.Modules;
            foreach (var module in modules)
            {
                var procedure = module as VmProcedure;
                if (procedure != null && !string.IsNullOrEmpty(procedure.Name))
                {
                    names.Add(procedure.Name);
                }
            }

            return names.ToArray();
        }

        /// <summary>
        /// 方案未加载时抛出（由统一异常处理转 ERR 响应）
        /// </summary>
        private static void EnsureLoaded()
        {
            if (!_solutionLoaded)
            {
                throw new InvalidOperationException("方案未加载，请先执行 Load 命令");
            }
        }

        /// <summary>
        /// 关闭方案并释放
        /// </summary>
        private static void ShutdownSolution()
        {
            lock (SolutionLock)
            {
                if (!_solutionLoaded)
                {
                    return;
                }

                try
                {
                    VmSolution.Instance.CloseSolution();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[VmBridge] CloseSolution 异常：" + ex.Message);
                }

                _solutionLoaded = false;
                Console.WriteLine("[VmBridge] 方案已关闭");
            }
        }

        // ══════════════ 管道帧收发 ══════════════

        /// <summary>
        /// 读取一帧请求；客户端断开返回 null
        /// </summary>
        private static KeyValuePair<byte, byte[]>? ReadRequest(NamedPipeServerStream server)
        {
            var header = new byte[VmBridgeProtocol.HeaderLength];
            if (!ReadExact(server, header, VmBridgeProtocol.HeaderLength))
            {
                return null;
            }

            int length = VmBridgeProtocol.TryGetPayloadLength(header, header.Length);
            var payload = new byte[length];
            if (length > 0 && !ReadExact(server, payload, length))
            {
                return null;
            }

            return new KeyValuePair<byte, byte[]>(header[0], payload);
        }

        /// <summary>
        /// 精确读取 n 字节（不足即视为断开）
        /// </summary>
        private static bool ReadExact(PipeStream stream, byte[] buffer, int count)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        /// <summary>
        /// 写一帧响应
        /// </summary>
        private static void WriteFrame(NamedPipeServerStream server, byte command, byte[] payload)
        {
            var frame = VmBridgeProtocol.EncodeFrame(command, payload);
            server.Write(frame, 0, frame.Length);
            server.Flush();
        }
    }
}