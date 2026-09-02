using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using UiTopMachine.Models;

namespace UiTopMachine.Views.Controls
{
    /// <summary>
    /// 抽屉状态指示灯自绘控件：编号在圆圈左上角（大字号），圆圈扁平纯色（无 3D 渐变/高光）
    /// 状态色：就绪=绿 / 预警=黄 / 空闲=LightGray
    /// 纯 View 控件：只根据 Status 属性渲染，不含业务逻辑
    /// </summary>
    public class DrawerIndicatorControl : Control
    {
        private DrawerStatus _status = DrawerStatus.Idle;
        private int _index;

        /// <summary>
        /// 当前抽屉状态（驱动颜色）
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public DrawerStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    Invalidate(); // 触发重绘
                }
            }
        }

        /// <summary>
        /// 抽屉编号（显示在圆圈外上方）
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public int Index
        {
            get => _index;
            set
            {
                if (_index != value)
                {
                    _index = value;
                    Invalidate();
                }
            }
        }

        /// <summary>
        /// 构造：启用双缓冲；控件尺寸随父容器（TableLayoutPanel 单元格）自适应，
        /// 圆体与编号按控件大小等比缩放
        /// </summary>
        public DrawerIndicatorControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            Size = new Size(84, 104);
            // 注意：普通 Control 未启用 SupportsTransparentBackColor 时不支持 Color.Transparent，
            // 会导致 ArgumentException。此处与父容器（白色卡片）保持一致即可。
            BackColor = Color.White;
            Cursor = Cursors.Default;
        }

        /// <summary>
        /// 按状态取色（用户指定：空闲=LightGray / 就绪=绿 / 预警=黄）
        /// </summary>
        private static Color StatusColor(DrawerStatus status) => status switch
        {
            DrawerStatus.Ready => Color.LightGreen,             // 有料有配方：就绪
            DrawerStatus.Warning => Color.Yellow,               // 其余：预警
            _ => Color.LightGray                                // 无料无配方：空闲
        };

        /// <summary>
        /// 按状态取描边色（比主体色深一档）
        /// </summary>
        private static Color StatusBorderColor(DrawerStatus status) => status switch
        {
            DrawerStatus.Ready => Color.FromArgb(120, 200, 120),
            DrawerStatus.Warning => Color.FromArgb(225, 200, 60),
            _ => Color.FromArgb(200, 203, 207)                  // 灰描边
        };

        /// <summary>
        /// 自绘（扁平风格）：纯色圆体 + 大字号编号（位于圆圈左上角一点点，轻微压住圆边）
        /// 尺寸策略：先按控件尺寸算圆体（顶部预留编号高度），编号紧贴圆左上角，
        /// 保证任意窗口尺寸下圆圈与编号都完整显示、绝不被裁剪
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // ── 状态圆体（自适应：顶部预留编号空间，圆圈完整落在剩余空间内）──
            var color = StatusColor(_status);
            float numReserve = Height * 0.26f;                  // 顶部预留编号高度
            float circleAreaH = Height - numReserve;
            int diameter = (int)Math.Min(Width * 0.85f, circleAreaH * 0.92f);
            if (diameter < 8)
            {
                return; // 控件过小时跳过绘制，避免畸形
            }

            int x = (Width - diameter) / 2;
            int y = (int)(numReserve + (circleAreaH - diameter) / 2);
            var rect = new Rectangle(x, y, diameter, diameter);

            // 纯色填充（扁平，无渐变）
            using (var brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, rect);
            }

            // 边缘描边（深一档同色系，线宽随直径缩放，1~2px）
            float penWidth = Math.Clamp(diameter / 64f, 1f, 2f);
            using (var pen = new Pen(StatusBorderColor(_status), penWidth))
            {
                g.DrawEllipse(pen, rect);
            }

            // ── 编号（放大字号 LightBlue，位于圆圈左上角一点点：轻微压住圆的左上边缘）──
            if (_index > 0)
            {
                // 字号随圆体直径缩放（直径的 30%），钳制 14~34px
                float fontSize = Math.Clamp(diameter * 0.30f, 14f, 34f);
                using var numFont = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var numBrush = new SolidBrush(Color.LightBlue);

                // 左侧偏移圆左缘 6% 直径（用户要求编号再向左移一点），钳制 ≥1 防越界
                float numX = Math.Max(1f, rect.X - diameter * 0.06f);
                float numY = rect.Y - fontSize * 1.08f;
                if (numY < 1)
                {
                    numY = 1; // 防贴顶越界
                }

                g.DrawString(_index.ToString(), numFont, numBrush, numX, numY);
            }
        }
    }
}