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
    public class MainViewModel : ObservableObject, IPanelStatusPublisher
    {
        private readonly IDrawerService _drawerService;
        private readonly ILogService _logService;
        private readonly IPlcCommunicationService _plcService;

        /// <summary>
        /// 构造时捕获的 UI 线程同步上下文：后台线程事件必须用它调度回 UI 线程。
        /// ⚠️ 不能在后台事件里现取 SynchronizationContext.Current（后台线程为 null，
        /// 新建的 WindowsFormsSynchronizationContext 无消息泵，Post 的回调永远不执行，ERR-023）
        /// </summary>
        private readonly SynchronizationContext _uiContext;

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

        private string _plcStatusText = "未连接";
        private LogLevel _plcStatusLevel = LogLevel.Error;

        /// <summary>
        /// PLC 当前连接状态文字（显示在 Status 面板顶部状态行，如「已连接 127.0.0.1:502 站号1」）
        /// </summary>
        public string PlcStatusText
        {
            get => _plcStatusText;
            private set => SetProperty(ref _plcStatusText, value);
        }

        /// <summary>
        /// PLC 当前连接状态级别（决定状态行颜色：Success=绿/Warning=橙/Error=红）
        /// </summary>
        public LogLevel PlcStatusLevel
        {
            get => _plcStatusLevel;
            private set => SetProperty(ref _plcStatusLevel, value);
        }

        private IReadOnlyList<RecipeGroupModel> _recipeGroups = new List<RecipeGroupModel>();

        /// <summary>
        /// 抽屉配方分组（派生数据）：有配方（Trim 后非空）的抽屉按配方值分组，
        /// 组内编号按填入先后顺序，组间按分组形成顺序；同抽屉多次写入只保留最后一次
        /// （供后续「按分组下发 PLC」使用，语义同参考程序 merList）
        /// </summary>
        public IReadOnlyList<RecipeGroupModel> RecipeGroups
        {
            get => _recipeGroups;
            private set => SetProperty(ref _recipeGroups, value);
        }

        /// <summary>抽屉编号 → 配方填入次序号（重写配方即更新次序，实现"最后一次写入生效"）</summary>
        private readonly Dictionary<int, int> _recipeSequences = new();

        /// <summary>配方填入次序计数器（单调递增）</summary>
        private int _recipeSequenceCounter;

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

            // 捕获 UI 线程同步上下文（构造发生在 UI 线程），供后台事件调度 UI 更新（ERR-023）
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            // 订阅抽屉状态变化推送（Mock 服务已不再启动随机监控，PLC 物料为真值源，订阅保留兼容）
            _drawerService.DrawerChanged += OnDrawerChanged;

            // 面板（Status 列表）只显示 PLC 对接信息：一般操作日志仅经 LogService 落文件，不再订阅 LogEmitted 进入 Logs
            _plcService.ConnectionStateChanged += OnPlcConnectionStateChanged;

            // 订阅 PLC 抽屉物料推送（PLC 为有料状态唯一真值源，驱动抽屉状态灯）
            _plcService.DrawerMaterialsChanged += OnDrawerMaterialsChanged;

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
                    var drawer = new DrawerItemViewModel(model.Index, model.HasMaterial, model.Recipe, _logService);
                    drawer.PropertyChanged += OnDrawerPropertyChanged; // 监听配方输入，维护分组
                    Drawers.Add(drawer);
                }

                RefreshStatistics();
                _logService.Success($"已加载 {Drawers.Count} 个抽屉状态");
            }
            else
            {
                _logService.Error(result.ErrorMessage ?? "加载抽屉状态失败");
            }

            // 启动 PLC 后台自动连接（连接成功后心跳与抽屉物料轮询自动启动）
            // 注：不再调用 Mock 的 StartMonitoring——PLC 物料轮询为有料状态唯一真值源，避免随机模拟与真值互相覆盖
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
        /// PLC 连接状态变化处理：
        /// ① 刷新面板顶部常驻状态行（PlcStatusText/PlcStatusLevel，一眼可见当前连接状态）
        /// ② 消息流（Status 列表）只存 PLC 对接信息——连接成功提示（绿）与错误（红）；
        ///    连接中等过程信息只写文件日志，不进面板；全部状态均经 LogService 落文件留痕
        /// </summary>
        private void OnPlcConnectionStateChanged(object? sender, PlcConnectionEventArgs e)
        {
            var context = _uiContext;
            context.Post(_ =>
            {
                (PlcStatusText, PlcStatusLevel) = e.State switch
                {
                    PlcConnectionState.Connected => ($"已连接 {_plcService.Target}", LogLevel.Success),
                    PlcConnectionState.Connecting => ("连接中…", LogLevel.Warning),
                    PlcConnectionState.HeartbeatLost => ("心跳丢失，自动重连中", LogLevel.Error),
                    _ => ("未连接（自动重连中）", LogLevel.Error)
                };
            }, null);

            switch (e.State)
            {
                case PlcConnectionState.Connected:
                    _logService.Success($"PLC：{e.Message}");
                    PublishPanelEntry(LogLevel.Success, e.Message);
                    break;

                case PlcConnectionState.HeartbeatLost:
                case PlcConnectionState.Disconnected:
                    _logService.Error($"PLC：{e.Message}");
                    PublishPanelEntry(LogLevel.Error, e.Message);
                    break;

                case PlcConnectionState.Connecting:
                default:
                    _logService.Info($"PLC：{e.Message}");
                    break;
            }
        }

        /// <inheritdoc />
        public void PublishPanelEntry(LogLevel level, string message)
        {
            _uiContext.Post(_ => InsertPanelEntry(level, $"PLC：{message}"), null);
        }

        /// <summary>
        /// 面板条目插入（必须已经由 PublishPanelEntry 调度到 UI 线程后执行）
        /// </summary>
        private void InsertPanelEntry(LogLevel level, string message)
        {
            var vm = new LogEntryViewModel(new LogEntryModel { Level = level, Message = message });
            Logs.Insert(0, vm);
            LatestLog = vm;

            // 限制面板条数，防止内存膨胀
            while (Logs.Count > 200)
            {
                Logs.RemoveAt(Logs.Count - 1);
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
        /// PLC 抽屉物料推送处理（后台线程事件 → 调度到 UI 线程）：
        /// 数组下标 1~18 对应抽屉 1~18（下标 0 不使用），复用抽屉更新逻辑（只同步物料、配方保留用户输入），
        /// 批量更新后统一刷新统计
        /// </summary>
        private void OnDrawerMaterialsChanged(object? sender, DrawerMaterialsChangedEventArgs e)
        {
            var context = _uiContext;
            context.Post(_ =>
            {
                for (int i = 1; i < e.Values.Count; i++)
                {
                    var drawer = Drawers.FirstOrDefault(d => d.Index == i);
                    drawer?.UpdateFromModel(new DrawerModel { Index = i, HasMaterial = e.Values[i] });
                }

                RefreshStatistics();
            }, null);
        }

        /// <summary>
        /// 抽屉属性变化处理：配方输入（用户在输入框填写）→ 更新填入次序并重算分组；
        /// 其余属性（物料/状态）由各自链路处理，这里统一刷新统计
        /// </summary>
        private void OnDrawerPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender is not DrawerItemViewModel drawer)
            {
                return;
            }

            if (e.PropertyName == nameof(DrawerItemViewModel.Recipe))
            {
                RefreshRecipeSequence(drawer.Index);
            }

            RefreshStatistics();
        }

        /// <summary>
        /// 刷新指定抽屉的配方填入次序并重算分组（对应参考程序 textBox_Leave → AddToList）：
        /// 配方非空 → 记录/刷新次序（重写即刷新，重复写入同一配方也排到组尾）；
        /// 配方为空/空白 → 移出分组（次序记录删除，重新填写视为新填入）。
        /// View 在输入框焦点离开（Leave）时调用，保证"重复写入相同配方"也按最后一次写入计序
        /// </summary>
        public void RefreshRecipeSequence(int drawerIndex)
        {
            var drawer = Drawers.FirstOrDefault(d => d.Index == drawerIndex);
            if (drawer is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(drawer.Recipe))
            {
                _recipeSequences.Remove(drawerIndex);
            }
            else
            {
                _recipeSequences[drawerIndex] = ++_recipeSequenceCounter;
            }

            RecomputeRecipeGroups();
        }

        /// <summary>
        /// 重算配方分组（派生数据，与抽屉当前配方恒一致）：
        /// 有配方（Trim 后非空）的抽屉按配方值分组；组内按填入次序升序（=填入先后顺序）；
        /// 组间按组内最小次序升序（=分组形成顺序）
        /// </summary>
        private void RecomputeRecipeGroups()
        {
            RecipeGroups = Drawers
                .Where(d => !string.IsNullOrWhiteSpace(d.Recipe) && _recipeSequences.ContainsKey(d.Index))
                .GroupBy(d => d.Recipe.Trim())
                .Select(g => new
                {
                    Recipe = g.Key,
                    Ordered = g.OrderBy(d => _recipeSequences[d.Index]).ToList()
                })
                .OrderBy(x => x.Ordered.Min(d => _recipeSequences[d.Index]))
                .Select(x => new RecipeGroupModel
                {
                    RecipeName = x.Recipe,
                    DrawerIndexes = x.Ordered.Select(d => d.Index).ToList()
                })
                .ToList();
        }

        /// <summary>
        /// 抽屉状态推送处理（后台线程事件 → 调度到 UI 线程）
        /// </summary>
        private void OnDrawerChanged(object? sender, DrawerModel model)
        {
            // WinForms 控件绑定要求 UI 线程，通过 WindowsFormsSynchronizationContext 调度
            var context = _uiContext;
            context.Post(_ =>
            {
                var drawer = Drawers.FirstOrDefault(d => d.Index == model.Index);
                drawer?.UpdateFromModel(model);
                RefreshStatistics();
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