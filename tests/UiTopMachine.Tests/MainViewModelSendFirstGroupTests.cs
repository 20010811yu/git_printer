using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 发送命令（发送第一组配方）测试（v1.31）：
    /// 点击发送 → 取配方分组第 1 组，批量写入抽屉编号序列到 PLC D4000（不含配方值）；
    /// 写入成功后清空对应抽屉配方输入框（状态灯按三态规则自动回落）并输出编号与配方日志；
    /// 写入失败不清空输入框；无分组时不写 PLC 仅告警
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

        /// <summary>构造 VM：18 抽屉全部有料（真值与生产一致），配方在初始化后由测试写入以触发分组</summary>
        private static (MainViewModel Vm, StubLogService Log, StubPlcCommunicationService Plc) Create()
        {
            var log = new StubLogService();
            var plc = new StubPlcCommunicationService();
            var drawerService = new StubDrawerService
            {
                SeedDrawers = Enumerable.Range(1, 18).Select(i => new DrawerModel
                {
                    Index = i,
                    HasMaterial = true,
                    Recipe = string.Empty
                }).ToList()
            };
            var vm = new MainViewModel(drawerService, log, plc);
            vm.InitializeAsync().GetAwaiter().GetResult();
            return (vm, log, plc);
        }

        [Fact]
        public async Task 发送_批量写入第一组编号到D4000_清空对应输入框并输出日志()
        {
            var (vm, log, plc) = Create();

            // 抽屉 1/3 填配方 A，抽屉 2 填配方 B → 第一组 = A 组（编号 1、3，按填入顺序）
            vm.Drawers.First(d => d.Index == 1).Recipe = "A";
            vm.Drawers.First(d => d.Index == 3).Recipe = "A";
            vm.Drawers.First(d => d.Index == 2).Recipe = "B";

            await vm.SendCommand.ExecuteAsync(null);

            // 批量写入：地址 D4000，值为编号序列 [1, 3]（不含配方值），且只写了一次
            var write = Assert.Single(plc.BatchWrites);
            Assert.Equal("D4000", write.Address);
            Assert.Equal(new short[] { 1, 3 }, write.Values);

            // 成功后清空对应抽屉配方；第二组（B）不受影响
            Assert.Equal(string.Empty, vm.Drawers.First(d => d.Index == 1).Recipe);
            Assert.Equal(string.Empty, vm.Drawers.First(d => d.Index == 3).Recipe);
            Assert.Equal("B", vm.Drawers.First(d => d.Index == 2).Recipe);

            // A 组被移出分组，仅剩 B 组
            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal("B", group.RecipeName);

            // 日志输出已发送编号与对应配方
            Assert.Contains(log.Entries, e => e.Level == "Success"
                && e.Message.Contains("1, 3") && e.Message.Contains("A"));
        }

        [Fact]
        public async Task 无已填写配方_发送不写PLC_仅告警()
        {
            var (vm, log, plc) = Create();

            await vm.SendCommand.ExecuteAsync(null);

            Assert.Empty(plc.BatchWrites);
            Assert.Contains(log.Entries, e => e.Level == "Warn" && e.Message.Contains("未发送"));
        }

        [Fact]
        public async Task PLC写入失败_保留输入框现场_错误日志()
        {
            var (vm, log, plc) = Create();
            plc.BatchWriteResult = UiTopMachine.Services.Interfaces.Result<bool>.Fail("PLC 未连接");
            vm.Drawers.First(d => d.Index == 1).Recipe = "A";
            vm.Drawers.First(d => d.Index == 3).Recipe = "A";

            await vm.SendCommand.ExecuteAsync(null);

            Assert.Single(plc.BatchWrites);
            // 失败不动输入框与分组，保留现场供重试
            Assert.Equal("A", vm.Drawers.First(d => d.Index == 1).Recipe);
            Assert.Equal("A", vm.Drawers.First(d => d.Index == 3).Recipe);
            Assert.Equal(2, vm.RecipeGroups[0].DrawerIndexes.Count);
            Assert.Contains(log.Entries, e => e.Level == "Error" && e.Message.Contains("下发失败"));
        }

        [Fact]
        public async Task 发送完成_IsBusy复位_命令可再次执行()
        {
            var (vm, _, plc) = Create();
            vm.Drawers.First(d => d.Index == 1).Recipe = "A";

            await vm.SendCommand.ExecuteAsync(null);

            Assert.False(vm.IsBusy);
            Assert.True(vm.SendCommand.CanExecute(null));
        }
    }
}
