using System;
using UiTopMachine.Models;

namespace UiTopMachine.Services.Interfaces
{
    /// <summary>
    /// 日志服务接口：ViewModel 通过此接口记录日志，不直接写文件
    /// </summary>
    public interface ILogService
    {
        /// <summary>
        /// 日志产生事件（UI 可订阅以刷新显示）
        /// </summary>
        event EventHandler<LogEntryModel>? LogEmitted;

        /// <summary>
        /// 记录信息日志
        /// </summary>
        void Info(string message);

        /// <summary>
        /// 记录警告日志
        /// </summary>
        void Warn(string message);

        /// <summary>
        /// 记录错误日志
        /// </summary>
        void Error(string message);

        /// <summary>
        /// 记录成功日志
        /// </summary>
        void Success(string message);
    }
}