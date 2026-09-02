using System;
using UiTopMachine.Common.Commands;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.ViewModels
{
    /// <summary>
    /// 单个抽屉的视图模型：状态判定 + 配方双向绑定
    /// </summary>
    public class DrawerItemViewModel : ObservableObject
    {
        private readonly ILogService _logService;

        private int _index;
        private bool _hasMaterial;
        private string _recipe = string.Empty;
        private DrawerStatus _status = DrawerStatus.Idle;

        /// <summary>
        /// 抽屉编号（1~18）
        /// </summary>
        public int Index
        {
            get => _index;
            private set => SetProperty(ref _index, value);
        }

        /// <summary>
        /// 是否有料
        /// </summary>
        public bool HasMaterial
        {
            get => _hasMaterial;
            set
            {
                if (SetProperty(ref _hasMaterial, value))
                {
                    RefreshStatus();
                }
            }
        }

        /// <summary>
        /// 配方名称（与输入框双向绑定，输入变化即时联动状态灯）
        /// </summary>
        public string Recipe
        {
            get => _recipe;
            set
            {
                if (SetProperty(ref _recipe, value ?? string.Empty))
                {
                    RefreshStatus();
                }
            }
        }

        /// <summary>
        /// 抽屉状态（三态）
        /// </summary>
        public DrawerStatus Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        /// <summary>
        /// 状态描述文本（显示在状态灯下方提示）
        /// </summary>
        public string StatusText => Status switch
        {
            DrawerStatus.Ready => "就绪",
            DrawerStatus.Warning => "预警",
            _ => "空闲"
        };

        /// <summary>
        /// 构造抽屉项 VM
        /// </summary>
        public DrawerItemViewModel(int index, bool hasMaterial, string recipe, ILogService logService)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            Index = index;
            _hasMaterial = hasMaterial;
            _recipe = recipe ?? string.Empty;
            RefreshStatus();
        }

        /// <summary>
        /// 由服务推送更新物料状态（外部数据同步）
        /// </summary>
        public void UpdateFromModel(DrawerModel model)
        {
            if (model is null || model.Index != Index)
            {
                return;
            }

            // 配方以输入框（用户侧）为准，仅同步物料状态
            HasMaterial = model.HasMaterial;
        }

        /// <summary>
        /// 三态判定核心逻辑：
        /// 有料 + 有配方 → 就绪(绿)；无料 + 无配方 → 空闲(灰)；其余 → 预警(黄)
        /// </summary>
        private void RefreshStatus()
        {
            var oldStatus = Status;
            Status = (HasMaterial, !string.IsNullOrWhiteSpace(Recipe)) switch
            {
                (true, true) => DrawerStatus.Ready,     // 绿：有料有配方
                (false, false) => DrawerStatus.Idle,    // 灰：无料无配方
                _ => DrawerStatus.Warning               // 黄：其余
            };

            if (oldStatus != Status)
            {
                _logService.Info($"抽屉 {Index} 状态变更：{oldStatus} → {Status}（{(HasMaterial ? "有料" : "无料")}，配方：{(string.IsNullOrWhiteSpace(Recipe) ? "无" : Recipe)}）");
            }

            OnPropertyChanged(nameof(StatusText));
        }
    }
}