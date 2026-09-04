using UiTopMachine.Models;

namespace UiTopMachine.Services.Interfaces
{
    /// <summary>
    /// 图像视觉检测服务抽象（仿参考程序 VisionMaster/VmSolution 流程）：
    /// 方案加载 → 加载成功事件 → 周期/单次运行检测 → 返回结果图。
    /// 真实实现（海康 VisionMaster SDK，本地 dll 依赖）就绪后替换注册即可，VM/View 无需改动
    /// </summary>
    public interface IImageInspectionService
    {
        /// <summary>
        /// 方案加载成功事件（后台线程触发，对应参考 VmSolution_OnSolutionLoadEndEvent）
        /// </summary>
        event EventHandler? SolutionLoaded;

        /// <summary>方案是否已加载成功（对应参考 _vmConnected）</summary>
        bool IsSolutionLoaded { get; }

        /// <summary>目标流程名称（对应参考 VmSolution.Instance["Testing"]）</summary>
        string ProcedureName { get; }

        /// <summary>
        /// 加载检测方案（对应参考 VmSolution.Load；重复加载幂等）
        /// </summary>
        Task<Result<bool>> LoadSolutionAsync(string solutionPath);

        /// <summary>
        /// 运行一次检测（对应参考 VmProcedure.SyncRun：采集 + 执行流程 + 返回结果图）；
        /// 方案未加载时返回失败
        /// </summary>
        Task<Result<ImageInspectionResult>> RunInspectionAsync();

        /// <summary>
        /// 停止服务并释放资源（应用退出时调用）
        /// </summary>
        void Shutdown();
    }
}
