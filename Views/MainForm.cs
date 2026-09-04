using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Controls;
using UiTopMachine.Views.Pages;

namespace UiTopMachine.Views
{
    /// <summary>
    /// 主窗体（纯 View）：导航壳 —— 顶栏（公司名/退出）+ 底部 Tab 导航 + 中央页面容器 + 右侧全局 Status 日志
    /// 页面切换由 NavigationViewModel 驱动，本窗体仅做页面可见性切换，零业务逻辑
    /// </summary>
    public class MainForm : Form
    {
        // ══════════════ 依赖（ViewModel 注入） ══════════════
        private readonly NavigationViewModel _navigation;
        private readonly MainViewModel _mainViewModel;
        private readonly PrintPageViewModel _printViewModel;
        private readonly ImagePageViewModel _imageViewModel;
        private readonly RecipePageViewModel _recipeViewModel;

        // ══════════════ 布局控件 ══════════════
        private Panel _topBar = null!;
        private AntdUI.Button _exitButton = null!;
        private Label _companyLabel = null!;
        private LogPanelControl _logPanel = null!;
        private Panel _pageHost = null!;

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
            Load += async (_, _) => await _mainViewModel.InitializeAsync();
        }

        /// <summary>
        /// 窗体关闭：停止 PLC 服务（关闭心跳并断开连接），短超时兜底避免退出卡住
        /// </summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            var shutdown = _mainViewModel.ShutdownAsync();
            try
            {
                shutdown.Wait(TimeSpan.FromSeconds(3));
            }
            catch
            {
                // 关闭路径的停止失败不阻断退出（重连循环已随进程结束）
            }
        }

        // ══════════════ UI 构建 ══════════════

        /// <summary>
        /// 构建界面布局（AntdUI 风格：浅色现代、圆角、轻描边）
        /// </summary>
        private void InitializeUi()
        {
            // 窗体基础
            Text = "进料抽屉监控系统";
            Size = new Size(1500, 940);
            MinimumSize = new Size(1280, 800);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(244, 247, 250);
            Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular, GraphicsUnit.Point);

            // ── 顶部栏（白底 + 分隔线）──
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 76,
                BackColor = Color.White,
                Padding = new Padding(20, 0, 20, 0)
            };
            _topBar.Paint += (s, e) =>
                e.Graphics.DrawLine(new Pen(Color.FromArgb(226, 232, 240)), 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);

            // 公司名标题（左上，加大字体）
            _companyLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = Color.FromArgb(38, 50, 66),
                Location = new Point(20, 20)
            };

            // 退出按钮（右上角，AntdUI 危险语义红色，Anchor 右侧随窗口自适应）
            _exitButton = new AntdUI.Button
            {
                Text = "退出",
                Type = AntdUI.TTypeMini.Error,
                Size = new Size(104, 44),
                Location = new Point(1200, 16),
                Radius = 8,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            _topBar.Controls.Add(_exitButton);
            _topBar.Controls.Add(_companyLabel);

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
                Font = new Font("Segoe UI", 18f, FontStyle.Bold | FontStyle.Italic, GraphicsUnit.Point),
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

            // 布局完成后定位右上角退出按钮（基于真实客户区宽度）
            PerformLayout();
            _exitButton.Location = new Point(_topBar.ClientSize.Width - _exitButton.Width - 20, 16);

            // 初始显示默认页（进料抽屉）
            ShowPage(_navigation.CurrentPage);
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
            // 标题绑定（VM → View 单向）
            _companyLabel.DataBindings.Add(nameof(Label.Text), _mainViewModel, nameof(MainViewModel.CompanyTitle),
                false, DataSourceUpdateMode.Never);

            // 退出按钮绑定命令
            CommandManagerHelper.Bind(_exitButton, _mainViewModel.ExitCommand);

            // 日志面板绑定（全局）
            _logPanel.Bind(_mainViewModel.Logs);

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