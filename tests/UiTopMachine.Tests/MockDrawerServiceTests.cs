using System.Linq;
using System.Threading.Tasks;
using UiTopMachine.Models;
using UiTopMachine.Services;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// Mock 抽屉服务默认状态测试：18 个托盘初始一律无料无配方（空闲灰态，v1.17），
    /// 有料状态由 PLC 物料轮询推送覆盖（PLC 为唯一真值源）
    /// </summary>
    public class MockDrawerServiceTests
    {
        [Fact]
        public async Task 初始抽屉_全部无料无配方_共18个()
        {
            var service = new MockDrawerService();

            var result = await service.GetAllDrawersAsync();

            Assert.True(result.Success);
            Assert.NotNull(result.Data);
            Assert.Equal(18, result.Data.Count);
            Assert.All(result.Data, d =>
            {
                Assert.False(d.HasMaterial);
                Assert.Equal(string.Empty, d.Recipe);
            });
            Assert.Equal(Enumerable.Range(1, 18), result.Data.Select(d => d.Index));
        }

        [Fact]
        public void 启动监控_为空操作_不推送随机状态()
        {
            // v1.12 起 StartMonitoring 已停用随机演示（与 PLC 真值冲突）；
            // 调用后不应产生任何抽屉变化（DrawerChanged 不触发、数据不被随机翻转）
            var service = new MockDrawerService();
            var changed = 0;
            service.DrawerChanged += (_, _) => changed++;

            service.StartMonitoring();

            Assert.Equal(0, changed);
        }
    }
}
