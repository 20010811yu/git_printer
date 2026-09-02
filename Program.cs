using System;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using UiTopMachine.Views;

namespace UiTopMachine
{
    /// <summary>
    /// 应用程序入口：组装依赖注入，启动主窗体
    /// </summary>
    internal static class Program
    {
        /// <summary>
        /// 应用主入口
        /// </summary>
        [STAThread]
        static void Main()
        {
            // WinForms 高 DPI 与视觉样式初始化（.NET 6+ 官方推荐方式）
            ApplicationConfiguration.Initialize();

            // 依赖注入容器：Service 层单例，ViewModel/View 瞬态
            var services = new ServiceCollection();
            ConfigureServices(services);

            using var provider = services.BuildServiceProvider();

            // 全局异常捕获（工业软件不允许崩溃，记录后优雅处理）
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
                MessageBox.Show($"发生未处理异常：{e.Exception.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                MessageBox.Show($"发生致命异常：{e.ExceptionObject}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);

            // 启动主窗体（构造注入 ViewModel）
            var mainForm = provider.GetRequiredService<MainForm>();
            Application.Run(mainForm);
        }

        /// <summary>
        /// 注册服务（分层依赖：View → ViewModel → Service）
        /// </summary>
        static void ConfigureServices(IServiceCollection services)
        {
            // Service 层（后续替换真实设备实现只需改这里）
            services.AddSingleton<IDrawerService, MockDrawerService>();
            services.AddSingleton<ILogService, LogService>();
            // 配方文件服务（ClosedXML 读写 D:\Printer\Data\Recipe.xlsx）
            services.AddSingleton<IRecipeFileService, RecipeFileService>();

            // ViewModel 层
            // MainViewModel 单例：RecipePageViewModel 与 FeedDrawersPage 需共享同一抽屉集合
            services.AddSingleton<MainViewModel>();
            // 各页面 VM 单例：页面懒创建多次切换时保持状态
            services.AddSingleton<NavigationViewModel>();
            services.AddSingleton<PrintPageViewModel>();
            services.AddSingleton<ImagePageViewModel>();
            services.AddSingleton<RecipePageViewModel>();

            // View 层
            services.AddTransient<MainForm>();
        }
    }
}