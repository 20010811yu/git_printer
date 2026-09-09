using System;
using System.Collections.Generic;
using System.IO;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Controls;
using UiTopMachine.Views.Pages;

namespace UiTopMachine.Views
{
    /// <summary>
    /// 主窗体（纯 View）：导航壳 —— 顶栏（公司 Logo/退出）+ 底部 Tab 导航 + 中央页面容器 + 右侧全局 Status 日志
    /// 页面切换由 NavigationViewModel 驱动，本窗体仅做页面可见性切换，零业务逻辑
    /// </summary>
    public class MainForm : Form
    {
        // 无边框窗体拖动所需的 Win32 消息（顶栏按下左键伪装成标题栏拖动）
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HT_CAPTION = 0x2;

        // 无边框窗体边缘拉伸（ERR-033）：WM_NCHITTEST 命中测试返回八个方向命中码
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        /// <summary>边缘可抓取宽度（px）</summary>
        private const int ResizeGrip = 8;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // ══════════════ 依赖（ViewModel 注入） ══════════════
        private readonly NavigationViewModel _navigation;
        private readonly MainViewModel _mainViewModel;
        private readonly PrintPageViewModel _printViewModel;
        private readonly ImagePageViewModel _imageViewModel;
        private readonly RecipePageViewModel _recipeViewModel;

        // ══════════════ 布局控件 ══════════════
        private Panel _topBar = null!;
        private PictureBox _companyLogo = null!;
        private LogPanelControl _logPanel = null!;
        private Panel _pageHost = null!;

        /// <summary>右上角窗口控制按钮（无边框窗体自绘：最小化/最大化(全屏)/关闭）</summary>
        private AntdUI.Button _minimizeButton = null!;
        private AntdUI.Button _maximizeButton = null!;
        private AntdUI.Button _closeButton = null!;

        /// <summary>底部 Tab 控件（按 PageType 索引）</summary>
        private readonly Dictionary<PageType, TabItemControl> _tabs = new();

        /// <summary>页面缓存（首次导航时创建，之后仅切换可见性）</summary>
        private readonly Dictionary<PageType, Control> _pages = new();

        /// <summary>
        /// 构造：注入 ViewModel，初始化 UI 与绑定
        /// </summary>
        public MainForm(
            NavigationViewModel navigation,
            MainViewModel mainViewModel,
            PrintPageViewModel printViewModel,
            ImagePageViewModel imageViewModel,
            RecipePageViewModel recipeViewModel)
        {
            _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
            _printViewModel = printViewModel ?? throw new ArgumentNullException(nameof(printViewModel));
            _imageViewModel = imageViewModel ?? throw new ArgumentNullException(nameof(imageViewModel));
            _recipeViewModel = recipeViewModel ?? throw new ArgumentNullException(nameof(recipeViewModel));

            InitializeUi();
            BindViewModel();

            // 程序启动初始化：PLC 服务启动 + 视觉方案自动加载（v1.26：图像页「加载方案」按钮已移除，
            // 方案改由启动即加载；图像页内 InitializeAsync 仍保留，服务幂等不重复加载）
            Load += async (_, _) =>
            {
                await _mainViewModel.InitializeAsync();
                await _imageViewModel.InitializeAsync();
            };
        }

        /// <summary>
        /// 窗体关闭：停止 PLC 服务（关闭心跳并断开连接），短超时兜底避免退出卡住。
        /// ⚠️ 必须 Task.Run 包裹：直接 Wait 会死锁——异步延续需回 UI 线程，而 UI 线程正被 Wait 阻塞（ERR-023）
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try
            {
                Task.Run(() => _mainViewModel.ShutdownAsync()).Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 关闭路径的停止失败不阻断退出（重连循环已随进程结束）
            }
        }

        // ══════════════ UI 构建 ══════════════

        /// <summary>
        /// 加载程序图标（编译期嵌入 exe 的 Resources\App.ico）；失败返回 null（窗体用默认图标）
        /// </summary>
        private static Icon? TryLoadAppIcon()
        {
            try
            {
                return Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 创建窗口控制按钮（AntdUI.Button；符号 — 最小化 / □ 最大化(❐ 还原) / ✕ 关闭）。
        /// 尺寸与 WindowButtonLayout 常量一致
        /// </summary>
        private AntdUI.Button CreateWindowButton(string symbol, AntdUI.TTypeMini type, Action onClick)
        {
            var btn = new AntdUI.Button
            {
                Text = symbol,
                Type = type,
                Size = new Size(WindowButtonLayout.ButtonWidth, WindowButtonLayout.ButtonHeight),
                Radius = 6,
                Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold, GraphicsUnit.Point),
                Cursor = Cursors.Hand
            };
            btn.Click += (_, _) => onClick();
            return btn;
        }

        /// <summary>
        /// 按顶栏当前宽度重排右上角三按钮位置（右对齐 + 垂直居中）。
        /// ⚠️ 禁用 Anchor：ERR-024 教训——Anchor=Right 在容器未定型时设置会冻结负右缘距离，
        /// 把按钮推出窗口外（点击有效但屏幕不可见）；Resize 时实时重算是唯一可靠方式
        /// </summary>
        private void LayoutWindowButtons()
        {
            _minimizeButton.Location = WindowButtonLayout.GetMinimizeLocation(_topBar.Width);
            _maximizeButton.Location = WindowButtonLayout.GetMaximizeLocation(_topBar.Width);
            _closeButton.Location = WindowButtonLayout.GetCloseLocation(_topBar.Width);
        }

        /// <summary>
        /// 最大化 / 还原切换（全屏功能）。还原时窗口重新居中，
        /// 避免还原到之前被拖出屏幕外的位置导致右上角按钮不可见
        /// </summary>
        private void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
                CenterToScreen(); // 还原后重新居中，确保窗口整体在屏幕内
            }
            else
            {
                WindowState = FormWindowState.Maximized;
            }

            _maximizeButton.Text = WindowState == FormWindowState.Maximized ? "❐" : "□"; // ❐=还原
        }

        /// <summary>
        /// 无边框窗体边缘拉伸（ERR-033）：窗体 Padding 留出 6px 外沿为窗体自身表面，
        /// 鼠标落在此环带时按位置返回方向命中码，系统即可按住拖拽调整窗口大小；
        /// 最大化状态下不命中（保持原样）
        /// </summary>
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                // LParam 屏幕坐标（多显示器下可为负，必须按 16 位有符号截取）
                int x = (short)(m.LParam.ToInt64() & 0xFFFF);
                int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                var p = PointToClient(new Point(x, y));

                bool left = p.X <= ResizeGrip;
                bool right = p.X >= ClientSize.Width - ResizeGrip;
                bool top = p.Y <= ResizeGrip;
                bool bottom = p.Y >= ClientSize.Height - ResizeGrip;

                if (top && left) { m.Result = (IntPtr)HTTOPLEFT; return; }
                if (top && right) { m.Result = (IntPtr)HTTOPRIGHT; return; }
                if (bottom && left) { m.Result = (IntPtr)HTBOTTOMLEFT; return; }
                if (bottom && right) { m.Result = (IntPtr)HTBOTTOMRIGHT; return; }
                if (left) { m.Result = (IntPtr)HTLEFT; return; }
                if (right) { m.Result = (IntPtr)HTRIGHT; return; }
                if (top) { m.Result = (IntPtr)HTTOP; return; }
                if (bottom) { m.Result = (IntPtr)HTBOTTOM; return; }
            }

            base.WndProc(ref m);
        }

        /// <summary>
        /// 构建界面布局（AntdUI 风格：浅色现代、圆角、轻描边）
        /// </summary>
        private void InitializeUi()
        {
            // 窗体基础：无边框样式（去掉系统标题栏的图标与名称；右上角自绘 最小化/最大化(全屏)/关闭 三按钮），
            // 任务栏仍显示图标与名称（Text/Icon 保留）；窗口拖动经顶栏鼠标事件实现
            FormBorderStyle = FormBorderStyle.None;
            Text = "上海寅铠";
            Icon = TryLoadAppIcon();
            ShowInTaskbar = true;
            Size = new Size(1500, 940);
            MinimumSize = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(244, 247, 250);
            Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Regular, GraphicsUnit.Point);
            // 外沿 8px 留给窗体自身表面（配合 WndProc 边缘命中测试，实现无边框窗体拉伸，ERR-033）
            Padding = new Padding(8);

            // ── 顶部栏（白底 + 分隔线；Logo 向上占满并向右延伸，同时承担窗口拖动）──
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = Color.White,
                Padding = new Padding(0)
            };
            _topBar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);

            // 无边框窗体拖动：顶栏按下左键时伪装成标题栏拖动（ReleaseCapture + WM_NCLBUTTONDOWN）
            _topBar.MouseDown += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ReleaseCapture();
                    _ = SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HT_CAPTION, IntPtr.Zero);
                }
            };

            // 公司 Logo（左上，替代原公司名文本；向上占满顶栏并向右延伸，Resources\tittle.png 等比缩放）
            _companyLogo = new PictureBox
            {
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(448, 76),
                Location = new Point(0, 0),
                BackColor = Color.White
            };
            try
            {
                var logoPath = Path.Combine(AppContext.BaseDirectory, "Resources", "tittle.png");
                if (File.Exists(logoPath))
                {
                    _companyLogo.Image = Image.FromFile(logoPath);
                }
            }
            catch
            {
                // Logo 加载失败不阻断启动（顶栏留白）
            }

            // 窗口控制按钮（右上角：— 最小化 / □ 最大化全屏（❐ 还原）/ ✕ 关闭退出；
            // 关闭走 Close() → OnFormClosing → 停止服务，替代原红色退出按钮）
            _minimizeButton = CreateWindowButton("—", AntdUI.TTypeMini.Default,
                () => WindowState = FormWindowState.Minimized);
            _maximizeButton = CreateWindowButton("□", AntdUI.TTypeMini.Default, ToggleMaximize);
            _closeButton = CreateWindowButton("✕", AntdUI.TTypeMini.Error, () => Close());

            // 右上角排布（⚠️ ERR-024 教训：禁用 Anchor——顶栏 Dock 宽度未定型时设 Anchor=Right
            // 会冻结负右缘距离把按钮推出窗口外；改由 Resize 事件按当前宽度实时重算位置）
            _topBar.Controls.Add(_minimizeButton);
            _topBar.Controls.Add(_maximizeButton);
            _topBar.Controls.Add(_closeButton);
            _topBar.Controls.Add(_companyLogo);
            _topBar.Resize += (_, _) => LayoutWindowButtons();
            LayoutWindowButtons(); // 初始排布一次（顶栏仍为默认宽度，加入 Form 展宽后 Resize 再触发）

            // ── 底部导航栏（TabItemControl，绑定导航命令）──
            var bottomBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Color.White
            };
            bottomBar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, 0, bottomBar.Width, 0);

            AddTab(bottomBar, PageType.Print, "打  印", 16);
            AddTab(bottomBar, PageType.Image, "图  像", 150);
            AddTab(bottomBar, PageType.FeedDrawers, "进料抽屉", 284);
            AddTab(bottomBar, PageType.Recipe, "配  方", 418);

            // ── 右侧：全局 Status 日志卡片（所有页面共用）──
            var statusCard = new Panel { Dock = DockStyle.Right, Width = 430, BackColor = Color.White };
            statusCard.Resize += (_, _) => statusCard.Invalidate();
            statusCard.Paint += (s, e) =>
            {
                var rect = new Rectangle(0, 0, statusCard.Width - 1, statusCard.Height - 1);
                using var pen = new Pen(Color.FromArgb(224, 230, 238));
                e.Graphics.DrawRectangle(pen, rect);
            };

            var statusTitle = new Label
            {
                Text = "Status",
                Font = new Font("Segoe UI", 20f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66),
                Location = new Point(20, 14),
                AutoSize = true
            };

            _logPanel = new LogPanelControl
            {
                Location = new Point(14, 62),
                Size = new Size(statusCard.Width - 28, statusCard.Height - 78),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor = Color.FromArgb(250, 251, 253)
            };

            statusCard.Controls.Add(statusTitle);
            statusCard.Controls.Add(_logPanel);

            // ── 中央页面容器（承载当前页面）──
            _pageHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(244, 247, 250)
            };

            // ── 组装（z 序：先加的在底层；Dock 布局按加入逆序停靠）──
            Controls.Add(_pageHost);
            Controls.Add(statusCard);
            Controls.Add(bottomBar);
            Controls.Add(_topBar);

            // 初始显示默认页（进料抽屉）
            ShowPage(_navigation.CurrentPage);

            // 启动即最大化（工业上位机标准形态）：窗口始终占满屏幕，
            // 右上角窗口控制按钮不会被拖出屏幕外（此前窗口被拖至超出屏幕右缘导致按钮不可见）
            WindowState = FormWindowState.Maximized;
        }

        /// <summary>
        /// 添加底部导航 Tab：绑定 NavigateCommand（参数 = PageType）
        /// </summary>
        private void AddTab(Panel bar, PageType page, string text, int x)
        {
            var tab = new TabItemControl(text) { Location = new Point(x, 3) };
            _tabs[page] = tab;
            bar.Controls.Add(tab);

            // 点击 Tab → 导航命令（CanExecute 校验 + Execute 切页）
            CommandManagerHelper.Bind(tab, _navigation.NavigateCommand, page);
        }

        // ══════════════ 页面切换 ══════════════

        /// <summary>
        /// 显示指定页面（懒创建 + 可见性切换，避免一次性构建全部页面）
        /// </summary>
        private void ShowPage(PageType page)
        {
            if (!_pages.TryGetValue(page, out var control))
            {
                control = CreatePage(page);
                _pages[page] = control;
                _pageHost.Controls.Add(control);
            }

            // 仅显示目标页面（Dock=Fill 铺满容器）
            foreach (Control c in _pageHost.Controls)
            {
                c.Visible = ReferenceEquals(c, control);
            }
            control.BringToFront();

            // 同步 Tab 高亮
            foreach (var kv in _tabs)
            {
                kv.Value.IsSelected = kv.Key == page;
            }
        }

        /// <summary>
        /// 按页面类型创建页面实例（View 构造注入对应 ViewModel）
        /// </summary>
        private Control CreatePage(PageType page) => page switch
        {
            PageType.Print => new PrintPage(_printViewModel),
            PageType.Image => new ImagePage(_imageViewModel),
            PageType.Recipe => new RecipePage(_recipeViewModel),
            _ => new FeedDrawersPage(_mainViewModel)
        };

        // ══════════════ 数据绑定 ══════════════

        /// <summary>
        /// 绑定 ViewModel（View ↔ VM）
        /// </summary>
        private void BindViewModel()
        {
            // 日志面板绑定（全局）
            _logPanel.Bind(_mainViewModel.Logs);

            // PLC 连接状态 → 面板顶部常驻状态行（VM 经 SynchronizationContext 更新，此处已在 UI 线程）
            _mainViewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.PlcStatusText)
                    || e.PropertyName == nameof(MainViewModel.PlcStatusLevel))
                {
                    _logPanel.UpdatePlcStatus(_mainViewModel.PlcStatusText, _mainViewModel.PlcStatusLevel);
                }
            };
            _logPanel.UpdatePlcStatus(_mainViewModel.PlcStatusText, _mainViewModel.PlcStatusLevel);

            // 导航状态变化 → 页面切换（VM 属性驱动 View 表现）
            _navigation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NavigationViewModel.CurrentPage))
                {
                    if (InvokeRequired)
                    {
                        BeginInvoke(new Action(() => ShowPage(_navigation.CurrentPage)));
                    }
                    else
                    {
                        ShowPage(_navigation.CurrentPage);
                    }
                }
            };
        }
    }
}