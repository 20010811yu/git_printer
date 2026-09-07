using System;
using System.Collections.Generic;
using System.Text;

namespace UiTopMachine.Common.VmBridge
{
    /// <summary>
    /// 视觉桥接进程（VmVisionBridge）二进制帧协议（主程序与桥接项目共享同一份源码，杜绝两侧漂移）：
    /// 帧结构 = [命令:1B][payload长度:4B 小端][payload]
    /// 请求 payload 为 UTF8 文本（Load=方案路径 / Run=流程名 / 其余为空）
    /// 响应 payload = 头部文本（key=value 行，\n 分隔，空行结束）+ 可选 PNG 二进制
    /// 目标框架兼容：net48（C# 7.3）与 net10.0 通用语法，不含版本特定 API
    /// </summary>
    public static class VmBridgeProtocol
    {
        // ══════════════ 常量 ══════════════

        /// <summary>帧头长度：命令 1 字节 + payload 长度 4 字节</summary>
        public const int HeaderLength = 5;

        /// <summary>payload 最大长度（512MB，防御异常帧）</summary>
        public const int MaxPayloadLength = 512 * 1024 * 1024;

        // ══════════════ 命令 ══════════════

        /// <summary>桥接命令字节（请求与响应共用同一命令号）</summary>
        public static class Command
        {
            /// <summary>探活</summary>
            public const byte Ping = 1;

            /// <summary>加载方案（payload = UTF8 方案路径）</summary>
            public const byte Load = 2;

            /// <summary>运行一次检测（payload = UTF8 流程名；响应含 PNG 结果图）</summary>
            public const byte Run = 3;

            /// <summary>关闭方案并退出（payload = 空）</summary>
            public const byte Close = 4;

            /// <summary>枚举方案内全部流程名（payload = 空）</summary>
            public const byte ListProcedures = 5;
        }

        // ══════════════ 帧 编解码 ══════════════

        /// <summary>
        /// 编码一帧：[命令 1B][长度 4B LE][payload]
        /// </summary>
        public static byte[] EncodeFrame(byte command, byte[] payload)
        {
            payload = payload ?? new byte[0];
            if (payload.Length > MaxPayloadLength)
            {
                throw new InvalidOperationException($"payload 超过最大长度限制：{payload.Length}");
            }

            var frame = new byte[HeaderLength + payload.Length];
            frame[0] = command;
            // 小端写入长度
            frame[1] = (byte)(payload.Length & 0xFF);
            frame[2] = (byte)((payload.Length >> 8) & 0xFF);
            frame[3] = (byte)((payload.Length >> 16) & 0xFF);
            frame[4] = (byte)((payload.Length >> 24) & 0xFF);
            Buffer.BlockCopy(payload, 0, frame, HeaderLength, payload.Length);
            return frame;
        }

        /// <summary>
        /// 解码帧头：返回 payload 长度（-1 表示缓冲不足需继续读取）
        /// </summary>
        public static int TryGetPayloadLength(byte[] buffer, int count)
        {
            if (buffer == null || count < HeaderLength)
            {
                return -1;
            }

            int length = buffer[1] | (buffer[2] << 8) | (buffer[3] << 16) | (buffer[4] << 24);
            if (length < 0 || length > MaxPayloadLength)
            {
                throw new InvalidOperationException($"帧 payload 长度非法：{length}");
            }

            return length;
        }

        /// <summary>
        /// 编码请求 payload（UTF8 文本；null 视为空）
        /// </summary>
        public static byte[] EncodeRequestText(string text)
        {
            return Encoding.UTF8.GetBytes(text ?? string.Empty);
        }

        // ══════════════ 响应 构建与解析 ══════════════

        /// <summary>
        /// 桥接响应（解析结果）
        /// </summary>
        public sealed class VmBridgeResponse
        {
            public bool Ok { get; set; }

            /// <summary>失败原因（Ok=false 时非空）</summary>
            public string? Error { get; set; }

            /// <summary>流程名列表（ListProcedures 命令有效）</summary>
            public string[]? Procedures { get; set; }

            /// <summary>检测是否 OK（Run 命令有效）</summary>
            public bool IsOk { get; set; }

            /// <summary>流程耗时（毫秒，Run 命令有效）</summary>
            public double ElapsedMs { get; set; }

            /// <summary>结果图宽（像素，Run 命令有效）</summary>
            public int Width { get; set; }

            /// <summary>结果图高（像素，Run 命令有效）</summary>
            public int Height { get; set; }

            /// <summary>PNG 结果图字节（Run 命令有效，可能为 null）</summary>
            public byte[]? PngBytes { get; set; }
        }

        /// <summary>
        /// 构建响应 payload（桥接侧使用）：头部文本 + 空行 + PNG 二进制
        /// </summary>
        public static byte[] BuildResponse(
            bool ok,
            string error,
            string[] procedures,
            bool isOk,
            double elapsedMs,
            int width,
            int height,
            byte[] pngBytes)
        {
            var sb = new StringBuilder();
            sb.Append("status=").Append(ok ? "OK" : "ERR").Append('\n');
            if (!ok && !string.IsNullOrEmpty(error))
            {
                sb.Append("error=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(error))).Append('\n');
            }

            if (procedures != null)
            {
                sb.Append("procedures=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(",", procedures)))).Append('\n');
            }

            if (ok && pngBytes != null)
            {
                sb.Append("isok=").Append(isOk ? "1" : "0").Append('\n');
                sb.Append("elapsed=").Append(elapsedMs.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                sb.Append("width=").Append(width).Append('\n');
                sb.Append("height=").Append(height).Append('\n');
                sb.Append("pnglen=").Append(pngBytes.Length).Append('\n');
                sb.Append('\n'); // 头部结束标记（\n\n）：ParseResponse 依此切分 PNG 二进制
            }

            var header = Encoding.UTF8.GetBytes(sb.ToString());
            var payload = new byte[header.Length + (pngBytes != null && ok ? pngBytes.Length : 0)];
            Buffer.BlockCopy(header, 0, payload, 0, header.Length);
            if (ok && pngBytes != null && pngBytes.Length > 0)
            {
                Buffer.BlockCopy(pngBytes, 0, payload, header.Length, pngBytes.Length);
            }

            return payload;
        }

        /// <summary>
        /// 解析响应 payload（主程序侧使用）：头部文本（到空行为止）+ 可选 PNG
        /// </summary>
        public static VmBridgeResponse ParseResponse(byte[] payload)
        {
            var response = new VmBridgeResponse { Ok = false, Error = "响应为空" };
            if (payload == null || payload.Length == 0)
            {
                return response;
            }

            // 头部以 \n\n 结束（最后一个键值行后的空行）；兼容无 PNG 时不带尾部空行的情况
            int headerEnd;
            int pngStart;
            int separator = IndexOfDoubleNewline(payload, out headerEnd, out pngStart);

            string headerText;
            byte[]? png = null;
            if (separator >= 0)
            {
                headerText = Encoding.UTF8.GetString(payload, 0, headerEnd);
                if (pngStart < payload.Length)
                {
                    png = new byte[payload.Length - pngStart];
                    Buffer.BlockCopy(payload, pngStart, png, 0, png.Length);
                }
            }
            else
            {
                headerText = Encoding.UTF8.GetString(payload);
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var lines = headerText.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd('\r');
                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                fields[trimmed.Substring(0, eq)] = trimmed.Substring(eq + 1);
            }

            response.Ok = fields.TryGetValue("status", out var status) && status == "OK";
            if (fields.TryGetValue("error", out var errorB64) && errorB64.Length > 0)
            {
                try
                {
                    response.Error = Encoding.UTF8.GetString(Convert.FromBase64String(errorB64));
                }
                catch (FormatException)
                {
                    response.Error = errorB64;
                }
            }

            if (fields.TryGetValue("procedures", out var procB64) && procB64.Length > 0)
            {
                try
                {
                    var joined = Encoding.UTF8.GetString(Convert.FromBase64String(procB64));
                    response.Procedures = joined.Length == 0
                        ? new string[0]
                        : joined.Split(',');
                }
                catch (FormatException)
                {
                    response.Procedures = new string[0];
                }
            }

            if (response.Ok && fields.TryGetValue("pnglen", out var pngLenText))
            {
                int pngLen = 0;
                int.TryParse(pngLenText, out pngLen);
                response.IsOk = fields.TryGetValue("isok", out var isok) && isok == "1";
                response.ElapsedMs = fields.TryGetValue("elapsed", out var elapsed) ? ParseDoubleInvariant(elapsed) : 0;
                response.Width = fields.TryGetValue("width", out var w) ? ParseInt(w) : 0;
                response.Height = fields.TryGetValue("height", out var h) ? ParseInt(h) : 0;
                response.PngBytes = (png != null && png.Length == pngLen) ? png : png;
            }

            return response;
        }

        // ══════════════ 内部辅助 ══════════════

        /// <summary>
        /// 在 payload 中定位头部结束标记（\n\n），输出头部结束位置与 PNG 起始位置
        /// </summary>
        private static int IndexOfDoubleNewline(byte[] payload, out int headerEnd, out int pngStart)
        {
            for (int i = 0; i < payload.Length - 1; i++)
            {
                if (payload[i] == (byte)'\n' && payload[i + 1] == (byte)'\n')
                {
                    headerEnd = i;
                    pngStart = i + 2;
                    return i;
                }
            }

            headerEnd = payload.Length;
            pngStart = payload.Length;
            return -1;
        }

        private static double ParseDoubleInvariant(string text)
        {
            double value;
            double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
            return value;
        }

        private static int ParseInt(string text)
        {
            int value;
            int.TryParse(text, out value);
            return value;
        }
    }
}