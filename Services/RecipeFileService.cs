using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// 配方文件服务：ClosedXML 读写配方 Excel（Service 层封装 Excel SDK，不弹窗）
    /// 支持多配方文件管理：默认主配方文件 + 指定路径加载/保存 + 新建带时间戳的空白配方文件（原文件保留）
    /// 所有 IO 均 async（Task.Run 包裹同步读写，避免阻塞 UI）
    /// </summary>
    public class RecipeFileService : IRecipeFileService
    {
        // ══════════════ 属性 ══════════════

        /// <inheritdoc />
        public string FilePath { get; private set; }

        /// <inheritdoc />
        public string FolderPath { get; }

        /// <summary>默认表头（新建空白配方时使用）</summary>
        private static readonly string[] DefaultHeaders = { "配方编号", "配方名称", "参数1", "参数2", "备注" };

        /// <summary>"配方编号"列名（新建配方首行写入编号的列，与 ViewModel 唯一性校验列保持一致）</summary>
        private const string RecipeIdColumn = "配方编号";

        // ══════════════ 构造 ══════════════

        /// <summary>
        /// 构造：默认数据源 D:\Printer\Data\Recipe.xlsx
        /// </summary>
        public RecipeFileService()
            : this(@"D:\Printer\Data\Recipe.xlsx")
        {
        }

        /// <summary>
        /// 构造（可注入自定义路径，便于单元测试）
        /// </summary>
        public RecipeFileService(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            FolderPath = Path.GetDirectoryName(FilePath) ?? string.Empty;
        }

        // ══════════════ 业务方法 ══════════════

        /// <inheritdoc />
        public async Task<Result<DataTable>> LoadAsync()
        {
            // 加载当前工作文件（FilePath 可能已被 CreateBlankAsync/带路径重载切换）
            return await LoadCoreAsync(FilePath);
        }

        /// <inheritdoc />
        public async Task<Result<DataTable>> LoadAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Result<DataTable>.Fail("配方文件路径为空，无法加载");
            }

            var result = await LoadCoreAsync(filePath);
            if (result.Success)
            {
                FilePath = filePath; // 加载成功后切换当前数据源（后续保存均指向该文件）
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<Result<bool>> SaveAsync(DataTable table)
        {
            // 保存到当前工作文件
            return await SaveCoreAsync(table, FilePath);
        }

        /// <inheritdoc />
        public async Task<Result<bool>> SaveAsync(DataTable table, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return Result<bool>.Fail("配方文件路径为空，无法保存");
            }

            var result = await SaveCoreAsync(table, filePath);
            if (result.Success)
            {
                FilePath = filePath; // 保存成功后切换当前数据源
            }
            return result;
        }

        /// <inheritdoc />
        public async Task<Result<string>> CreateBlankAsync(string recipeName, string recipeId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(recipeName))
                {
                    recipeName = "新建配方"; // 缺省名兜底
                }

                var newFilePath = await Task.Run(() =>
                {
                    Directory.CreateDirectory(FolderPath);

                    // 生成唯一文件名：安全化配方名 + 时间戳（同一秒重复创建时自动递增序号，保证不覆盖任何已有文件）
                    var baseName = SanitizeFileName(recipeName);
                    string newFile;
                    int seq = 1;
                    do
                    {
                        var suffix = seq == 1
                            ? DateTime.Now.ToString("_yyyyMMdd_HHmmss")
                            : $"{DateTime.Now:yyyyMMdd_HHmmss}_{seq}";
                        newFile = Path.Combine(FolderPath, $"{baseName}{suffix}.xlsx");
                        seq++;
                    } while (File.Exists(newFile));

                    using var workbook = new XLWorkbook();
                    var ws = workbook.Worksheets.Add("配方");

                    // 写默认表头
                    for (int c = 0; c < DefaultHeaders.Length; c++)
                    {
                        ws.Cell(1, c + 1).Value = DefaultHeaders[c];
                        ws.Cell(1, c + 1).Style.Font.Bold = true;
                        ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");
                    }

                    // 首行配方编号（编号列存在且非空时写入；唯一性由 ViewModel 校验）
                    var idCol = Array.IndexOf(DefaultHeaders, RecipeIdColumn);
                    if (idCol >= 0 && !string.IsNullOrWhiteSpace(recipeId))
                    {
                        ws.Cell(2, idCol + 1).Value = recipeId;
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(newFile); // 只写新文件，不影响任何已有文件
                    return newFile;
                });

                // 创建成功后当前数据源切换为新文件（后续加载/保存均指向新配方）
                FilePath = newFilePath;
                return Result<string>.OK(newFilePath);
            }
            catch (Exception ex)
            {
                return Result<string>.Fail($"新建空白配方失败：{ex.Message}");
            }
        }

        /// <inheritdoc />
        public Result<bool> OpenFolder()
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                Process.Start("explorer.exe", FolderPath);
                return Result<bool>.OK(true);
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"打开配方文件夹失败：{ex.Message}");
            }
        }

        // ══════════════ 私有辅助 ══════════════

        /// <summary>
        /// 加载核心逻辑：读取指定路径 Excel 第一张工作表到 DataTable
        /// （文件不存在时返回含默认表头的空表，首次运行友好）
        /// </summary>
        private async Task<Result<DataTable>> LoadCoreAsync(string path)
        {
            try
            {
                return await Task.Run(() =>
                {
                    var table = CreateEmptyTable();

                    // 文件不存在：返回默认表头的空表（首次运行友好）
                    if (!File.Exists(path))
                    {
                        return Result<DataTable>.OK(table);
                    }

                    using var workbook = new XLWorkbook(path);
                    var ws = workbook.Worksheet(1);
                    var usedRange = ws.RangeUsed();
                    if (usedRange is null)
                    {
                        return Result<DataTable>.OK(table);
                    }

                    // 读表头（第一行）：按文件实际表头重建列结构（空表头用「列N」兜底）。
                    // ⚠️ 不基于预置默认表头做「重命名」：当文件某列名与默认表头中
                    // 其它列重名时（如文件「备注」列位于第 3 列而默认表头第 5 列也是
                    // 「备注」），重命名会触发 DuplicateNameException 导致加载失败
                    //（单元测试 ERR-014 回归套件发现，2026-09-03 修复）
                    var headerRow = usedRange.FirstRow();
                    int colCount = usedRange.ColumnCount();
                    var fileTable = new DataTable("配方");
                    for (int c = 1; c <= colCount; c++)
                    {
                        string header = headerRow.Cell(c).GetString();
                        fileTable.Columns.Add(string.IsNullOrWhiteSpace(header) ? $"列{c}" : header);
                    }
                    table = fileTable; // 后续数据行装载与返回均使用文件实际列结构

                    // 读数据行（⚠️ 不用 RowsUsed() 枚举：它只返回有内容的行，空行会被跳过，
                    // 导致保存的空行在重新加载时消失（详 ERR-014）。
                    // 改用末行行号逐行循环装载，空行/中间空行全部保留为空白行；
                    // 空格占位单元格经 Trim 还原为空字符串）
                    var lastRow = ws.LastRowUsed();
                    if (lastRow is null)
                    {
                        table.AcceptChanges();
                        return Result<DataTable>.OK(table); // 表内无任何行（仅表头都不存在）
                    }

                    int lastRowNumber = lastRow.RowNumber();
                    for (int r = 2; r <= lastRowNumber; r++)
                    {
                        var dataRow = table.NewRow();
                        for (int c = 1; c <= table.Columns.Count; c++)
                        {
                            var cellText = ws.Cell(r, c).GetString();
                            dataRow[c - 1] = cellText.Trim();
                        }
                        table.Rows.Add(dataRow);
                    }

                    table.AcceptChanges();
                    return Result<DataTable>.OK(table);
                });
            }
            catch (Exception ex)
            {
                return Result<DataTable>.Fail($"加载配方文件失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 保存核心逻辑：将 DataTable 覆写写入指定路径的 Excel
        /// </summary>
        private async Task<Result<bool>> SaveCoreAsync(DataTable table, string path)
        {
            try
            {
                if (table is null)
                {
                    return Result<bool>.Fail("配方表格为空，无法保存");
                }

                return await Task.Run(() =>
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir); // 确保目标目录存在
                    }

                    using var workbook = new XLWorkbook();
                    var ws = workbook.Worksheets.Add("配方");

                    // 写表头
                    for (int c = 0; c < table.Columns.Count; c++)
                    {
                        ws.Cell(1, c + 1).Value = table.Columns[c].ColumnName;
                        ws.Cell(1, c + 1).Style.Font.Bold = true;
                        ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#E8F0FE");
                    }

                    // 写数据（⚠️ ClosedXML 对纯空字符串单元格不落任何痕迹 → 整行全空时
                    // 该行在 xlsx 文件 XML 层面不存在，重新加载时读不到，
                    // 表现为「新增行/清空的行在刷新后消失」（详 ERR-014）。
                    // 解决：整行全空时向首列写入单个空格占位，保证该行在文件中真实存在；
                    // 空格经 IsNullOrWhiteSpace 判定为空白，不影响编号唯一性校验等既有逻辑）
                    for (int r = 0; r < table.Rows.Count; r++)
                    {
                        var isRowEmpty = true;
                        for (int c = 0; c < table.Columns.Count; c++)
                        {
                            var text = table.Rows[r][c]?.ToString() ?? string.Empty;
                            ws.Cell(r + 2, c + 1).Value = text;
                            if (text.Length > 0)
                            {
                                isRowEmpty = false;
                            }
                        }

                        if (isRowEmpty && table.Columns.Count > 0)
                        {
                            ws.Cell(r + 2, 1).Value = " "; // 空格占位：让空行在文件中留下痕迹
                        }
                    }

                    ws.Columns().AdjustToContents();
                    workbook.SaveAs(path);
                    return Result<bool>.OK(true);
                });
            }
            catch (Exception ex)
            {
                return Result<bool>.Fail($"保存配方文件失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 创建含默认表头的空表（列可编辑/可增删）
        /// </summary>
        private static DataTable CreateEmptyTable()
        {
            var table = new DataTable("配方");
            foreach (var header in DefaultHeaders)
            {
                table.Columns.Add(header);
            }
            return table;
        }

        /// <summary>
        /// 安全化文件名：非法字符（\ / : * ? 等）替换为下划线，防止配方名导致文件创建失败
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.Trim();
        }
    }
}