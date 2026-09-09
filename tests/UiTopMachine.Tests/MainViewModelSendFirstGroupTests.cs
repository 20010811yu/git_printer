using System;
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
    /// 发送命令（发送第一组配方）测试（v1.31b）：
    /// 点击发送 → 取配方分组第 1 组，逐抽屉写入编号到独立地址（抽屉 i → D(4000+i-1)，即
    /// 抽屉 1→D4000、抽屉 3→D4002，不含配方值）；全部成功后清空对应抽屉配方输入框（状态灯按
    /// 三态规则自动回落）、弹窗提醒发送成功并输出编号与配方日志；
    /// 任一失败则错误信息写入 Status 面板列表（listbox），输入框全部保留供重试；
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
        public async Task 发送_逐抽屉写独立地址_清空对应输入框_弹窗提醒成功()
        {
            var (vm, log, plc) = Create();

            // 抽屉 1/3 填配方 A，抽屉 2 填配方 B → 第一组 = A 组（编号 1、3，按填入顺序）
            vm.Drawers.First(d => d.Index == 1).Recipe = "A";
            vm.Drawers.First(d => d.Index == 3).Recipe = "A";
            vm.Drawers.First(d => d.Index == 2).Recipe = "B";

            MessageRequestEventArgs? request = null;
            vm.MessageRequested += (_, r) => request = r;

            await vm.SendCommand.ExecuteAsync(null);

            // 逐抽屉独立地址：抽屉 1→D4000=1，抽屉 3→D4002=3（不含配方值），共 2 次写入
            Assert.Equal(new[] { ("D4000", (short)1), ("D4002", (short)3) }, plc.RegisterWrites);

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
            var (vm, log, plc) = Create();
            var raised = false;
            vm.MessageRequested += (_, _) => raised = true;

            await vm.SendCommand.ExecuteAsync(null);

            Assert.Empty(plc.RegisterWrites);
            Assert.False(raised);
            Assert.Contains(log.Entries, e => e.Level == "Warn" && e.Message.Contains("未发送"));
        }

        [Fact]
        public async Task 写入失败_错误信息进面板列表_保留输入框现场()
        {
            var (vm, log, plc) = Create();
            // 抽屉 3 写入失败（模拟 PLC 通讯异常），抽屉 1 成功
            plc.RegisterWriteHandler = (address, _) => address == "D4002"
                ? Result<bool>.Fail("PLC 未连接")
                : Result<bool>.OK(true);

            vm.Drawers.First(d => d.Index == 1).Recipe = "A";
            vm.Drawers.First(d => d.Index == 3).Recipe = "A";

            await vm.SendCommand.ExecuteAsync(null);

            // 错误信息显示在 Status 面板列表（listbox），含失败抽屉与原因
            var entry = Assert.Single(vm.Logs);
            Assert.Equal("错误", entry.LevelText);
            Assert.Contains("下发失败", entry.Message);
            Assert.Contains("抽屉 3", entry.Message);
            Assert.Contains("PLC 未连接", entry.Message);

            // 失败不动任何输入框与分组，保留现场供重试
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
