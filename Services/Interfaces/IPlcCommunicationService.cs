namespace UiTopMachine.Services.Interfaces
{
    /// <summary>
    /// PLC 连接状态
    /// </summary>
    public enum PlcConnectionState
    {
        /// <summary>连接中（含自动重连）</summary>
        Connecting,

        /// <summary>已连接（心跳运行中）</summary>
        Connected,

        /// <summary>已断开（连接失败或通讯异常）</summary>
        Disconnected,

        /// <summary>心跳丢失（PLC 侧心跳寄存器停滞，触发自动重连）</summary>
        HeartbeatLost
    }

    /// <summary>
    /// PLC 连接状态变化事件参数（纯数据载体，消息为中文描述，可直接入日志）
    /// </summary>
    public class PlcConnectionEventArgs : EventArgs
    {
        /// <summary>连接状态</summary>
        public PlcConnectionState State { get; init; }

        /// <summary>中文描述消息</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>发生时间</summary>
        public DateTime Timestamp { get; init; } = DateTime.Now;
    }

    /// <summary>
    /// PLC 通讯服务（Modbus TCP）：
    /// 启动后后台自动连接（断线自动重连），连接成功后自动启动双向心跳——
    /// PC 侧周期向写心跳寄存器递增写数（证明 PC 在线），同时监听读心跳寄存器变化（证明 PLC 在线），
    /// PLC 侧心跳停滞连续超过阈值判定心跳丢失并触发重连。退出时 StopAsync 关闭心跳并断开连接
    /// </summary>
    public interface IPlcCommunicationService
    {
        /// <summary>
        /// 连接状态变化事件（后台线程触发，订阅方自行调度 UI 线程）
        /// </summary>
        event EventHandler<PlcConnectionEventArgs>? ConnectionStateChanged;

        /// <summary>当前连接状态</summary>
        PlcConnectionState State { get; }

        /// <summary>
        /// 启动服务：开启后台自动连接循环（幂等，重复调用无效）；连接成功后心跳自动启动
        /// </summary>
        Task StartAsync();

        /// <summary>
        /// 停止服务：关闭心跳 → 断开连接（应用退出时调用）
        /// </summary>
        Task StopAsync();

        /// <summary>
        /// 手动启动心跳（已连接时有效；自动连接模式下连接成功后已自动启动，重复调用幂等返回成功）
        /// </summary>
        Task<Result<bool>> StartHeartbeatAsync();

        /// <summary>
        /// 手动关闭心跳（连接保持，不再收发心跳报文）
        /// </summary>
        Task<Result<bool>> StopHeartbeatAsync();

        /// <summary>
        /// 读取保持寄存器（16 位有符号整数）
        /// </summary>
        Task<Result<short>> ReadRegisterAsync(string address);

        /// <summary>
        /// 写入保持寄存器（16 位有符号整数）
        /// </summary>
        Task<Result<bool>> WriteRegisterAsync(string address, short value);
    }
}
