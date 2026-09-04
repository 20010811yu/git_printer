using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 抽屉配方分组测试（v1.19，语义同参考程序 list/merList）：
    /// 按配方值分组；组内编号按填入先后顺序（非编号大小）；编号不重复；
    /// 同抽屉多次写入配方只保留最后一次（旧组失去该抽屉，新组排末尾）；清空配方移出分组
    /// </summary>
    public class RecipeGroupingTests
    {
        /// <summary>构造已初始化的 MainViewModel（18 个默认抽屉：无料无配方）</summary>
        private static MainViewModel CreateInitializedViewModel()
        {
            var drawerService = new StubDrawerService
            {
                SeedDrawers = Enumerable.Range(1, 18)
                    .Select(i => new DrawerModel { Index = i, HasMaterial = true, Recipe = string.Empty })
                    .ToList()
            };
            var vm = new MainViewModel(drawerService, new StubLogService(), new StubPlcCommunicationService());
            return vm;
        }

        private static void SetRecipe(MainViewModel vm, int drawerIndex, string recipe)
        {
            vm.Drawers.First(d => d.Index == drawerIndex).Recipe = recipe;
        }

        [Fact]
        public async Task 同配方抽屉_组内按填入先后顺序_非编号排序()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            // 先填 5、后填 3（编号大小顺序相反）
            SetRecipe(vm, 5, "R001");
            SetRecipe(vm, 3, "R001");

            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal("R001", group.RecipeName);
            Assert.Equal(new[] { 5, 3 }, group.DrawerIndexes); // 填入顺序，而非 [3,5]
        }

        [Fact]
        public async Task 不同配方_分为多组_组间按形成顺序()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 1, "B"); // B 组先形成
            SetRecipe(vm, 2, "A"); // A 组后形成
            SetRecipe(vm, 4, "B");

            Assert.Equal(2, vm.RecipeGroups.Count);

            // 组间按形成顺序：B（先）在前，A（后）在后；组内按填入顺序
            Assert.Equal("B", vm.RecipeGroups[0].RecipeName);
            Assert.Equal(new[] { 1, 4 }, vm.RecipeGroups[0].DrawerIndexes);
            Assert.Equal("A", vm.RecipeGroups[1].RecipeName);
            Assert.Equal(new[] { 2 }, vm.RecipeGroups[1].DrawerIndexes);
        }

        [Fact]
        public async Task 同抽屉重写配方_只保留最后一次写入()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 3, "R001");
            SetRecipe(vm, 5, "R001");
            SetRecipe(vm, 3, "R002"); // 抽屉 3 从 R001 改写 R002

            Assert.Equal(2, vm.RecipeGroups.Count);

            // R001 失去抽屉 3，只剩 5；R002 获得抽屉 3（作为其最后一次写入）
            var r001 = vm.RecipeGroups.First(g => g.RecipeName == "R001");
            Assert.Equal(new[] { 5 }, r001.DrawerIndexes);

            var r002 = vm.RecipeGroups.First(g => g.RecipeName == "R002");
            Assert.Equal(new[] { 3 }, r002.DrawerIndexes);

            // 编号不重复：3 只出现在一个组
            var allIndexes = vm.RecipeGroups.SelectMany(g => g.DrawerIndexes).ToList();
            Assert.Equal(allIndexes.Count, allIndexes.Distinct().Count());
        }

        [Fact]
        public async Task 重写同配方_仍保持组内最后位置()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 3, "R001");
            SetRecipe(vm, 5, "R001");
            SetRecipe(vm, 3, "R001");
            vm.RefreshRecipeSequence(3); // 焦点离开时刷新次序（重复写入同一配方也按最后一次写入计序）

            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal(new[] { 5, 3 }, group.DrawerIndexes); // 3 的次序晚于 5（最后写入）
        }

        [Fact]
        public async Task 不同配方重写_旧组失去该抽屉_新组排末尾()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 3, "R001");
            SetRecipe(vm, 5, "R001");
            SetRecipe(vm, 3, "R002"); // 抽屉 3 改写为 R002（值变化自动触发重算）

            Assert.Equal(2, vm.RecipeGroups.Count);
            Assert.Equal(new[] { 5 }, vm.RecipeGroups.First(g => g.RecipeName == "R001").DrawerIndexes);
            Assert.Equal(new[] { 3 }, vm.RecipeGroups.First(g => g.RecipeName == "R002").DrawerIndexes);
        }

        [Fact]
        public async Task 清空配方_移出分组_重新填写视为新填入()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 3, "R001");
            SetRecipe(vm, 5, "R001");
            SetRecipe(vm, 3, string.Empty); // 清空抽屉 3 配方

            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal(new[] { 5 }, group.DrawerIndexes);
            Assert.Empty(vm.Drawers.First(d => d.Index == 3).Recipe);

            SetRecipe(vm, 3, "R001"); // 重新填写
            Assert.Equal(new[] { 5, 3 }, Assert.Single(vm.RecipeGroups).DrawerIndexes);
        }

        [Fact]
        public async Task 纯空白配方_视为无配方_不分组()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 3, "   "); // 纯空白
            SetRecipe(vm, 5, "R001");

            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal(new[] { 5 }, group.DrawerIndexes);
        }

        [Fact]
        public async Task 配方值分组_按Trim后比较_前后空格不拆组()
        {
            var vm = CreateInitializedViewModel();
            await vm.InitializeAsync();

            SetRecipe(vm, 3, "R001");
            SetRecipe(vm, 5, " R001 "); // 带空格，Trim 后同组

            var group = Assert.Single(vm.RecipeGroups);
            Assert.Equal("R001", group.RecipeName); // 组名为 Trim 后的值
            Assert.Equal(new[] { 3, 5 }, group.DrawerIndexes);
        }
    }
}
