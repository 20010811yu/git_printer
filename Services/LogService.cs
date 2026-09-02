using System;
using System.IO;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// 日志服务：事件通知 + 文件落盘（logs/ 目录，按日期滚动）
    /// </summary>
    public class LogService : ILogService
    {
        private readonly string _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static readonly object _fileLock = new();

        /// <inheritdoc />
        public event EventHandler<LogEntryModel>? LogEmitted;

        /// <inheritdoc />
        public void Info(string message) => Write(LogLevel.Info, message);

        /// <inheritdoc />
        public void Warn(string message) => Write(LogLevel.Warning, message);

        /// <inheritdoc />
        public void Error(string message) => Write(LogLevel.Error, message);

        /// <inheritdoc />
        public void Success(string message) => Write(LogLevel.Success, message);

        /// <summary>
        /// 写日志：事件推送 + 文件记录
        /// </summary>
        private void Write(LogLevel level, string message)
        {
            var entry = new LogEntryModel { Timestamp = DateTime.Now, Level = level, Message = message };

            // 通知 UI（订阅方负责线程调度）
            LogEmitted?.Invoke(this, entry);

            // 落盘（异常不影响主流程）
            try
            {
                Directory.CreateDirectory(_logDirectory);
                var file = Path.Combine(_logDirectory, $"{DateTime.Now:yyyyMMdd}.log");
                lock (_fileLock)
                {
                    File.AppendAllText(file, $"{entry.Timestamp:HH:mm:ss.fff} [{entry.Level}] {entry.Message}{Environment.NewLine}");
                }
            }
            catch
            {
                // 日志落盘失败静默处理，避免影响业务流程
            }
        }
    }
}