using System;

namespace UiTopMachine.Models
{
    /// <summary>
    /// 抽屉数据实体（纯 POCO，只存数据）
    /// </summary>
    public class DrawerModel
    {
        /// <summary>
        /// 抽屉编号（1~18）
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 是否有料
        /// </summary>
        public bool HasMaterial { get; set; }

        /// <summary>
        /// 配方名称（空表示无配方）
        /// </summary>
        public string Recipe { get; set; } = string.Empty;
    }
}