using HslCommunication.ModBus;
using HslCommunication.Profinet.Inovance;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Communications.Plc
{
    /// <summary>
    /// 基于 HslCommunication 的 Modbus TCP 传输实现：
    /// 默认实例化 InovanceTcpNet（汇川协议，继承自 ModbusTcpNet），可切换为标准 ModbusTcpNet。
    /// 连接/收发超时 3 秒；V12 版本默认长连接，无需再设置 SetPersistentConnection（已过时）；
    /// Hsl 的 OperateResult 在此层转换为返回值或异常，OperateResult/客户端实例不外泄到上层。
    /// ⚠️ 地址格式由所选协议类决定，原样透传不转换（ERR-022）：
    /// InovanceTcpNet 要求汇川软元件格式——位地址 "M1000"，字地址 "D100"/"R100"，纯数字无法解析；
    /// 标准 ModbusTcpNet 用纯数字——线圈 "1000"，保持寄存器 "100"
    /// </summary>
    public class HslModbusTransport : IPlcTransport
    {
        private const int ConnectTimeoutMs = 10000;
        private const int ReceiveTimeoutMs = 5000;

        private readonly ModbusTcpNet _plc;

        /// <summary>
        /// 构造：指定 PLC 地址与协议类型
        /// </summary>
        /// <param name="ipAddress">PLC IP 地址</param>
        /// <param name="port">Modbus TCP 端口</param>
        /// <param name="station">站号</param>
        /// <param name="useInovance">true=InovanceTcpNet（汇川），false=标准 ModbusTcpNet</param>
        public HslModbusTransport(string ipAddress = "127.0.0.1", int port = 502, byte station = 1, bool useInovance = true)
        {
            // InovanceTcpNet 必须显式指定系列（默认 AM 系列不支持 D 字地址，ERR-022 实证）：
            // H5U 系列——位地址 M/B/S/X/Y（如 "M1000"→线圈 1000），字地址 D/R（如 "D100"→寄存器 100）
            _plc = useInovance
                ? new InovanceTcpNet(InovanceSeries.H5U, ipAddress, port, station)
                : new ModbusTcpNet(ipAddress, port, station);
            _plc.ConnectTimeOut = ConnectTimeoutMs;
            _plc.ReceiveTimeOut = ReceiveTimeoutMs;
        }

        /// <inheritdoc />
        public async Task ConnectAsync()
        {
            var result = await _plc.ConnectServerAsync();
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"PLC 连接失败：{result.Message}");
            }
        }

        /// <inheritdoc />
        public async Task<short> ReadShortAsync(string address)
        {
            var result = await _plc.ReadInt16Async(address);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"PLC 读取寄存器 {address} 失败：{result.Message}");
            }

            return result.Content;
        }

        /// <inheritdoc />
        public async Task<bool[]> ReadBoolsAsync(string address, ushort length)
        {
            var result = await _plc.ReadBoolAsync(address, length);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"PLC 读取位地址 {address} 失败：{result.Message}");
            }

            return result.Content;
        }

        /// <inheritdoc />
        public async Task WriteShortAsync(string address, short value)
        {
            var result = await _plc.WriteAsync(address, value);
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException($"PLC 写入寄存器 {address} 失败：{result.Message}");
            }
        }

        /// <inheritdoc />
        public Task CloseAsync()
        {
            try
            {
                _plc.ConnectClose();
            }
            catch
            {
                // 关闭失败不影响上层状态机（重连循环会兜底）
            }

            return Task.CompletedTask;
        }
    }
}
