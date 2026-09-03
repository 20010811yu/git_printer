using System;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Common;
using UiTopMachine.Common.Commands;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;

namespace UiTopMachine.Views.Pages
{
    /// <summary>
    /// 打印管理页（纯 View）：标题 + 说明 + 当前流水号 + 自定义内容输入 + 码型选择 + 张数输入 + 打印按钮 + 计数，
    /// 零业务逻辑（打印流程全部在 VM）
    /// </summary>
    public class PrintPage : UserControl
    {
        private readonly PrintPageViewModel _viewModel;

        // ══════════════ 布局控件 ══════════════
        private Label _titleLabel = null!;
        private Label _descriptionLabel = null!;
        private Label _serialCaption = null!;
        private Label _serialLabel = null!;
        private Label _contentCaption = null!;
        private TextBox _contentInput = null!;
        private Label _codeTypeCaption = null!;
        private ComboBox _codeTypeCombo = null!;
        private Label _quantityCaption = null!;
        private NumericUpDown _quantityInput = null!;
        private AntdUI.Button _printButton = null!;
        private Label _countLabel = null!;

        /// <summary>
        /// 构造：注入 ViewModel，初始化 UI 与绑定
        /// </summary>
        public PrintPage(PrintPageViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeUi();
            BindViewModel();

            // 首次进入页面：加载持久化的流水号（View 仅转发调用，业务在 VM）
            Load += async (_, _) => await _viewModel.InitializeAsync();
        }

        /// <summary>
        /// 构建页面布局（居中卡片式：标题 → 流水号 → 码型/张数 → 打印 → 计数）
        /// </summary>
        private void InitializeUi()
        {
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(244, 247, 250);
            Padding = new Padding(20, 8, 20, 16);

            var card = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            card.Paint += (s, e) =>
            {
                var rect = new Rectangle(0, 0, card.Width - 1, card.Height - 1);
                using var pen = new Pen(Color.FromArgb(224, 230, 238));
                e.Graphics.DrawRectangle(pen, rect);
            };

            var labelFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);
            var inputFont = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Point);

            _titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66)
            };

            _descriptionLabel = new Label
            {
                AutoSize = true,
                Font = labelFont,
                ForeColor = Color.FromArgb(120, 132, 148)
            };

            // ── 当前流水号（打印成功后自动 +1 并持久化） ──
            _serialCaption = new Label
            {
                Text = "当前流水号",
                AutoSize = true,
                Font = labelFont,
                ForeColor = Color.FromArgb(120, 132, 148)
            };
            _serialLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Consolas", 22f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(22, 119, 255)
            };

            // ── 自定义打印内容（Trim 后非空则每张打印该内容；留空走流水号） ──
            _contentCaption = new Label
            {
                Text = "打印内容（留空则打印流水号）",
                AutoSize = true,
                Font = labelFont,
                ForeColor = Color.FromArgb(120, 132, 148)
            };
            _contentInput = new TextBox
            {
                Font = inputFont,
                Size = new Size(440, 34),
                PlaceholderText = "输入标签内容，如：批次号 / 物料编码"
            };

            // ── 码型选择 ──
            _codeTypeCaption = new Label
            {
                Text = "码型",
                AutoSize = true,
                Font = labelFont,
                ForeColor = Color.FromArgb(120, 132, 148)
            };
            _codeTypeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList, // 只能选不能输入
                Font = inputFont,
                Size = new Size(160, 34),
                FlatStyle = FlatStyle.Flat
            };
            _codeTypeCombo.Items.AddRange(new object[]
            {
                "二维码 (QR Code)",
                "Code 39 条码",
                "Code 128 条码",
                "PDF417 条码",
                "数字文本"
            });
            _codeTypeCombo.SelectedIndex = 0; // 默认二维码

            // ── 打印张数 ──
            _quantityCaption = new Label
            {
                Text = "打印张数",
                AutoSize = true,
                Font = labelFont,
                ForeColor = Color.FromArgb(120, 132, 148)
            };
            _quantityInput = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 999,
                Value = 1,
                Font = inputFont,
                Size = new Size(100, 34),
                TextAlign = HorizontalAlignment.Center
            };

            _printButton = new AntdUI.Button
            {
                Text = "打印标签",
                Type = AntdUI.TTypeMini.Primary,
                Size = new Size(160, 48),
                Radius = 8
            };

            _countLabel = new Label
            {
                AutoSize = true,
                Font = labelFont,
                ForeColor = Color.FromArgb(56, 70, 88)
            };

            card.Controls.AddRange(new Control[]
            {
                _titleLabel, _descriptionLabel,
                _serialCaption, _serialLabel,
                _contentCaption, _contentInput,
                _codeTypeCaption, _codeTypeCombo,
                _quantityCaption, _quantityInput,
                _printButton, _countLabel
            });
            Controls.Add(card);

            // 布局完成后居中排布
            card.Resize += (_, _) => CenterLayout(card);
            PerformLayout();
            CenterLayout(card);
        }

        /// <summary>
        /// 控件居中纵向排布（纯布局，无业务）：每行说明标题在控件上方
        /// </summary>
        private void CenterLayout(Control card)
        {
            int cx = card.Width / 2;
            int mid = card.Height / 2;

            _titleLabel.Location = new Point((card.Width - _titleLabel.PreferredWidth) / 2, mid - 250);
            _descriptionLabel.Location = new Point((card.Width - _descriptionLabel.PreferredWidth) / 2, mid - 195);

            // 流水号行
            _serialCaption.Location = new Point(cx - 80, mid - 150);
            _serialLabel.Location = new Point(cx - 80, mid - 122);

            // 自定义内容行
            _contentCaption.Location = new Point(cx - 80, mid - 58);
            _contentInput.Location = new Point(cx - 80, mid - 28);

            // 码型 / 张数双列行
            _codeTypeCaption.Location = new Point(cx - 210, mid + 28);
            _codeTypeCombo.Location = new Point(cx - 210, mid + 54);
            _quantityCaption.Location = new Point(cx + 40, mid + 28);
            _quantityInput.Location = new Point(cx + 40, mid + 54);

            _printButton.Location = new Point((card.Width - _printButton.Width) / 2, mid + 118);
            _countLabel.Location = new Point((card.Width - _countLabel.PreferredWidth) / 2, mid + 182);
        }

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM）
        /// </summary>
        private void BindViewModel()
        {
            _titleLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.Title),
                false, DataSourceUpdateMode.Never);
            _descriptionLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.Description),
                false, DataSourceUpdateMode.Never);
            _serialLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.CurrentSerial),
                false, DataSourceUpdateMode.Never);
            _countLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(PrintPageViewModel.PrintCountText),
                false, DataSourceUpdateMode.Never);

            // 码型下拉 → VM（SelectedItem 变化映射枚举）
            _codeTypeCombo.SelectedIndexChanged += (_, _) =>
            {
                _viewModel.SelectedCodeType = _codeTypeCombo.SelectedIndex switch
                {
                    1 => ZplCodeType.Code39,
                    2 => ZplCodeType.Code128,
                    3 => ZplCodeType.Pdf417,
                    4 => ZplCodeType.Number,
                    _ => ZplCodeType.QRCode
                };
            };

            // 张数输入 → VM（双向即时）
            _quantityInput.ValueChanged += (_, _) => _viewModel.Quantity = (int)_quantityInput.Value;

            // 自定义内容输入 → VM（即时；Trim 后非空走自定义，留空走流水号）
            _contentInput.TextChanged += (_, _) => _viewModel.CustomContent = _contentInput.Text;

            // 打印按钮绑定命令（AsyncRelayCommand 内置 IsBusy 防重复）
            CommandManagerHelper.Bind(_printButton, _viewModel.PrintCommand);

            // VM 消息提示（打印失败/流水号错误）→ 弹窗（纯 UI 转发，后台线程经 BeginInvoke 封送）
            _viewModel.MessageRequested += (_, request) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => ShowMessage(request)));
                }
                else
                {
                    ShowMessage(request);
                }
            };
        }

        /// <summary>弹出消息提示框（模态，纯 UI 转发）</summary>
        private void ShowMessage(MessageRequestEventArgs request)
        {
            MessageBox.Show(FindForm(), request.Message, request.Title,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}