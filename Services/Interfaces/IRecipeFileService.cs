using System;
using System.Data;
using System.Threading.Tasks;

namespace UiTopMachine.Services.Interfaces
{
    /// <summary>
    /// 配方文件服务接口：隔离 Excel（xlsx）读写实现，ViewModel 不接触 ClosedXML 对象
    /// 支持多配方文件管理：默认主配方文件 + 新建带时间戳的空白配方文件（原文件保留）
    /// </summary>
    public interface IRecipeFileService
    {
        /// <summary>
        /// 默认主配方文件完整路径（D:\Printer\Data\Recipe.xlsx）
        /// </summary>
        string FilePath { get; }

        /// <summary>
        /// 配方所在文件夹路径（D:\Printer\Data，新建配方也保存在此目录）
        /// </summary>
        string FolderPath { get; }

        /// <summary>
        /// 加载默认主配方文件（文件不存在时返回含表头的空表）
        /// </summary>
        Task<Result<DataTable>> LoadAsync();

        /// <summary>
        /// 加载指定路径的配方文件（用于切换到新建配方；文件不存在时返回含表头的空表）
        /// </summary>
        Task<Result<DataTable>> LoadAsync(string filePath);

        /// <summary>
        /// 保存配方表格到默认主配方文件（覆写）
        /// </summary>
        Task<Result<bool>> SaveAsync(DataTable table);

        /// <summary>
        /// 保存配方表格到指定路径（覆写，用于保存当前工作文件）
        /// </summary>
        Task<Result<bool>> SaveAsync(DataTable table, string filePath);

        /// <summary>
        /// 新建空白配方文件（不影响任何已有文件）：
        /// 文件名 = 安全化配方名_yyyyMMdd_HHmmss.xlsx（文件名后附加时间戳，重名自动追加序号），
        /// 保存到当前配方表格所在目录（FolderPath），并写入默认表头 + 首行配方编号。
        /// </summary>
        /// <param name="recipeName">配方名称（作为新文件名前缀）</param>
        /// <param name="recipeId">配方编号（写入新表首行编号列，唯一性由 ViewModel 校验）</param>
        /// <returns>成功时返回新配方文件完整路径</returns>
        Task<Result<string>> CreateBlankAsync(string recipeName, string recipeId);

        /// <summary>
        /// 打开配方所在文件夹（资源管理器）
        /// </summary>
        Result<bool> OpenFolder();
    }
}