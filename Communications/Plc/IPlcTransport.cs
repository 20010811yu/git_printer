namespace UiTopMachine.Communications.Plc
{
    /// <summary>
    /// PLC 传输层抽象：屏蔽具体协议实现（HslCommunication 的 InovanceTcpNet / ModbusTcpNet），
    /// 上层 Service 只依赖本接口，SDK 对象不外泄，单元测试可注入假实现
    /// </summary>
    public interface IPlcTransport
    {
        /// <summary>
        /// 建立与 PLC 的连接（带超时，失败抛出异常）
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// 读取保持寄存器（16 位有符号整数，失败抛出异常）
        /// </summary>
        Task<short> ReadShortAsync(string address);

        /// <summary>
        /// 从起始地址连续读取一段位（bool 数组，失败抛出异常）
        /// </summary>
        Task<bool[]> ReadBoolsAsync(string address, ushort length);

        /// <summary>
        /// 写入保持寄存器（16 位有符号整数，失败抛出异常）
        /// </summary>
        Task WriteShortAsync(string address, short value);

        /// <summary>
        /// 关闭与 PLC 的连接（可重复调用，不抛异常）
        /// </summary>
        Task CloseAsync();
    }
}
