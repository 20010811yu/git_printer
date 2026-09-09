using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using UiTopMachine.Communications.Plc;
using UiTopMachine.Models;
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

            // VMControls 渲染控件依赖兜底解析（v1.26）：控件自身随程序分发，
            // 但其引用的 VM.PlatformSDKCS 等程序集仅存在于 GAC，而 .NET 10 不读 GAC——
            // 首次实例化 VmRenderControl 时经此 resolver 从 GAC 物理路径补加载（冒烟实证必要）
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

            // 全局异常模式必须最先设置——在创建任何控件/同步上下文之前，
            // 否则报"线程上已创建控件，异常模式不能再更改"导致启动即崩（v1.21 教训）
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            // 依赖注入容器：Service 层单例，ViewModel/View 瞬态
            var services = new ServiceCollection();
            ConfigureServices(services);

            using var provider = services.BuildServiceProvider();

            // 主界面 VM 单例（提前解析：全局异常处理需要向面板发布运行时错误）
            var mainViewModel = provider.GetRequiredService<MainViewModel>();

            // 全局异常捕获（工业软件不允许崩溃，记录后优雅处理）：
            // 异常信息写入 Status 面板（v1.21 面板含"程序运行时错误"）并弹窗提示
            Application.ThreadException += (_, e) =>
            {
                mainViewModel.PublishPanelEntry(LogLevel.Error, $"程序运行时错误：{e.Exception.Message}");
                MessageBox.Show($"发生未处理异常：{e.Exception.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    mainViewModel.PublishPanelEntry(LogLevel.Error, $"程序致命异常：{ex.Message}");
                }

                MessageBox.Show($"发生致命异常：{e.ExceptionObject}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

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
            // ZPL 打印服务（打印页走 Spooler RAW 连接打印机名 "zpl"，TCP 直连 192.168.1.200:9100 为备用通道；流水号持久化 D:\Printer\Data\SerialNumber.txt）
            services.AddSingleton<IPrintService, ZplPrinterService>();
            // 图像视觉检测服务（真实版 v1.24）：VisionMaster 桥接进程方案——
            // 主程序 net10.0 无法直接引用 VM SDK（.NET Framework 程序集），由 tools/VmVisionBridge（net48）
            // 承载 VmSolution SDK，本服务经命名管道收发命令与 PNG 结果图；
            // 桥接 exe 缺失/启动失败自动降级报错，Mock 实现（ImageInspectionService）保留可随时切回
            // 加密方案文件（VMENC1 壳，防方案外泄 v1.30）：服务加载时自动解密到临时目录交桥接进程，明文用完即删；
            // 由 tools/SolutionEncryptor 从明文 .sol 生成（encrypt <明文> <输出.dll>）
            var solutionPath = @"D:\Printer\VisionTesting.dll";
            // 桥接 exe 位于仓库 tools 目录（net48 独立构建）：从 bin\Debug\net10.0-windows\ 回溯 3 级到仓库根
            // （net10.0-windows → Debug → bin → GitRepo；此前误写 4 级解析到 D:\tools\ 致"桥接进程不存在"，ERR-026）
            var bridgeExePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..",
                "tools", "VmVisionBridge", "bin", "Debug", "net48", "VmVisionBridge.exe");
            services.AddSingleton<IImageInspectionService>(sp =>
                new VisionMasterBridgeInspectionService(
                    bridgeExePath: Path.GetFullPath(bridgeExePath),
                    solutionPath: solutionPath,
                    procedureName: "流程1", // 流程名：方案文件容器内实证（VisionTesting.sol 的 VmServer.xml 与 Test.sol 一致，probe 实测见 v1.24）
                    logService: sp.GetRequiredService<ILogService>()));
            // PLC 传输层（标准 ModbusTcpNet，读写均用纯数字地址；长连接。IP 当前为本地调试 127.0.0.1，真机改 192.168.1.88）
            services.AddSingleton<IPlcTransport>(new HslModbusTransport("127.0.0.1", 502, 1, useInovance: false));
            // PLC 通讯服务（后台自动连接 + 心跳：心跳与抽屉物料合一——周期读 1000×19，读成功即通讯正常并驱动抽屉，连续 3 次读失败判心跳丢失并重连）
            services.AddSingleton<IPlcCommunicationService>(sp =>
                new PlcCommunicationService(
                    sp.GetRequiredService<IPlcTransport>(),
                    target: "127.0.0.1:502 站号1",
                    materialAddress: "1000"));
            // 设备对接状态面板发布器（MainViewModel 单例实现；图像页等通过接口发布状态，避免依赖具体 VM）
            services.AddSingleton<IPanelStatusPublisher>(sp => sp.GetRequiredService<MainViewModel>());

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