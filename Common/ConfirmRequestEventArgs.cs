using System;

namespace UiTopMachine.Common
{
    /// <summary>
    /// 确认请求事件参数（纯数据载体）：
    /// ViewModel 通过事件向 View 发起"需要用户确认"的请求（不接触任何 UI 控件），
    /// View 弹出确认弹框后把用户选择回填到 Confirmed，
    /// 删除等危险操作的执行与业务处理仍全部在 ViewModel 内完成
    /// </summary>
    public class ConfirmRequestEventArgs : EventArgs
    {
        /// <summary>弹框标题</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>确认提示内容（说明将执行的操作与后果）</summary>
        public string Message { get; init; } = string.Empty;

        /// <summary>用户是否确认（View 回填：确定=true，取消/关闭=false）</summary>
        public bool Confirmed { get; set; }
    }
}