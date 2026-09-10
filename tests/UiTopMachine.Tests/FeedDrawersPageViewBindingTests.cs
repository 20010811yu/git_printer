using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using UiTopMachine.Models;
using UiTopMachine.ViewModels;
using UiTopMachine.Views.Pages;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 进料抽屉页 View 绑定守护测试（v1.32 输入框换 AntdUI.Input）：
    /// 守护不变量——① 18 个配方输入框全部为 AntdUI.Input；② 与 DrawerItemViewModel.Recipe
    /// 双向绑定生效（AntdUI.Input.SetText 触发 OnTextChanged，UI→VM 才能成立）；
    /// ③ 只读权限联动（无料只读）；④ 宽度随单元格自适应且落在常量区间内。
    /// 注：WinForms 绑定在控件未挂到已显示窗体（无 BindingContext/句柄）时不激活（ERR-029 同源），
    /// 因此用例在 STA 线程上建宿主 Form 并 Show 后再断言。
    /// </summary>
    public class FeedDrawersPageViewBindingTests
    {
        private static object? GetField(object instance, string name) =>
            instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(instance);

        private static (FeedDrawersPage Page, MainViewModel Vm, Form Host) CreateHostedPage(bool seedHasMaterial)
        {
            FeedDrawersPage? page = null;
            MainViewModel? vm = null;
            Form? host = null;
            var thread = new Thread(() =>
            {
                var drawerService = new StubDrawerService
                {
                    SeedDrawers = new List<DrawerModel>
                    {
                        new() { Index = 1, HasMaterial = seedHasMaterial, Recipe = string.Empty },
                        new() { Index = 2, HasMaterial = true, Recipe = string.Empty }
                    }
                };
                vm = new MainViewModel(drawerService, new StubLogService(),
                    new StubPlcCommunicationService(), new StubRecipeFileService());
                page = new FeedDrawersPage(vm); // 先建页面（订阅 Drawers.CollectionChanged）
                host = new Form { Size = new System.Drawing.Size(1280, 800) };
                host.Controls.Add(page);
                host.Show(); // 创建句柄 + BindingContext，激活绑定管道（否则绑定静默失效）
                vm.InitializeAsync().GetAwaiter().GetResult(); // 再装载 18 抽屉，同步触发逐格绑定
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.NotNull(page);
            Assert.NotNull(vm);
            Assert.NotNull(host);
            return (page!, vm!, host!);
        }

        private static List<object> GetCells(FeedDrawersPage page) =>
            ((System.Collections.IEnumerable?)GetField(page, "_cells"))?.Cast<object>().ToList()
                ?? throw new InvalidOperationException("未找到 _cells 字段");

        [Fact]
        public void 配方输入框全部为AntdUI_Input且宽度落在自适应区间()
        {
            var (page, _, host) = CreateHostedPage(seedHasMaterial: true);
            var cells = GetCells(page);
            Assert.Equal(18, cells.Count);

            foreach (var cell in cells)
            {
                var box = Assert.IsType<AntdUI.Input>(GetField(cell, "RecipeBox"));
                Assert.InRange(box.Width, FeedDrawersPage.InputMinWidth, FeedDrawersPage.InputMaxWidth);
            }
            host.Dispose();
        }

        [Fact]
        public void 配方输入框与VM双向绑定生效()
        {
            var (page, vm, host) = CreateHostedPage(seedHasMaterial: true);
            var box = Assert.IsType<AntdUI.Input>(GetField(GetCells(page)[0], "RecipeBox"));

            // VM → UI：PLC/程序侧写入配方，输入框同步显示
            vm.Drawers[0].Recipe = "R009";
            Assert.Equal("R009", box.Text);

            // UI → VM：用户键入（程序性赋值同样触发 TextChanged），回写 VM 驱动状态灯
            box.Text = "R010";
            Assert.Equal("R010", vm.Drawers[0].Recipe);
            host.Dispose();
        }

        [Fact]
        public void 无料抽屉输入框只读联动生效()
        {
            var (page, vm, host) = CreateHostedPage(seedHasMaterial: false);
            var cells = GetCells(page);
            var readonlyBox = Assert.IsType<AntdUI.Input>(GetField(cells[0], "RecipeBox"));
            var editableBox = Assert.IsType<AntdUI.Input>(GetField(cells[1], "RecipeBox"));

            Assert.True(readonlyBox.ReadOnly, "无料抽屉输入框应为只读（IsInputReadOnly 绑定失效守护）");
            Assert.False(editableBox.ReadOnly, "有料抽屉输入框应可编辑");
            host.Dispose();
        }
    }
}
