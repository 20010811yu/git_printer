using System.Drawing;

namespace UiTopMachine.Models
{
    /// <summary>
    /// 单次视觉检测结果实体（纯数据）
    /// </summary>
    public class ImageInspectionResult
    {
        /// <summary>
        /// 检测结果图像（由检测服务生成，UI 层负责显示；生命周期归 VM/View 管理）。
        /// null = 本运行无新帧（低速图像源常态，按跳过处理，ERR-028）
        /// </summary>
        public Image? Image { get; set; }

        /// <summary>检测结论：true=OK / false=NG</summary>
        public bool IsOk { get; set; }

        /// <summary>检测序号（第几次检测）</summary>
        public int Sequence { get; set; }

        /// <summary>检测耗时</summary>
        public TimeSpan Elapsed { get; set; }
    }
}
