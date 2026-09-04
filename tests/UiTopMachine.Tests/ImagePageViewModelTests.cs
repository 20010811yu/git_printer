using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 图像页 VM 测试：方案加载联动（状态/命令）、单次检测计数、连续检测启停。
    /// 连续检测的结果经 VM 捕获的 _uiContext 调度，测试用 Immediate 上下文同步执行（ERR-023 守护环境）
    /// </summary>
    public class ImagePageViewModelTests : IDisposable
    {
        private readonly ImageInspectionService _inspectionService = new();
        private readonly StubLogService _log = new();
        private readonly SynchronizationContext? _originalContext;

        public ImagePageViewModelTests()
        {
            _originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        }

        public void Dispose()
        {
            _inspectionService.Shutdown();
            SynchronizationContext.SetSynchronizationContext(_originalContext);
        }

        private ImagePageViewModel CreateViewModel() => new(_log, _inspectionService);

        [Fact]
        public async Task 初始状态_方案未加载_加载后状态翻转()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(); // 自动加载方案

            Assert.True(vm.IsSolutionLoaded);
            Assert.Contains("方案已加载", vm.SolutionStatusText);
        }

        [Fact]
        public async Task 单次检测_更新结果图与计数_互不干扰()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync();

            await vm.CaptureOnceCommand.ExecuteAsync(null);
            await vm.CaptureOnceCommand.ExecuteAsync(null);

            Assert.Equal(2, vm.OkCount + vm.NgCount);
            Assert.NotNull(vm.CurrentImage);
            Assert.Contains(vm.CurrentVerdict, new[] { "OK", "NG" });
        }

        [Fact]
        public async Task 连续检测_开启后产生结果_可停止()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync();

            await vm.StartContinuousCommand.ExecuteAsync(null);
            Assert.True(vm.IsContinuousRunning);

            // 等待产生至少一次检测结果（Mock 检测间隔 300ms）
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (vm.OkCount + vm.NgCount == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            await vm.StopContinuousCommand.ExecuteAsync(null);
            Assert.False(vm.IsContinuousRunning);
            Assert.True(vm.OkCount + vm.NgCount >= 1);
        }

        [Fact]
        public async Task 未加载方案_单次与连续检测命令不可用()
        {
            var vm = CreateViewModel();

            Assert.False(vm.CaptureOnceCommand.CanExecute(null));
            Assert.False(vm.StartContinuousCommand.CanExecute(null));
            Assert.True(vm.LoadSolutionCommand.CanExecute(null));

            await Task.CompletedTask;
        }

        [Fact]
        public async Task 页面停机_Shutdown取消连续检测()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync();
            await vm.StartContinuousCommand.ExecuteAsync(null);

            vm.Shutdown();

            // 后台循环被取消（不再产生新结果）
            var countBefore = vm.OkCount + vm.NgCount;
            await Task.Delay(400);
            Assert.True(vm.OkCount + vm.NgCount <= countBefore + 1);
        }
    }
}
