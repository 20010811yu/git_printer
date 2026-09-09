using System;

namespace UiTopMachine.Common
{
    /// <summary>
    /// 保存路径请求事件参数（纯数据载体，ERR-032a）：
    /// ViewModel 通过事件向 View 发起"需要用户选择保存位置"的请求（不接触任何 UI 控件），
    /// View 弹出保存对话框后把结果回填到 Confirmed / FullPath。
    /// 注意：不能把 SaveFileDialog 放进 CommandManagerHelper 的参数提供器——
    /// 提供器在绑定与每次命令状态刷新时都会被调用，会导致弹窗失控
    /// </summary>
    public class SavePathRequestEventArgs : EventArgs
    {
        /// <summary>弹框标题</summary>
        public string Title { get; init; } = string.Empty;

        /// <summary>建议的初始目录（View 尽力创建/使用）</summary>
        public string InitialDirectory { get; init; } = string.Empty;

        /// <summary>建议的默认文件名</summary>
        public string FileName { get; init; } = string.Empty;

        /// <summary>用户是否确认保存（View 回填：保存=true，取消/关闭=false）</summary>
        public bool Confirmed { get; set; }

        /// <summary>用户选择的完整保存路径（View 回填）</summary>
        public string FullPath { get; set; } = string.Empty;
    }
}
