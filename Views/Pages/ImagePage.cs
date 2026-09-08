using System;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.ViewModels;

namespace UiTopMachine.Views.Pages
{
    /// <summary>
    /// 图像管理页（纯 View）：方案加载 + 结果图像显示区 + OK/NG 统计 + 单次/连续检测控制，零业务逻辑
    /// </summary>
    public class ImagePage : UserControl
    {
        private readonly ImagePageViewModel _viewModel;

        // ══════════════ 布局控件 ══════════════
        private Label _titleLabel = null!;
        private Label _solutionStatusLabel = null!;
        private AntdUI.Button _loadSolutionButton = null!;
        private PictureBox _resultImageBox = null!;
        private Label _verdictLabel = null!;
        private Label _statisticsLabel = null!;
        private AntdUI.Button _captureOnceButton = null!;
        private AntdUI.Button _startContinuousButton = null!;
        private AntdUI.Button _stopContinuousButton = null!;
        private System.Windows.Forms.Timer _uiPollTimer = null!;
        private Image? _lastShownImage;

        /// <summary>
        /// 构造：注入 ViewModel，初始化 UI 与绑定
        /// </summary>
        public ImagePage(ImagePageViewModel viewModel)
        {
            _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
            InitializeUi();
            BindViewModel();

            // 页面加载时自动加载检测方案（仿参考 Form1_Shown 自动加载）
            Load += async (_, _) => await _viewModel.InitializeAsync();

            // 注入 UI 线程编组器（ERR-028：HandleCreated 时本控件必然在 UI 线程，
            // VM 后台结果经 BeginInvoke 调度，替代构造期捕获的 SynchronizationContext）
            HandleCreated += (_, _) =>
            {
                _viewModel.AttachUiMarshaller(action => BeginInvoke(action));

                // UI 轮询刷新（ERR-028）：结果图/结论/统计的 INPC 绑定在本环境下静默失效
                // （INPC 已证实在 UI 线程触发而界面不更新），改用 WinForms 定时器直接同步
                // ——定时器 Tick 固定在 UI 线程，对线程/编组/绑定全部免疫
                _uiPollTimer = new System.Windows.Forms.Timer { Interval = 500 };
                _uiPollTimer.Tick += (_, _) => SyncDisplayFromViewModel();
                _uiPollTimer.Start();
            };

            // 页面销毁时停止连续检测与轮询（防止后台循环残留）
            Disposed += (_, _) =>
            {
                _viewModel.Shutdown();
                _uiPollTimer?.Dispose();
            };
        }

        /// <summary>
        /// 构建页面布局（左侧结果图像区 + 右侧操作与统计栏）
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

            // ── 标题行 ──
            _titleLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 20f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66)
            };

            _solutionStatusLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(211, 47, 47)
            };

            _loadSolutionButton = new AntdUI.Button
            {
                Text = "加载方案",
                Type = AntdUI.TTypeMini.Default,
                Size = new Size(120, 40),
                Radius = 8
            };

            // ── 结果图像显示区 ──
            _resultImageBox = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.FromArgb(28, 30, 34),
                BorderStyle = BorderStyle.FixedSingle
            };

            // 检测结论角标（叠加在图像区上方）
            _verdictLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 16f, FontStyle.Bold, GraphicsUnit.Point),
                BackColor = Color.Transparent
            };
            _resultImageBox.Controls.Add(_verdictLabel);

            // ── 右侧操作栏 ──
            _captureOnceButton = new AntdUI.Button
            {
                Text = "单次检测",
                Type = AntdUI.TTypeMini.Primary,
                Size = new Size(150, 46),
                Radius = 8
            };

            _startContinuousButton = new AntdUI.Button
            {
                Text = "开始连续检测",
                Type = AntdUI.TTypeMini.Success,
                Size = new Size(150, 46),
                Radius = 8
            };

            _stopContinuousButton = new AntdUI.Button
            {
                Text = "停止连续检测",
                Type = AntdUI.TTypeMini.Error,
                Size = new Size(150, 46),
                Radius = 8
            };

            _statisticsLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(56, 70, 88)
            };

            card.Controls.Add(_titleLabel);
            card.Controls.Add(_solutionStatusLabel);
            card.Controls.Add(_loadSolutionButton);
            card.Controls.Add(_resultImageBox);
            card.Controls.Add(_verdictLabel);
            card.Controls.Add(_captureOnceButton);
            card.Controls.Add(_startContinuousButton);
            card.Controls.Add(_stopContinuousButton);
            card.Controls.Add(_statisticsLabel);
            Controls.Add(card);

            card.Resize += (_, _) => LayoutControls(card);
            Load += (_, _) => LayoutControls(card);
        }

        /// <summary>
        /// 布局排布：左侧图像区自适应，右侧操作栏固定宽度（纯布局，无业务）
        /// </summary>
        private void LayoutControls(Control card)
        {
            int margin = 20;
            int rightWidth = 200;

            _titleLabel.Location = new Point(margin, 14);
            _solutionStatusLabel.Location = new Point(margin + _titleLabel.PreferredWidth + 24, 32);
            _loadSolutionButton.Location = new Point(card.Width - rightWidth - margin, 16);

            int imageTop = 70;
            int imageWidth = card.Width - rightWidth - margin * 3;
            int imageHeight = card.Height - imageTop - margin;
            _resultImageBox.Location = new Point(margin, imageTop);
            _resultImageBox.Size = new Size(Math.Max(imageWidth, 200), Math.Max(imageHeight, 150));

            _verdictLabel.Location = new Point(12, 10);

            int rightX = margin + _resultImageBox.Width + margin;
            int buttonTop = imageTop + 30;
            _captureOnceButton.Location = new Point(rightX, buttonTop);
            _startContinuousButton.Location = new Point(rightX, buttonTop + 70);
            _stopContinuousButton.Location = new Point(rightX, buttonTop + 140);
            _statisticsLabel.Location = new Point(rightX, buttonTop + 230);
        }

        /// <summary>
        /// UI 轮询同步：把 VM 的结果图/结论/统计/方案状态直接刷到控件
        /// （结果图仅在引用变化时替换并释放上一张——图像所有权自 VM 移交至本页，ERR-028）
        /// </summary>
        private void SyncDisplayFromViewModel()
        {
            var img = _viewModel.CurrentImage;
            if (!ReferenceEquals(img, _lastShownImage))
            {
                var previous = _lastShownImage;
                _lastShownImage = img;
                _resultImageBox.Image = img;
                previous?.Dispose();
            }

            _verdictLabel.Text = _viewModel.CurrentVerdict;
            _verdictLabel.ForeColor = _viewModel.CurrentVerdict == "OK"
                ? Color.FromArgb(46, 125, 50)
                : Color.FromArgb(211, 47, 47);

            _statisticsLabel.Text = _viewModel.StatisticsText;

            _solutionStatusLabel.ForeColor = _viewModel.IsSolutionLoaded
                ? Color.FromArgb(46, 125, 50)
                : Color.FromArgb(211, 47, 47);
            _loadSolutionButton.Enabled = !_viewModel.IsSolutionLoaded;
        }

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM）
        /// </summary>
        private void BindViewModel()
        {
            // 标题与方案状态（INPC 绑定在本环境可用，保留）
            _titleLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(ImagePageViewModel.Title),
                false, DataSourceUpdateMode.Never);
            _solutionStatusLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(ImagePageViewModel.SolutionStatusText),
                false, DataSourceUpdateMode.Never);

            // 结果图/结论/统计改由 UI 轮询定时器同步（ERR-028），不再使用 INPC 绑定

            // 方案状态与命令绑定
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ImagePageViewModel.IsSolutionLoaded)
                    || e.PropertyName == nameof(ImagePageViewModel.IsContinuousRunning))
                {
                    _loadSolutionButton.Enabled = !_viewModel.IsSolutionLoaded;
                }
            };
            _loadSolutionButton.Enabled = !_viewModel.IsSolutionLoaded;

            CommandManagerHelper.Bind(_loadSolutionButton, _viewModel.LoadSolutionCommand);
            CommandManagerHelper.Bind(_captureOnceButton, _viewModel.CaptureOnceCommand);
            CommandManagerHelper.Bind(_startContinuousButton, _viewModel.StartContinuousCommand);
            CommandManagerHelper.Bind(_stopContinuousButton, _viewModel.StopContinuousCommand);
        }
    }
}
