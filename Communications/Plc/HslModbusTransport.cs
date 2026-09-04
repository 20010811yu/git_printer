using HslCommunication.ModBus;
using HslCommunication.Profinet.Inovance;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Communications.Plc
{
    /// <summary>
    /// 基于 HslCommunication 的 Modbus TCP 传输实现：
    /// 默认实例化 InovanceTcpNet（汇川协议，继承自 ModbusTcpNet），可切换为标准 ModbusTcpNet。
    /// 连接/收发超时 3 秒；V12 版本默认长连接，无需再设置 SetPersistentConnection（已过时）；
    /// Hsl 的 OperateResult 在此层转换为返回值或异常，OperateResult/客户端实例不外泄到上层
    /// </summary>
    public class HslModbusTransport : IPlcTransport
    {
        private const int ConnectTimeoutMs = 3000;
        private const int ReceiveTimeoutMs = 3000;

        private readonly ModbusTcpNet _plc;

        /// <summary>
        /// 构造：指定 PLC 地址与协议类型
        /// </summary>
        /// <param name="ipAddress">PLC IP 地址</param>
        /// <param name="port">Modbus TCP 端口</param>
        /// <param name="station">站号</param>
        /// <param name="useInovance">true=InovanceTcpNet（汇川），false=标准 ModbusTcpNet</param>
        public HslModbusTransport(string ipAddress = "192.168.1.88", int port = 502, byte station = 1, bool useInovance = true)
        {
            _plc = useInovance
                ? new InovanceTcpNet(ipAddress, port, station)
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
