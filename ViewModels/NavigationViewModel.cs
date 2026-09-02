using System;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 导航视图模型：持有当前页面状态与导航命令（底部 Tab 切换）
    /// </summary>
    public class NavigationViewModel : ObservableObject
    {
        private readonly ILogService _logService;

        private PageType _currentPage = PageType.FeedDrawers;

        /// <summary>
        /// 当前页面（默认进料抽屉监控页）
        /// </summary>
        public PageType CurrentPage
        {
            get => _currentPage;
            private set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    OnPropertyChanged(nameof(CurrentPageTitle));
                }
            }
        }

        /// <summary>
        /// 当前页面标题（供日志等场景使用）
        /// </summary>
        public string CurrentPageTitle => PageTitleOf(CurrentPage);

        /// <summary>
        /// 导航命令：参数为 PageType（Tab 点击 → 切换页面）
        /// </summary>
        public RelayCommand NavigateCommand { get; }

        /// <summary>
        /// 构造：注入日志服务
        /// </summary>
        public NavigationViewModel(ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));

            // 导航命令：CanExecute 校验参数类型，Execute 执行页面切换
            NavigateCommand = new RelayCommand(
                execute: p => Navigate(p),
                canExecute: p => p is PageType);
        }

        /// <summary>
        /// 执行页面切换（同页重复点击忽略），并记录导航日志
        /// </summary>
        private void Navigate(object? parameter)
        {
            if (parameter is not PageType page || CurrentPage == page)
            {
                return;
            }

            CurrentPage = page;
            _logService.Info($"切换页面 → {PageTitleOf(page)}");
        }

        /// <summary>
        /// 页面类型 → 中文标题映射
        /// </summary>
        public static string PageTitleOf(PageType page) => page switch
        {
            PageType.Print => "打印管理",
            PageType.Image => "图像管理",
            PageType.Recipe => "配方管理",
            _ => "进料抽屉监控"
        };
    }
}