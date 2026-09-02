using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Common.Commands;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 配方管理页视图模型：Excel 配方的展示、单元格编辑（编号唯一校验）、
    /// 新增行/列、新建空白配方（带时间戳不删原文件）、保存、打开文件夹
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
                    LoadCommand.RaiseCanExecuteChanged();
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
                    LoadCommand.RaiseCanExecuteChanged();
                    CreateBlankCommand.RaiseCanExecuteChanged();
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
                    SaveCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>"配方编号"列名（编号唯一性校验依据；文件无此列时校验自动跳过）</summary>
        private const string RecipeIdColumn = "配方编号";

        // ══════════════ 命令 ══════════════

        /// <summary>刷新命令：重新从当前 Excel 文件加载数据（异步，IsLoading 防重复）</summary>
        public AsyncRelayCommand LoadCommand { get; }

        /// <summary>保存命令：将当前表格写入 Excel 文件（异步，IsSaving 防重复；保存前做编号唯一校验）</summary>
        public AsyncRelayCommand SaveCommand { get; }

        /// <summary>新增行命令：表格末尾追加一行（自动生成唯一配方编号）</summary>
        public RelayCommand AddRowCommand { get; }

        /// <summary>新增列命令：表格末尾追加一列（列名自动避免重复）</summary>
        public RelayCommand AddColumnCommand { get; }

        /// <summary>
        /// 新建空白配方命令：在当前配方目录创建"原文件名+时间戳.xlsx"的新空白配方，
        /// 原有配方文件保留不删除，创建成功后自动切换数据源到新文件
        /// </summary>
        public AsyncRelayCommand CreateBlankCommand { get; }

        /// <summary>打开文件夹命令：资源管理器定位配方所在目录</summary>
        public RelayCommand OpenFolderCommand { get; }

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
                await SaveCoreAsync(successMessage: $"配方已保存：{{0}}");
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
                var text = newValue ?? string.Empty;

                // 未变化：跳过写盘
                if (string.Equals(oldValue, text, StringComparison.Ordinal))
                {
                    return true;
                }

                var columnName = RecipeTable.Columns[columnIndex].ColumnName;

                // 配方编号唯一性校验（大小写敏感按工业惯例保持原样比较）
                if (columnName == RecipeIdColumn && IsDuplicateRecipeId(text, excludeRowIndex: rowIndex))
                {
                    _logService.Error($"配方编号「{text}」已存在，修改被拒绝（编号必须唯一）");
                    return false; // View 收到 false 后还原单元格显示
                }

                // 写回数据源
                RecipeTable.Rows[rowIndex][columnIndex] = text;
                _logService.Info($"单元格已修改 [{rowIndex + 1}行/{columnName}]：{oldValue} → {text}");

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

                // 自动生成唯一配方编号（列存在时）
                var idCol = RecipeTable.Columns.IndexOf(RecipeIdColumn);
                if (idCol >= 0)
                {
                    row[idCol] = GenerateUniqueRecipeId();
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
        /// 新增列：末尾追加"新列N"（N 自增避免与现有列名冲突）
        /// </summary>
        private void AddColumn()
        {
            try
            {
                string name;
                int n = RecipeTable.Columns.Count + 1;
                do
                {
                    name = $"新列{n}";
                    n++;
                } while (RecipeTable.Columns.Contains(name));

                RecipeTable.Columns.Add(name);
                TableVersion++; // 通知 View 重建表格以显示新列
                _logService.Success($"已新增列「{name}」");

                _ = AutoSaveAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"新增列异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 新建空白配方：文件名 = 安全化配方名 + 时间戳，保存在当前配方目录下，
        /// 原有配方文件保留不删除；成功后加载新表并切换当前数据源
        /// </summary>
        private async Task CreateBlankRecipeAsync()
        {
            IsLoading = true;
            try
            {
                // 基于当前表生成唯一配方编号（R001、R002…），配方名与编号一致便于识别
                var recipeId = GenerateUniqueRecipeId();
                var result = await _recipeFileService.CreateBlankAsync(recipeName: recipeId, recipeId: recipeId);
                if (result.Success && result.Data is not null)
                {
                    // 加载新文件内容（空表 + 默认表头 + 首行编号）
                    var loadResult = await _recipeFileService.LoadAsync();
                    if (loadResult.Success && loadResult.Data is not null)
                    {
                        RecipeTable = loadResult.Data;
                    }

                    TableVersion++;
                    OnPropertyChanged(nameof(Description));
                    _logService.Success($"新建空白配方成功：{result.Data}（原配方文件保留）");
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

        // ══════════════ 私有辅助 ══════════════

        /// <summary>
        /// 后台自动保存（修改/增删后触发；静默失败，不干扰用户操作）
        /// </summary>
        private async Task AutoSaveAsync()
        {
            try
            {
                await SaveCoreAsync(successMessage: null); // 自动保存：成功仅记 Info 级日志
            }
            catch (Exception ex)
            {
                _logService.Warn($"自动保存异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 保存核心逻辑（自动/手动保存共用）：
        /// 信号量串行化（防并发写文件锁冲突）→ 编号唯一校验（失败拒绝落盘）→ 写 Excel
        /// </summary>
        /// <param name="successMessage">手动保存时传成功提示模板（{0}=文件路径）；自动保存传 null</param>
        private async Task SaveCoreAsync(string? successMessage)
        {
            await _saveLock.WaitAsync();
            try
            {
                // 保存前强制校验编号唯一性（防止重复编号落盘）
                if (!ValidateRecipeIdUnique(out var dupError))
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
        /// 校验整表"配方编号"列唯一性（不存在该列时视为通过）
        /// </summary>
        /// <param name="errorMessage">校验失败时的错误描述</param>
        private bool ValidateRecipeIdUnique(out string errorMessage)
        {
            errorMessage = string.Empty;
            var idCol = RecipeTable.Columns.IndexOf(RecipeIdColumn);
            if (idCol < 0)
            {
                return true;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (DataRow row in RecipeTable.Rows)
            {
                var value = row[idCol]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue; // 空编号不参与唯一性校验
                }

                if (!seen.Add(value))
                {
                    errorMessage = $"配方编号「{value}」重复，保存被拒绝（编号必须唯一）";
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

            var idCol = RecipeTable.Columns.IndexOf(RecipeIdColumn);
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

                var other = RecipeTable.Rows[r][idCol]?.ToString() ?? string.Empty;
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
            var idCol = RecipeTable.Columns.IndexOf(RecipeIdColumn);
            var existing = new HashSet<string>(StringComparer.Ordinal);
            if (idCol >= 0)
            {
                foreach (DataRow row in RecipeTable.Rows)
                {
                    existing.Add(row[idCol]?.ToString() ?? string.Empty);
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