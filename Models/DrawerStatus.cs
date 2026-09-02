using System;

namespace UiTopMachine.Models
{
    /// <summary>
    /// 抽屉状态枚举（三态）
    /// </summary>
    public enum DrawerStatus
    {
        /// <summary>
        /// 空闲：无料 且 无配方（灰色）
        /// </summary>
        Idle = 0,

        /// <summary>
        /// 就绪：有料 且 有配方（绿色）
        /// </summary>
        Ready = 1,

        /// <summary>
        /// 预警：其余状态，如有料无配方 / 无料有配方（黄色）
        /// </summary>
        Warning = 2
    }
}