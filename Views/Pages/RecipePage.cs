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
    /// 配方管理页（纯 View）：标题区 + 工具栏（刷新/保存/新增行/新增列/删除行/删除列/新建配方/打开文件夹）
    /// + AntdUI Table 展示与双击单元格编辑（编辑提交经 VM 校验，本页零业务逻辑）。
    /// 删除行/列经 VM 的确认请求事件弹确认框，用户确认后才执行删除
    /// </summary>
    public class RecipePage : UserControl
    {
        // ══════════════ 依赖（ViewModel 注入） ══════════════
        private readonly RecipePageViewModel _viewModel;

        /// <summary>
        /// 当前焦点单元格索引（AntdUI Table CellFocused 事件维护，供删除命令动态取参；
        /// -1 = 未选中。重建表格后焦点丢失需重置）
        /// </summary>
        private int _focusedRowIndex = -1;
        private int _focusedColumnIndex = -1;

        // ══════════════ 布局控件 ══════════════
        private Label _titleLabel = null!;
        private Label _descriptionLabel = null!;
        private FlowLayoutPanel _toolbar = null!;
        private AntdUI.Button _refreshButton = null!;
        private AntdUI.Button _saveButton = null!;
        private AntdUI.Button _addRowButton = null!;
        private AntdUI.Button _addColumnButton = null!;
        private AntdUI.Button _deleteRowButton = null!;
        private AntdUI.Button _deleteColumnButton = null!;
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

            // 工具栏：8 按钮单行排列（刷新/保存/新增行/新增列/删除行/删除列/新建配方/打开文件夹）
            _toolbar = new FlowLayoutPanel
            {
                AutoSize = true,
                WrapContents = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.White
            };
            _refreshButton = CreateToolButton("刷新", AntdUI.TTypeMini.Primary);
            _saveButton = CreateToolButton("保存", AntdUI.TTypeMini.Primary);
            // 增/删按钮用语义色区分（绿=新增、红=删除），避免 Default 灰白样式被误认为禁用
            _addRowButton = CreateToolButton("新增行", AntdUI.TTypeMini.Success);
            _addColumnButton = CreateToolButton("新增列", AntdUI.TTypeMini.Success);
            _deleteRowButton = CreateToolButton("删除行", AntdUI.TTypeMini.Error);
            _deleteColumnButton = CreateToolButton("删除列", AntdUI.TTypeMini.Error);
            _createBlankButton = CreateToolButton("新建配方", AntdUI.TTypeMini.Warn);
            _openFolderButton = CreateToolButton("打开文件夹", AntdUI.TTypeMini.Default);
            _toolbar.Controls.AddRange(new Control[]
            {
                _refreshButton, _saveButton, _addRowButton, _addColumnButton,
                _deleteRowButton, _deleteColumnButton, _createBlankButton, _openFolderButton
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
                EditLostFocus = true,
                // 行高调整（v1.7b）：数据行与表头行更舒展，页面显示和谐
                RowHeight = 36,
                RowHeightHeader = 40
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

            // 删除按钮：动态参数提供器实时取焦点单元格索引（点击/刷新时调用，详 ERR-004 教训）
            CommandManagerHelper.Bind(_deleteRowButton, _viewModel.DeleteRowCommand,
                () => _focusedRowIndex);
            CommandManagerHelper.Bind(_deleteColumnButton, _viewModel.DeleteColumnCommand,
                () => _focusedColumnIndex);

            CommandManagerHelper.Bind(_createBlankButton, _viewModel.CreateBlankCommand);
            CommandManagerHelper.Bind(_openFolderButton, _viewModel.OpenFolderCommand);

            // VM 请求列名输入 → 弹出输入弹框（纯 UI 转发：弹框展示与输入收集，
            // 结果回填事件参数交由 VM 校验与处理，本页零业务逻辑）
            _viewModel.ColumnNamingRequested += (_, request) => ShowInputDialog(request);

            // VM 请求用户确认（删除行/列、新建配方备份轮转等）→ 弹出确认弹框
            //（纯 UI 转发：确认框展示与用户选择回填，业务执行与校验全部在 VM）
            _viewModel.ConfirmationRequested += (_, request) => ShowConfirmDialog(request);

            // VM 请求消息提示（编号重复拒绝/保存校验失败等）→ 弹出消息框
            //（纯 UI 转发；VM 事件可能在后台自动保存线程触发，经 BeginInvoke 封送到 UI 线程）
            _viewModel.MessageRequested += (_, request) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => ShowMessage(request)));
                }
                else
                {
                    ShowMessage(request);
                }
            };

            // VM 数据/结构变化 → 重建 AntdUI Table（新增行/列/删除行/列/加载/新建配方均触发）
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(RecipePageViewModel.RecipeTable) ||
                    e.PropertyName == nameof(RecipePageViewModel.TableVersion))
                {
                    BindTable(_viewModel.RecipeTable);
                }
            };

            // 首次布局/尺寸变化 → 依表格可见高度重算期望行数（v1.7b：页面始终填满）
            _recipeTable.Resize += (_, _) => EnsureFillRows();

            // 焦点单元格变化 → 记录索引并刷新删除命令可用态（无选中行/列时删除按钮禁用）。
            // ⚠️ 实测 CellFocused 在鼠标单击时不触发（偏向键盘焦点导航），
            // 因此 CellClick（鼠标单击，含 RowIndex/ColumnIndex）与 CellFocused 双订阅保证鼠标/键盘都能跟踪
            // ⚠️ 二次实证（ERR-017 真根因）：两事件的 RowIndex 均为【含表头的 1 基内部 INDEX】
            //（运行时实证：内部 rows[0]=表头 INDEX=0，首条数据行 INDEX=1，事件值与之一致），
            // 而 ColumnIndex 为 0 基——行必须减 1 换算为 DataTable 0 基索引，列原样使用。
            // 未换算会导致删除行/列定位到相邻行（点击首行实际删除第 2 行）
            void UpdateFocus(int rowIndex, int columnIndex)
            {
                // 行：1 基 INDEX → 0 基数据行（INDEX<1 即表头/空白区，视为取消选中，删除按钮回到禁用）
                _focusedRowIndex = rowIndex >= 1 ? rowIndex - 1 : -1;
                // 列：0 基，点击表头（<0）视为取消选中
                _focusedColumnIndex = columnIndex >= 0 ? columnIndex : -1;
                _viewModel.DeleteRowCommand.RaiseCanExecuteChanged();
                _viewModel.DeleteColumnCommand.RaiseCanExecuteChanged();
            }
            _recipeTable.CellClick += (_, e) => UpdateFocus(e.RowIndex, e.ColumnIndex);
            _recipeTable.CellFocused += (_, e) => UpdateFocus(e.RowIndex, e.ColumnIndex);

            // 单元格编辑完成 → 转发 VM 校验提交（业务逻辑全部在 VM，View 仅转发）；
            // ⚠️ 二次实证（ERR-017 真根因）：CellEndEdit 的 RowIndex 是【含表头的 1 基内部 INDEX】
            //（首条数据行 = 1），直接传 VM 会把值写到 DataTable 的下一行——用户症状
            //「修改内容跑到下一行同列」、编辑末行时索引越界静默失败、查重读错行被
            //「未变化」分支跳过。必须减 1 换算为 0 基数据行索引；ColumnIndex 为 0 基原样传递。
            // 恒返回 false 阻止 AntdUI 内部落值，VM 提交成功后经 TableVersion++ 重建表格
            // 同步显示（VM = 唯一事实源）；VM 返回 false（编号重复/越界）时同样由重建还原显示
            _recipeTable.CellEndEdit += (s, e) =>
            {
                var newValue = e.Value?.ToString() ?? string.Empty;
                _viewModel.TryCommitCellEdit(e.RowIndex - 1, e.ColumnIndex, newValue);
                return false; // 恒 false：阻止 AntdUI 内部落值，显示统一走 TableVersion 重建
            };

            // 初始绑定（空表占位，加载完成后自动刷新）
            BindTable(_viewModel.RecipeTable);
        }

        /// <summary>
        /// 依表格可见高度计算期望的最小总行数并请求 VM 补空白行（v1.7b）：
        /// 行数 = 可用高度 / 行高（扣表头 40px；留 1 行余量防滚动条闪烁）。
        /// VM 不足时在末尾补真实可编辑空白行（加载/新建/删除行后均会触发本方法链）
        /// </summary>
        private void EnsureFillRows()
        {
            if (_recipeTable.Height <= 40 || _viewModel.RecipeTable.Columns.Count == 0)
            {
                return; // 未完成布局或无列结构
            }

            int minRows = Math.Max(1, (_recipeTable.Height - 40) / 36);
            _viewModel.EnsureMinRows(minRows);
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
        /// 弹出确认弹框（模态）：展示标题/提示内容，回填用户选择；
        /// 确定 → Confirmed=true；取消/关闭 → Confirmed=false（由 VM 判定后续流程）
        /// </summary>
        /// <param name="request">确认请求参数（VM 创建，本方法仅回填结果）</param>
        private void ShowConfirmDialog(ConfirmRequestEventArgs request)
        {
            using var dialog = new ConfirmDialog(request.Title, request.Message);
            request.Confirmed = dialog.ShowDialog(FindForm()) == DialogResult.OK;
        }

        /// <summary>
        /// 弹出消息提示框（模态）：展示校验失败等重要提示，纯单向通知无回填
        ///（编号重复拒绝/保存校验失败等用户必须立即感知的场景）
        /// </summary>
        /// <param name="request">消息请求参数（VM 创建）</param>
        private void ShowMessage(MessageRequestEventArgs request)
        {
            MessageBox.Show(FindForm(), request.Message, request.Title,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>
        /// 将 DataTable 转换为 AntdUI Table 数据并绑定（UI 数据适配：依列结构重建列，再逐行装载）
        /// </summary>
        private void BindTable(DataTable data)
        {
            // 重建表格后焦点索引钳制到有效范围（删除行/列后焦点自动落到相邻行/列，
            // 连续删除无需重新点选；初始 -1 经 Math.Min 保持 -1，加载新表时若旧焦点越界自动钳制）
            _focusedRowIndex = data.Rows.Count > 0
                ? Math.Min(_focusedRowIndex, data.Rows.Count - 1) : -1;
            _focusedColumnIndex = data.Columns.Count > 0
                ? Math.Min(_focusedColumnIndex, data.Columns.Count - 1) : -1;
            _viewModel.DeleteRowCommand.RaiseCanExecuteChanged();
            _viewModel.DeleteColumnCommand.RaiseCanExecuteChanged();

            // 恢复行选中高亮（AntdUI SelectedIndex 可写；-1 = 不选中）
            // ⚠️ 二次实证（ERR-017）：SelectedIndex 与行事件同为 1 基内部 INDEX
            //（0=表头），_focusedRowIndex 为 0 基 → 恢复高亮需 +1，否则高亮偏上一行
            _recipeTable.SelectedIndex = _focusedRowIndex >= 0 ? _focusedRowIndex + 1 : -1;

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