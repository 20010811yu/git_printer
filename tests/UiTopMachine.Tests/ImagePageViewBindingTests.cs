using System;
using System.Reflection;
using System.Threading;
using UiTopMachine.Models;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Pages;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 图像页 View 绑定守护测试（ERR-031）：v1.26 重构遗漏了三个检测按钮 → VM 命令的绑定，
    /// 按钮点击无响应、检测永不运行、页面无图。守护不变量：ImagePage 构造完成后，
    /// 三个按钮必须已被 CommandManagerHelper 接管——未加载方案时按钮 Enabled=false
    /// （绑定会立即按 CanExecute 同步控件态；若绑定缺失，按钮保持默认可点击）。
    /// </summary>
    public class ImagePageViewBindingTests
    {
        /// <summary>
        /// VmRenderControl 静态依赖 VM.PlatformSDKCS（GAC），测试进程没有 Program.cs 的
        /// AssemblyResolve 兜底会加载失败——静态复制同一解析逻辑（ERR-030）
        /// </summary>
        static ImagePageViewBindingTests()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                try
                {
                    var name = new AssemblyName(args.Name).Name;
                    var gacDir = Path.Combine(@"C:\Windows\Microsoft.Net\assembly\GAC_MSIL", name ?? string.Empty);
                    if (name != null && Directory.Exists(gacDir))
                    {
                        var dll = Directory.GetFiles(gacDir, name + ".dll", SearchOption.AllDirectories);
                        if (dll.Length > 0)
                        {
                            return Assembly.LoadFrom(dll[0]);
                        }
                    }
                }
                catch
                {
                    // 解析失败按正常流程抛 FileNotFoundException
                }

                return null;
            };
        }

        private sealed class NotLoadedInspectionService : IImageInspectionService
        {
            public bool IsSolutionLoaded => false;
            public string ProcedureName => "TestFlow";
            public event EventHandler? SolutionLoaded;
            public Task<Result<bool>> LoadSolutionAsync(string? solutionPath = null) => Task.FromResult(Result<bool>.Fail("not loaded"));
            public Task<Result<ImageInspectionResult>> RunInspectionAsync() => Task.FromResult(Result<ImageInspectionResult>.Fail("not loaded"));
            public void Shutdown() { }
        }

        private static object? GetField(object instance, string name) =>
            instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

        [Fact]
        public void ERR031_三个检测按钮已绑定命令_未加载方案时禁用()
        {
            Control? page = null;
            var thread = new Thread(() =>
            {
                var vm = new ImagePageViewModel(
                    new StubLogService(), new NotLoadedInspectionService(), new StubPanelPublisher());
                page = new ImagePage(vm);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.NotNull(page);

            var capture = (Control?)GetField(page!, "_captureOnceButton");
            var start = (Control?)GetField(page!, "_startContinuousButton");
            var stop = (Control?)GetField(page!, "_stopContinuousButton");

            Assert.NotNull(capture);
            Assert.NotNull(start);
            Assert.NotNull(stop);

            // 方案未加载 → CanExecute=false → 绑定立即禁用按钮；
            // 绑定缺失时 AntdUI.Button 默认 Enabled=true，本断言即失败（事故形态守护）
            Assert.False(capture!.Enabled, "单次检测按钮未被命令绑定接管（方案未加载时应禁用）");
            Assert.False(start!.Enabled, "开始连续检测按钮未被命令绑定接管（方案未加载时应禁用）");
            Assert.False(stop!.Enabled, "停止连续检测按钮未被命令绑定接管（命令不可用时应禁用）");
        }
    }
}
