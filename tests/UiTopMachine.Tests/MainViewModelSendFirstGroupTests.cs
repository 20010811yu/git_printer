using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Common;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 发送命令（发送第一组配方）测试（v1.31h，标准 ModbusTcpNet）：
    /// 点击发送 → 取配方分组第 1 组，一次批量写入编号数组（ModbusTcpNet.Write("4000", array)），
    /// PLC 侧 D4000 起为 16 位 INT 连续地址，元素按顺序落连续寄存器，不含配方值；
    /// 同时按编号匹配配方表行，除编号外的列值依次写 3000 起连续寄存器（每参数一地址，非数字按 0）；
    /// 全部成功后清空对应抽屉配方输入框（状态灯按三态规则自动回落）、弹窗提醒发送成功并输出编号与配方日志；
    /// 写入失败则错误信息写入 Status 面板列表（listbox），输入框全部保留供重试；
    /// 无分组时不写 PLC 仅告警
    /// </summary>
    public class MainViewModelSendFirstGroupTests : IDisposable
    {
        private readonly SynchronizationContext? _originalContext;

        public MainViewModelSendFirstGroupTests()
        {
            _originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(_originalContext);
        }

        /// <summary>构造 VM：18 抽屉全部有料（真值与生产一致），配方在初始化后由测试写入以触发分组；配方表含 A/B 两行</summary>
        private static (MainViewModel Vm, StubLogService Log, StubPlcCommunicationService Plc, StubRecipeFileService Recipe) Create()
        {
            var log = new StubLogService();
            var plc = new StubPlcCommunicationService();

            // 配方表：编号列 + 参数列（含非数字列，按 0 发送）
            var table = new DataTable("配方");
            table.Columns.Add("编号");
            table.Columns.Add("配方名称");
            table.Columns.Add("参数1");
            table.Columns.Add("参数2");
            table.Columns.Add("备注");
            table.Rows.Add("A", "名称A", "10", "20", "备注A");
            table.Rows.Add("B", "名称B", "11", "21", "备注B");
            var recipeService = new StubRecipeFileService { Table = table };

            var drawerService = new StubDrawerService
            {
                SeedDrawers = Enumerable.Range(1, 18).Select(i => new DrawerModel
                {
                    Index = i,
                    HasMaterial = true,
                    Recipe = string.Empty
                }).ToList()
            };
            var vm = new MainViewModel(drawerService, log, plc, recipeService);
            vm.InitializeAsync().GetAwaiter().GetResult();
            return (vm, log, plc, recipeService);
        }

        [Fact]
        public async Task 发送_批量写入编号数组_清空对应输入框_弹窗提醒成功()
        {
            var (vm, log, plc, recipe) = Create();

            // 抽屉 1/3 填配方 A，抽屉 2 填配方 B → 第一组 = A 组（编号 1、3，按填入顺序）
            vm.Drawers.First(d => d.Index == 1).Recipe = "A";
            vm.Drawers.First(d => d.Index == 3).Recipe = "A";
            vm.Drawers.First(d => d.Index == 2).Recipe = "B";

            MessageRequestEventArgs? request = null;
            vm.MessageRequested += (_, r) => request = r;

            await vm.SendCommand.ExecuteAsync(null);

            // 一次批量写入：地址 "4000"（ModbusTcpNet 纯数字，16 位 INT 连续地址），
            // 数组 [1, 3] 按顺序落 4000、4001，不含配方值
            var write = Assert.Single(plc.BatchWrites.Where(w => w.Address == "4000"));
            Assert.Equal("4000", write.Address);
            Assert.Equal(new short[] { 1, 3 }, write.Values);

            // 同时批量写入配方参数：编号 "A" 行除编号外的列值依次写 3000 起连续寄存器，
            // 非数字列（名称/备注）按 0 → [0, 10, 20, 0]
            var parameterWrite = Assert.Single(plc.BatchWrites.Where(w => w.Address == "3000"));
            Assert.Equal(new short[] { 0, 10, 20, 0 }, parameterWrite.Values);

            // 成功后清空对应抽屉配方；第二组（B）不受影响
            Assert.Equal(string.Empty, vm.Drawers.First(d => d.Index == 1).Recipe);
            Assert.Equal(string.Empty, vm.Drawers.First(d => d.Index == 3).Recipe);
            Assert.Equal("B", vm.Drawers.First(d => d.Index == 2).Recipe);

            // A 组被移出分组，仅剩 B 组
            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal("B", group.RecipeName);

            // 弹窗提醒发送成功 + 日志输出编号与配方
            Assert.NotNull(request);
            Assert.Contains("发送成功", request!.Title);
            Assert.Contains("A", request.Message);
            Assert.Contains(log.Entries, e => e.Level == "Success"
                && e.Message.Contains("1, 3") && e.Message.Contains("A"));
        }

        [Fact]
        public async Task 无已填写配方_发送不写PLC不弹窗_仅告警()
        {
            var (vm, log, plc, recipe) = Create();
            var raised = false;
            vm.MessageRequested += (_, _) => raised = true;

            await vm.SendCommand.ExecuteAsync(null);

            Assert.Empty(plc.BatchWrites);
            Assert.False(raised);
            Assert.Contains(log.Entries, e => e.Level == "Warn" && e.Message.Contains("未发送"));
        }

        [Fact]
        public async Task 写入失败_错误信息进面板列表_保留输入框现场()
        {
            var (vm, log, plc, recipe) = Create();
            // 模拟批量写入失败（PLC 通讯异常）
            plc.BatchWriteResult = Result<bool>.Fail("PLC 未连接");

            vm.Drawers.First(d => d.Index == 1).Recipe = "A";
            vm.Drawers.First(d => d.Index == 3).Recipe = "A";

            await vm.SendCommand.ExecuteAsync(null);

            // 错误信息显示在 Status 面板列表（listbox），含失败原因
            var entry = Assert.Single(vm.Logs);
            Assert.Equal("错误", entry.LevelText);
            Assert.Contains("下发失败", entry.Message);
            Assert.Contains("PLC 未连接", entry.Message);

            // 失败不动任何输入框与分组，保留现场供重试
            Assert.Equal("A", vm.Drawers.First(d => d.Index == 1).Recipe);
            Assert.Equal("A", vm.Drawers.First(d => d.Index == 3).Recipe);
            Assert.Equal(2, vm.RecipeGroups[0].DrawerIndexes.Count);
            Assert.Contains(log.Entries, e => e.Level == "Error" && e.Message.Contains("下发失败"));
        }

        [Fact]
        public async Task 配方表无匹配编号行_参数不发送_错误进面板保留现场()
        {
            var (vm, log, plc, recipe) = Create();
            recipe.Table.Rows.Clear(); // 清空配方表 → 编号 "A" 无匹配行
            recipe.Table.Rows.Add("X", "名称X", "99", "0", "无");

            vm.Drawers.First(d => d.Index == 1).Recipe = "A";

            await vm.SendCommand.ExecuteAsync(null);

            // 4000 区编号已写，3000 区参数未写；错误进面板列表，输入框保留
            Assert.Single(plc.BatchWrites.Where(w => w.Address == "4000"));
            Assert.Empty(plc.BatchWrites.Where(w => w.Address == "3000"));
            var entry = Assert.Single(vm.Logs);
            Assert.Equal("错误", entry.LevelText);
            Assert.Contains("参数下发失败", entry.Message);
            Assert.Equal("A", vm.Drawers.First(d => d.Index == 1).Recipe);
        }

        [Fact]
        public async Task 发送完成_IsBusy复位_命令可再次执行()
        {
            var (vm, _, plc, _) = Create();
            vm.Drawers.First(d => d.Index == 1).Recipe = "A";

            await vm.SendCommand.ExecuteAsync(null);

            Assert.False(vm.IsBusy);
            Assert.True(vm.SendCommand.CanExecute(null));
        }
    }
}
