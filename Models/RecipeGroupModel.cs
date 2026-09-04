using System.Collections.Generic;

namespace UiTopMachine.Models
{
    /// <summary>
    /// 抽屉配方分组实体（纯 POCO）：同配方值的抽屉归为一组。
/// DrawerIndexes 为组内抽屉编号列表，按配方的填入先后顺序排列（非编号大小排序）；
    /// 每个抽屉当前只有一个配方，只会出现在一个组中（多次写入只保留最后一次）
    /// </summary>
    public class RecipeGroupModel
    {
        /// <summary>配方值（Trim 后，分组依据）</summary>
        public string RecipeName { get; set; } = string.Empty;

        /// <summary>组内抽屉编号（按填入先后顺序，编号不重复）</summary>
        public IReadOnlyList<int> DrawerIndexes { get; set; } = new List<int>();
    }
}
