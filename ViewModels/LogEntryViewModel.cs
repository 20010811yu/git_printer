using System;
using System.Drawing;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 日志条目视图模型：为 UI 提供着色信息
    /// </summary>
    public class LogEntryViewModel : ObservableObject
    {
        /// <summary>
        /// 时间文本（HH:mm:ss）
        /// </summary>
        public string TimeText { get; }

        /// <summary>
        /// 日志内容
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// 级别名称
        /// </summary>
        public string LevelText { get; }

        /// <summary>
        /// 内容颜色（按级别）
        /// </summary>
        public Color TextColor { get; }

        /// <summary>
        /// 由日志实体构造 VM
        /// </summary>
        public LogEntryViewModel(LogEntryModel model)
        {
            if (model is null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            TimeText = model.Timestamp.ToString("HH:mm:ss");
            Message = model.Message;
            LevelText = model.Level switch
            {
                LogLevel.Warning => "警告",
                LogLevel.Error => "错误",
                LogLevel.Success => "成功",
                _ => "信息"
            };
            TextColor = model.Level switch
            {
                LogLevel.Warning => Color.FromArgb(230, 145, 0),    // 橙
                LogLevel.Error => Color.FromArgb(211, 47, 47),      // 红
                LogLevel.Success => Color.FromArgb(46, 125, 50),    // 绿
                _ => Color.FromArgb(66, 82, 102)                    // 深灰蓝
            };
        }
    }
}