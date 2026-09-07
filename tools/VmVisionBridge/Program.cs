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
                procedure.Run(); // 同步执行（采集 + 流程）
                sw.Stop();

                // 取流程输出图（官方约定输出键 "ImageData"；失败回退模块渲染图）
                ImageBaseData imageData = null;
                try
                {
                    imageData = procedure.ModuResult.GetOutputImageV2("ImageData");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("[VmBridge] GetOutputImageV2 失败：" + ex.Message);
                }

                if (imageData == null)
                {
                    return VmBridgeProtocol.BuildResponse(
                        false, "流程无输出图（GetOutputImageV2(\"ImageData\") 为空，请在方案中配置图像输出）",
                        null, false, sw.Elapsed.TotalMilliseconds, 0, 0, null);
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