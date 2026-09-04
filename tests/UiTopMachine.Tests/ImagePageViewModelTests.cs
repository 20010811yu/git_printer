using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 测试桩：IPanelStatusPublisher —— 记录发布到 Status 面板的条目供断言
    /// </summary>
    public class StubPanelPublisher : IPanelStatusPublisher
    {
        /// <summary>已发布的面板条目（级别, 消息）</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public void PublishPanelEntry(LogLevel level, string message)
        {
            Entries.Add((level, message));
        }
    }

    /// <summary>
    /// 图像页 VM 测试：方案加载联动（状态/命令）、单次检测计数、连续检测启停、面板状态发布。
    /// 连续检测的结果经 VM 捕获的 _uiContext 调度，测试用 Immediate 上下文同步执行（ERR-023 守护环境）
    /// </summary>
    public class ImagePageViewModelTests : IDisposable
    {
        private readonly ImageInspectionService _inspectionService = new();
        private readonly StubLogService _log = new();
        private readonly StubPanelPublisher _panel = new();
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

        private ImagePageViewModel CreateViewModel() => new(_log, _inspectionService, _panel);

        [Fact]
        public async Task 初始状态_方案未加载_加载后状态翻转()
        {
            var vm = CreateViewModel();
            await vm.InitializeAsync(); // 自动加载方案

            Assert.True(vm.IsSolutionLoaded);
            Assert.Contains("方案已加载", vm.SolutionStatusText);
        }

        [Fact]
        public async Task 方案加载成功_发布面板成功条目()
        {
            var vm = CreateViewModel();

            await vm.InitializeAsync();

            var entry = Assert.Single(_panel.Entries);
            Assert.Equal(LogLevel.Success, entry.Level);
            Assert.Contains("视觉方案加载成功", entry.Message);
        }

        /// <summary>测试桩：恒定失败的检测服务（可配置"已加载"以进入连续检测循环）</summary>
        private class FailingInspectionService : IImageInspectionService
        {
            public bool IsSolutionLoaded { get; set; }

            public string ProcedureName => "Testing";

#pragma warning disable CS0067 // 测试桩无需真正触发事件
            public event EventHandler? SolutionLoaded;
#pragma warning restore CS0067

            public Task<Result<bool>> LoadSolutionAsync(string solutionPath) =>
                Task.FromResult(Result<bool>.Fail("模拟方案文件不存在"));

            public Task<Result<ImageInspectionResult>> RunInspectionAsync() =>
                Task.FromResult(Result<ImageInspectionResult>.Fail("模拟检测运行失败"));

            public void Shutdown()
            {
            }
        }

        [Fact]
        public async Task 方案加载失败_发布含原因的错误条目()
        {
            var vm = new ImagePageViewModel(_log, new FailingInspectionService(), _panel);

            await vm.InitializeAsync();

            var entry = Assert.Single(_panel.Entries);
            Assert.Equal(LogLevel.Error, entry.Level);
            Assert.Contains("视觉方案加载失败", entry.Message);
            Assert.Contains("模拟方案文件不存在", entry.Message); // 失败的具体原因进面板
        }

        [Fact]
        public async Task 连续检测运行失败_发布含原因的错误条目()
        {
            // 已加载但检测恒失败：连续循环每轮失败 → 面板发布"视觉检测失败：原因"
            var vm = new ImagePageViewModel(_log, new FailingInspectionService { IsSolutionLoaded = true }, _panel);
            await vm.StartContinuousCommand.ExecuteAsync(null);

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!_panel.Entries.Any(e => e.Message.Contains("视觉检测失败")) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            Assert.Contains(_panel.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("视觉检测失败"));
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
