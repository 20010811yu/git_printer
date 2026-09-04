using UiTopMachine.Models;
using UiTopMachine.ViewModels;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 抽屉三态判定测试（核心业务规则）：
    /// 有料+有配方 → Ready(绿)；无料+无配方 → Idle(灰)；其余 → Warning(黄)
    /// </summary>
    public class DrawerItemViewModelTests
    {
        [Theory]
        [InlineData(true, true, DrawerStatus.Ready)]    // 绿：有料有配方
        [InlineData(true, false, DrawerStatus.Warning)] // 黄：有料无配方
        [InlineData(false, true, DrawerStatus.Warning)] // 黄：无料有配方
        [InlineData(false, false, DrawerStatus.Idle)]   // 灰：无料无配方
        public void 三态判定_物料与配方组合_状态符合业务规则(
            bool hasMaterial, bool hasRecipe, DrawerStatus expected)
        {
            var vm = new DrawerItemViewModel(
                index: 1, hasMaterial: hasMaterial,
                recipe: hasRecipe ? "R001" : string.Empty,
                logService: new StubLogService());

            Assert.Equal(expected, vm.Status);
        }

        [Theory]
        [InlineData(true, true, "就绪")]
        [InlineData(true, false, "预警")]
        [InlineData(false, true, "预警")]
        [InlineData(false, false, "空闲")]
        public void 状态描述文本_与状态联动(
            bool hasMaterial, bool hasRecipe, string expectedText)
        {
            var vm = new DrawerItemViewModel(
                1, hasMaterial, hasRecipe ? "R001" : string.Empty, new StubLogService());

            Assert.Equal(expectedText, vm.StatusText);
        }

        [Fact]
        public void 空白配方文本_视为无配方_状态为预警()
        {
            // 有料 + 纯空白配方 = "其余" → Warning（空白不应视为有配方）
            var vm = new DrawerItemViewModel(1, hasMaterial: true, recipe: "   ", new StubLogService());

            Assert.Equal(DrawerStatus.Warning, vm.Status);
        }

        [Theory]
        [InlineData(true, false)]   // 有料（黄，无配方）→ 可编辑
        [InlineData(false, true)]   // 无料（灰）→ 只读
        public void 输入框编辑权限_由有料状态决定(bool hasMaterial, bool expectedReadOnly)
        {
            var vm = new DrawerItemViewModel(1, hasMaterial, "", new StubLogService());

            Assert.Equal(expectedReadOnly, vm.IsInputReadOnly);
        }

        [Fact]
        public void 有料状态变化_编辑权限属性通知联动()
        {
            var vm = new DrawerItemViewModel(1, hasMaterial: false, "", new StubLogService());
            Assert.True(vm.IsInputReadOnly);

            string? notified = null;
            vm.PropertyChanged += (_, e) => notified = e.PropertyName;

            vm.HasMaterial = true; // PLC 推送有料（灰→黄）

            Assert.False(vm.IsInputReadOnly);
            Assert.Equal(nameof(DrawerItemViewModel.IsInputReadOnly), notified);
        }

        [Fact]
        public void 初始抽屉_默认无料无配方_输入框只读()
        {
            // 每次启动的默认态：灰色空闲 + 配方输入只读（不保留上次运行结果，配方无持久化）
            var vm = new DrawerItemViewModel(1, hasMaterial: false, recipe: string.Empty, new StubLogService());

            Assert.Equal(DrawerStatus.Idle, vm.Status);
            Assert.True(vm.IsInputReadOnly);
            Assert.Equal(string.Empty, vm.Recipe);
        }

        [Fact]
        public void 配方输入变化_状态即时联动()
        {
            var vm = new DrawerItemViewModel(1, hasMaterial: true, recipe: "", new StubLogService());
            Assert.Equal(DrawerStatus.Warning, vm.Status); // 初始：有料无配方

            vm.Recipe = "配方A";
            Assert.Equal(DrawerStatus.Ready, vm.Status); // 输入配方 → 就绪

            vm.Recipe = "   ";
            Assert.Equal(DrawerStatus.Warning, vm.Status); // 清空（空白）→ 预警
        }

        [Fact]
        public void 物料状态推送同步_HasMaterial更新且配方保留()
        {
            var vm = new DrawerItemViewModel(3, hasMaterial: false, recipe: "配方X", new StubLogService());
            Assert.Equal(DrawerStatus.Warning, vm.Status); // 无料有配方

            vm.UpdateFromModel(new DrawerModel { Index = 3, HasMaterial = true, Recipe = "服务端配方" });

            // 物料同步为有料；配方以用户输入侧为准（不被服务端覆盖）
            Assert.True(vm.HasMaterial);
            Assert.Equal("配方X", vm.Recipe);
            Assert.Equal(DrawerStatus.Ready, vm.Status);
        }

        [Fact]
        public void 物料推送编号不匹配_忽略更新()
        {
            var vm = new DrawerItemViewModel(3, hasMaterial: false, recipe: "", new StubLogService());

            vm.UpdateFromModel(new DrawerModel { Index = 99, HasMaterial = true });

            Assert.False(vm.HasMaterial); // 编号不匹配，不更新
        }

        [Fact]
        public void 构造传入null日志服务_抛ArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new DrawerItemViewModel(1, false, "", null!));
        }
    }
}