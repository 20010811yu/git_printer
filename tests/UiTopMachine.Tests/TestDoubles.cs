using System;
using System.Collections.Generic;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 测试桩：ILogService 的内存实现（记录日志消息供断言，不写文件、不弹 UI）
    /// </summary>
    public class StubLogService : ILogService
    {
        /// <summary>已记录的日志（级别 + 消息）</summary>
        public List<(string Level, string Message)> Entries { get; } = new();

#pragma warning disable CS0067 // 测试桩无需真正触发事件
        public event EventHandler<LogEntryModel>? LogEmitted;
#pragma warning restore CS0067

        public void Info(string message) => Entries.Add(("Info", message));

        public void Warn(string message) => Entries.Add(("Warn", message));

        public void Error(string message) => Entries.Add(("Error", message));

        public void Success(string message) => Entries.Add(("Success", message));

        /// <summary>是否存在指定级别的日志（断言辅助）</summary>
        public bool HasLevel(string level) => Entries.Exists(e => e.Level == level);
    }
}