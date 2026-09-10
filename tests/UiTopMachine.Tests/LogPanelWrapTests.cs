using System;
using System.Drawing;
using UiTopMachine.Views.Controls;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// Status 面板消息换行守护测试（v1.34）：LogPanelControl 消息流由固定单行改为
    /// 自动换行 + 动态行高，守护不变量——行数计算纯函数：长消息多行、短消息单行、
    /// 宽度越窄行数不减、非法入参兜底 1 行。
    /// </summary>
    public class LogPanelWrapTests : IDisposable
    {
        private readonly Bitmap _bitmap = new(1, 1);
        private readonly Graphics _g;
        private readonly Font _font = new("Microsoft YaHei UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel);

        public LogPanelWrapTests()
        {
            _g = Graphics.FromImage(_bitmap);
        }

        public void Dispose()
        {
            _g.Dispose();
            _bitmap.Dispose();
            _font.Dispose();
        }

        [Fact]
        public void 短消息测得单行_长消息测得多行()
        {
            var shortText = "PLC 连接成功";
            var longText = "发送失败：写入寄存器 4000 超时（3 秒），抽屉 1/3 配方未下发，"
                           + "错误码 OperationResult.Timeout，请检查 PLC 通讯链路后重试，"
                           + "输入框内容已保留现场可供再次发送使用";

            Assert.Equal(1, LogPanelControl.CountWrappedLines(_g, shortText, _font, 400));
            Assert.True(LogPanelControl.CountWrappedLines(_g, longText, _font, 400) > 1,
                "超长消息应测得多行（换行失效守护）");
        }

        [Fact]
        public void 宽度越窄行数不减_非法入参兜底单行()
        {
            var text = "视觉方案加载失败：桥接进程启动超时，已自动重启桥接进程以便下次调用重试";

            int wide = LogPanelControl.CountWrappedLines(_g, text, _font, 800);
            int narrow = LogPanelControl.CountWrappedLines(_g, text, _font, 100);

            Assert.True(narrow >= wide, "更窄的可用宽度不应测得更少行数");
            Assert.Equal(1, LogPanelControl.CountWrappedLines(_g, string.Empty, _font, 100));
        }
    }
}
