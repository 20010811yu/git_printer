using System.Drawing;

namespace UiTopMachine.Models
{
    /// <summary>
    /// 单次视觉检测结果实体（纯数据）
    /// </summary>
    public class ImageInspectionResult
    {
        /// <summary>检测结果图像（由检测服务生成，UI 层负责显示；生命周期归 VM 管理）</summary>
        public Image Image { get; set; } = new Bitmap(1, 1);

        /// <summary>检测结论：true=OK / false=NG</summary>
        public bool IsOk { get; set; }

        /// <summary>检测序号（第几次检测）</summary>
        public int Sequence { get; set; }

        /// <summary>检测耗时</summary>
        public TimeSpan Elapsed { get; set; }
    }
}
