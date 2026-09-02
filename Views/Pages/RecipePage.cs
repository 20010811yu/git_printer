using System;
using System.Data;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Common;
using UiTopMachine.Common.Commands;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Dialogs;

namespace UiTopMachine.Views.Pages
{
    /// <summary>
    /// 配方管理页（纯 View）：标题区 + 工具栏（刷新/保存/新增行/新增列/新建配方/打开文件夹）
    /// + AntdUI Table 展示与双击单元格编辑（编辑提交经 VM 校验，本页零业务逻辑）
    /// </summary>
    public class RecipePage : UserControl
    {
        // ══════════════ 依赖（ViewModel 注入） ══════════════
        private readonly RecipePageViewModel _viewModel;

        // ══════════════ 布局控件 ══════════════
        private Label _titleLabel = null!;
        private Label _descriptionLabel = null!;
        private FlowLayoutPanel _toolbar = null!;
        private AntdUI.Button _refreshButton = null!;
        private AntdUI.Button _saveButton = null!;
        private AntdUI.Button _addRowButton = null!;
        private AntdUI.Button _addColumnButton = null!;
        private AntdUI.Button _createBlankButton = null!;
        private AntdUI.Button _openFolderButton = null!;
        private AntdUI.Table _recipeTable = null!;

        /// <summary>
        /// 构造：注入 ViewModel，初始化 UI 与绑定
        /// </summary>
        public RecipePage(RecipePageViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeUi();
            BindViewModel();

            // 首次进入页面时触发数据加载（View 仅转发调用，业务仍在 VM）
            Load += async (_, _) => await _viewModel.InitializeAsync();
        }

        // ══════════════ UI 构建 ══════════════

        /// <summary>
        /// 构建页面布局：卡片 = 头部（标题/说明/工具栏）+ AntdUI Table（铺满剩余）
        /// </summary>
        private void InitializeUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(244, 247, 250);
            Padding = new Padding(20, 8, 20, 16);

            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            card.Paint += (s, e) =>
            {
                var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using var pen = new Pen(Color.FromArgb(224, 230, 238));
                e.Graphics.DrawRectangle(pen, rect);
            };

            // ── 头部：标题 + 数据源说明 + 工具栏按钮组 ──
            var header = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.White };

            _titleLabel = new Label
            {
                Location = new Point(24, 14),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66)
            };

            _descriptionLabel = new Label
            {
                Location = new Point(26, 68),
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(120, 132, 148)
            };

            // 工具栏：6 按钮单行排列（刷新/保存/新增行/新增列/新建配方/打开文件夹）
            _toolbar = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.White
            };
            _refreshButton = CreateToolButton("刷新", AntdUI.TTypeMini.Primary);
            _saveButton = CreateToolButton("保存", AntdUI.TTypeMini.Primary);
            _addRowButton = CreateToolButton("新增行", AntdUI.TTypeMini.Default);
            _addColumnButton = CreateToolButton("新增列", AntdUI.TTypeMini.Default);
            _createBlankButton = CreateToolButton("新建配方", AntdUI.TTypeMini.Warn);
            _openFolderButton = CreateToolButton("打开文件夹", AntdUI.TTypeMini.Default);
            _toolbar.Controls.AddRange(new Control[]
            {
                _refreshButton, _saveButton, _addRowButton,
                _addColumnButton, _createBlankButton, _openFolderButton
            });

            header.Controls.AddRange(new Control[] { _titleLabel, _descriptionLabel, _toolbar });

            // 头部尺寸变化时重定位工具栏（保持右侧留白 24px、垂直居中）
            header.Resize += (_, _) =>
            {
                _toolbar.Location = new Point(
                    Math.Max(24, header.Width - _toolbar.Width - 24),
                    (header.Height - _toolbar.Height) / 2);
            };

            // ── AntdUI Table：配方数据展示 + 双击单元格编辑区 ──
            _recipeTable = new AntdUI.Table
            {
                Dock = DockStyle.Fill,
                Bordered = true,
                Location = new Point(0, 112),
                // 双击进入编辑（Click 模式易误触；None 为只读）
                EditMode = AntdUI.TEditMode.DoubleClick,
                // 失焦自动提交编辑（工业操作习惯：点击别处即确认）
                EditLostFocus = true
            };

            // 组装（WinForms Dock 布局：Fill 先加占剩余，Top 后加停靠顶部）
            card.Controls.Add(_recipeTable);
            card.Controls.Add(header);
            Controls.Add(card);
        }

        /// <summary>
        /// 创建工具栏按钮（统一样式）
        /// </summary>
        private static AntdUI.Button CreateToolButton(string text, AntdUI.TTypeMini type)
            => new()
            {
                Text = text,
                Type = type,
                Size = new Size(Math.Max(84, 24 + text.Length * 18), 42),
                Radius = 8,
                Margin = new Padding(4, 0, 4, 0)
            };

        // ══════════════ 数据绑定 ══════════════

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM：展示绑定 + 命令绑定 + 编辑事件转发）
        /// </summary>
        private void BindViewModel()
        {
            _titleLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(RecipePageViewModel.Title),
                false, DataSourceUpdateMode.Never);
            _descriptionLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(RecipePageViewModel.Description),
                false, DataSourceUpdateMode.Never);

            // 命令绑定（命令状态自动驱动按钮 Enabled）
            CommandManagerHelper.Bind(_refreshButton, _viewModel.LoadCommand);
            CommandManagerHelper.Bind(_saveButton, _viewModel.SaveCommand);
            CommandManagerHelper.Bind(_addRowButton, _viewModel.AddRowCommand);
            CommandManagerHelper.Bind(_addColumnButton, _viewModel.AddColumnCommand);
            CommandManagerHelper.Bind(_createBlankButton, _viewModel.CreateBlankCommand);
            CommandManagerHelper.Bind(_openFolderButton, _viewModel.OpenFolderCommand);

            // VM 请求列名输入 → 弹出输入弹框（纯 UI 转发：弹框展示与输入收集，
            // 结果回填事件参数交由 VM 校验与处理，本页零业务逻辑）
            _viewModel.ColumnNamingRequested += (_, request) => ShowInputDialog(request);

            // VM 数据/结构变化 → 重建 AntdUI Table（新增行/列/加载/新建配方均触发）
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RecipePageViewModel.RecipeTable) ||
                    e.PropertyName == nameof(RecipePageViewModel.TableVersion))
                {
                    BindTable(_viewModel.RecipeTable);
                }
            };

            // 单元格编辑完成 → 转发 VM 校验提交（业务逻辑全部在 VM，View 仅转发）；
            // 返回 false 时 AntdUI 自动还原单元格原值（编号重复被拒绝后 UI 同步回退）
            _recipeTable.CellEndEdit += (s, e) =>
            {
                var newValue = e.Value?.ToString() ?? string.Empty;
                return _viewModel.TryCommitCellEdit(e.RowIndex, e.ColumnIndex, newValue);
            };

            // 初始绑定（空表占位，加载完成后自动刷新）
            BindTable(_viewModel.RecipeTable);
        }

        /// <summary>
        /// 弹出输入弹框（模态）：展示标题/说明，收集用户输入并回填事件参数；
        /// 确定 → Confirmed=true + InputText；取消/关闭 → Confirmed=false（由 VM 判定后续流程）
        /// </summary>
        /// <param name="request">输入请求参数（VM 创建，本方法仅回填结果）</param>
        private void ShowInputDialog(InputRequestEventArgs request)
        {
            using var dialog = new InputDialog(request.Title, request.Prompt);
            request.Confirmed = dialog.ShowDialog(FindForm()) == DialogResult.OK;
            request.InputText = dialog.InputText;
        }

        /// <summary>
        /// 将 DataTable 转换为 AntdUI Table 数据并绑定（UI 数据适配：依列结构重建列，再逐行装载）
        /// </summary>
        private void BindTable(DataTable data)
        {
            // 依 Excel 表头重建列（key 与标题同名）
            _recipeTable.Columns.Clear();
            foreach (DataColumn col in data.Columns)
            {
                _recipeTable.Columns.Add(new AntdUI.Column(col.ColumnName, col.ColumnName));
            }

            // 逐行转换为 AntItem[]（key = 列字段名，value = 单元格文本）
            var rows = new AntdUI.AntList<AntdUI.AntItem[]>();
            foreach (DataRow row in data.Rows)
            {
                var items = new AntdUI.AntItem[data.Columns.Count];
                for (int i = 0; i < data.Columns.Count; i++)
                {
                    items[i] = new AntdUI.AntItem(data.Columns[i].ColumnName,
                        row[i]?.ToString() ?? string.Empty);
                }
                rows.Add(items);
            }
            _recipeTable.Binding(rows);
        }
    }
}