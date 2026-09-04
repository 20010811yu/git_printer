using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 主界面视图模型：抽屉集合、命令、日志、状态汇总
    /// </summary>
    public class MainViewModel : ObservableObject
    {
        private readonly IDrawerService _drawerService;
        private readonly ILogService _logService;
        private readonly IPlcCommunicationService _plcService;

        private string _companyTitle = "上海寅铠精密机械制造有限公司";
        private string _pageTitle = "进料抽屉状态";
        private bool _isBusy;
        private LogEntryViewModel? _latestLog;

        /// <summary>
        /// 公司标题
        /// </summary>
        public string CompanyTitle
        {
            get => _companyTitle;
            set => SetProperty(ref _companyTitle, value);
        }

        /// <summary>
        /// 页面标题
        /// </summary>
        public string PageTitle
        {
            get => _pageTitle;
            set => SetProperty(ref _pageTitle, value);
        }

        /// <summary>
        /// 是否忙碌（发送命令执行中，防重复点击）
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        /// <summary>
        /// 抽屉集合（18 个）
        /// </summary>
        public ObservableCollection<DrawerItemViewModel> Drawers { get; } = new();

        /// <summary>
        /// 日志集合（最新在前）
        /// </summary>
        public ObservableCollection<LogEntryViewModel> Logs { get; } = new();

        /// <summary>
        /// 最新一条日志摘要（显示在 Status 区顶部）
        /// </summary>
        public LogEntryViewModel? LatestLog
        {
            get => _latestLog;
            private set => SetProperty(ref _latestLog, value);
        }

        /// <summary>就绪（绿）抽屉数</summary>
        public int ReadyCount => Drawers.Count(d => d.Status == DrawerStatus.Ready);

        /// <summary>预警（黄）抽屉数</summary>
        public int WarningCount => Drawers.Count(d => d.Status == DrawerStatus.Warning);

        /// <summary>空闲（灰）抽屉数</summary>
        public int IdleCount => Drawers.Count(d => d.Status == DrawerStatus.Idle);

        /// <summary>
        /// 发送命令：将全部配方下发到设备（异步，IsBusy 防重复）
        /// </summary>
        public AsyncRelayCommand SendCommand { get; }

        /// <summary>
        /// 退出命令（左上角退出按钮）
        /// </summary>
        public RelayCommand ExitCommand { get; }

        /// <summary>
        /// 构造：依赖注入服务
        /// </summary>
        public MainViewModel(IDrawerService drawerService, ILogService logService, IPlcCommunicationService plcService)
        {
            _drawerService = drawerService ?? throw new ArgumentNullException(nameof(drawerService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _plcService = plcService ?? throw new ArgumentNullException(nameof(plcService));

            SendCommand = new AsyncRelayCommand(_ => SendAllRecipesAsync(), _ => !IsBusy);
            ExitCommand = new RelayCommand(_ => ExitApplication());

            // 订阅日志事件
            _logService.LogEmitted += OnLogEmitted;

            // 订阅抽屉状态变化推送
            _drawerService.DrawerChanged += OnDrawerChanged;

            // 订阅 PLC 连接状态变化（写入日志集合，显示在 Status 列表面板）
            _plcService.ConnectionStateChanged += OnPlcConnectionStateChanged;

            _logService.Info("系统初始化完成，正在读取抽屉状态…");
        }

        /// <summary>
        /// 初始化：加载抽屉 + 启动监控（由 View 的 Load 事件调用）
        /// </summary>
        public async Task InitializeAsync()
        {
            var result = await _drawerService.GetAllDrawersAsync();
            if (result.Success && result.Data is not null)
            {
                Drawers.Clear();
                foreach (var model in result.Data)
                {
                    Drawers.Add(new DrawerItemViewModel(model.Index, model.HasMaterial, model.Recipe, _logService));
                }

                RefreshStatistics();
                _logService.Success($"已加载 {Drawers.Count} 个抽屉状态");
            }
            else
            {
                _logService.Error(result.ErrorMessage ?? "加载抽屉状态失败");
            }

            _drawerService.StartMonitoring();

            // 启动 PLC 后台自动连接（连接成功后心跳自动启动，状态经事件写入日志列表）
            await _plcService.StartAsync();
        }

        /// <summary>
        /// 停机：关闭心跳并断开 PLC 连接（由主窗体关闭事件调用）
        /// </summary>
        public async Task ShutdownAsync()
        {
            try
            {
                await _plcService.StopAsync();
            }
            catch (Exception ex)
            {
                _logService.Error($"PLC 服务停止异常：{ex.Message}");
            }
        }

        /// <summary>
        /// PLC 连接状态变化处理：按状态分级写入日志（LogService 内部已调度 UI 线程）
        /// </summary>
        private void OnPlcConnectionStateChanged(object? sender, PlcConnectionEventArgs e)
        {
            switch (e.State)
            {
                case PlcConnectionState.Connected:
                    _logService.Success($"PLC：{e.Message}");
                    break;
                case PlcConnectionState.Connecting:
                    _logService.Info($"PLC：{e.Message}");
                    break;
                case PlcConnectionState.HeartbeatLost:
                    _logService.Error($"PLC：{e.Message}");
                    break;
                case PlcConnectionState.Disconnected:
                default:
                    _logService.Warn($"PLC：{e.Message}");
                    break;
            }
        }

        /// <summary>
        /// 发送全部配方（异步，不阻塞 UI）
        /// </summary>
        private async Task SendAllRecipesAsync()
        {
            IsBusy = true;
            try
            {
                _logService.Info("开始下发全部配方…");

                var tasks = Drawers
                    .Where(d => !string.IsNullOrWhiteSpace(d.Recipe))
                    .Select(d => _drawerService.SendRecipeAsync(d.Index, d.Recipe));

                var results = await Task.WhenAll(tasks);
                var okCount = results.Count(r => r.Success);

                if (okCount == results.Length)
                {
                    _logService.Success($"全部配方下发完成（{okCount}/{results.Length}）");
                }
                else
                {
                    _logService.Warn($"配方下发完成，存在失败（成功 {okCount}/{results.Length}）");
                }
            }
            catch (Exception ex)
            {
                // 捕获业务异常，设置错误提示（不弹窗，记录日志）
                _logService.Error($"下发配方异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 退出应用（UI 层可重写关闭行为，此处仅记录日志后退出）
        /// </summary>
        private void ExitApplication()
        {
            _logService.Info("用户点击退出，应用即将关闭…");
            Application.Exit();
        }

        /// <summary>
        /// 抽屉状态推送处理（后台线程事件 → 调度到 UI 线程）
        /// </summary>
        private void OnDrawerChanged(object? sender, DrawerModel model)
        {
            // WinForms 控件绑定要求 UI 线程，通过 WindowsFormsSynchronizationContext 调度
            var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            context.Post(_ =>
            {
                var drawer = Drawers.FirstOrDefault(d => d.Index == model.Index);
                drawer?.UpdateFromModel(model);
                RefreshStatistics();
            }, null);
        }

        /// <summary>
        /// 日志事件处理（调度到 UI 线程，最新日志置顶显示）
        /// </summary>
        private void OnLogEmitted(object? sender, LogEntryModel entry)
        {
            var context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            context.Post(_ =>
            {
                var vm = new LogEntryViewModel(entry);
                Logs.Insert(0, vm);
                LatestLog = vm;

                // 限制日志条数，防止内存膨胀
                while (Logs.Count > 200)
                {
                    Logs.RemoveAt(Logs.Count - 1);
                }
            }, null);
        }

        /// <summary>
        /// 刷新统计数字（状态汇总）
        /// </summary>
        private void RefreshStatistics()
        {
            OnPropertyChanged(nameof(ReadyCount));
            OnPropertyChanged(nameof(WarningCount));
            OnPropertyChanged(nameof(IdleCount));
        }
    }
}