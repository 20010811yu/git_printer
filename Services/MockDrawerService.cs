using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// 模拟抽屉服务：提供 18 个抽屉的初始数据（默认无料无配方 = 空闲灰态）。
    /// 有料状态由 PLC 物料轮询推送（v1.12 起 PLC 为唯一真值源，随机演示已停用），
    /// 配方由用户输入驱动
    /// </summary>
    public class MockDrawerService : IDrawerService
    {
        private readonly List<DrawerModel> _drawers = new();
        private readonly object _lock = new();

        /// <summary>
        /// 保留接口事件（v1.12 起 PLC 物料轮询为真值源，Mock 不再触发推送）
        /// </summary>
#pragma warning disable CS0067 // 接口要求实现；Mock 已停用随机推送，不触发该事件
        public event EventHandler<DrawerModel>? DrawerChanged;
#pragma warning restore CS0067

        /// <summary>
        /// 初始化：生成 18 个抽屉的默认状态（无料无配方 = 空闲）
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
                        HasMaterial = false,         // 默认无料（PLC 连接后由物料轮询推送真实状态）
                        Recipe = string.Empty        // 默认无配方（由用户输入驱动状态灯）
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
            // 空操作：v1.12 起 PLC 物料轮询为有料状态唯一真值源，
            // 随机演示推送已停用（与 PLC 真值互相覆盖会造成状态跳动）
        }
    }
}
