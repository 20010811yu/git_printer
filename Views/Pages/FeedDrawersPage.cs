using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Controls;

namespace UiTopMachine.Views.Pages
{
    /// <summary>
    /// 进料抽屉监控页（纯 View）：18 抽屉网格（6 列 × 3 行）+ 发送按钮 + 页面标题
    /// 布局与绑定逻辑自 MainForm 迁移，业务全部在 MainViewModel
    /// </summary>
    public class FeedDrawersPage : UserControl
    {
        private readonly MainViewModel _viewModel;

        /// <summary>每行抽屉数（强制 6 个）</summary>
        private const int Columns = 6;
        /// <summary>行数（强制 3 行）</summary>
        private const int Rows = 3;

        // ══════════════ 布局控件 ══════════════
        private AntdUI.Button _sendButton = null!;
        private Label _pageTitleLabel = null!;
        private TableLayoutPanel _drawersGrid = null!;
        private readonly List<DrawerCell> _cells = new();

        /// <summary>
        /// 抽屉单元格：状态灯（编号在圆外）+ 配方输入框（View 局部组合）
        /// </summary>
        private sealed class DrawerCell
        {
            public DrawerIndicatorControl Indicator = null!;
            public TextBox RecipeBox = null!;
            public bool IsBound;
        }

        /// <summary>
        /// 构造：注入 ViewModel，初始化 UI 与绑定
        /// </summary>
        public FeedDrawersPage(MainViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeUi();
            BindViewModel();
        }

        // ══════════════ UI 构建 ══════════════

        /// <summary>
        /// 构建页面布局（白色卡片：标题 + 发送按钮 + 18 抽屉网格）
        /// </summary>
        private void InitializeUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(244, 247, 250);
            Padding = new Padding(20, 8, 20, 16);

            // 白色卡片容器（浅描边）
            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            card.Resize += (_, _) => card.Invalidate();
            card.Paint += (s, e) =>
            {
                var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using var pen = new Pen(Color.FromArgb(224, 230, 238));
                e.Graphics.DrawRectangle(pen, rect);
            };

            // 发送按钮（右上角，Anchor 右侧随窗口自适应）
            _sendButton = new AntdUI.Button
            {
                Text = "发送",
                Type = AntdUI.TTypeMini.Primary,
                Size = new Size(112, 44),
                Radius = 8,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            // 页面标题（居中显示）
            _pageTitleLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 64,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66)
            };

            // 18 抽屉网格：TableLayoutPanel 强制 6 列 × 3 行，单元格百分比自适应
            _drawersGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = Columns,
                RowCount = Rows,
                BackColor = Color.White,
                Margin = new Padding(0)
            };
            for (int c = 0; c < Columns; c++)
            {
                _drawersGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / Columns));
            }
            for (int r = 0; r < Rows; r++)
            {
                _drawersGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / Rows));
            }
            for (int i = 1; i <= Columns * Rows; i++)
            {
                _drawersGrid.Controls.Add(CreateDrawerCell(i));
            }

            // 加入顺序决定 z 序：按钮最上层，其次标题，网格填充剩余
            card.Controls.Add(_sendButton);
            card.Controls.Add(_pageTitleLabel);
            card.Controls.Add(_drawersGrid);

            Controls.Add(card);

            // 布局完成后定位发送按钮（基于真实客户区宽度）
            PerformLayout();
            _sendButton.Location = new Point(card.ClientSize.Width - _sendButton.Width - 20, 12);
        }

        /// <summary>
        /// 创建单个抽屉单元格：内部 TableLayoutPanel 双行布局
        /// ┌──────────────┐
        /// │  状态灯（填充） │ ← 指示灯控件（编号左上角+自适应圆圈）
        /// ├──────────────┤
        /// │   配方输入框   │ ← 固定 46px 行，输入框水平居中
        /// └──────────────┘
        /// 随窗口缩放，输入框永远完整可见
        /// </summary>
        private Panel CreateDrawerCell(int index)
        {
            var cell = new DrawerCell();
            var container = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                // 顶部边距加大（8→26）：第一、二行抽屉整体下移，
                // 保证左上角编号与圆圈不被标题/上一行遮挡
                Margin = new Padding(10, 26, 10, 8)
            };

            // 单元格内部：双行 TableLayoutPanel（指示灯行 100% + 输入框行固定 46px）
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = Color.White,
                Margin = new Padding(0)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));   // 指示灯：占剩余全部
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));    // 输入框：固定高 46px

            // 状态灯：编号左上角，圆圈自适应完整显示
            cell.Indicator = new DrawerIndicatorControl
            {
                Index = index,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };

            // 配方输入框：Text 默认为空，固定行内水平居中（宽度 ≤ 行宽 - 16）
            cell.RecipeBox = new TextBox
            {
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(56, 70, 88),
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Center,
                Text = string.Empty,
                Size = new Size(120, 30),
                Anchor = AnchorStyles.None
            };

            layout.Controls.Add(cell.Indicator, 0, 0);
            layout.Controls.Add(cell.RecipeBox, 0, 1);
            container.Controls.Add(layout);

            // 输入框宽度跟随单元格伸缩（极窄时保底 48px），居中由 TLP Anchor.None 自动完成
            container.Resize += (_, _) =>
            {
                int w = Math.Max(48, Math.Min(220, layout.ClientSize.Width - 16));
                if (cell.RecipeBox.Width != w)
                {
                    cell.RecipeBox.Width = w;
                }
            };

            _cells.Add(cell);
            return container;
        }

        // ══════════════ 数据绑定 ══════════════

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM）
        /// </summary>
        private void BindViewModel()
        {
            // 页面标题绑定（VM → View 单向）
            _pageTitleLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(MainViewModel.PageTitle),
                false, DataSourceUpdateMode.Never);

            // 发送按钮绑定命令（点击转发 + Enabled 联动）
            CommandManagerHelper.Bind(_sendButton, _viewModel.SendCommand);

            // 抽屉集合加载后逐项绑定（集合在 InitializeAsync 填充）
            _viewModel.Drawers.CollectionChanged += OnDrawersCollectionChanged;
        }

        /// <summary>
        /// 抽屉集合变化：调度到 UI 线程执行绑定
        /// </summary>
        private void OnDrawersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(BindDrawerCells));
            }
            else
            {
                BindDrawerCells();
            }
        }

        /// <summary>
        /// 逐项绑定抽屉 VM ↔ 控件（按索引对齐，防重复绑定）
        /// </summary>
        private void BindDrawerCells()
        {
            for (int i = 0; i < _viewModel.Drawers.Count && i < _cells.Count; i++)
            {
                var vm = _viewModel.Drawers[i];
                var cell = _cells[i];

                if (cell.IsBound)
                {
                    continue;
                }

                cell.IsBound = true;

                // 状态灯：Status 枚举驱动颜色重绘（lightgray/lightgreen/yellow）
                cell.Indicator.DataBindings.Add(nameof(DrawerIndicatorControl.Status), vm,
                    nameof(DrawerItemViewModel.Status), false, DataSourceUpdateMode.Never);

                // 配方输入框：双向绑定（用户输入即时回写 VM，驱动状态灯变色）
                cell.RecipeBox.DataBindings.Add(nameof(TextBox.Text), vm,
                    nameof(DrawerItemViewModel.Recipe), true, DataSourceUpdateMode.OnPropertyChanged);

                // 编辑权限：无料（灰）只读、有料（黄/绿）可编辑，随 PLC 物料推送联动
                cell.RecipeBox.DataBindings.Add(nameof(TextBox.ReadOnly), vm,
                    nameof(DrawerItemViewModel.IsInputReadOnly), false, DataSourceUpdateMode.Never);

                // 配方填写完成（焦点离开）→ 刷新分组次序（重复写入同一配方也按最后一次写入计序，仿参考 textBox_Leave）
                cell.RecipeBox.Leave += (_, _) => _viewModel.RefreshRecipeSequence(vm.Index);
            }
        }
    }
}