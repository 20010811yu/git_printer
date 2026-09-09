using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Controls;
using VMControls.Winform.Release;

namespace UiTopMachine.Views.Pages
{
    /// <summary>
    /// 图像管理页（纯 View）：结果图像显示区（VmRenderControl：内置鼠标拖拽平移 + 滚轮缩放）+
    /// OK/NG 结论角标 + 单次/连续检测控制，零业务逻辑。
    /// v1.26：移除「加载方案」按钮（方案经 MainForm.Load 启动自动加载，失败入 Status 面板）与
    /// 总数/OK/NG 计数显示；PictureBox 弃用改 VmRenderControl（混合架构：桥接进程出图，本控件纯显示）
    /// </summary>
    public class ImagePage : UserControl
    {
        private readonly ImagePageViewModel _viewModel;

        // ══════════════ 布局控件 ══════════════
        private Label _titleLabel = null!;
        private Label _solutionStatusLabel = null!;
        private VmRenderControl _renderControl = null!;
        private Label _verdictLabel = null!;
        private AntdUI.Button _captureOnceButton = null!;
        private AntdUI.Button _startContinuousButton = null!;
        private AntdUI.Button _stopContinuousButton = null!;
        private AntdUI.Button _saveImageButton = null!;
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

            // 注入 UI 线程编组器（ERR-028：HandleCreated 时本控件必然在 UI 线程，
            // VM 后台结果经 BeginInvoke 调度，替代构造期捕获的 SynchronizationContext）
            HandleCreated += (_, _) =>
            {
                _viewModel.AttachUiMarshaller(action => BeginInvoke(action));

                // UI 轮询刷新（ERR-028）：结果图/结论的 INPC 绑定在本环境下静默失效
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
        /// 构建页面布局（左侧结果图像区 + 右侧操作栏）
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

            // ── 结果图像显示区（VmRenderControl：内置拖拽平移 + 滚轮缩放）──
            _renderControl = new VmRenderControl
            {
                BackColor = Color.FromArgb(28, 30, 34),
                Dock = DockStyle.None
            };
            // 尽力禁用控件自带右键菜单（其保存路径依赖 OpenCvSharp 原生库，未随程序分发必崩，ERR-032）；
            // 控件内部自建菜单若不受此控制，以页面「保存图片」按钮为准
            _renderControl.ContextMenuStrip = null;

            // 检测结论角标（叠加在图像区上方）
            _verdictLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Arial", 16f, FontStyle.Bold, GraphicsUnit.Point),
                BackColor = Color.Transparent
            };
            _renderControl.Controls.Add(_verdictLabel);

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

            // ── 保存图片（ERR-032：应用层 GDI+ 保存，绕开控件右键菜单的 OpenCvSharp 原生依赖）──
            _saveImageButton = new AntdUI.Button
            {
                Text = "保存图片",
                Type = AntdUI.TTypeMini.Primary,
                Size = new Size(150, 46),
                Radius = 8
            };

            card.Controls.Add(_titleLabel);
            card.Controls.Add(_solutionStatusLabel);
            card.Controls.Add(_renderControl);
            card.Controls.Add(_captureOnceButton);
            card.Controls.Add(_startContinuousButton);
            card.Controls.Add(_stopContinuousButton);
            card.Controls.Add(_saveImageButton);
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

            int imageTop = 70;
            int imageWidth = card.Width - rightWidth - margin * 3;
            int imageHeight = card.Height - imageTop - margin;
            _renderControl.Location = new Point(margin, imageTop);
            _renderControl.Size = new Size(Math.Max(imageWidth, 200), Math.Max(imageHeight, 150));

            _verdictLabel.Location = new Point(12, 10);

            int rightX = margin + _renderControl.Width + margin;
            int buttonTop = imageTop + 30;
            _captureOnceButton.Location = new Point(rightX, buttonTop);
            _startContinuousButton.Location = new Point(rightX, buttonTop + 70);
            _stopContinuousButton.Location = new Point(rightX, buttonTop + 140);
            _saveImageButton.Location = new Point(rightX, buttonTop + 210);
        }

        /// <summary>
        /// UI 轮询同步：把 VM 的结果图/结论直接刷到控件
        /// （结果图仅在引用变化时替换并释放上一张——图像所有权自 VM 移交至本页，ERR-028；
        /// 新图经 VmBitmapImageData 包装后喂 VmRenderControl.ImageSource，
        /// 平移/缩放交互由控件内置处理，包装失败时回退 Image 属性显示）
        /// </summary>
        private void SyncDisplayFromViewModel()
        {
            var img = _viewModel.CurrentImage;
            if (!ReferenceEquals(img, _lastShownImage))
            {
                var previous = _lastShownImage;
                _lastShownImage = img;

                var adapted = img is null ? null : VmBitmapImageData.TryCreate(img);
                if (adapted != null)
                {
                    _renderControl.ImageSource = adapted; // 包装失败（非位图/null）时保留上一帧显示
                }

                previous?.Dispose();
            }

            _verdictLabel.Text = _viewModel.CurrentVerdict;
            _verdictLabel.ForeColor = _viewModel.CurrentVerdict == "OK"
                ? Color.FromArgb(46, 125, 50)
                : Color.FromArgb(211, 47, 47);

            _solutionStatusLabel.ForeColor = _viewModel.IsSolutionLoaded
                ? Color.FromArgb(46, 125, 50)
                : Color.FromArgb(211, 47, 47);
        }

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM）：标题与方案状态文本（INPC 绑定在本环境可用）；
        /// 结果图/结论走 UI 轮询同步（ERR-028），不经 INPC 绑定
        /// </summary>
        private void BindViewModel()
        {
            _titleLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(ImagePageViewModel.Title),
                false, DataSourceUpdateMode.Never);
            _solutionStatusLabel.DataBindings.Add(nameof(Label.Text), _viewModel, nameof(ImagePageViewModel.SolutionStatusText),
                false, DataSourceUpdateMode.Never);

            // 检测控制命令绑定（ERR-031：v1.26 重构遗漏按钮绑定，点击无响应 → 检测不运行 → 页面无图）
            CommandManagerHelper.Bind(_captureOnceButton, _viewModel.CaptureOnceCommand);
            CommandManagerHelper.Bind(_startContinuousButton, _viewModel.StartContinuousCommand);
            CommandManagerHelper.Bind(_stopContinuousButton, _viewModel.StopContinuousCommand);

            // 保存图片（ERR-032）：无参绑定——对话框绝不能放进参数提供器，
            // 提供器在绑定与每次命令状态刷新时都会被调用（ERR-032a 弹窗失控根因）
            CommandManagerHelper.Bind(_saveImageButton, _viewModel.SaveImageCommand);

            // 保存路径请求：View 弹保存对话框并回填 Confirmed/FullPath（用户取消原样返回）
            _viewModel.SavePathRequested += (_, request) =>
            {
                var pick = new Action(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(request.InitialDirectory);
                    }
                    catch
                    {
                        // 目录创建失败交由对话框默认路径兜底
                    }

                    using var dialog = new SaveFileDialog
                    {
                        InitialDirectory = request.InitialDirectory,
                        FileName = request.FileName,
                        Filter = "PNG 图片 (*.png)|*.png"
                    };
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        request.Confirmed = true;
                        request.FullPath = dialog.FileName;
                    }
                });

                if (InvokeRequired)
                {
                    BeginInvoke(pick);
                }
                else
                {
                    pick();
                }
            };

            // 保存成功/失败/无图提示弹窗（VM→View 消息请求模式，后台线程经 BeginInvoke 封送）
            _viewModel.MessageRequested += (_, request) =>
            {
                var show = new Action(() => MessageBox.Show(this, request.Message, request.Title,
                    MessageBoxButtons.OK, MessageBoxIcon.Information));
                if (InvokeRequired)
                {
                    BeginInvoke(show);
                }
                else
                {
                    show();
                }
            };
        }
    }
}
