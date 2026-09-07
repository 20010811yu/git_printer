using System;
using System.Drawing;

namespace UiTopMachine.Views
{
    /// <summary>
    /// 右上角窗口控制按钮（最小化/最大化/关闭）布局计算 —— 纯函数，便于单元测试守护。
    /// 背景（ERR-024 复现）：Anchor=Top|Right 在顶栏（Dock 宽度）未定型时设置，会冻结
    /// "控件右缘−容器右缘"的负距离，顶栏展宽后按钮被推出窗口外（点击有效但屏幕不可见）。
    /// 规避模式：不使用 Anchor，位置一律由 MainForm 在 _topBar.Resize 时按当前宽度重算。
    /// </summary>
    public static class WindowButtonLayout
    {
        // ══════════════ 布局常量（与 MainForm 顶栏/按钮实际尺寸一致） ══════════════

        /// <summary>按钮统一宽度</summary>
        public const int ButtonWidth = 56;

        /// <summary>按钮统一高度</summary>
        public const int ButtonHeight = 42;

        /// <summary>距容器右缘边距</summary>
        public const int RightMargin = 16;

        /// <summary>按钮水平间距</summary>
        public const int Spacing = 8;

        /// <summary>顶栏高度（与 MainForm 顶栏一致）</summary>
        public const int TopBarHeight = 76;

        /// <summary>按钮顶部边距（顶栏内垂直居中）</summary>
        public const int TopMargin = (TopBarHeight - ButtonHeight) / 2;

        // ══════════════ 位置计算（右对齐 + 垂直居中 + 依次向左） ══════════════

        /// <summary>关闭按钮位置（最右，右缘 = 容器宽 − 右边距）</summary>
        public static Point GetCloseLocation(int containerWidth) =>
            new(containerWidth - RightMargin - ButtonWidth, TopMargin);

        /// <summary>最大化/还原按钮位置（中间，右缘 = 关闭按钮左缘 − 间距）</summary>
        public static Point GetMaximizeLocation(int containerWidth)
        {
            var close = GetCloseLocation(containerWidth);
            return new Point(close.X - Spacing - ButtonWidth, TopMargin);
        }

        /// <summary>最小化按钮位置（最左，右缘 = 最大化按钮左缘 − 间距）</summary>
        public static Point GetMinimizeLocation(int containerWidth)
        {
            var max = GetMaximizeLocation(containerWidth);
            return new Point(max.X - Spacing - ButtonWidth, TopMargin);
        }
    }
}