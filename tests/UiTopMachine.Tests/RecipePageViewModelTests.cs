using System;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using UiTopMachine.Common;
using UiTopMachine.Services;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 配方页 ViewModel 业务逻辑测试（v1.0~v1.4 功能覆盖）：
    /// 删除行/列（v1.4 确认弹框事件回填）、新增列校验（v1.3）、
    /// 编号唯一性（v1.0）、命令可用性（ERR-013 全量刷新）
    /// View 弹框经事件参数回填模拟，零 UI 依赖
    /// </summary>
    public class RecipePageViewModelTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly RecipeFileService _service;
        private readonly StubLogService _log;
        private readonly RecipePageViewModel _vm;

        public RecipePageViewModelTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"UiTopMachineVmTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _service = new RecipeFileService(Path.Combine(_tempDir, "Recipe.xlsx"));
            _log = new StubLogService();
            _vm = new RecipePageViewModel(_service, _log);
        }

        public void Dispose()
        {
            // 后台自动保存为 fire-and-forget，可能与本方法竞态（文件句柄未释放）：
            // 带短暂重试；仍失败则忽略残留（独立临时目录，不影响其它测试与仓库）
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    if (Directory.Exists(_tempDir))
                    {
                        Directory.Delete(_tempDir, recursive: true);
                    }
                    return;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(200);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(200);
                }
            }
        }

        /// <summary>构造带默认表头与 N 行数据的表</summary>
        private static DataTable BuildTable(params (string Id, string Name)[] rows)
        {
            var table = new DataTable("配方");
            table.Columns.Add("配方编号");
            table.Columns.Add("配方名称");
            table.Columns.Add("备注");

            foreach (var (id, name) in rows)
            {
                var row = table.NewRow();
                row["配方编号"] = id;
                row["配方名称"] = name;
                row["备注"] = "";
                table.Rows.Add(row);
            }
            return table;
        }

        /// <summary>构造「编号」表头（用户真实配方表列结构）与 N 行数据的表</summary>
        private static DataTable BuildTableWithIdHeader(params (string Id, string Name)[] rows)
        {
            var table = new DataTable("配方");
            table.Columns.Add("编号");
            table.Columns.Add("名称");

            foreach (var (id, name) in rows)
            {
                var row = table.NewRow();
                row["编号"] = id;
                row["名称"] = name;
                table.Rows.Add(row);
            }
            return table;
        }

        /// <summary>把表装入 VM 并等待自动保存静默完成</summary>
        private async Task<RecipePageViewModel> LoadTableIntoVmAsync(DataTable table)
        {
            var save = await _service.SaveAsync(table);
            Assert.True(save.Success, $"前置保存失败：{save.ErrorMessage}");
            var load = await _service.LoadAsync();
            Assert.True(load.Success, $"前置加载失败：{load.ErrorMessage}");

            // 触发 VM 加载命令（私有方法经 LoadCommand 驱动）
            _vm.LoadCommand.Execute(null);
            // 等待 IsLoading 回落（命令完成后为 false）
            while (_vm.IsLoading)
            {
                await Task.Delay(10);
            }
            return _vm;
        }

        // ══════════════ v1.4 删除行/列（确认弹框事件回填） ══════════════

        [Fact]
        public async Task 删除行_确认后_行被移除且TableVersion自增()
        {
            await LoadTableIntoVmAsync(BuildTable(
                ("R001", "甲"), ("R002", "乙"), ("R003", "丙")));
            var versionBefore = _vm.TableVersion;

            ConfirmRequestEventArgs? confirmRequest = null;
            _vm.DeletionConfirmRequested += (_, e) =>
            {
                confirmRequest = e;
                e.Confirmed = true; // 模拟用户点击"确定"
            };

            _vm.DeleteRowCommand.Execute(1); // 删除第 2 行（R002）

            Assert.NotNull(confirmRequest);
            Assert.Contains("R002", confirmRequest!.Message); // 确认文案带配方编号便于核对
            Assert.Equal(2, _vm.RecipeTable.Rows.Count);
            Assert.Equal("R001", _vm.RecipeTable.Rows[0]["配方编号"].ToString());
            Assert.Equal("R003", _vm.RecipeTable.Rows[1]["配方编号"].ToString());
            Assert.Equal(versionBefore + 1, _vm.TableVersion);
        }

        [Fact]
        public async Task 删除行_用户取消_行保留不动()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            _vm.DeletionConfirmRequested += (_, e) => e.Confirmed = false; // 模拟取消

            _vm.DeleteRowCommand.Execute(0);

            Assert.Equal(2, _vm.RecipeTable.Rows.Count); // 未删除
        }

        [Fact]
        public async Task 删除列_确认后_列被移除()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            _vm.DeletionConfirmRequested += (_, e) => e.Confirmed = true;

            _vm.DeleteColumnCommand.Execute(2); // 删除"备注"列

            Assert.Equal(2, _vm.RecipeTable.Columns.Count);
            Assert.False(_vm.RecipeTable.Columns.Contains("备注"));
        }

        [Fact]
        public async Task 删除行_索引越界_拒绝执行并记警告()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            _vm.DeleteRowCommand.Execute(99); // 越界

            Assert.Equal(1, _vm.RecipeTable.Rows.Count); // 未删除
            Assert.Contains(_log.Entries, e => e.Level == "Warn");
        }

        [Fact]
        public async Task 删除命令CanExecute_无选中时禁用_有效索引时可用()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            Assert.False(_vm.DeleteRowCommand.CanExecute(-1));   // 未选中
            Assert.False(_vm.DeleteRowCommand.CanExecute(99));   // 越界
            Assert.True(_vm.DeleteRowCommand.CanExecute(0));     // 有效
            Assert.False(_vm.DeleteColumnCommand.CanExecute(5)); // 列越界
        }

        // ══════════════ v1.3 新增列校验 ══════════════

        [Fact]
        public async Task 新增列_列名有效_列被追加()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            _vm.ColumnNamingRequested += (_, e) =>
            {
                e.Confirmed = true;
                e.InputText = "新参数";
            };
            _vm.AddColumnCommand.Execute(null);

            Assert.True(_vm.RecipeTable.Columns.Contains("新参数"));
            Assert.Equal(4, _vm.RecipeTable.Columns.Count);
        }

        [Fact]
        public async Task 新增列_列名为空_新增失败并记错误日志()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));
            var countBefore = _vm.RecipeTable.Columns.Count;

            _vm.ColumnNamingRequested += (_, e) =>
            {
                e.Confirmed = true;
                e.InputText = "   "; // 纯空白
            };
            _vm.AddColumnCommand.Execute(null);

            Assert.Equal(countBefore, _vm.RecipeTable.Columns.Count); // 列数不变
            Assert.Contains(_log.Entries, e =>
                e.Level == "Error" && e.Message.Contains("列名不能为空"));
        }

        [Fact]
        public async Task 新增列_列名重复_被拒绝()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));
            var countBefore = _vm.RecipeTable.Columns.Count;

            _vm.ColumnNamingRequested += (_, e) =>
            {
                e.Confirmed = true;
                e.InputText = "配方编号"; // 与现有列重复
            };
            _vm.AddColumnCommand.Execute(null);

            Assert.Equal(countBefore, _vm.RecipeTable.Columns.Count);
            Assert.Contains(_log.Entries, e =>
                e.Level == "Error" && e.Message.Contains("不可重复"));
        }

        [Fact]
        public async Task 新增列_用户取消_静默放弃不记错误()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));
            var countBefore = _vm.RecipeTable.Columns.Count;

            _vm.ColumnNamingRequested += (_, e) => e.Confirmed = false;

            _vm.AddColumnCommand.Execute(null);

            Assert.Equal(countBefore, _vm.RecipeTable.Columns.Count);
            Assert.DoesNotContain(_log.Entries, e => e.Level == "Error");
        }

        // ══════════════ v1.0 编号唯一性 ══════════════

        [Fact]
        public async Task 单元格编辑_编号重复_拒绝提交()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            // 把第 2 行编号改成第 1 行的 R001 → 拒绝
            var committed = _vm.TryCommitCellEdit(1, 0, "R001");

            Assert.False(committed);
            Assert.Equal("R002", _vm.RecipeTable.Rows[1]["配方编号"].ToString()); // 原值保留
            Assert.Contains(_log.Entries, e => e.Level == "Error");
        }

        [Fact]
        public async Task 单元格编辑_编号唯一_提交成功并写回数据源()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            var committed = _vm.TryCommitCellEdit(1, 0, "R009");

            Assert.True(committed);
            Assert.Equal("R009", _vm.RecipeTable.Rows[1]["配方编号"].ToString());
        }

        [Fact]
        public async Task 单元格编辑_保留自身行的原编号_允许不改编号只改其它单元格()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            // 自身行编辑排除自身（excludeRowIndex），同值写回不算重复
            var committed = _vm.TryCommitCellEdit(0, 1, "新名字");

            Assert.True(committed);
            Assert.Equal("新名字", _vm.RecipeTable.Rows[0]["配方名称"].ToString());
        }

        [Fact]
        public async Task 单元格编辑_索引越界_返回false()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            Assert.False(_vm.TryCommitCellEdit(-1, 0, "X"));
            Assert.False(_vm.TryCommitCellEdit(99, 0, "X"));
            Assert.False(_vm.TryCommitCellEdit(0, 99, "X"));
        }

        [Fact]
        public async Task 保存_编号重复_拒绝落盘并记错误()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));
            // 直接在数据源制造重复编号（绕过单元格校验模拟历史数据）
            var row = _vm.RecipeTable.NewRow();
            row["配方编号"] = "R001"; // 与第 0 行重复
            row["配方名称"] = "重复行";
            _vm.RecipeTable.Rows.Add(row);

            _vm.SaveCommand.Execute(null);
            while (_vm.IsSaving)
            {
                await Task.Delay(10);
            }

            Assert.Contains(_log.Entries, e =>
                e.Level == "Error" && e.Message.Contains("重复"));
        }

        // ══════════════ ERR-013 命令可用性（全量刷新） ══════════════

        [Fact]
        public async Task 加载完成后_新增行命令自动恢复可用_ERR013回归()
        {
            // 初始空表（0 列）时 AddRowCommand 禁用；加载后必须自动恢复（ERR-013 漏刷回归）
            Assert.False(_vm.AddRowCommand.CanExecute(null));

            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            Assert.True(_vm.AddRowCommand.CanExecute(null));
        }

        [Fact]
        public async Task 新增行_自动生成唯一编号跳过已占用()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R003", "丙")));

            _vm.AddRowCommand.Execute(null);

            Assert.Equal(3, _vm.RecipeTable.Rows.Count);
            // R001/R003 已占用 → 自动编号应为 R002
            Assert.Equal("R002", _vm.RecipeTable.Rows[2]["配方编号"].ToString());
        }

        // ══════════════ v1.4c 空行场景联动（VM ↔ Service） ══════════════

        [Fact]
        public async Task 新增行后自动保存_重载不丢行_ERR014联动()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            // 模拟无编号列表格的新增空行场景：直接加入全空行
            var emptyRow = _vm.RecipeTable.NewRow();
            _vm.RecipeTable.Rows.Add(emptyRow);

            // 手动触发保存（自动保存为后台 fire-and-forget，测试中显式等待落盘）
            _vm.SaveCommand.Execute(null);
            while (_vm.IsSaving)
            {
                await Task.Delay(10);
            }
            await Task.Delay(200); // 等待写盘完成

            var reload = await _service.LoadAsync();
            Assert.NotNull(reload.Data);
            Assert.Equal(_vm.RecipeTable.Rows.Count, reload.Data!.Rows.Count); // 空行保留
        }

        // ══════════════ 修改功能·空格规范化（ERR-016 回归：Trim 口径必须与重载一致） ══════════════

        [Fact]
        public async Task 单元格修改_编号带首尾空格_提交后规范化为去空格值()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            var committed = _vm.TryCommitCellEdit(0, 0, "  R009  ");

            Assert.True(committed);
            // LoadCoreAsync 重载时对全部单元格 Trim，内存值必须与重载结果一致，否则往返后数据漂移
            Assert.Equal("R009", _vm.RecipeTable.Rows[0]["配方编号"].ToString());
        }

        [Fact]
        public async Task 单元格修改_编号带空格但与现有编号重复_拒绝提交()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            // " R001 " 去空格后与 R001 相同 → 必须拒绝（否则重载 Trim 后产生重复编号）
            var committed = _vm.TryCommitCellEdit(1, 0, " R001 ");

            Assert.False(committed);
            Assert.Equal("R002", _vm.RecipeTable.Rows[1]["配方编号"].ToString()); // 原值保留
        }

        [Fact]
        public async Task 保存校验_带空格的重复编号_拒绝落盘()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));
            // 直接在数据源制造带空格的"伪不同"编号（模拟历史数据绕过单元格校验）
            _vm.RecipeTable.Rows[1]["配方编号"] = " R001 ";

            _vm.SaveCommand.Execute(null);
            while (_vm.IsSaving)
            {
                await Task.Delay(10);
            }

            Assert.Contains(_log.Entries, e =>
                e.Level == "Error" && e.Message.Contains("重复"));
        }

        [Fact]
        public async Task 单元格修改_普通文本列带空格_提交后规范化与重载一致()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));

            var committed = _vm.TryCommitCellEdit(0, 1, "  新名字  ");

            Assert.True(committed);
            Assert.Equal("新名字", _vm.RecipeTable.Rows[0]["配方名称"].ToString());
        }

        // ══════════════ 修改功能·位置正确性（ERR-017 回归：写对格子 + 保存往返不串位） ══════════════

        [Fact]
        public async Task 位置正确性_多单元格乱序编辑_各值落在正确位置()
        {
            // 4 行 3 列：乱序编辑互不相邻的格子，逐格断言位置与值
            await LoadTableIntoVmAsync(BuildTable(
                ("R001", "甲"), ("R002", "乙"), ("R003", "丙"), ("R004", "丁")));

            Assert.True(_vm.TryCommitCellEdit(0, 1, "甲改"));    // 第 1 行 名称
            Assert.True(_vm.TryCommitCellEdit(3, 2, "尾备注"));  // 第 4 行 备注
            Assert.True(_vm.TryCommitCellEdit(1, 0, "R902"));    // 第 2 行 编号
            Assert.True(_vm.TryCommitCellEdit(2, 1, "丙改"));    // 第 3 行 名称

            Assert.Equal("甲改", _vm.RecipeTable.Rows[0]["配方名称"].ToString());
            Assert.Equal("R902", _vm.RecipeTable.Rows[1]["配方编号"].ToString());
            Assert.Equal("丙改", _vm.RecipeTable.Rows[2]["配方名称"].ToString());
            Assert.Equal("尾备注", _vm.RecipeTable.Rows[3]["备注"].ToString());
            // 其余格子不被误写（AntdUI 错位写症状：值跑到上一行/首行改不到）
            Assert.Equal("乙", _vm.RecipeTable.Rows[1]["配方名称"].ToString());
            Assert.Equal("R001", _vm.RecipeTable.Rows[0]["配方编号"].ToString());
            Assert.Equal("R004", _vm.RecipeTable.Rows[3]["配方编号"].ToString());
        }

        [Fact]
        public async Task 位置正确性_首行单元格修改_值落在首行_ERR017症状()
        {
            // ERR-017 症状：内部 1 基提交导致「首行永远改不到」——回归守护
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            Assert.True(_vm.TryCommitCellEdit(0, 1, "首行改名"));

            Assert.Equal("首行改名", _vm.RecipeTable.Rows[0]["配方名称"].ToString());
            Assert.Equal("乙", _vm.RecipeTable.Rows[1]["配方名称"].ToString()); // 第 2 行不被误写
        }

        [Fact]
        public async Task 编号列_重输自身原值_视为未变化不误报拒绝()
        {
            // ERR-017 连带症状守护：索引错位时 excludeRowIndex 排除错行，
            // 重输自身原编号会被误报「已存在」。修复后必须放行（未变化短路）
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            var committed = _vm.TryCommitCellEdit(1, 0, "R002");

            Assert.True(committed); // 自身原值 = 未变化，放行
            Assert.Equal("R002", _vm.RecipeTable.Rows[1]["配方编号"].ToString());
            Assert.DoesNotContain(_log.Entries, e =>
                e.Level == "Error" && e.Message.Contains("已存在"));
        }

        [Fact]
        public async Task 位置正确性_末行单元格修改_提交成功且落在末行()
        {
            // ERR-017 真根因症状守护：RowIndex 未减 1 换算时，末行编辑
            // （e.RowIndex = 行数）传 VM 越界 → 静默失败「改不动」；
            // View 换算修正后末行必须可正常编辑（VM 契约：rowIndex=Count-1 有效）
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            var committed = _vm.TryCommitCellEdit(1, 1, "末行改名");

            Assert.True(committed);
            Assert.Equal("末行改名", _vm.RecipeTable.Rows[1]["配方名称"].ToString());
            Assert.Equal("甲", _vm.RecipeTable.Rows[0]["配方名称"].ToString()); // 首行不被误写
        }

        [Fact]
        public async Task 位置正确性_编辑提交后TableVersion自增驱动UI重建()
        {
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲")));
            var versionBefore = _vm.TableVersion;

            Assert.True(_vm.TryCommitCellEdit(0, 1, "新值"));

            // View 订阅 TableVersion 重建 AntdUI 表格（ERR-017 修复：UI 显示由 VM 数据驱动）
            Assert.Equal(versionBefore + 1, _vm.TableVersion);
        }

        // ══════════════ 编号列候选识别（v1.6：真实表头「编号」查重生效） ══════════════

        [Fact]
        public async Task 编号列_表头为编号_编辑重复编号_拒绝提交并触发消息弹窗()
        {
            // 用户反馈「编号相同仍保存」根因：查重硬编码只认「配方编号」，
            // 真实表头「编号」被跳过——本用例守护候选识别后的拒绝链路（含弹窗事件）
            await LoadTableIntoVmAsync(BuildTableWithIdHeader(("R001", "甲"), ("R002", "乙")));
            var versionBefore = _vm.TableVersion;

            MessageRequestEventArgs? messageRequest = null;
            _vm.MessageRequested += (_, e) => messageRequest = e;

            var committed = _vm.TryCommitCellEdit(1, 0, "R001"); // 与第 1 行重复

            Assert.False(committed);
            Assert.Equal("R002", _vm.RecipeTable.Rows[1]["编号"].ToString()); // 原值保留
            Assert.NotNull(messageRequest);
            Assert.Contains("R001", messageRequest!.Message);
            Assert.Contains("已存在", messageRequest!.Message);
            Assert.Equal(versionBefore + 1, _vm.TableVersion); // 拒绝也强制重建还原显示
        }

        [Fact]
        public async Task 编号列_表头为编号_编辑唯一编号_提交成功且不弹窗()
        {
            await LoadTableIntoVmAsync(BuildTableWithIdHeader(("R001", "甲"), ("R002", "乙")));

            MessageRequestEventArgs? messageRequest = null;
            _vm.MessageRequested += (_, e) => messageRequest = e;

            var committed = _vm.TryCommitCellEdit(1, 0, "R009");

            Assert.True(committed);
            Assert.Equal("R009", _vm.RecipeTable.Rows[1]["编号"].ToString());
            Assert.Null(messageRequest); // 唯一编号不触发弹窗
        }

        [Fact]
        public async Task 编号列_表头为编号_新增行自动编号_跳过已占用编号()
        {
            await LoadTableIntoVmAsync(BuildTableWithIdHeader(("R001", "甲"), ("R003", "丙")));

            _vm.AddRowCommand.Execute(null);

            Assert.Equal(3, _vm.RecipeTable.Rows.Count);
            Assert.Equal("R002", _vm.RecipeTable.Rows[2]["编号"].ToString()); // R001/R003 已占用
        }

        [Fact]
        public async Task 编号列_表头为编号_新增行后手改新行编号为重复值_拒绝()
        {
            // 用户场景：新增行自动编号 R002 后手动改成已有编号 R001 → 必须被拦
            await LoadTableIntoVmAsync(BuildTableWithIdHeader(("R001", "甲")));

            _vm.AddRowCommand.Execute(null); // 新增行自动编号 R002
            Assert.Equal("R002", _vm.RecipeTable.Rows[1]["编号"].ToString());

            // 把新增行（第 2 行）编号改成第 1 行的 R001 → 拒绝
            var committed = _vm.TryCommitCellEdit(1, 0, "R001");

            Assert.False(committed);
            Assert.Equal("R002", _vm.RecipeTable.Rows[1]["编号"].ToString()); // 原值保留
            Assert.Contains(_log.Entries, e => e.Level == "Error" && e.Message.Contains("已存在"));
        }

        [Fact]
        public async Task 保存兜底_表头为编号且存在历史重复_拒绝落盘并触发弹窗()
        {
            // 手动保存兜底防线（含历史数据场景）+ 弹窗提示
            await LoadTableIntoVmAsync(BuildTableWithIdHeader(("R001", "甲")));
            _vm.RecipeTable.Rows[0]["编号"] = "DUP";
            var dupRow = _vm.RecipeTable.NewRow();
            dupRow["编号"] = "DUP"; // 直接在数据源制造重复（模拟历史数据）
            dupRow["名称"] = "重复行";
            _vm.RecipeTable.Rows.Add(dupRow);

            MessageRequestEventArgs? messageRequest = null;
            _vm.MessageRequested += (_, e) => messageRequest = e;

            _vm.SaveCommand.Execute(null); // 手动保存（userInitiated=true → 弹窗）
            while (_vm.IsSaving)
            {
                await Task.Delay(10);
            }

            Assert.Contains(_log.Entries, e => e.Level == "Error" && e.Message.Contains("重复"));
            Assert.NotNull(messageRequest);
            Assert.Contains("DUP", messageRequest!.Message);
        }

        // ══════════════ 新建空白配方（v1.7：表头沿用当前表 + 10 空白行 + 保存往返） ══════════════

        [Fact]
        public async Task 新建空白配方_表头沿用当前表_10空白行_保存往返一致()
        {
            // 用户需求：新表表头与已有数据配方表一致、数据全空等待录入、显示多行空白行
            await LoadTableIntoVmAsync(BuildTableWithIdHeader(("R001", "甲")));
            var versionBefore = _vm.TableVersion;

            _vm.CreateBlankCommand.Execute(null);
            while (_vm.IsLoading)
            {
                await Task.Delay(10);
            }

            // 表头与原表完全一致（编号/名称），不再是默认 5 列
            Assert.Equal(2, _vm.RecipeTable.Columns.Count);
            Assert.Equal("编号", _vm.RecipeTable.Columns[0].ColumnName);
            Assert.Equal("名称", _vm.RecipeTable.Columns[1].ColumnName);

            // 10 空白行全部无内容（等待用户输入；不再写首行编号）
            Assert.Equal(10, _vm.RecipeTable.Rows.Count);
            Assert.All(_vm.RecipeTable.Rows.Cast<DataRow>(), row =>
                Assert.True(row.ItemArray.All(c => string.IsNullOrEmpty(c?.ToString())),
                    "新建配方的数据区应全空"));
            Assert.Equal(versionBefore + 1, _vm.TableVersion);

            // 用户在第 1 行录入编号（唯一）→ 保存 → 重载：显示即所建
            _vm.TryCommitCellEdit(0, 0, "R900");
            _vm.SaveCommand.Execute(null);
            while (_vm.IsSaving)
            {
                await Task.Delay(10);
            }

            var reload = await _service.LoadAsync();
            Assert.True(reload.Success);
            Assert.NotNull(reload.Data);
            Assert.Equal(10, reload.Data!.Rows.Count); // 空白行持久化不蒸发
            Assert.Equal("R900", reload.Data.Rows[0]["编号"].ToString());
            Assert.True(reload.Data.Rows[1].ItemArray.All(c => string.IsNullOrEmpty(c?.ToString())));
        }

        // ══════════════ 失败弹窗反馈（v1.6：校验失败用户必须可见） ══════════════

        [Fact]
        public async Task 编号列_表头为配方编号_编辑重复_同样弹窗与拒绝_候选兼容()
        {
            // 候选列表兼容性：新建空白配方的默认表头「配方编号」链路不受影响
            await LoadTableIntoVmAsync(BuildTable(("R001", "甲"), ("R002", "乙")));

            MessageRequestEventArgs? messageRequest = null;
            _vm.MessageRequested += (_, e) => messageRequest = e;

            var committed = _vm.TryCommitCellEdit(1, 0, "R001");

            Assert.False(committed);
            Assert.NotNull(messageRequest);
        }

        [Fact]
        public async Task 位置正确性_修改后保存重载_各值仍在原位置()
        {
            // 端到端位置守护：乱序编辑 → 保存 → 重载 → 逐格断言（模拟用户「刷新」操作）
            await LoadTableIntoVmAsync(BuildTable(
                ("R001", "甲"), ("R002", "乙"), ("R003", "丙")));

            Assert.True(_vm.TryCommitCellEdit(1, 1, "乙改"));
            Assert.True(_vm.TryCommitCellEdit(2, 2, "丙备注"));

            // 等待自动保存写盘
            _vm.SaveCommand.Execute(null);
            while (_vm.IsSaving)
            {
                await Task.Delay(10);
            }
            await Task.Delay(200);

            var reload = await _service.LoadAsync();
            Assert.True(reload.Success, $"重载失败：{reload.ErrorMessage}");
            Assert.NotNull(reload.Data);
            Assert.Equal(3, reload.Data!.Rows.Count);

            // 位置断言：修改值在原位，未编辑格子不被污染
            Assert.Equal("R001", reload.Data.Rows[0]["配方编号"].ToString());
            Assert.Equal("甲", reload.Data.Rows[0]["配方名称"].ToString());
            Assert.Equal("R002", reload.Data.Rows[1]["配方编号"].ToString());
            Assert.Equal("乙改", reload.Data.Rows[1]["配方名称"].ToString());
            Assert.Equal("R003", reload.Data.Rows[2]["配方编号"].ToString());
            Assert.Equal("丙备注", reload.Data.Rows[2]["备注"].ToString());
        }
    }
}
