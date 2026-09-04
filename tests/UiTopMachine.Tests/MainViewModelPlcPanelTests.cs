using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 测试桩：IDrawerService —— 初始抽屉可配置（默认空），不推送随机状态
    /// </summary>
    public class StubDrawerService : IDrawerService
    {
#pragma warning disable CS0067 // 测试桩无需真正触发事件
        public event EventHandler<DrawerModel>? DrawerChanged;
#pragma warning restore CS0067

        /// <summary>初始抽屉列表（GetAllDrawersAsync 返回其副本）</summary>
        public List<DrawerModel> SeedDrawers { get; set; } = new();

        public Task<Result<List<DrawerModel>>> GetAllDrawersAsync() =>
            Task.FromResult(Result<List<DrawerModel>>.OK(SeedDrawers
                .Select(d => new DrawerModel { Index = d.Index, HasMaterial = d.HasMaterial, Recipe = d.Recipe })
                .ToList()));

        public Task<Result<bool>> SendRecipeAsync(int drawerIndex, string recipe) =>
            Task.FromResult(Result<bool>.OK(true));

        public void StartMonitoring()
        {
        }
    }

    /// <summary>
    /// 测试桩：IPlcCommunicationService —— 记录启停调用，供测试手动触发状态/物料事件
    /// </summary>
    public class StubPlcCommunicationService : IPlcCommunicationService
    {
        public event EventHandler<PlcConnectionEventArgs>? ConnectionStateChanged;

        public event EventHandler<DrawerMaterialsChangedEventArgs>? DrawerMaterialsChanged;

        public PlcConnectionState State { get; set; } = PlcConnectionState.Disconnected;

        public string Target { get; set; } = "127.0.0.1:502 站号1";

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Task StartAsync()
        {
            StartCalls++;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            StopCalls++;
            return Task.CompletedTask;
        }

        public Task<Result<bool>> StartHeartbeatAsync() => Task.FromResult(Result<bool>.OK(true));

        public Task<Result<bool>> StopHeartbeatAsync() => Task.FromResult(Result<bool>.OK(true));

        public Task<Result<short>> ReadRegisterAsync(string address) => Task.FromResult(Result<short>.OK(0));

        public Task<Result<bool>> WriteRegisterAsync(string address, short value) => Task.FromResult(Result<bool>.OK(true));

        /// <summary>模拟 PLC 状态变化（同步触发事件，事件回调内的 UI 调度由测试线程的即时 SynchronizationContext 执行）</summary>
        public void Raise(PlcConnectionState state, string message)
        {
            State = state;
            ConnectionStateChanged?.Invoke(this, new PlcConnectionEventArgs { State = state, Message = message });
        }

        /// <summary>模拟 PLC 抽屉物料推送（同步触发事件）</summary>
        public void RaiseMaterials(bool[] values) =>
            DrawerMaterialsChanged?.Invoke(this, new DrawerMaterialsChangedEventArgs { Values = values });
    }

    /// <summary>
    /// 即时 SynchronizationContext：Post/Send 在当前线程同步执行（测试无 WinForms 消息泵，替代 WindowsFormsSynchronizationContext）
    /// </summary>
    public class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);

        public override void Send(SendOrPostCallback d, object? state) => d(state);
    }

    /// <summary>
    /// MainViewModel Status 面板过滤测试（v1.11）：
    /// 面板只存 PLC 对接信息（连接成功提示 + 错误），一般系统操作日志不再进入面板
    /// </summary>
    public class MainViewModelPlcPanelTests : IDisposable
    {
        private readonly SynchronizationContext? _originalContext;

        public MainViewModelPlcPanelTests()
        {
            _originalContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new ImmediateSynchronizationContext());
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(_originalContext);
        }

        private static (MainViewModel Vm, StubLogService Log, StubPlcCommunicationService Plc) Create()
        {
            var log = new StubLogService();
            var plc = new StubPlcCommunicationService();
            var vm = new MainViewModel(new StubDrawerService(), log, plc);
            return (vm, log, plc);
        }

        [Fact]
        public async Task 一般系统操作日志_不进入面板()
        {
            var (vm, log, plc) = Create();
            await vm.InitializeAsync();

            // 初始化与业务过程产生的一般日志（仅落文件）
            log.Info("系统初始化完成，正在读取抽屉状态…");
            log.Success("已加载 0 个抽屉状态");
            log.Info("开始下发全部配方…");

            Assert.Empty(vm.Logs);
        }

        [Fact]
        public async Task PLC连接成功_进入面板且为成功级()
        {
            var (vm, _, plc) = Create();
            await vm.InitializeAsync();

            plc.Raise(PlcConnectionState.Connected, "PLC 已连接，心跳已启动");

            var entry = Assert.Single(vm.Logs);
            Assert.Contains("PLC 已连接，心跳已启动", entry.Message);
            Assert.Equal("成功", entry.LevelText);
            Assert.Same(entry, vm.LatestLog);
        }

        [Fact]
        public async Task PLC连接失败与心跳丢失_进入面板且为错误级()
        {
            var (vm, _, plc) = Create();
            await vm.InitializeAsync();

            plc.Raise(PlcConnectionState.Disconnected, "PLC 连接失败（第 1 次）：模拟连接失败，5 秒后自动重试");
            plc.Raise(PlcConnectionState.HeartbeatLost, "PLC 心跳丢失：读心跳寄存器 101 连续 5 个周期无变化，即将断开重连");

            Assert.Equal(2, vm.Logs.Count);
            Assert.All(vm.Logs, e => Assert.Equal("错误", e.LevelText));
            // 最新在前：心跳丢失是最新一条
            Assert.Contains("心跳丢失", vm.Logs[0].Message);
            Assert.Contains("连接失败", vm.Logs[1].Message);
        }

        [Fact]
        public async Task PLC连接中过程信息_不进入面板()
        {
            var (vm, _, plc) = Create();
            await vm.InitializeAsync();

            plc.Raise(PlcConnectionState.Connecting, "PLC 连接中…");

            Assert.Empty(vm.Logs);
        }

        [Fact]
        public async Task 混合日志_面板仅存PLC条目且全部落文件()
        {
            var (vm, log, plc) = Create();
            await vm.InitializeAsync();

            log.Info("一般操作 A");
            plc.Raise(PlcConnectionState.Connected, "PLC 已连接，心跳已启动");
            log.Warn("一般操作 B");
            plc.Raise(PlcConnectionState.HeartbeatLost, "PLC 心跳丢失，即将断开重连");
            log.Error("一般操作 C");

            Assert.Equal(2, vm.Logs.Count);
            Assert.All(vm.Logs, e => Assert.StartsWith("PLC：", e.Message));
            // 全部状态（含 PLC）仍应写入文件日志流
            Assert.Contains(log.Entries, e => e.Message.Contains("一般操作 A"));
            Assert.Contains(log.Entries, e => e.Message.Contains("一般操作 B"));
            Assert.Contains(log.Entries, e => e.Message.Contains("一般操作 C"));
            Assert.Contains(log.Entries, e => e.Message.Contains("PLC 已连接"));
        }

        [Fact]
        public async Task InitializeAsync_启动PLC服务_ShutdownAsync_停止PLC服务()
        {
            var (vm, _, plc) = Create();

            await vm.InitializeAsync();
            Assert.Equal(1, plc.StartCalls);

            await vm.ShutdownAsync();
            Assert.Equal(1, plc.StopCalls);
        }

        [Fact]
        public async Task PLC连接状态变化_刷新面板顶部状态行()
        {
            var (vm, _, plc) = Create();
            await vm.InitializeAsync();

            // 初始：未连接（红）
            Assert.Equal("未连接", vm.PlcStatusText);
            Assert.Equal(LogLevel.Error, vm.PlcStatusLevel);

            plc.Raise(PlcConnectionState.Connecting, "PLC 连接中…");
            Assert.Equal("连接中…", vm.PlcStatusText);
            Assert.Equal(LogLevel.Warning, vm.PlcStatusLevel);

            plc.Raise(PlcConnectionState.Connected, "PLC 已连接，心跳已启动");
            Assert.Contains(plc.Target, vm.PlcStatusText);
            Assert.StartsWith("已连接", vm.PlcStatusText);
            Assert.Equal(LogLevel.Success, vm.PlcStatusLevel);

            plc.Raise(PlcConnectionState.HeartbeatLost, "PLC 心跳丢失，即将断开重连");
            Assert.Equal("心跳丢失，自动重连中", vm.PlcStatusText);
            Assert.Equal(LogLevel.Error, vm.PlcStatusLevel);

            plc.Raise(PlcConnectionState.Disconnected, "PLC 连接失败，5 秒后自动重试");
            Assert.Equal("未连接（自动重连中）", vm.PlcStatusText);
            Assert.Equal(LogLevel.Error, vm.PlcStatusLevel);
        }

        [Fact]
        public void 后台线程触发PLC事件_状态行仍更新_守护ERR023()
        {
            var log = new StubLogService();
            var plc = new StubPlcCommunicationService();
            var vm = new MainViewModel(new StubDrawerService(), log, plc);

            // 模拟生产场景：PLC 事件从后台线程触发（后台线程 SynchronizationContext.Current 为 null）。
            // 回归守护：VM 必须用构造时捕获的上下文调度，而不是在事件线程现取（现取会新建无消息泵的
            // WindowsFormsSynchronizationContext，Post 回调永远不执行，界面永远显示初始"未连接"，ERR-023）
            SynchronizationContext? observed = SynchronizationContext.Current;
            var thread = new Thread(() =>
            {
                observed = SynchronizationContext.Current;
                plc.Raise(PlcConnectionState.Connected, "PLC 已连接，心跳已启动");
            });
            thread.Start();
            thread.Join();

            Assert.Null(observed); // 前置确认：事件确实来自无上下文的后台线程
            Assert.Contains("已连接", vm.PlcStatusText);
            Assert.Equal(LogLevel.Success, vm.PlcStatusLevel);
        }

        [Fact]
        public async Task PLC物料推送_更新对应抽屉有料状态_配方保留()
        {
            var log = new StubLogService();
            var plc = new StubPlcCommunicationService();
            var drawerService = new StubDrawerService
            {
                SeedDrawers = Enumerable.Range(1, 18).Select(i => new DrawerModel
                {
                    Index = i,
                    HasMaterial = false,
                    Recipe = i <= 2 ? "R-001" : string.Empty
                }).ToList()
            };
            var vm = new MainViewModel(drawerService, log, plc);
            await vm.InitializeAsync();

            // PLC 推送：下标 1=抽屉1 有料；下标 5=抽屉5 有料；下标 0 不使用；其余无料
            var values = new bool[19];
            values[1] = true;
            values[5] = true;
            plc.RaiseMaterials(values);

            // 抽屉 1：有料 + 有配方 → 就绪；配方保留用户侧输入不被覆盖
            var drawer1 = vm.Drawers.First(d => d.Index == 1);
            Assert.True(drawer1.HasMaterial);
            Assert.Equal("R-001", drawer1.Recipe);
            Assert.Equal(DrawerStatus.Ready, drawer1.Status);

            // 抽屉 2：PLC 推无料，配方保留 → 有配方无料 = 预警
            var drawer2 = vm.Drawers.First(d => d.Index == 2);
            Assert.False(drawer2.HasMaterial);
            Assert.Equal("R-001", drawer2.Recipe);
            Assert.Equal(DrawerStatus.Warning, drawer2.Status);

            // 抽屉 5：有料无配方 = 预警
            Assert.True(vm.Drawers.First(d => d.Index == 5).HasMaterial);

            // 汇总：1 就绪；抽屉 2/5 + …… 预警 3 个（2、5，另一个为——无）；其余 15 个空闲
            Assert.Equal(1, vm.ReadyCount);
            Assert.Equal(2, vm.WarningCount);
            Assert.Equal(15, vm.IdleCount);
        }
    }
}
