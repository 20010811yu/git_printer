using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UiTopMachine.Models;

namespace UiTopMachine.Services.Interfaces
{
    /// <summary>
    /// 抽屉服务返回结果（统一 Result 模式）
    /// </summary>
    public class Result<T>
    {
        /// <summary>是否成功</summary>
        public bool Success { get; init; }

        /// <summary>错误消息（失败时）</summary>
        public string? ErrorMessage { get; init; }

        /// <summary>返回数据</summary>
        public T? Data { get; init; }

        /// <summary>成功结果</summary>
        public static Result<T> OK(T data) => new() { Success = true, Data = data };

        /// <summary>失败结果</summary>
        public static Result<T> Fail(string message) => new() { Success = false, ErrorMessage = message };
    }

    /// <summary>
    /// 抽屉服务接口：隔离具体设备/PLC 实现
    /// </summary>
    public interface IDrawerService
    {
        /// <summary>
        /// 抽屉状态变化事件（后台线程触发，订阅方需自行调度到 UI 线程）
        /// </summary>
        event EventHandler<DrawerModel>? DrawerChanged;

        /// <summary>
        /// 读取全部抽屉状态
        /// </summary>
        Task<Result<List<DrawerModel>>> GetAllDrawersAsync();

        /// <summary>
        /// 下发配方到指定抽屉
        /// </summary>
        /// <param name="drawerIndex">抽屉编号</param>
        /// <param name="recipe">配方名称</param>
        Task<Result<bool>> SendRecipeAsync(int drawerIndex, string recipe);

        /// <summary>
        /// 启动状态模拟（仅 Mock 服务实现，真实设备实现为空操作）
        /// </summary>
        void StartMonitoring();
    }
}