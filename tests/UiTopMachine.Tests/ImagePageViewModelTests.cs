using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Common;
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

            public Task<Result<bool>> LoadSolutionAsync(string? solutionPath = null) =>
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

        /// <summary>测试桩：返回无图成功结果（模拟低速图像源"无新帧"，ERR-028）</summary>
        private class NoFrameInspectionService : IImageInspectionService
        {
            public bool IsSolutionLoaded { get; set; } = true;

            public string ProcedureName => "流程1";

#pragma warning disable CS0067 // 测试桩无需真正触发事件
            public event EventHandler? SolutionLoaded;
#pragma warning restore CS0067

            public Task<Result<bool>> LoadSolutionAsync(string? solutionPath = null) =>
                Task.FromResult(Result<bool>.OK(true));

            public Task<Result<ImageInspectionResult>> RunInspectionAsync() =>
                Task.FromResult(Result<ImageInspectionResult>.OK(new ImageInspectionResult
                {
                    Image = null,
                    IsOk = true,
                    Sequence = 1
                }));

            public void Shutdown()
            {
            }
        }

        [Fact]
        public async Task 检测结果无新帧_跳过显示不更新结论不报错()
        {
            var vm = new ImagePageViewModel(_log, new NoFrameInspectionService(), _panel);

            await vm.CaptureOnceCommand.ExecuteAsync(null);

            Assert.Equal("—", vm.CurrentVerdict); // 结论不变
            Assert.Null(vm.CurrentImage); // 无新帧不出图
            Assert.DoesNotContain(_panel.Entries, e => e.Level == LogLevel.Error); // 不报错
            Assert.Contains(_log.Entries, e => e.Message.Contains("无新帧")); // 仅记 Info 日志
        }

        /// <summary>测试桩：恒定产出结果图的检测服务（可配置 OK/NG 与延时）</summary>
        private class StaticInspectionService : IImageInspectionService
        {
            private readonly TaskDelayMs _delay;

            public StaticInspectionService(TaskDelayMs delay = TaskDelayMs.Mock)
            {
                _delay = delay;
            }

            public bool IsSolutionLoaded { get; set; } = true;

            public string ProcedureName => "流程1";

#pragma warning disable CS0067 // 测试桩无需真正触发事件
            public event EventHandler? SolutionLoaded;
#pragma warning restore CS0067

            public Task<Result<bool>> LoadSolutionAsync(string? solutionPath = null) =>
                Task.FromResult(Result<bool>.OK(true));

            public async Task<Result<ImageInspectionResult>> RunInspectionAsync()
            {
                if (_delay == TaskDelayMs.Mock)
                {
                    await Task.Delay(300); // 同 Mock 节奏
                }

                return Result<ImageInspectionResult>.OK(new ImageInspectionResult
                {
                    Image = new System.Drawing.Bitmap(8, 8),
                    IsOk = true,
                    Sequence = 1
                });
            }

            public void Shutdown()
            {
            }

            /// <summary>延时档位枚举（避免魔法数字）</summary>
            public enum TaskDelayMs
            {
                None = 0,
                Mock = 300
            }
        }

        [Fact]
        public async Task 单次检测_更新结果图与结论()
        {
            var vm = new ImagePageViewModel(_log, new StaticInspectionService(StaticInspectionService.TaskDelayMs.None), _panel);

            await vm.CaptureOnceCommand.ExecuteAsync(null);

            Assert.NotNull(vm.CurrentImage);
            Assert.Equal("OK", vm.CurrentVerdict);
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
            while (vm.CurrentImage is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            await vm.StopContinuousCommand.ExecuteAsync(null);
            Assert.False(vm.IsContinuousRunning);
            Assert.NotNull(vm.CurrentImage);
        }

        [Fact]
        public async Task 未加载方案_单次与连续检测命令不可用()
        {
            var vm = new ImagePageViewModel(_log, new FailingInspectionService(), _panel);

            Assert.False(vm.CaptureOnceCommand.CanExecute(null));
            Assert.False(vm.StartContinuousCommand.CanExecute(null));

            await Task.CompletedTask;
        }

        [Fact]
        public async Task 页面停机_Shutdown取消连续检测()
        {
            var vm = new ImagePageViewModel(_log, new StaticInspectionService(), _panel);

            await vm.StartContinuousCommand.ExecuteAsync(null);

            // 等待产生首次结果（证明循环在跑）
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (vm.CurrentImage is null && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20);
            }

            vm.Shutdown();

            // 等在途结果落地（桩延时 300ms 内），再静置超过一个轮询周期：
            // 取消生效的判据 = 结果图引用不再变化（桩每次 Run 都产出新 Bitmap）
            await Task.Delay(500);
            var imageAtRest = vm.CurrentImage;
            await Task.Delay(1300);

            Assert.Same(imageAtRest, vm.CurrentImage);
        }

        // ══════════════ 保存图片（ERR-032） ══════════════

        /// <summary>订阅 VM 的 MessageRequested 并收集全部弹窗请求供断言</summary>
        private static List<MessageRequestEventArgs> CaptureMessages(ImagePageViewModel vm)
        {
            var messages = new List<MessageRequestEventArgs>();
            vm.MessageRequested += (_, request) => messages.Add(request);
            return messages;
        }

        [Fact]
        public async Task ERR032_保存图片_有图传路径_文件落盘并提示保存成功()
        {
            var vm = new ImagePageViewModel(_log, new StaticInspectionService(StaticInspectionService.TaskDelayMs.None), _panel);
            await vm.CaptureOnceCommand.ExecuteAsync(null);
            var messages = CaptureMessages(vm);

            var dir = Path.Combine(Path.GetTempPath(), "uitop_err032_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "save.png");
                await vm.SaveImageCommand.ExecuteAsync(path);

                Assert.True(File.Exists(path)); // 文件落盘
                using var loaded = new System.Drawing.Bitmap(path); // 内容可加载
                Assert.True(loaded.Width > 0);
                Assert.Contains(messages, m => m.Title == "保存成功" && m.Message.Contains(path)); // 弹窗告知成功（含路径）
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task ERR032_保存图片_取消对话框_null路径_不落盘不弹窗()
        {
            var vm = new ImagePageViewModel(_log, new StaticInspectionService(StaticInspectionService.TaskDelayMs.None), _panel);
            await vm.CaptureOnceCommand.ExecuteAsync(null);
            var messages = CaptureMessages(vm);

            await vm.SaveImageCommand.ExecuteAsync(null); // 用户取消保存对话框

            Assert.Empty(messages); // 纯取消不弹任何提示
            Assert.DoesNotContain(_log.Entries, e => e.Message.Contains("保存"));
        }

        [Fact]
        public async Task ERR032_保存图片_无图_提示无图可保存且不落盘()
        {
            var vm = new ImagePageViewModel(_log, new NoFrameInspectionService(), _panel);
            await vm.CaptureOnceCommand.ExecuteAsync(null); // 无新帧 → 无图
            var messages = CaptureMessages(vm);

            var path = Path.Combine(Path.GetTempPath(), "uitop_err032_never_" + Guid.NewGuid().ToString("N") + ".png");
            await vm.SaveImageCommand.ExecuteAsync(path);

            Assert.False(File.Exists(path));
            Assert.Contains(messages, m => m.Message.Contains("无图片可保存"));
        }
    }
}
