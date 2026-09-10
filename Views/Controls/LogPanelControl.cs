using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Models;
using UiTopMachine.ViewModels;

namespace UiTopMachine.Views.Controls
{
    /// <summary>
    /// Status 日志面板（纯 View）：顶部常驻渲染 PLC 连接状态行（圆点 + 状态文字，随状态刷新），
    /// 下方订阅 VM 的日志集合变化并渲染消息流，按级别着色、最新置顶、超出自动裁剪
    /// </summary>
    public class LogPanelControl : Control
    {
        private const int StatusRowHeight = 30;
        /// <summary>单行消息行高（px，换行后行高按内容实测增长）</summary>
        private const int ItemHeight = 24;
        /// <summary>行距（px，叠加在实测文本高度上，防行间粘连）</summary>
        private const int LineGap = 3;
        /// <summary>消息区左缘（时间列之后）与右缘留白（px）</summary>
        private const int MessageLeft = 78;
        private const int MessageRight = 10;
        /// <summary>重建时最多装载的条目数（绘制按控件高度自然截断，窗口越高显示越多）</summary>
        private const int MaxItems = 32;
        private readonly List<LogEntryViewModel> _items = new();

        private ObservableCollection<LogEntryViewModel>? _boundCollection;
        private string _plcStatusText = "未连接";
        private LogLevel _plcStatusLevel = LogLevel.Error;

        /// <summary>
        /// 构造：双缓冲
        /// </summary>
        public LogPanelControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.UserPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        /// <summary>
        /// 绑定日志集合（View 只观察集合变化，不做业务判断）
        /// </summary>
        public void Bind(ObservableCollection<LogEntryViewModel> logs)
        {
            if (_boundCollection is not null)
            {
                _boundCollection.CollectionChanged -= OnLogsChanged;
            }

            _boundCollection = logs;
            _boundCollection.CollectionChanged += OnLogsChanged;

            Rebuild();
        }

        /// <summary>
        /// 更新顶部 PLC 连接状态行（调用方已在 UI 线程，直接刷新）
        /// </summary>
        public void UpdatePlcStatus(string text, LogLevel level)
        {
            _plcStatusText = text;
            _plcStatusLevel = level;
            Invalidate();
        }

        /// <summary>
        /// 集合变化：同步到本地渲染列表
        /// </summary>
        private void OnLogsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (IsHandleCreated)
            {
                BeginInvoke(new Action(Rebuild));
            }
            else
            {
                Rebuild();
            }
        }

        /// <summary>
        /// 重建渲染列表（取前 N 条，绘制阶段按控件高度截断）
        /// </summary>
        private void Rebuild()
        {
            _items.Clear();
            if (_boundCollection is not null)
            {
                foreach (var item in _boundCollection)
                {
                    _items.Add(item);
                    if (_items.Count >= MaxItems)
                    {
                        break;
                    }
                }
            }

            Invalidate();
        }

        /// <summary>
        /// 测量消息在指定宽度内的换行行数（纯静态，供绘制与测试共用；非法入参按 1 行兜底）。
        /// 用 GenericTypographic 消除 MeasureString 默认留白，并对测量高度做 1px 容差防行数虚高
        /// </summary>
        public static int CountWrappedLines(Graphics g, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 1;
            }

            using var format = new StringFormat(StringFormat.GenericTypographic);
            var size = g.MeasureString(text, font, (int)Math.Max(1, width), format);
            if (size.Height <= 0 || font.Height <= 0)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Ceiling((size.Height - 1f) / font.Height));
        }

        /// <summary>
        /// 自绘：顶部 PLC 连接状态行（圆点 + 状态文字）+ 日志消息行（时间浅灰 + 消息级别色）
        /// </summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using var timeFont = new Font("Consolas", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var msgFont = new Font("Microsoft YaHei UI", 13.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            using var statusFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var timeBrush = new SolidBrush(Color.FromArgb(150, 160, 172));

            // ── 顶部 PLC 连接状态行 ──
            var statusColor = _plcStatusLevel switch
            {
                LogLevel.Success => Color.FromArgb(46, 125, 50),    // 绿
                LogLevel.Warning => Color.FromArgb(230, 145, 0),    // 橙
                LogLevel.Error => Color.FromArgb(211, 47, 47),      // 红
                _ => Color.FromArgb(66, 82, 102)                    // 深灰蓝
            };
            using (var dotBrush = new SolidBrush(statusColor))
            using (var statusBrush = new SolidBrush(statusColor))
            {
                g.FillEllipse(dotBrush, 10, 10 + StatusRowHeight / 2f - 4, 8, 8);
                g.DrawString($"PLC：{_plcStatusText}", statusFont, statusBrush, 26, 6);
            }

            // ── 消息流（超长消息自动换行：RectangleF 区域绘制 + 按内容实测动态行高）──
            float y = StatusRowHeight + 6;
            float messageWidth = Math.Max(1, Width - MessageLeft - MessageRight);
            foreach (var item in _items)
            {
                int lines = CountWrappedLines(g, item.Message, msgFont, messageWidth);
                float rowHeight = Math.Max(ItemHeight, lines * msgFont.Height + LineGap);
                if (y + rowHeight > Height)
                {
                    break;
                }

                g.DrawString(item.TimeText, timeFont, timeBrush, 10, y);
                using var msgBrush = new SolidBrush(item.TextColor);
                g.DrawString(item.Message, msgFont, msgBrush,
                    new RectangleF(MessageLeft, y - 1, messageWidth, rowHeight + LineGap));
                y += rowHeight;
            }

            // 空态提示
            if (_items.Count == 0)
            {
                using var hintBrush = new SolidBrush(Color.FromArgb(170, 178, 189));
                using var hintFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
                g.DrawString("暂无消息", hintFont, hintBrush, 10, StatusRowHeight + 8);
            }
        }
    }
}