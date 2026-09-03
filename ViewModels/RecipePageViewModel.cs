using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Common;
using UiTopMachine.Common.Commands;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 配方管理页视图模型：Excel 配方的展示、单元格编辑（编号唯一校验）、
    /// 新增行/列、删除行/列（删除前经确认弹框）、新建空白配方（备份轮转：原文件改名+时间戳，新配方沿用原文件名）、
    /// 行序整理（数据连续、空白垫底）、自动补空白行、保存、打开文件夹
    /// ViewModel 不接触 ClosedXML 对象，仅持有 Service 返回的 DataTable 数据源
    /// </summary>
    public class RecipePageViewModel : ObservableObject
    {
        // ══════════════ 依赖 ══════════════
        private readonly IRecipeFileService _recipeFileService;
        private readonly ILogService _logService;

        /// <summary>
        /// 保存互斥锁：自动保存与手动保存可能并发触发，
        /// 用信号量串行化写文件操作，避免文件锁冲突（IOException）
        /// </summary>
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        // ══════════════ 属性 ══════════════
        private DataTable _recipeTable = new DataTable("配方");
        private bool _isLoading;
        private bool _isSaving;
        private int _tableVersion;

        /// <summary>页面标题</summary>
        public string Title => "配方管理";

        /// <summary>
        /// 页面说明（动态展示当前配方文件路径，新建配方后自动更新）
        /// </summary>
        public string Description => $"当前配方：{_recipeFileService.FilePath}（双击单元格可编辑）";

        /// <summary>
        /// 配方表格数据源（对应 Excel 第一张工作表，列结构随文件动态变化）
        /// </summary>
        public DataTable RecipeTable
        {
            get => _recipeTable;
            private set
            {
                if (SetProperty(ref _recipeTable, value))
                {
                    // 表格结构变化（加载/新建）：依赖行列数的命令全部刷新可用态
                    //（详 ERR-013：漏刷 AddRowCommand 导致按钮永久禁用）
                    RefreshAllCommandStates();
                }
            }
        }

        /// <summary>
        /// 表格版本号：每次数据结构变化（新增行/列、新建配方）时自增，
        /// View 订阅此属性重建表格 UI（AntdUI Table 绑定后新增行不会自动出现，需重建）
        /// </summary>
        public int TableVersion
        {
            get => _tableVersion;
            private set => SetProperty(ref _tableVersion, value);
        }

        /// <summary>是否正在加载（加载中禁用刷新/新建命令，防重复触发）</summary>
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    // 加载状态变化：所有命令的 CanExecute 都依赖 !IsLoading，统一刷新
                    //（详 ERR-013：漏刷导致依赖命令状态不同步）
                    RefreshAllCommandStates();
                }
            }
        }

        /// <summary>是否正在保存（保存中禁用保存命令，防重复触发）</summary>
        public bool IsSaving
        {
            get => _isSaving;
            private set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    RefreshAllCommandStates();
                }
            }
        }

        // ══════════════ 编号列识别 ══════════════

        /// <summary>
        /// 编号列候选表头名（匹配时 Trim + 忽略大小写）：
        /// ⚠️ 用户真实配方表表头为「编号」（新建空白配方默认表头为「配方编号」），
        /// 硬编码单一列名会令唯一性校验在真实文件上全部静默失效——用户反馈
        /// 「编号相同仍保存写盘」的根因（详 activeContext v1.6）
        /// </summary>
        private static readonly string[] RecipeIdColumnCandidates = { "配方编号", "编号" };

        // ══════════════ 事件 ══════════════

        /// <summary>
        /// 列名输入请求事件：VM 需要用户输入列名时触发（参数为纯数据，不接触任何 UI 控件），
        /// View 订阅此事件弹出输入弹框，并把用户选择（是否确认 + 输入内容）回填到事件参数
        /// </summary>
        public event EventHandler<InputRequestEventArgs>? ColumnNamingRequested;

        /// <summary>
        /// 通用确认请求事件：VM 执行删除行/列、新建配方（当前配方将备份轮转）等
        /// 危险/重要操作前向 View 请求用户确认（参数为纯数据，不接触任何 UI 控件），
        /// View 订阅此事件弹出确认弹框，并把用户选择（是否确认）回填到事件参数
        /// </summary>
        public event EventHandler<ConfirmRequestEventArgs>? ConfirmationRequested;

        /// <summary>
        /// 消息提示请求事件：VM 需要向用户展示校验失败等重要提示时触发（参数为纯数据，不接触 UI 控件），
        /// View 订阅后弹消息框展示（编号重复拒绝、保存校验失败等用户必须立即感知的场景）
        /// </summary>
        public event EventHandler<MessageRequestEventArgs>? MessageRequested;

        // ══════════════ 命令 ══════════════

        /// <summary>刷新命令：重新从当前 Excel 文件加载数据（异步，IsLoading 防重复）</summary>
        public AsyncRelayCommand LoadCommand { get; }

        /// <summary>保存命令：将当前表格写入 Excel 文件（异步，IsSaving 防重复；保存前做编号唯一校验）</summary>
        public AsyncRelayCommand SaveCommand { get; }

        /// <summary>新增行命令：表格末尾追加一行（自动生成唯一配方编号）</summary>
        public RelayCommand AddRowCommand { get; }

        /// <summary>新增列命令：弹出列名输入弹框，列名校验通过后表格末尾追加一列</summary>
        public RelayCommand AddColumnCommand { get; }

        /// <summary>
        /// 删除行命令：确认弹框通过后删除指定行（参数 = 行索引，由 View 依表格焦点实时提供；
        /// 无选中行时命令禁用）
        /// </summary>
        public RelayCommand DeleteRowCommand { get; }

        /// <summary>
        /// 删除列命令：确认弹框通过后删除指定列（参数 = 列索引，由 View 依表格焦点实时提供；
        /// 无选中列时命令禁用）
        /// </summary>
        public RelayCommand DeleteColumnCommand { get; }

        /// <summary>
        /// 新建空白配方命令：用户确认后原配方自动备份（原文件名+时间戳），
        /// 新空白配方沿用原文件名（同表头 + 空白行），页面立即显示新配方
        /// </summary>
        public AsyncRelayCommand CreateBlankCommand { get; }

        /// <summary>打开文件夹命令：资源管理器定位配方所在目录</summary>
        public RelayCommand OpenFolderCommand { get; }

        // ══════════════ 私有辅助 ══════════════

        /// <summary>
        /// 统一刷新全部命令的 CanExecuteChanged（CommandManagerHelper 据此同步按钮 Enabled）：
        /// 任何影响命令可用性的状态（RecipeTable/IsLoading/IsSaving）变化后必须调用，
        /// 避免逐个列举遗漏（详 ERR-013：AddRowCommand 漏刷导致按钮永久禁用）
        /// </summary>
        private void RefreshAllCommandStates()
        {
            LoadCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            AddRowCommand.RaiseCanExecuteChanged();
            AddColumnCommand.RaiseCanExecuteChanged();
            DeleteRowCommand.RaiseCanExecuteChanged();
            DeleteColumnCommand.RaiseCanExecuteChanged();
            CreateBlankCommand.RaiseCanExecuteChanged();
            OpenFolderCommand.RaiseCanExecuteChanged();
        }

        /// <summary>
        /// 定位编号列索引（候选表头 Trim + 忽略大小写匹配；未找到返回 -1，
        /// 此时编号唯一性校验不启用并已在加载/新增行时记 Warn 日志明确告知）
        /// </summary>
        private int FindRecipeIdColumnIndex()
        {
            foreach (DataColumn col in RecipeTable.Columns)
            {
                var name = (col.ColumnName ?? string.Empty).Trim();
                foreach (var candidate in RecipeIdColumnCandidates)
                {
                    if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return col.Ordinal;
                    }
                }
            }
            return -1;
        }

        /// <summary>向 View 发起消息提示请求（弹窗由 View 展示，VM 不接触 UI 控件）</summary>
        private void RaiseMessage(string title, string message)
            => MessageRequested?.Invoke(this, new MessageRequestEventArgs { Title = title, Message = message });

        // ══════════════ 构造 ══════════════

        /// <summary>
        /// 构造：注入配方文件服务与日志服务（依赖由 DI 提供）
        /// </summary>
        public RecipePageViewModel(IRecipeFileService recipeFileService, ILogService logService)
        {
            _recipeFileService = recipeFileService ?? throw new ArgumentNullException(nameof(recipeFileService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            LoadCommand = new AsyncRelayCommand(
                _ => LoadRecipeAsync(),
                _ => !IsLoading);

            SaveCommand = new AsyncRelayCommand(
                _ => SaveRecipeAsync(),
                _ => !IsSaving);

            AddRowCommand = new RelayCommand(
                _ => AddRow(),
                _ => !IsLoading && RecipeTable.Columns.Count > 0);

            AddColumnCommand = new RelayCommand(
                _ => AddColumn(),
                _ => !IsLoading);

            DeleteRowCommand = new RelayCommand(
                p => DeleteRow(p),
                p => !IsLoading && p is int row && row >= 0 && row < RecipeTable.Rows.Count);

            DeleteColumnCommand = new RelayCommand(
                p => DeleteColumn(p),
                p => !IsLoading && p is int col && col >= 0 && col < RecipeTable.Columns.Count);

            CreateBlankCommand = new AsyncRelayCommand(
                _ => CreateBlankRecipeAsync(),
                _ => !IsLoading);

            OpenFolderCommand = new RelayCommand(
                _ => OpenFolder());
        }

        // ══════════════ 业务方法 ══════════════

        /// <summary>
        /// 页面初始化：首次进入配方页时触发数据加载（View 仅转发调用，业务仍在 VM）
        /// </summary>
        public async Task InitializeAsync() => await LoadRecipeAsync();

        /// <summary>
        /// 从 Excel 文件加载配方数据（异步不阻塞 UI；Result 统一返回，异常分层处理）
        /// </summary>
        private async Task LoadRecipeAsync()
        {
            IsLoading = true;
            try
            {
                _logService.Info($"正在加载配方文件：{_recipeFileService.FilePath}");
                var result = await _recipeFileService.LoadAsync();

                if (result.Success && result.Data is not null)
                {
                    RecipeTable = result.Data;
                    TableVersion++; // 通知 View 重建表格
                    OnPropertyChanged(nameof(Description));
                    CompactRows(); // 行序整理：外部文件中间空行也连续排列（v1.7b）
                    _logService.Success($"配方加载完成：{result.Data.Rows.Count} 行 / {result.Data.Columns.Count} 列");
                }
                else
                {
                    _logService.Error(result.ErrorMessage ?? "配方加载失败");
                }
            }
            catch (Exception ex)
            {
                _logService.Error($"加载配方异常：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 保存当前表格到 Excel 文件（保存前做配方编号唯一校验，校验失败不落盘）
        /// </summary>
        private async Task SaveRecipeAsync()
        {
            IsSaving = true;
            try
            {
                await SaveCoreAsync(successMessage: $"配方已保存：{{0}}", userInitiated: true);
            }
            catch (Exception ex)
            {
                _logService.Error($"保存配方异常：{ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>
        /// 单元格编辑提交（View 的 CellEndEdit 事件转发至此，业务逻辑全部在 VM）：
        /// 写回 DataTable；若编辑的是"配方编号"列，校验唯一性，重复则拒绝并还原原值
        /// </summary>
        /// <param name="rowIndex">行索引</param>
        /// <param name="columnIndex">列索引</param>
        /// <param name="newValue">用户新输入的值</param>
        /// <returns>true=提交成功；false=校验失败（调用方应还原 UI 显示）</returns>
        public bool TryCommitCellEdit(int rowIndex, int columnIndex, string newValue)
        {
            try
            {
                if (RecipeTable is null || rowIndex < 0 || rowIndex >= RecipeTable.Rows.Count
                    || columnIndex < 0 || columnIndex >= RecipeTable.Columns.Count)
                {
                    return false; // 索引越界（如编辑空表占位行），静默忽略
                }

                var oldValue = RecipeTable.Rows[rowIndex][columnIndex]?.ToString() ?? string.Empty;
                // 提交前 Trim 规范化：与 LoadCoreAsync 重载时的 Trim 口径保持一致，
                // 避免内存值带首尾空格而重载后被裁剪造成数据漂移（详 ERR-016）
                var text = (newValue ?? string.Empty).Trim();

                // 未变化：跳过写盘
                if (string.Equals(oldValue, text, StringComparison.Ordinal))
                {
                    return true;
                }

                var columnName = RecipeTable.Columns[columnIndex].ColumnName;

                // 编号列唯一性校验（候选表头识别；校验值已 Trim，大小写敏感按工业惯例保持原样比较）
                if (columnIndex == FindRecipeIdColumnIndex() && IsDuplicateRecipeId(text, excludeRowIndex: rowIndex))
                {
                    _logService.Error($"编号「{text}」已存在，修改被拒绝（编号必须唯一）");
                    // 弹窗提示 + TableVersion++ 强制重建表格：拒绝场景同样以数据源真值刷新 UI
                    //（VM 唯一事实源原则覆盖拒绝场景，杜绝 AntdUI 编辑残留显示假象）
                    RaiseMessage("编号重复", $"编号「{text}」已存在，修改失败（编号必须唯一）");
                    TableVersion++;
                    return false;
                }

                // 写回数据源
                RecipeTable.Rows[rowIndex][columnIndex] = text;
                _logService.Info($"单元格已修改 [{rowIndex + 1}行/{columnName}]：{oldValue} → {text}");

                // 通知 View 重建表格：AntdUI 内部提交与事件 RowIndex 错位（ERR-017），
                // View 已改为返回 false 阻止其内部写入，UI 显示必须由本表数据重建（VM = 唯一事实源）
                TableVersion++;

                // 清空中间行会留下空行 → 行序整理：数据连续、空白垫底（v1.7b）
                CompactRows();

                // 修改后自动后台保存（不阻塞 UI；失败仅记日志，用户可手动保存重试）
                _ = AutoSaveAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logService.Error($"单元格修改异常：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 新增行：末尾追加空行，"配方编号"列自动生成唯一编号（R001、R002…跳过已占用号）
        /// </summary>
        private void AddRow()
        {
            try
            {
                if (RecipeTable.Columns.Count == 0)
                {
                    _logService.Warn("表格无列结构，无法新增行");
                    return;
                }

                var row = RecipeTable.NewRow();

                // 自动生成唯一配方编号（列存在时）；无编号列则提示新行为全空行
                //（空行依赖 Service 层空格占位落盘，否则刷新后该行会消失，详 ERR-014）
                var idCol = FindRecipeIdColumnIndex();
                if (idCol >= 0)
                {
                    row[idCol] = GenerateUniqueRecipeId();
                }
                else
                {
                    _logService.Warn("未识别到编号列（候选表头：配方编号/编号），新增行暂为空白行，请双击单元格录入数据");
                }

                RecipeTable.Rows.Add(row);
                TableVersion++; // 通知 View 重建表格以显示新行
                _logService.Success($"已新增第 {RecipeTable.Rows.Count} 行");

                _ = AutoSaveAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"新增行异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 新增列：向 View 发起列名输入请求（弹框由 View 弹出，业务校验全部在 VM）——
        /// 用户取消 → 静默放弃；列名为空 → 新增列失败（记错误日志）；
        /// 列名重复 → 拒绝；校验通过 → 末尾追加列并自动保存
        /// </summary>
        private void AddColumn()
        {
            try
            {
                // ① 向 View 发起列名输入请求（View 弹模态框后同步回填结果，流程在此等待）
                var request = new InputRequestEventArgs
                {
                    Title = "新增列",
                    Prompt = "请输入新列的列名："
                };
                ColumnNamingRequested?.Invoke(this, request);

                // ② 用户取消/关闭弹框：放弃本次新增（非失败场景，静默返回）
                if (!request.Confirmed)
                {
                    return;
                }

                // ③ 校验一：列名为空（含纯空白）→ 新增列失败
                var columnName = request.InputText?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(columnName))
                {
                    _logService.Error("新增列失败：列名不能为空，请重新操作并输入列名");
                    return;
                }

                // ④ 校验二：列名与现有列重复 → 拒绝（DataTable 不允许重复列名）
                if (RecipeTable.Columns.Contains(columnName))
                {
                    _logService.Error($"新增列失败：列名「{columnName}」已存在（列名不可重复）");
                    return;
                }

                // ⑤ 校验通过：末尾追加列并通知 View 重建表格显示新列
                RecipeTable.Columns.Add(columnName);
                TableVersion++;
                _logService.Success($"已新增列「{columnName}」");

                _ = AutoSaveAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"新增列异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 删除行：向 View 发起确认请求（弹框由 View 弹出，业务执行全部在 VM）——
        /// 用户取消 → 静默放弃；确认 → 移除指定行 + 行序整理 + 通知 View 重建表格 + 自动保存。
        /// 索引无效（未选中行）时记录警告日志
        /// </summary>
        /// <param name="parameter">行索引（View 动态参数提供器传入的 int）</param>
        private void DeleteRow(object? parameter)
        {
            try
            {
                // ① 校验行索引有效性（越界/未选中直接拒绝，不弹框）
                if (parameter is not int rowIndex || rowIndex < 0 || rowIndex >= RecipeTable.Rows.Count)
                {
                    _logService.Warn("删除行失败：请先在表格中点击选中要删除的行");
                    return;
                }

                // ② 组装确认提示（带行号与配方编号，便于用户核对目标行）
                var idCol = FindRecipeIdColumnIndex();
                var recipeId = idCol >= 0 ? RecipeTable.Rows[rowIndex][idCol]?.ToString() : null;
                var rowDesc = string.IsNullOrWhiteSpace(recipeId)
                    ? $"第 {rowIndex + 1} 行"
                    : $"第 {rowIndex + 1} 行（配方编号：{recipeId}）";

                // ③ 向 View 发起删除确认请求（View 弹模态框后同步回填结果）
                var request = new ConfirmRequestEventArgs
                {
                    Title = "删除行",
                    Message = $"确定删除{rowDesc}吗？\r\n删除后该行所有数据不可恢复。"
                };
                ConfirmationRequested?.Invoke(this, request);
                if (!request.Confirmed)
                {
                    return; // 用户取消：静默放弃
                }

                // ④ 确认通过：移除行 + 行序整理（v1.7b）+ 通知 View 重建表格
                RecipeTable.Rows.RemoveAt(rowIndex);
                TableVersion++;
                _logService.Success($"已删除{rowDesc}");

                CompactRows();

                _ = AutoSaveAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"删除行异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 删除列：向 View 发起确认请求（弹框由 View 弹出，业务执行全部在 VM）——
        /// 用户取消 → 静默放弃；确认 → 移除指定列并通知 View 重建表格 + 自动保存。
        /// 索引无效（未选中列）时记录警告日志
        /// </summary>
        /// <param name="parameter">列索引（View 动态参数提供器传入的 int）</param>
        private void DeleteColumn(object? parameter)
        {
            try
            {
                // ① 校验列索引有效性（越界/未选中直接拒绝，不弹框）
                if (parameter is not int columnIndex || columnIndex < 0 || columnIndex >= RecipeTable.Columns.Count)
                {
                    _logService.Warn("删除列失败：请先在表格中点击选中要删除的列");
                    return;
                }

                // ② 组装确认提示（带列名，便于用户核对目标列）
                var columnName = RecipeTable.Columns[columnIndex].ColumnName;

                // ③ 向 View 发起删除确认请求（View 弹模态框后同步回填结果）
                var request = new ConfirmRequestEventArgs
                {
                    Title = "删除列",
                    Message = $"确定删除列「{columnName}」吗？\r\n该列的所有数据将被移除且不可恢复。"
                };
                ConfirmationRequested?.Invoke(this, request);
                if (!request.Confirmed)
                {
                    return; // 用户取消：静默放弃
                }

                // ④ 确认通过：移除列并通知 View 重建表格
                //（编号列被删时编号唯一校验自动跳过，见 ValidateRecipeIdUnique 的列存在性检查）
                RecipeTable.Columns.RemoveAt(columnIndex);
                TableVersion++;
                _logService.Success($"已删除列「{columnName}」");

                _ = AutoSaveAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"删除列异常：{ex.Message}");
            }
        }

        /// <summary>新建配方默认预置的空白行数（页面美观 + 录入起点）</summary>
        private const int BlankRowCount = 10;

        /// <summary>
        /// 新建空白配方（备份轮转模式，v1.7b）：
        /// ① 向用户确认（当前配方将自动备份）；
        /// ② Service 轮转：原文件重命名为「原名_时间戳.xlsx」备份（原数据完整保留），
        ///    模板表（表头沿用当前表 + N 空白行）写入原路径——新配方沿用原文件名；
        /// ③ 内存构造同构空白表立即显示（无需文件重载）。
        /// 空白行首列空格占位持久化（ERR-014），刷新/重开后不消失
        /// </summary>
        private async Task CreateBlankRecipeAsync()
        {
            IsLoading = true;
            try
            {
                // ① 用户确认（当前配方将被备份轮转）
                var confirm = new ConfirmRequestEventArgs
                {
                    Title = "新建配方",
                    Message = "确定新建空白配方吗？\r\n当前配方将自动备份（原文件名+时间戳），新配方沿用当前文件名。"
                };
                ConfirmationRequested?.Invoke(this, confirm);
                if (!confirm.Confirmed)
                {
                    return; // 用户取消：一切不变
                }

                // ② 表头沿用当前表列结构（新表与已有数据配方表一致）
                var headers = RecipeTable.Columns.Cast<DataColumn>()
                    .Select(c => c.ColumnName)
                    .ToList();

                // ③ Service 轮转：原文件备份 + 模板表写入原路径（新配方沿用原文件名）
                var result = await _recipeFileService.CreateBlankAsync(
                    headers: headers, blankRowCount: BlankRowCount);

                if (result.Success)
                {
                    // ④ 内存构造同构空白表立即显示（与写入文件内容完全一致）
                    var newTable = new DataTable("配方");
                    foreach (var header in headers)
                    {
                        newTable.Columns.Add(header);
                    }
                    for (int r = 0; r < BlankRowCount; r++)
                    {
                        newTable.Rows.Add(newTable.NewRow());
                    }

                    RecipeTable = newTable;
                    TableVersion++; // 通知 View 重建表格显示新表
                    OnPropertyChanged(nameof(Description));

                    var backupInfo = string.IsNullOrEmpty(result.Data)
                        ? "无原文件，直接新建"
                        : $"原配方已备份：{Path.GetFileName(result.Data)}";
                    _logService.Success($"新建空白配方成功（{BlankRowCount} 空白行；{backupInfo}）");
                }
                else
                {
                    _logService.Error(result.ErrorMessage ?? "新建空白配方失败");
                }
            }
            catch (Exception ex)
            {
                _logService.Error($"新建空白配方异常：{ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>打开配方所在文件夹（资源管理器）</summary>
        private void OpenFolder()
        {
            var result = _recipeFileService.OpenFolder();
            if (!result.Success)
            {
                _logService.Error(result.ErrorMessage ?? "打开文件夹失败");
            }
        }

        /// <summary>
        /// 自动补空白行（v1.7b）：总行数不足 minRows 时在末尾补全**真实可编辑的空白行**——
        /// 双击即可录入数据（走正常编辑/查重/自动保存链路），空格占位持久化，刷新不消失。
        /// minRows 由 View 依表格可见高度计算传入，让表格页面始终填满、显示和谐美观
        /// </summary>
        /// <param name="minRows">期望的最小总行数（数据行 + 空白行）</param>
        public void EnsureMinRows(int minRows)
        {
            try
            {
                if (RecipeTable.Columns.Count == 0 || minRows <= RecipeTable.Rows.Count)
                {
                    return;
                }

                int addCount = minRows - RecipeTable.Rows.Count;
                for (int i = 0; i < addCount; i++)
                {
                    RecipeTable.Rows.Add(RecipeTable.NewRow());
                }

                TableVersion++; // 通知 View 重建显示
                _logService.Info($"已自动补 {addCount} 行空白行（页面填满，双击可录入数据）");
            }
            catch (Exception ex)
            {
                _logService.Error($"补空白行异常：{ex.Message}");
            }
        }

        // ══════════════ 私有辅助 ══════════════

        /// <summary>
        /// 后台自动保存（修改/增删后触发；静默失败，不干扰用户操作）
        /// </summary>
        private async Task AutoSaveAsync()
        {
            try
            {
                await SaveCoreAsync(successMessage: null, userInitiated: false); // 自动保存：成功仅记 Info 级日志
            }
            catch (Exception ex)
            {
                _logService.Warn($"自动保存异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 判断一行是否为全空行（所有单元格 Trim 后均为空白）
        /// </summary>
        private static bool IsRowEmpty(DataRow row)
            => row.ItemArray.All(c => string.IsNullOrWhiteSpace(c?.ToString()));

        /// <summary>
        /// 行序整理（v1.7b）：将中间的全空行稳定移到表格末尾——
        /// 数据行保持原有相对顺序连续排列，数据与数据之间不存在空行，空白行只垫底。
        /// 触发点：加载完成 / 单元格修改后 / 删除行后。
        /// 有实际移动时通知 View 重建 + 自动保存（文件与显示一致，下次打开仍整齐）
        /// </summary>
        private void CompactRows()
        {
            try
            {
                var rows = RecipeTable.Rows.Cast<DataRow>().ToList();
                var dataRows = rows.Where(r => !IsRowEmpty(r)).ToList();
                var emptyRows = rows.Where(IsRowEmpty).ToList();

                // 无需整理：无空行、或全是空行（顺序无关）
                if (emptyRows.Count == 0 || dataRows.Count == 0)
                {
                    return;
                }

                // 已是「数据在前连续 + 空白垫底」则跳过
                bool changed = false;
                for (int i = 0; i < dataRows.Count; i++)
                {
                    if (!ReferenceEquals(rows[i], dataRows[i]))
                    {
                        changed = true;
                        break;
                    }
                }
                if (!changed)
                {
                    return;
                }

                // 重建行序：数据行（原顺序）在前 + 空白行垫底。
                // ⚠️ 用「新表整体替换」而非原表 Remove+ImportRow：后者在异常时
                // 会留下「全部行已移除但未回填」的空表中间态（防御性设计）
                var newTable = RecipeTable.Clone();
                foreach (var row in dataRows.Concat(emptyRows))
                {
                    newTable.ImportRow(row);
                }
                newTable.AcceptChanges();
                RecipeTable = newTable; // setter 内刷新全部命令可用态

                TableVersion++; // 通知 View 重建显示
                _logService.Info($"行序已整理：{dataRows.Count} 行数据连续排列，{emptyRows.Count} 行空白垫底");
                _ = AutoSaveAsync(); // 整理结果落盘（文件与显示一致）
            }
            catch (Exception ex)
            {
                _logService.Error($"行序整理异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 保存核心逻辑（自动/手动保存共用）：
        /// 信号量串行化（防并发写文件锁冲突）→ 编号唯一校验（失败拒绝落盘）→ 写 Excel
        /// </summary>
        /// <param name="successMessage">手动保存时传成功提示模板（{0}=文件路径）；自动保存传 null</param>
        /// <param name="userInitiated">是否用户主动触发（手动保存=true：校验失败弹窗提示；
        /// 自动保存=false：仅记日志不打断操作）</param>
        private async Task SaveCoreAsync(string? successMessage, bool userInitiated)
        {
            await _saveLock.WaitAsync();
            try
            {
                // 保存前强制校验编号唯一性（防止重复编号落盘）
                if (!ValidateRecipeIdUnique(notifyUser: userInitiated, out var dupError))
                {
                    _logService.Error(dupError);
                    return;
                }

                var result = await _recipeFileService.SaveAsync(RecipeTable);
                if (result.Success)
                {
                    if (successMessage is not null)
                    {
                        _logService.Success(string.Format(successMessage, _recipeFileService.FilePath));
                    }
                    else
                    {
                        _logService.Info($"已自动保存至 {_recipeFileService.FilePath}");
                    }
                }
                else
                {
                    var message = $"保存失败：{result.ErrorMessage}（可点击「保存」重试）";
                    if (successMessage is not null)
                    {
                        _logService.Error(message);
                    }
                    else
                    {
                        _logService.Warn(message);
                    }
                }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// 校验整表编号列唯一性（候选表头识别；无编号列时视为通过）
        /// </summary>
        /// <param name="notifyUser">true 时校验失败经 MessageRequested 事件弹窗提示（手动保存场景）</param>
        /// <param name="errorMessage">校验失败时的错误描述</param>
        private bool ValidateRecipeIdUnique(bool notifyUser, out string errorMessage)
        {
            errorMessage = string.Empty;
            var idCol = FindRecipeIdColumnIndex();
            if (idCol < 0)
            {
                return true;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataRow row in RecipeTable.Rows)
            {
                // Trim 后比较：消除「 R001 」与「R001」的伪不同（重载后 Trim 会使其撞车，详 ERR-016）
                var value = (row[idCol]?.ToString() ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue; // 空编号不参与唯一性校验
                }

                if (!seen.Add(value))
                {
                    errorMessage = $"编号「{value}」重复，保存被拒绝（编号必须唯一）";
                    if (notifyUser)
                    {
                        RaiseMessage("编号重复", $"编号「{value}」已存在，保存失败（编号必须唯一，请修改后重试）");
                    }
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 判断编号是否与"排除行之外"的其它行重复（单元格编辑校验用）
        /// </summary>
        /// <param name="value">待校验编号</param>
        /// <param name="excludeRowIndex">排除的行索引（当前编辑行自身）</param>
        private bool IsDuplicateRecipeId(string value, int excludeRowIndex)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false; // 空编号允许（不参与唯一性约束）
            }

            var idCol = FindRecipeIdColumnIndex();
            if (idCol < 0)
            {
                return false;
            }

            for (int r = 0; r < RecipeTable.Rows.Count; r++)
            {
                if (r == excludeRowIndex)
                {
                    continue;
                }

                // Trim 后比较：与重载 Trim 口径一致（详 ERR-016）
                var other = (RecipeTable.Rows[r][idCol]?.ToString() ?? string.Empty).Trim();
                if (string.Equals(other, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 生成唯一配方编号：R001 起递增，跳过已被占用的编号
        /// </summary>
        private string GenerateUniqueRecipeId()
        {
            var idCol = FindRecipeIdColumnIndex();
            var existing = new HashSet<string>(StringComparer.Ordinal);
            if (idCol >= 0)
            {
                foreach (DataRow row in RecipeTable.Rows)
                {
                    existing.Add((row[idCol]?.ToString() ?? string.Empty).Trim());
                }
            }

            for (int i = 1; ; i++)
            {
                var candidate = $"R{i:D3}";
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }
        }
    }
}