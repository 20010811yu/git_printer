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
        /// 新建空白配方（备份轮转模式，v1.7b）：
        /// ① 当前工作文件存在 → 重命名备份为「原文件名_yyyyMMdd_HHmmss.xlsx」（原数据完整保留；
        ///    同一秒连续新建自动追加 _2/_3 序号，绝不覆盖任何已有文件）；
        /// ② 当前工作文件不存在 → 跳过备份直接新建；
        /// ③ 构造模板表（传入表头 + N 空白行，首列空格占位持久化）写入原路径——
        ///    新配方沿用原文件名，FilePath 不变。
        /// </summary>
        /// <param name="headers">新表的表头列名序列（应与既有配方表一致；空序列时回退默认表头）</param>
        /// <param name="blankRowCount">空白行数量（默认 10 行，便于页面美观与用户录入）</param>
        /// <returns>成功时返回备份文件完整路径（无备份时为空字符串，供日志提示）</returns>
        Task<Result<string>> CreateBlankAsync(IEnumerable<string> headers, int blankRowCount = 10);

        /// <summary>
        /// 打开配方所在文件夹（资源管理器）
        /// </summary>
        Result<bool> OpenFolder();
    }
}