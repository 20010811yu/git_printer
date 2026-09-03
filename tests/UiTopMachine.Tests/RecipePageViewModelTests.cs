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
            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, recursive: true);
                }
            }
            catch (IOException)
            {
                // 句柄释放延迟：忽略清理失败
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
    }
}
