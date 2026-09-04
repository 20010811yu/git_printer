using UiTopMachine.Models;

namespace UiTopMachine.Services.Interfaces
{
    /// <summary>
    /// 设备对接状态面板发布接口：向主窗体 Status 面板发布设备对接/运行状态信息。
    /// 面板定位（v1.21）：PLC 连接状态与失败原因、心跳错误、视觉方案加载、程序运行时错误；
    /// 一般操作信息不进面板（仅落文件日志）。
    /// 实现方负责线程调度（可在任意线程调用）
    /// </summary>
    public interface IPanelStatusPublisher
    {
        /// <summary>
        /// 发布一条面板状态（Success=绿 / Warning=橙 / Error=红；Info 级别不进面板由实现方酌情处理）
        /// </summary>
        void PublishPanelEntry(LogLevel level, string message);
    }
}
