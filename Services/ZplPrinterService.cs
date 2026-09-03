using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// ZPL 打印服务（整合自用户提供的 ZplPrinter 源码，严格 Service 层封装）：
    /// ① TCP 直连打印机发 ZPL（TcpClient，IP/端口可配置，含超时）
    /// ② Windows Spooler RAW 发 ZPL 到指定名称打印机（winspool.Drv P/Invoke，句柄私有封装）
    /// ③ ZPL 指令生成（二维码/Code39/Code128/PDF417/数字文本，纯函数）
    /// ④ 流水号持久化（D:\Printer\Data\SerialNumber.txt，6 位补零）
    /// 所有网络/Spooler IO 经 Task.Run 异步化（不阻塞 UI），统一 Result 返回
    /// </summary>
    public class ZplPrinterService : IPrintService
    {
        // ══════════════ 配置（构造可覆盖，默认沿用源码值） ══════════════

        /// <summary>打印机 IP 地址</summary>
        private readonly string _ipAddress;

        /// <summary>打印机 TCP 端口</summary>
        private readonly int _port;

        /// <summary>Windows Spooler 打印机名称</summary>
        private readonly string _printerName;

        /// <summary>TCP 连接超时（毫秒）</summary>
        private const int ConnectTimeoutMs = 3000;

        /// <summary>流水号持久化文件路径</summary>
        private readonly string _serialFilePath;

        /// <summary>流水号位数（不足补零）</summary>
        private const int SerialDigits = 6;

        /// <summary>默认起始流水号</summary>
        private const string DefaultSerial = "000001";

        // ══════════════ 构造 ══════════════

        /// <summary>
        /// 构造：默认沿用源码配置（IP 192.168.1.200:9100 / 打印机名 zpl）；
        /// 测试可注入自定义参数（含流水号文件路径隔离）
        /// </summary>
        public ZplPrinterService(
            string ipAddress = "192.168.1.200",
            int port = 9100,
            string printerName = "zpl",
            string? serialFilePath = null)
        {
            _ipAddress = ipAddress;
            _port = port;
            _printerName = printerName;
            _serialFilePath = serialFilePath ?? @"D:\Printer\Data\SerialNumber.txt";
        }

        // ══════════════ 打印（网络 / Spooler） ══════════════

        /// <inheritdoc />
        public async Task<Result<bool>> PrintByIpAsync(string zpl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(zpl))
                {
                    return Result<bool>.Fail("ZPL 指令为空，无法打印");
                }

                return await Task.Run(() =>
                {
                    var buffer = Encoding.UTF8.GetBytes(zpl);
                    // TCP 直连：连接失败（打印机离线/网络不通）抛 SocketException → 统一捕获返回失败
                    using var client = new TcpClient();
                    var connectTask = client.ConnectAsync(_ipAddress, _port);
                    if (!connectTask.Wait(ConnectTimeoutMs))
                    {
                        return Result<bool>.Fail($"连接打印机超时（{_ipAddress}:{_port}，{ConnectTimeoutMs}ms）");
                    }

                    using var stream = client.GetStream();
                    stream.Write(buffer, 0, buffer.Length);
                    stream.Flush();
                    return Result<bool>.OK(true);
                });
            }
            catch (SocketException ex)
            {
                return Result<bool>.Fail($"打印机连接失败（{_ipAddress}:{_port}）：{ex.Message}");
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"打印失败：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool>> PrintBySpoolerAsync(string zpl)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(zpl))
                {
                    return Result<bool>.Fail("ZPL 指令为空，无法打印");
                }

                return await Task.Run(() =>
                {
                    // winspool.Drv RAW 写入（句柄全程私有，不外泄）
                    if (!OpenPrinter(_printerName, out var hPrinter, IntPtr.Zero))
                    {
                        return Result<bool>.Fail($"无法打开打印机：{_printerName}（请确认打印机已安装并连接）");
                    }

                    try
                    {
                        var di = new DOCINFO
                        {
                            pDocName = "UiTopMachine 标签打印",
                            pDataType = "RAW"
                        };

                        if (!StartDocPrinter(hPrinter, 1, ref di))
                        {
                            return Result<bool>.Fail("启动打印文档失败");
                        }

                        try
                        {
                            if (!StartPagePrinter(hPrinter))
                            {
                                return Result<bool>.Fail("启动打印页失败");
                            }

                            var pBytes = Marshal.StringToCoTaskMemAnsi(zpl);
                            try
                            {
                                var written = WritePrinter(hPrinter, pBytes, zpl.Length, out var dwWritten);
                                if (!written || dwWritten != zpl.Length)
                                {
                                    return Result<bool>.Fail("写入打印机数据不完整");
                                }
                            }
                            finally
                            {
                                Marshal.FreeCoTaskMem(pBytes);
                                EndPagePrinter(hPrinter);
                            }
                        }
                        finally
                        {
                            EndDocPrinter(hPrinter);
                        }

                        return Result<bool>.OK(true);
                    }
                    finally
                    {
                        ClosePrinter(hPrinter);
                    }
                });
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"打印失败：{ex.Message}");
            }
        }

        // ══════════════ ZPL 指令生成（纯函数，源自 ZplPrinter 源码） ══════════════

        /// <inheritdoc />
        public string GenerateZpl(ZplCodeType codeType, string serialNumber)
            => codeType switch
            {
                ZplCodeType.QRCode => CreateQrCodeZpl(serialNumber),
                ZplCodeType.Code39 => CreateCode39Zpl(serialNumber),
                ZplCodeType.Code128 => CreateCode128Zpl(serialNumber),
                ZplCodeType.Pdf417 => CreatePdf417Zpl(serialNumber),
                ZplCodeType.Number => CreateNumberZpl(serialNumber),
                _ => throw new ArgumentOutOfRangeException(nameof(codeType), codeType, "未知码型")
            };

        /// <inheritdoc />
        public string CreateNumberZpl(string serialNumber)
        {
            // 数字文本（源码 CreateNum）
            var zpl = "^XA";
            zpl += "^FO50,80";
            zpl += "^AON,60,60"; // 字体高和宽
            zpl += "^FD";
            zpl += serialNumber;
            zpl += "^FS";
            zpl += "^XZ";
            return zpl;
        }

        /// <inheritdoc />
        public string CreateCode39Zpl(string serialNumber)
        {
            // Code 39 条码 ^B3（源码 Create39Code）
            var zpl = "^XA";
            zpl += "^PW240";          // 标签宽度 240 点
            zpl += "^LL240";          // 标签高度 240 点
            zpl += "^FO60,60";
            zpl += "^B3N,N,2,Y,N";
            zpl += "^FD";
            zpl += serialNumber;
            zpl += "^FS";
            zpl += "^XZ";
            return zpl;
        }

        /// <inheritdoc />
        public string CreateCode128Zpl(string serialNumber)
        {
            // Code 128 条码 ^BC（源码 Create128Code；修正笔误 "LL300" → "^LL300"）
            var zpl = "^XA";
            zpl += "^BW1";
            zpl += "^PW300";
            zpl += "^LL300";
            zpl += "^FO50,80";
            zpl += "^BCN,60,Y,N,N";
            zpl += "^FD";
            zpl += serialNumber;
            zpl += "^FS";
            zpl += "^XZ";
            return zpl;
        }

        /// <inheritdoc />
        public string CreatePdf417Zpl(string serialNumber)
        {
            // PDF417 条码 ^B7（源码 Create417Code）
            var zpl = "^XA";
            zpl += "^FO100,100^BY3";
            zpl += "^B7N,8,5,7,21,N";
            zpl += "^FD";
            zpl += serialNumber;
            zpl += "^FS";
            zpl += "^XZ";
            return zpl;
        }

        /// <inheritdoc />
        public string CreateQrCodeZpl(string serialNumber)
        {
            // 二维码 ^BQ：标签 30mm×30mm（240 点 × 240 点，203dpi），二维码约 20mm（源码 CreateLabelZpl）
            var zpl = "^XA";
            zpl += "^PW240";                  // 标签宽度 240 点
            zpl += "^LL240";                  // 标签高度 240 点
            zpl += "^FO60,60";                // 二维码坐标
            zpl += "^BQN,2,6";                // 自动版本，放大 6
            zpl += "^FDQA," + serialNumber;   // 数据: QA 前缀 + 流水号
            zpl += "^FS";
            zpl += "^XZ";
            return zpl;
        }

        // ══════════════ 校验 ══════════════

        /// <inheritdoc />
        public bool IsValidSerial(string serialNumber)
        {
            // 非空 + 纯数字（源码 JudgeText + JudgeSerialNum 合并口径）
            if (string.IsNullOrWhiteSpace(serialNumber))
            {
                return false;
            }
            return serialNumber.All(c => c >= '0' && c <= '9');
        }

        // ══════════════ 流水号持久化 ══════════════

        /// <inheritdoc />
        public async Task<Result<string>> LoadSerialAsync()
        {
            try
            {
                return await Task.Run(() =>
                {
                    if (!File.Exists(_serialFilePath))
                    {
                        return Result<string>.OK(DefaultSerial); // 首次使用：默认 000001
                    }

                    var text = File.ReadAllText(_serialFilePath).Trim();
                    return IsValidSerial(text)
                        ? Result<string>.OK(text)
                        : Result<string>.Fail($"流水号文件内容非法（应为纯数字）：{text}");
                });
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"读取流水号失败：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<Result<bool>> SaveSerialAsync(string serialNumber)
        {
            try
            {
                if (!IsValidSerial(serialNumber))
                {
                    return Result<bool>.Fail($"流水号非法（应为纯数字）：{serialNumber}");
                }

                return await Task.Run(() =>
                {
                    var dir = Path.GetDirectoryName(_serialFilePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    // 6 位补零保存（超出 6 位按实际位数保存，支持 999999→1000000 进位场景）
                    var padded = serialNumber.Length >= SerialDigits
                        ? serialNumber
                        : serialNumber.PadLeft(SerialDigits, '0');
                    File.WriteAllText(_serialFilePath, padded);
                    return Result<bool>.OK(true);
                });
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"保存流水号失败：{ex.Message}");
            }
        }

        // ══════════════ Windows Spooler P/Invoke（私有封装，不外泄） ══════════════

        [StructLayout(LayoutKind.Sequential)]
        private struct DOCINFO
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDataType;
        }

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] ref DOCINFO di);

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool EndDocPrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool StartPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool EndPagePrinter(IntPtr hPrinter);

        [DllImport("winspool.Drv", CharSet = CharSet.Unicode)]
        private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);
    }
}