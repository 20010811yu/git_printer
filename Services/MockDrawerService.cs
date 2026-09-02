using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// 模拟抽屉服务：定时随机改变 18 个抽屉的物料/配方状态（演示用）
    /// 真实设备时替换为 PLC 实现即可，UI/VM 层无需改动
    /// </summary>
    public class MockDrawerService : IDrawerService
    {
        private readonly List<DrawerModel> _drawers = new();
        private readonly Random _random = new();
        private readonly object _lock = new();
        private System.Threading.Timer? _timer;
        private static readonly string[] _recipeNames = { "R-101", "R-205", "R-330", "R-415", "R-508" };

        /// <inheritdoc />
        public event EventHandler<DrawerModel>? DrawerChanged;

        /// <summary>
        /// 初始化：生成 18 个抽屉的随机初始状态
        /// </summary>
        public MockDrawerService()
        {
            for (int i = 1; i <= 18; i++)
            {
                lock (_lock)
                {
                    _drawers.Add(new DrawerModel
                    {
                        Index = i,
                        HasMaterial = _random.NextDouble() > 0.4,   // 60% 有料
                        Recipe = string.Empty                        // 初始配方为空（由用户输入驱动状态灯）
                    });
                }
            }
        }

        /// <inheritdoc />
        public Task<Result<List<DrawerModel>>> GetAllDrawersAsync()
        {
            try
            {
                lock (_lock)
                {
                    // 返回深拷贝，避免外部直接修改内部数据
                    var snapshot = _drawers
                        .Select(d => new DrawerModel { Index = d.Index, HasMaterial = d.HasMaterial, Recipe = d.Recipe })
                        .ToList();
                    return Task.FromResult(Result<List<DrawerModel>>.OK(snapshot));
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<List<DrawerModel>>.Fail($"读取抽屉状态失败：{ex.Message}"));
            }
        }

        /// <inheritdoc />
        public Task<Result<bool>> SendRecipeAsync(int drawerIndex, string recipe)
        {
            try
            {
                // 模拟网络/PLC 通信延迟（500ms）
                return Task.Delay(500).ContinueWith(_ =>
                {
                    lock (_lock)
                    {
                        var drawer = _drawers.FirstOrDefault(d => d.Index == drawerIndex);
                        if (drawer is null)
                        {
                            return Result<bool>.Fail($"抽屉 {drawerIndex} 不存在");
                        }

                        drawer.Recipe = recipe;
                    }

                    return Result<bool>.OK(true);
                });
            }
            catch (Exception ex)
            {
                return Task.FromResult(Result<bool>.Fail($"下发配方失败：{ex.Message}"));
            }
        }

        /// <inheritdoc />
        public void StartMonitoring()
        {
            if (_timer is not null)
            {
                return; // 防止重复启动
            }

            // 每 2 秒随机改变一个抽屉的物料状态，并抛出变化事件（模拟真实设备状态推送）
            _timer = new System.Threading.Timer(_ =>
            {
                int index;
                DrawerModel changed;
                lock (_lock)
                {
                    var drawer = _drawers[_random.Next(_drawers.Count)];
                    drawer.HasMaterial = !drawer.HasMaterial;
                    index = drawer.Index;
                    changed = new DrawerModel { Index = drawer.Index, HasMaterial = drawer.HasMaterial, Recipe = drawer.Recipe };
                }

                DrawerChanged?.Invoke(this, changed);
            }, null, 1000, 2000);
        }
    }
}