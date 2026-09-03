using System;
using System.Threading.Tasks;
using UiTopMachine.Common;
using UiTopMachine.Common.Commands;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 打印管理页视图模型（v1.9 自定义打印内容）：
    /// 流水号自动递增（持久化 D:\Printer\Data\SerialNumber.txt，重开程序不断号）+
    /// 自定义打印内容（非空每张打印该内容，流水号不动；留空走流水号）+
    /// 码型选择（二维码/Code39/Code128/PDF417/数字文本）+ 打印张数 + 批量打印（Windows Spooler RAW 连接打印机）
    /// </summary>
    public class PrintPageViewModel : ObservableObject
    {
        // ══════════════ 依赖 ══════════════
        private readonly IPrintService _printService;
        private readonly ILogService _logService;

        // ══════════════ 属性 ══════════════
        private bool _isBusy;
        private int _printCount;
        private string _currentSerial = "------";
        private ZplCodeType _selectedCodeType = ZplCodeType.QRCode;
        private int _quantity = 1;
        private string _customContent = string.Empty;

        /// <summary>页面标题</summary>
        public string Title => "打印管理";

        /// <summary>页面说明</summary>
        public string Description => "标签打印（ZPL）：输入自定义内容或走流水号，选择码型与张数；流水号打印后自动递增并持久化";

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

        /// <summary>累计打印张数（本会话）</summary>
        public int PrintCount
        {
            get => _printCount;
            private set => SetProperty(ref _printCount, value);
        }

        /// <summary>打印次数展示文本</summary>
        public string PrintCountText => $"本次会话累计打印：{PrintCount} 张";

        /// <summary>
        /// 当前流水号（打印成功后自动 +1 并持久化，重开程序不断号）
        /// </summary>
        public string CurrentSerial
        {
            get => _currentSerial;
            private set => SetProperty(ref _currentSerial, value);
        }

        /// <summary>选中的码型（二维码/Code39/Code128/PDF417/数字文本）</summary>
        public ZplCodeType SelectedCodeType
        {
            get => _selectedCodeType;
            set => SetProperty(ref _selectedCodeType, value);
        }

        /// <summary>打印张数（≥1）</summary>
        public int Quantity
        {
            get => _quantity;
            set => SetProperty(ref _quantity, value < 1 ? 1 : value);
        }

        /// <summary>
        /// 自定义打印内容（可选）：Trim 后非空时每张打印该内容（流水号不递增不持久化）；留空走流水号自动递增
        /// </summary>
        public string CustomContent
        {
            get => _customContent;
            set => SetProperty(ref _customContent, value);
        }

        // ══════════════ 事件 ══════════════

        /// <summary>
        /// 消息提示请求事件：打印失败/流水号非法时弹窗提示（参数为纯数据，不接触 UI 控件）
        /// </summary>
        public event EventHandler<MessageRequestEventArgs>? MessageRequested;

        // ══════════════ 命令 ══════════════

        /// <summary>打印命令（批量打印，IsBusy 防重复）</summary>
        public AsyncRelayCommand PrintCommand { get; }

        /// <summary>初始化命令：页面进入时加载持久化的流水号</summary>
        public AsyncRelayCommand InitializeCommand { get; }

        // ══════════════ 构造 / 业务方法 ══════════════

        /// <summary>
        /// 构造：注入打印服务与日志服务
        /// </summary>
        public PrintPageViewModel(IPrintService printService, ILogService logService)
        {
            _printService = printService ?? throw new ArgumentNullException(nameof(printService));
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            PrintCommand = new AsyncRelayCommand(_ => PrintAsync(), _ => !IsBusy);
            InitializeCommand = new AsyncRelayCommand(_ => InitializeAsync(), _ => !IsBusy);
        }

        /// <summary>
        /// 页面初始化：加载持久化的流水号（View 的 Load 事件转发调用）
        /// </summary>
        public Task InitializeAsync() => LoadSerialCoreAsync();

        /// <summary>
        /// 加载持久化流水号（文件不存在时默认 000001）
        /// </summary>
        private async Task LoadSerialCoreAsync()
        {
            var result = await _printService.LoadSerialAsync();
            if (result.Success && result.Data is not null)
            {
                CurrentSerial = result.Data;
                _logService.Info($"当前流水号：{CurrentSerial}");
            }
            else
            {
                _logService.Warn(result.ErrorMessage ?? "流水号加载失败");
            }
        }

        /// <summary>
        /// 批量打印（Windows Spooler RAW 发送）：
        /// ① 自定义内容 Trim 后非空 → 每张打印该内容（每张相同），流水号不递增不持久化；
        /// ② 留空 → 从当前流水号起连续 N 张，全部成功后流水号 +N 并持久化
        /// （中途失败则停在失败张，不前进流水号——已成功张的号下次会重复打印，用户可从失败处手动继续）
        /// </summary>
        private async Task PrintAsync()
        {
            IsBusy = true;
            try
            {
                // ① 打印内容来源：自定义内容优先，留空走流水号
                var customContent = string.IsNullOrWhiteSpace(CustomContent) ? null : CustomContent.Trim();

                // ② 流水号路径校验（非空 + 纯数字）；自定义内容路径无流水号校验
                int digits = 0;
                ulong number = 0;
                if (customContent is null)
                {
                    var serial = CurrentSerial;
                    if (!_printService.IsValidSerial(serial))
                    {
                        var message = $"当前流水号非法（应为纯数字）：{serial}";
                        RaiseMessage("流水号错误", message);
                        _logService.Error(message);
                        return;
                    }

                    digits = serial.Length;          // 保留用户/持久化文件的位数
                    number = ulong.Parse(serial);
                }

                int quantity = Math.Max(1, Quantity);
                _logService.Info(customContent is null
                    ? $"开始打印 {quantity} 张（码型：{SelectedCodeType}，起始流水号：{CurrentSerial}）…"
                    : $"开始打印 {quantity} 张（码型：{SelectedCodeType}，内容：自定义输入）…");

                // ③ 逐张生成并打印（流水号连续递增保持原始位数——如 000001 而非 1；自定义内容每张相同）
                int successCount = 0;
                for (int i = 0; i < quantity; i++)
                {
                    // 流水号按原始位数补零（超出位数时自然扩展，如 999999→1000000）
                    var dataText = customContent ?? number.ToString().PadLeft(digits, '0');
                    var zpl = _printService.GenerateZpl(SelectedCodeType, dataText);
                    var result = await _printService.PrintBySpoolerAsync(zpl);
                    if (!result.Success)
                    {
                        // 中途失败：停在当前张，提示用户（流水号不前进，避免跳号；
                        // 已打印张的号下次会重复打印，由用户决定处理方式）
                        var message = $"第 {successCount + 1} 张打印失败：{result.ErrorMessage}（已成功 {successCount} 张"
                            + (customContent is null ? "，流水号未前进）" : "）");
                        RaiseMessage("打印失败", message);
                        _logService.Error(message);
                        return;
                    }

                    successCount++;
                    PrintCount++;
                    if (customContent is null)
                    {
                        number++;
                    }
                }

                OnPropertyChanged(nameof(PrintCountText));

                // ④ 流水号路径全部成功：流水号 +quantity 并持久化（补零格式与打印一致）；自定义内容路径不涉及
                if (customContent is null)
                {
                    var newSerial = number.ToString().PadLeft(digits, '0');
                    var saveResult = await _printService.SaveSerialAsync(newSerial);
                    if (!saveResult.Success)
                    {
                        var message = $"流水号保存失败：{saveResult.ErrorMessage}（当前打印成功，但重开程序可能重复使用 {newSerial} 前的编号）";
                        RaiseMessage("流水号保存失败", message);
                        _logService.Error(message);
                    }

                    CurrentSerial = newSerial;
                    _logService.Success($"打印完成：{quantity} 张成功（码型：{SelectedCodeType}），下一流水号：{CurrentSerial}");
                }
                else
                {
                    _logService.Success($"打印完成：{quantity} 张成功（码型：{SelectedCodeType}，内容：自定义输入），流水号未变动：{CurrentSerial}");
                }
            }
            catch (FormatException)
            {
                var message = "流水号超出可计算范围（过长），请手动修正";
                RaiseMessage("流水号错误", message);
                _logService.Error(message);
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

        /// <summary>向 View 发起消息提示请求（弹窗由 View 展示，VM 不接触 UI 控件）</summary>
        private void RaiseMessage(string title, string message)
            => MessageRequested?.Invoke(this, new MessageRequestEventArgs { Title = title, Message = message });
    }
}