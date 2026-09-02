using System;

namespace UiTopMachine.Common
{
    /// <summary>
    /// 输入请求事件参数（纯数据载体）：
    /// ViewModel 通过事件向 View 发起"需要用户输入"的请求（不接触任何 UI 控件），
    /// View 弹出输入框后把用户选择回填到 Confirmed / InputText，
    /// 校验与业务处理仍全部在 ViewModel 内完成
    /// </summary>
    public class InputRequestEventArgs : EventArgs
    {
        /// <summary>弹框标题</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>输入说明文字</summary>
        public string Prompt { get; init; } = string.Empty;

        /// <summary>用户是否确认（View 回填：确定=true，取消/关闭=false）</summary>
        public bool Confirmed { get; set; }

        /// <summary>用户输入内容（View 回填，原文不加工）</summary>
        public string InputText { get; set; } = string.Empty;
    }
}