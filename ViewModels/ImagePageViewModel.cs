using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 图像管理页视图模型（仿参考程序视觉流程）：
    /// 方案加载（自动+手动）→ 加载成功事件置就绪 → 单次检测 / 连续检测（周期轮询）→
    /// 结果图与 OK/NG 计数展示。视觉服务经 IImageInspectionService 抽象（Mock 实现，真机换 VisionMaster 实现）
    /// </summary>
    public class ImagePageViewModel : ObservableObject
    {
        /// <summary>连续检测轮询间隔</summary>
        private const int ContinuousIntervalMs = 1000;

        // ══════════════ 依赖 ══════════════
        private readonly ILogService _logService;
        private readonly IImageInspectionService _inspectionService;

        /// <summary>构造时捕获的 UI 线程同步上下文（后台检测事件经它调度 UI 更新，ERR-023）</summary>
        private readonly SynchronizationContext _uiContext;

        // ══════════════ 状态字段 ══════════════
        private bool _isBusy;
        private bool _isContinuousRunning;
        private int _okCount;
        private int _ngCount;
        private Image? _currentImage;
        private string _currentVerdict = "—";
        private CancellationTokenSource? _continuousCts;
        private Task? _continuousTask;

        // ══════════════ 属性 ══════════════
        /// <summary>页面标题</summary>
        public string Title => "图像管理";

        /// <summary>是否忙碌（单次检测命令执行中，防重复点击）</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CaptureOnceCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>检测方案是否已加载（对应参考 _vmConnected）</summary>
        public bool IsSolutionLoaded => _inspectionService.IsSolutionLoaded;

        /// <summary>方案状态展示文本</summary>
        public string SolutionStatusText => IsSolutionLoaded ? $"方案已加载（{ProcedureDisplay}）" : "方案未加载";

        private string ProcedureDisplay => $"{_inspectionService.ProcedureName} 流程";

        /// <summary>连续检测运行中</summary>
        public bool IsContinuousRunning
        {
            get => _isContinuousRunning;
            private set
            {
                if (SetProperty(ref _isContinuousRunning, value))
                {
                    StartContinuousCommand.RaiseCanExecuteChanged();
                    StopContinuousCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>当前结果图（新图替换时释放旧图，防内存膨胀）</summary>
        public Image? CurrentImage
        {
            get => _currentImage;
            private set
            {
                var old = _currentImage;
                if (SetProperty(ref _currentImage, value) && old is not null && !ReferenceEquals(old, value))
                {
                    old.Dispose();
                }
            }
        }

        /// <summary>当前检测结论文本（OK/NG，随结果图着色由 View 处理）</summary>
        public string CurrentVerdict
        {
            get => _currentVerdict;
            private set => SetProperty(ref _currentVerdict, value);
        }

        /// <summary>OK 数</summary>
        public int OkCount
        {
            get => _okCount;
            private set => SetProperty(ref _okCount, value);
        }

        /// <summary>NG 数</summary>
        public int NgCount
        {
            get => _ngCount;
            private set => SetProperty(ref _ngCount, value);
        }

        /// <summary>统计展示文本</summary>
        public string StatisticsText => $"总数：{OkCount + NgCount}    OK：{OkCount}    NG：{NgCount}";

        // ══════════════ 命令 ══════════════

        /// <summary>加载检测方案（对应参考 Form1_Shown 自动加载，也可手动触发）</summary>
        public AsyncRelayCommand LoadSolutionCommand { get; }

        /// <summary>单次检测命令（对应参考 SyncRun）</summary>
        public AsyncRelayCommand CaptureOnceCommand { get; }

        /// <summary>开始连续检测（对应参考 _vmInteraction 定时轮询采集）</summary>
        public AsyncRelayCommand StartContinuousCommand { get; }

        /// <summary>停止连续检测</summary>
        public AsyncRelayCommand StopContinuousCommand { get; }

        // ══════════════ 构造 / 业务方法 ══════════════

        /// <summary>
        /// 构造：注入日志与视觉检测服务
        /// </summary>
        public ImagePageViewModel(ILogService logService, IImageInspectionService inspectionService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _inspectionService = inspectionService ?? throw new ArgumentNullException(nameof(inspectionService));

            // 捕获 UI 线程同步上下文（构造发生在 UI 线程），后台检测结果经它调度 UI 更新（ERR-023）
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            LoadSolutionCommand = new AsyncRelayCommand(_ => LoadSolutionAsync(), _ => !IsSolutionLoaded);
            CaptureOnceCommand = new AsyncRelayCommand(_ => CaptureOnceAsync(), _ => !IsBusy && IsSolutionLoaded && !IsContinuousRunning);
            StartContinuousCommand = new AsyncRelayCommand(_ => StartContinuousAsync(), _ => IsSolutionLoaded && !IsContinuousRunning);
            StopContinuousCommand = new AsyncRelayCommand(_ => StopContinuousAsync(), _ => IsContinuousRunning);

            // 方案加载成功事件（后台线程）→ 刷新状态
            _inspectionService.SolutionLoaded += (_, _) =>
                _uiContext.Post(_ =>
                {
                    OnPropertyChanged(nameof(IsSolutionLoaded));
                    OnPropertyChanged(nameof(SolutionStatusText));
                    RefreshAllCommandStates();
                }, null);
        }

        /// <summary>
        /// 页面初始化：自动加载检测方案（仿参考 Form1_Shown 的自动加载）
        /// </summary>
        public Task InitializeAsync()
        {
            if (!IsSolutionLoaded)
            {
                return LoadSolutionAsync();
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// 加载检测方案
        /// </summary>
        private async Task LoadSolutionAsync()
        {
            _logService.Info("开始加载视觉检测方案…");

            var result = await _inspectionService.LoadSolutionAsync(@"D:\test\DetectionProcess.sol");
            if (result.Success)
            {
                OnPropertyChanged(nameof(IsSolutionLoaded));
                OnPropertyChanged(nameof(SolutionStatusText));
                RefreshAllCommandStates();
                _logService.Success("检测方案加载成功");
            }
            else
            {
                _logService.Error($"检测方案加载失败：{result.ErrorMessage}");
            }
        }

        /// <summary>
        /// 单次检测：运行一次流程并展示结果
        /// </summary>
        private async Task CaptureOnceAsync()
        {
            IsBusy = true;
            try
            {
                await RunInspectionCoreAsync();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// 开始连续检测（周期运行，对应参考 _vmInteraction 轮询）
        /// </summary>
        private Task StartContinuousAsync()
        {
            if (IsContinuousRunning || !IsSolutionLoaded)
            {
                return Task.CompletedTask;
            }

            _continuousCts = new CancellationTokenSource();
            var token = _continuousCts.Token;
            _continuousTask = Task.Run(() => ContinuousLoopAsync(token));
            IsContinuousRunning = true;
            _logService.Info("连续检测已开启");
            return Task.CompletedTask;
        }

        /// <summary>
        /// 停止连续检测
        /// </summary>
        private async Task StopContinuousAsync()
        {
            if (!IsContinuousRunning)
            {
                return;
            }

            _continuousCts?.Cancel();
            if (_continuousTask is not null)
            {
                try
                {
                    await _continuousTask;
                }
                catch
                {
                    // 取消过程中的收尾异常不影响停止
                }
            }

            _continuousTask = null;
            _continuousCts?.Dispose();
            _continuousCts = null;
            IsContinuousRunning = false;
            _logService.Info("连续检测已停止");
        }

        /// <summary>
        /// 连续检测循环（后台线程）：周期运行检测，结果经 _uiContext 调度 UI 更新
        /// </summary>
        private async Task ContinuousLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _inspectionService.RunInspectionAsync();

                _uiContext.Post(_ =>
                {
                    if (result.Success && result.Data is not null)
                    {
                        ApplyInspectionResult(result.Data);
                    }
                    else
                    {
                        _logService.Error($"连续检测失败：{result.ErrorMessage}");
                    }
                }, null);

                try
                {
                    await Task.Delay(ContinuousIntervalMs, token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// 运行一次检测并应用结果（UI 线程调用）
        /// </summary>
        private async Task RunInspectionCoreAsync()
        {
            var result = await _inspectionService.RunInspectionAsync();
            if (result.Success && result.Data is not null)
            {
                ApplyInspectionResult(result.Data);
            }
            else
            {
                _logService.Error($"检测失败：{result.ErrorMessage}");
            }
        }

        /// <summary>
        /// 应用检测结果（必须 UI 线程调用）：更新结果图、结论、OK/NG 计数
        /// </summary>
        private void ApplyInspectionResult(ImageInspectionResult data)
        {
            CurrentImage = data.Image;
            CurrentVerdict = data.IsOk ? "OK" : "NG";
            if (data.IsOk)
            {
                OkCount++;
            }
            else
            {
                NgCount++;
            }

            OnPropertyChanged(nameof(StatisticsText));
            _logService.Info($"检测完成（第 {data.Sequence} 次）：{(data.IsOk ? "OK" : "NG")}，耗时 {data.Elapsed.TotalMilliseconds:F0} ms");
        }

        /// <summary>
        /// 停止页面（由 View Disposed 调用）：停止连续检测
        /// </summary>
        public void Shutdown()
        {
            _continuousCts?.Cancel();
        }

        /// <summary>
        /// 统一刷新全部命令可用状态（防回归清单 #6a）
        /// </summary>
        private void RefreshAllCommandStates()
        {
            LoadSolutionCommand.RaiseCanExecuteChanged();
            CaptureOnceCommand.RaiseCanExecuteChanged();
            StartContinuousCommand.RaiseCanExecuteChanged();
            StopContinuousCommand.RaiseCanExecuteChanged();
        }
    }
}
