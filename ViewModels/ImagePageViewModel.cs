using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using UiTopMachine.Common;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 图像管理页视图模型（仿参考程序视觉流程）：
    /// 方案启动自动加载（MainForm.Load 触发，页面内调用幂等）→ 加载成功事件置就绪 →
    /// 单次检测 / 连续检测（周期轮询）→ 结果图与 OK/NG 结论展示（计数显示已按需求移除）。
    /// 视觉服务经 IImageInspectionService 抽象（桥接进程实现，Mock 保留可切回）
    /// </summary>
    public class ImagePageViewModel : ObservableObject
    {
        /// <summary>连续检测轮询间隔</summary>
        private const int ContinuousIntervalMs = 1000;

        // ══════════════ 依赖 ══════════════
        private readonly ILogService _logService;
        private readonly IImageInspectionService _inspectionService;
        private readonly IPanelStatusPublisher _panelPublisher;

        /// <summary>构造时捕获的 UI 线程同步上下文（后台检测事件经它调度 UI 更新，ERR-023）</summary>
        private readonly SynchronizationContext _uiContext;

        /// <summary>
        /// UI 线程编组器（ERR-028：由 View 在 UI 线程注入 Control.BeginInvoke——
        /// 构造期捕获的 SynchronizationContext 在部分进程环境下 Post 后仍脱离 UI 线程，绑定静默失效）
        /// </summary>
        private Action<Action>? _uiMarshaller;

        // ══════════════ 状态字段 ══════════════
        private bool _isBusy;
        private bool _isSaving;
        private bool _isContinuousRunning;
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

    /// <summary>保存图片进行中（防重复点击）</summary>
    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                SaveImageCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>
    /// 消息提示请求（ERR-032）：保存成功/失败、无图可保存等需要用户立即感知的信息，
    /// 经 View 弹窗展示（VM 零 UI 依赖，同 RecipePage 模式）
    /// </summary>
    public event EventHandler<MessageRequestEventArgs>? MessageRequested;

    /// <summary>
    /// 保存路径请求（ERR-032a）：View 弹保存对话框后回填 Confirmed/FullPath；
    /// 对话框绝不放进命令参数提供器——提供器在绑定与命令状态刷新时都会被调用，弹窗会失控
    /// </summary>
    public event EventHandler<SavePathRequestEventArgs>? SavePathRequested;

        /// <summary>当前结果图（ERR-028：所有权移交 View——View 轮询替换引用时负责释放旧图，VM 不再 Dispose）</summary>
        public Image? CurrentImage
        {
            get => _currentImage;
            private set
            {
                if (SetProperty(ref _currentImage, value))
                {
                    SaveImageCommand.RaiseCanExecuteChanged(); // 有图/无图切换联动保存按钮可用性
                }
            }
        }

        /// <summary>当前检测结论文本（OK/NG，随结果图着色由 View 处理）</summary>
        public string CurrentVerdict
        {
            get => _currentVerdict;
            private set => SetProperty(ref _currentVerdict, value);
        }

        // ══════════════ 命令 ══════════════

        /// <summary>单次检测命令（对应参考 SyncRun）</summary>
        public AsyncRelayCommand CaptureOnceCommand { get; }

        /// <summary>开始连续检测（对应参考 _vmInteraction 定时轮询采集）</summary>
        public AsyncRelayCommand StartContinuousCommand { get; }

    /// <summary>停止连续检测</summary>
    public AsyncRelayCommand StopContinuousCommand { get; }

    /// <summary>保存当前结果图（无参：路径经 SavePathRequested 事件向 View 请求）</summary>
    public AsyncRelayCommand SaveImageCommand { get; }

        // ══════════════ 构造 / 业务方法 ══════════════

        /// <summary>
        /// 构造：注入日志、视觉检测服务与面板发布器
        /// </summary>
        public ImagePageViewModel(ILogService logService, IImageInspectionService inspectionService, IPanelStatusPublisher panelPublisher)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _inspectionService = inspectionService ?? throw new ArgumentNullException(nameof(inspectionService));
            _panelPublisher = panelPublisher ?? throw new ArgumentNullException(nameof(panelPublisher));

            // 捕获 UI 线程同步上下文（构造发生在 UI 线程），后台检测结果经它调度 UI 更新（ERR-023）
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            CaptureOnceCommand = new AsyncRelayCommand(_ => CaptureOnceAsync(), _ => !IsBusy && IsSolutionLoaded && !IsContinuousRunning);
            StartContinuousCommand = new AsyncRelayCommand(_ => StartContinuousAsync(), _ => IsSolutionLoaded && !IsContinuousRunning);
            StopContinuousCommand = new AsyncRelayCommand(_ => StopContinuousAsync(), _ => IsContinuousRunning);
            SaveImageCommand = new AsyncRelayCommand(_ => SaveImageAsync(), _ => CurrentImage is not null && !IsSaving);

            // 方案加载成功事件（后台线程）→ 刷新状态
            _inspectionService.SolutionLoaded += (_, _) =>
                DispatchUi(() =>
                {
                    OnPropertyChanged(nameof(IsSolutionLoaded));
                    OnPropertyChanged(nameof(SolutionStatusText));
                    RefreshAllCommandStates();
                });
        }

        /// <summary>
        /// 注入 UI 线程编组器（由 ImagePage 在 UI 线程调用：a => BeginInvoke(a)，ERR-028）
        /// </summary>
        public void AttachUiMarshaller(Action<Action> marshal)
        {
            _uiMarshaller = marshal ?? throw new ArgumentNullException(nameof(marshal));
        }

        /// <summary>
        /// 调度到 UI 线程执行（优先 Control.BeginInvoke 编组器，回退构造期上下文）
        /// </summary>
        private void DispatchUi(Action action)
        {
            var marshal = _uiMarshaller;
            if (marshal != null)
            {
                marshal(action);
            }
            else
            {
                _uiContext.Post(_ => action(), null);
            }
        }

        /// <summary>
        /// 保存当前结果图（ERR-032）：路径经 SavePathRequested 事件向 View 请求，用户取消静默返回；
        /// 无图时弹窗提示。保存用克隆图在后台落盘——连续检测轮播时 View 会替换并释放旧图
        /// （ERR-028 图像所有权移交 View），直接持有原图保存存在 ObjectDisposedException 风险
        /// </summary>
        private async Task SaveImageAsync()
        {
            if (CurrentImage is null)
            {
                Notify("提示", "当前无图片可保存，请先执行检测");
                return;
            }

            var request = new SavePathRequestEventArgs
            {
                Title = "保存结果图片",
                InitialDirectory = @"D:\Printer\Data\Images",
                FileName = $"IMG_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };
            SavePathRequested?.Invoke(this, request);
            if (!request.Confirmed || string.IsNullOrWhiteSpace(request.FullPath))
            {
                return; // 用户取消保存对话框
            }

            IsSaving = true;
            try
            {
                using var clone = (Bitmap)CurrentImage.Clone();
                var path = request.FullPath;
                await Task.Run(() => clone.Save(path, System.Drawing.Imaging.ImageFormat.Png));

                _logService.Info($"结果图已保存：{path}");
                Notify("保存成功", $"图片保存成功：{path}");
            }
            catch (Exception ex)
            {
                _logService.Error($"结果图保存失败：{ex.Message}");
                Notify("保存失败", $"图片保存失败：{ex.Message}");
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>向 View 发起消息弹窗请求（纯数据，零 UI 依赖）</summary>
        private void Notify(string title, string message)
            => MessageRequested?.Invoke(this, new MessageRequestEventArgs { Title = title, Message = message });

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

            // 不传具体路径：使用服务层 DI 配置的方案路径（Program.cs 统一维护，VM 层不感知路径）
            var result = await _inspectionService.LoadSolutionAsync();
            if (result.Success)
            {
                OnPropertyChanged(nameof(IsSolutionLoaded));
                OnPropertyChanged(nameof(SolutionStatusText));
                RefreshAllCommandStates();
                _logService.Success("检测方案加载成功");
                _panelPublisher.PublishPanelEntry(LogLevel.Success, "视觉方案加载成功");
            }
            else
            {
                var message = $"视觉方案加载失败：{result.ErrorMessage}";
                _logService.Error(message);
                _panelPublisher.PublishPanelEntry(LogLevel.Error, message);
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
        /// 连续检测循环（后台线程）：周期运行检测，结果经 DispatchUi 调度 UI 更新；
        /// 单轮运行失败 → 面板报错误（检测完成的信息只落文件，防刷屏）
        /// </summary>
        private async Task ContinuousLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var result = await _inspectionService.RunInspectionAsync();

                DispatchUi(() =>
                {
                    if (result.Success && result.Data is not null)
                    {
                        ApplyInspectionResult(result.Data);
                    }
                    else
                    {
                        var message = $"视觉检测失败：{result.ErrorMessage}";
                        _logService.Error(message);
                        _panelPublisher.PublishPanelEntry(LogLevel.Error, message);
                    }
                });

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
        /// 应用检测结果（必须 UI 线程调用）：更新结果图与结论角标（OK/NG 计数已按需求移除）；
        /// Image 为 null 表示本运行无新帧（图像源帧率低于轮询频率，ERR-028）——
        /// 保留上一张结果图，仅记 Info 日志
        /// </summary>
        private void ApplyInspectionResult(ImageInspectionResult data)
        {
            if (data.Image is null)
            {
                _logService.Info($"检测轮询无新帧（第 {data.Sequence} 次），跳过显示");
                return;
            }

            CurrentImage = data.Image;
            CurrentVerdict = data.IsOk ? "OK" : "NG";
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
            CaptureOnceCommand.RaiseCanExecuteChanged();
            StartContinuousCommand.RaiseCanExecuteChanged();
            StopContinuousCommand.RaiseCanExecuteChanged();
        }
    }
}
