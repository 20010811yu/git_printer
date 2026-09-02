using System;

namespace UiTopMachine.Models
{
    /// <summary>
    /// 日志级别
    /// </summary>
    public enum LogLevel
    {
        /// <summary>信息（白/深灰文字）</summary>
        Info = 0,

        /// <summary>警告（橙色）</summary>
        Warning = 1,

        /// <summary>错误（红色）</summary>
        Error = 2,

        /// <summary>成功（绿色）</summary>
        Success = 3
    }

    /// <summary>
    /// 日志条目实体
    /// </summary>
    public class LogEntryModel
    {
        /// <summary>
        /// 记录时间
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// 日志级别
        /// </summary>
        public LogLevel Level { get; set; } = LogLevel.Info;

        /// <summary>
        /// 日志内容
        /// </summary>
        public string Message { get; set; } = string.Empty;
    }
}