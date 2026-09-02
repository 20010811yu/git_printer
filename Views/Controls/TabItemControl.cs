using System;
using System.Drawing;
using System.Windows.Forms;

namespace UiTopMachine.Views.Controls
{
    /// <summary>
    /// 底部导航 Tab 控件：文本 + 选中下划线（自绘）
    /// 点击经 CommandManagerHelper 命令绑定转发（导航命令 + PageType 参数）
    /// 纯 View 控件：仅根据 IsSelected 渲染样式，无业务逻辑
    /// </summary>
    public class TabItemControl : Label
    {
        private bool _isSelected;

        /// <summary>
        /// 是否选中（驱动高亮样式：加粗 + 主题色 + 下划线）
        /// </summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                ApplyStyle();
                Invalidate(); // 触发重绘下划线
            }
        }

        /// <summary>
        /// 构造：固定尺寸 Tab（随导航栏绝对定位）
        /// </summary>
        public TabItemControl(string text)
        {
            Text = text ?? throw new ArgumentNullException(nameof(text));
            AutoSize = false;
            Size = new Size(124, 50);
            TextAlign = ContentAlignment.MiddleCenter;
            Cursor = Cursors.Hand;
            BackColor = Color.White;
            ApplyStyle();
        }

        /// <summary>
        /// 按选中态应用字体与前景色
        /// </summary>
        private void ApplyStyle()
        {
            Font = new Font("Microsoft YaHei UI", _isSelected ? 14f : 13f,
                            _isSelected ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
            ForeColor = _isSelected ? Color.FromArgb(45, 125, 210) : Color.FromArgb(96, 108, 124);
        }

        /// <summary>
        /// 自绘选中下划线（文本由基类 Label 绘制）
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_isSelected)
            {
                using var brush = new SolidBrush(Color.FromArgb(45, 125, 210));
                e.Graphics.FillRectangle(brush, 22, Height - 5, Width - 44, 4);
            }
        }
    }
}