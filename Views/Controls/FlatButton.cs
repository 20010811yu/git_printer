using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace UiTopMachine.Views.Controls
{
    /// <summary>
    /// 扁平圆角按钮（浅色现代风）：支持主色/危险色（退出）两种语义色
    /// </summary>
    public class FlatButton : Control
    {
        private bool _hover;
        private bool _pressed;

        /// <summary>
        /// 按钮语义类型
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public FlatButtonStyle Style { get; set; } = FlatButtonStyle.Primary;

        /// <summary>
        /// 构造：双缓冲 + 手势绘制
        /// </summary>
        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw
                     | ControlStyles.SupportsTransparentBackColor, true);
            Size = new Size(120, 40);
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
            Cursor = Cursors.Hand;
        }

        /// <summary>
        /// 圆角路径
        /// </summary>
        private GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// 按语义与交互状态取背景色
        /// </summary>
        private Color BackColorForState()
        {
            // 各语义基色：Primary=青蓝，Danger=红，Neutral=白底描边
            Color baseColor = Style switch
            {
                FlatButtonStyle.Danger => Color.FromArgb(229, 57, 53),
                FlatButtonStyle.Neutral => Color.White,
                _ => Color.FromArgb(45, 125, 210)
            };

            if (Style == FlatButtonStyle.Neutral)
            {
                return _pressed ? Color.FromArgb(232, 238, 245)
                     : _hover ? Color.FromArgb(242, 246, 251)
                     : Color.White;
            }

            return _pressed ? ControlPaint.Dark(baseColor, 0.08f)
                 : _hover ? ControlPaint.Light(baseColor, 0.12f)
                 : baseColor;
        }

        /// <summary>
        /// 取文字颜色
        /// </summary>
        private Color ForeColorForState()
        {
            return Style == FlatButtonStyle.Neutral
                ? Color.FromArgb(66, 82, 102)
                : Color.White;
        }

        /// <summary>
        /// 自绘：圆角背景 + 描边 + 居中文本
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent?.BackColor ?? SystemColors.Control);

            var rect = new Rectangle(1, 1, Width - 2, Height - 2);
            using var path = RoundedPath(rect, 10);

            using (var brush = new SolidBrush(BackColorForState()))
            {
                g.FillPath(brush, path);
            }

            // Primary/Danger 悬停时描边加深，Neutral 常规描边
            var borderColor = Style == FlatButtonStyle.Neutral
                ? Color.FromArgb(210, 218, 228)
                : ControlPaint.Dark(BackColorForState(), 0.06f);
            using (var pen = new Pen(borderColor, 1.4f))
            {
                g.DrawPath(pen, path);
            }

            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var textBrush = new SolidBrush(ForeColorForState());
            var textRect = new RectangleF(rect.Location, rect.Size);
            textRect.Offset(0, 1); // 视觉垂直居中微调
            g.DrawString(Text, Font, textBrush, textRect, sf);
        }

        /// <summary>
        /// 鼠标进入：悬停态
        /// </summary>
        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        /// <summary>
        /// 鼠标离开：还原
        /// </summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        /// <summary>
        /// 按下
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _pressed = true;
            Invalidate();
        }

        /// <summary>
        /// 抬起
        /// </summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        /// <summary>
        /// 启用状态变化时重绘
        /// </summary>
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        /// <summary>
        /// 文本变化时重绘
        /// </summary>
        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }
    }

    /// <summary>
    /// 按钮语义样式
    /// </summary>
    public enum FlatButtonStyle
    {
        /// <summary>主操作（青蓝）</summary>
        Primary,

        /// <summary>危险操作（红，如退出）</summary>
        Danger,

        /// <summary>中性（白底）</summary>
        Neutral
    }
}