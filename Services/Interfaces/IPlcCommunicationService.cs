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
    /// 抽屉物料状态变化事件参数：
    /// Values[i] 为抽屉 i 的有料状态（下标 0 不使用，1~18 对应抽屉 1~18），true=有料 / false=无料
    /// </summary>
    public class DrawerMaterialsChangedEventArgs : EventArgs
    {
        /// <summary>物料状态数组（长度=读取长度，仅在有变化时触发）</summary>
        public IReadOnlyList<bool> Values { get; init; } = Array.Empty<bool>();

        /// <summary>发生时间</summary>
        public DateTime Timestamp { get; init; } = DateTime.Now;
    }

    /// <summary>
    /// PLC 通讯服务（Modbus TCP）：
    /// 启动后后台自动连接（断线自动重连），连接成功后自动开启心跳——
    /// 心跳与抽屉物料合一：周期读抽屉物料位区（M1000 起 19 个 bool，下标 i 对应抽屉 i），
    /// 读成功即通讯正常并推送物料变化事件，读失败计连续次数，连续达到阈值判心跳丢失并触发重连。
    /// 退出时 StopAsync 关闭心跳并断开连接
    /// </summary>
    public interface IPlcCommunicationService
    {
        /// <summary>
        /// 连接状态变化事件（后台线程触发，订阅方自行调度 UI 线程）
        /// </summary>
        event EventHandler<PlcConnectionEventArgs>? ConnectionStateChanged;

        /// <summary>
        /// 目标设备描述（如 "127.0.0.1:502 站号1"，用于界面展示连接状态）
        /// </summary>
        string Target { get; }

        /// <summary>
        /// 抽屉物料状态变化事件（后台线程触发，仅数值有变化时推送，订阅方自行调度 UI 线程）
        /// </summary>
        event EventHandler<DrawerMaterialsChangedEventArgs>? DrawerMaterialsChanged;

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
