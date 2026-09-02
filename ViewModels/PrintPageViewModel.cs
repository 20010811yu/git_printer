using System;
using System.Threading.Tasks;
using UiTopMachine.Common.Commands;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 打印管理页视图模型：打印任务占位（待接入真实打印服务）
    /// </summary>
    public class PrintPageViewModel : ObservableObject
    {
        // ══════════════ 依赖 ══════════════
        private readonly ILogService _logService;

        // ══════════════ 属性 ══════════════
        private bool _isBusy;
        private int _printCount;

        /// <summary>页面标题</summary>
        public string Title => "打印管理";

        /// <summary>页面说明（占位提示）</summary>
        public string Description => "标签打印任务管理（占位页面，待接入打印服务后启用真实打印）";

        /// <summary>是否忙碌（打印命令执行中，防重复点击）</summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    PrintCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>累计打印次数（演示状态展示）</summary>
        public int PrintCount
        {
            get => _printCount;
            private set => SetProperty(ref _printCount, value);
        }

        /// <summary>打印次数展示文本</summary>
        public string PrintCountText => $"累计打印：{PrintCount} 次";

        // ══════════════ 命令 ══════════════

        /// <summary>打印命令（异步模拟，IsBusy 防重复）</summary>
        public AsyncRelayCommand PrintCommand { get; }

        // ══════════════ 构造 / 业务方法 ══════════════

        /// <summary>
        /// 构造：注入日志服务
        /// </summary>
        public PrintPageViewModel(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            PrintCommand = new AsyncRelayCommand(_ => PrintAsync(), _ => !IsBusy);
        }

        /// <summary>
        /// 执行打印（模拟耗时操作，后续替换为 IPrintService 调用）
        /// </summary>
        private async Task PrintAsync()
        {
            IsBusy = true;
            try
            {
                _logService.Info("开始打印标签…");
                await Task.Delay(800); // 模拟打印耗时
                PrintCount++;
                OnPropertyChanged(nameof(PrintCountText));
                _logService.Success($"标签打印完成（第 {PrintCount} 次）");
            }
            catch (Exception ex)
            {
                _logService.Error($"打印异常：{ex.Message}");
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}