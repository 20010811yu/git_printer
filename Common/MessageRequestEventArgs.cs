using System;

namespace UiTopMachine.Common
{
    /// <summary>
    /// 消息提示请求事件参数（纯数据载体）：
    /// ViewModel 通过事件向 View 发起"需要向用户展示提示信息"的请求（不接触任何 UI 控件），
    /// View 弹出消息框展示，无需用户回填（纯单向通知，区别于需要回填 Confirmed 的确认请求）
    /// </summary>
    public class MessageRequestEventArgs : EventArgs
    {
        /// <summary>弹框标题</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>提示内容（校验失败原因等需要用户立即感知的信息）</summary>
        public string Message { get; init; } = string.Empty;
    }
}