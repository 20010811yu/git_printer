using System.Drawing;
using UiTopMachine.Views;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 右上角窗口控制按钮布局守护测试（ERR-024 复现防护）。
    /// 背景：Anchor=Top|Right 在顶栏 Dock 宽度未定型时设置会冻结负右缘距离，
    /// 把按钮推出窗口外（点击有效但屏幕不可见）——现改为 Resize 重算 + 纯函数布局。
    /// 用例锁死「任何容器宽度下按钮都必须落在容器内且右对齐」的不变量。
    /// </summary>
    public class WindowButtonLayoutTests
    {
        // ══════════════ 常量自洽性 ══════════════

        [Fact]
        public void 常量_按钮高度小于顶栏高度_垂直居中边距非负()
        {
            Assert.True(WindowButtonLayout.ButtonHeight < WindowButtonLayout.TopBarHeight);
            Assert.True(WindowButtonLayout.TopMargin >= 0);
        }

        // ══════════════ 核心不变量：任意宽度下按钮都在容器内且右对齐 ══════════════

        [Theory]
        [InlineData(200)]   // Panel 默认宽度（ERR-024 冻结负距离的元凶宽度）
        [InlineData(800)]
        [InlineData(1280)]  // MinimumSize 宽
        [InlineData(1500)]  // 初始窗口宽
        [InlineData(1870)]  // 最大化宽度（ERR-024 记录值）
        [InlineData(2560)]  // 2K 屏
        public void 布局_任意容器宽度_三按钮都落在容器内且关闭按钮右对齐(int containerWidth)
        {
            // Act
            var min = WindowButtonLayout.GetMinimizeLocation(containerWidth);
            var max = WindowButtonLayout.GetMaximizeLocation(containerWidth);
            var close = WindowButtonLayout.GetCloseLocation(containerWidth);

            // Assert — 关闭按钮右缘 = 容器宽 − 右边距（右对齐）
            Assert.Equal(containerWidth - WindowButtonLayout.RightMargin,
                         close.X + WindowButtonLayout.ButtonWidth);

            // Assert — 三按钮全部在容器内（X 非负、右缘不越界、垂直居中）
            foreach (var loc in new[] { min, max, close })
            {
                Assert.True(loc.X >= 0, $"按钮 X={loc.X} 越过容器左缘（宽 {containerWidth}）");
                Assert.True(loc.X + WindowButtonLayout.ButtonWidth <= containerWidth,
                    $"按钮 X={loc.X} 右缘越过容器右缘（宽 {containerWidth}）");
                Assert.Equal(WindowButtonLayout.TopMargin, loc.Y);
            }
        }

        // ══════════════ 排列关系：从右向左依次排列、间距一致、无重叠 ══════════════

        [Theory]
        [InlineData(200)]
        [InlineData(1280)]
        [InlineData(1920)]
        public void 布局_任意容器宽度_三按钮从右向左依次排列间距一致(int containerWidth)
        {
            // Act
            var min = WindowButtonLayout.GetMinimizeLocation(containerWidth);
            var max = WindowButtonLayout.GetMaximizeLocation(containerWidth);
            var close = WindowButtonLayout.GetCloseLocation(containerWidth);

            // Assert — X 严格递减（min < max < close），间距均为 Spacing + ButtonWidth
            Assert.True(min.X < max.X);
            Assert.True(max.X < close.X);
            Assert.Equal(WindowButtonLayout.Spacing + WindowButtonLayout.ButtonWidth, close.X - max.X);
            Assert.Equal(WindowButtonLayout.Spacing + WindowButtonLayout.ButtonWidth, max.X - min.X);

            // Assert — 无重叠：相邻按钮间隙 = Spacing
            Assert.Equal(WindowButtonLayout.Spacing, max.X - (min.X + WindowButtonLayout.ButtonWidth));
            Assert.Equal(WindowButtonLayout.Spacing, close.X - (max.X + WindowButtonLayout.ButtonWidth));
        }
    }
}