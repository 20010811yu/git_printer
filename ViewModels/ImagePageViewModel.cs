using System;
using System.Threading.Tasks;
using UiTopMachine.Common.Commands;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 图像管理页视图模型：视觉采集占位（待接入海康 VisionMaster SDK）
    /// </summary>
    public class ImagePageViewModel : ObservableObject
    {
        // ══════════════ 依赖 ══════════════
        private readonly ILogService _logService;

        // ══════════════ 属性 ══════════════
        private bool _isBusy;
        private int _captureCount;

        /// <summary>页面标题</summary>
        public string Title => "图像管理";

        /// <summary>页面说明（占位提示）</summary>
        public string Description => "视觉图像采集与查看（占位页面，待接入海康 VisionMaster SDK 后启用真实采集）";

        /// <summary>是否忙碌（采集命令执行中，防重复点击）</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    CaptureCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>累计采集图像数</summary>
        public int CaptureCount
        {
            get => _captureCount;
            private set => SetProperty(ref _captureCount, value);
        }

        /// <summary>采集数展示文本</summary>
        public string CaptureCountText => $"累计采集：{CaptureCount} 张";

        // ══════════════ 命令 ══════════════

        /// <summary>采集命令（异步模拟，IsBusy 防重复）</summary>
        public AsyncRelayCommand CaptureCommand { get; }

        // ══════════════ 构造 / 业务方法 ══════════════

        /// <summary>
        /// 构造：注入日志服务
        /// </summary>
        public ImagePageViewModel(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            CaptureCommand = new AsyncRelayCommand(_ => CaptureAsync(), _ => !IsBusy);
        }

        /// <summary>
        /// 执行采集（模拟相机取图，后续替换为 VisionCameraService 调用）
        /// </summary>
        private async Task CaptureAsync()
        {
            IsBusy = true;
            try
            {
                _logService.Info("触发相机采集…");
                await Task.Delay(600); // 模拟取图耗时
                CaptureCount++;
                OnPropertyChanged(nameof(CaptureCountText));
                _logService.Success($"图像采集完成（第 {CaptureCount} 张）");
            }
            catch (Exception ex)
            {
                _logService.Error($"采集异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}