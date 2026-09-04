using System.Drawing;
using System.Threading.Tasks;
using UiTopMachine.Services;
using UiTopMachine.Services.Interfaces;
using Xunit;

namespace UiTopMachine.Tests
{
    /// <summary>
    /// 图像视觉检测服务（Mock）测试：方案加载幂等、未加载拒绝检测、检测结果与图像生成
    /// </summary>
    public class ImageInspectionServiceTests : IDisposable
    {
        private readonly ImageInspectionService _service = new();

        public void Dispose() => _service.Shutdown();

        [Fact]
        public async Task 未加载方案时_检测失败_提示先加载()
        {
            var result = await _service.RunInspectionAsync();

            Assert.False(result.Success);
            Assert.Contains("未加载", result.ErrorMessage);
            Assert.False(_service.IsSolutionLoaded);
        }

        [Fact]
        public async Task 加载方案_成功且幂等_触发加载完成事件()
        {
            var loadedEvents = 0;
            _service.SolutionLoaded += (_, _) => loadedEvents++;

            var result = await _service.LoadSolutionAsync(@"D:\test\DetectionProcess.sol");

            Assert.True(result.Success);
            Assert.True(_service.IsSolutionLoaded);

            await _service.LoadSolutionAsync(@"D:\test\DetectionProcess.sol"); // 重复加载幂等
            Assert.Equal(1, loadedEvents);
        }

        [Fact]
        public async Task 空方案路径_加载失败()
        {
            var result = await _service.LoadSolutionAsync(" ");

            Assert.False(result.Success);
            Assert.False(_service.IsSolutionLoaded);
        }

        [Fact]
        public async Task 加载后运行检测_返回结果图与序号_图尺寸符合约定()
        {
            await _service.LoadSolutionAsync("sol");

            var r1 = await _service.RunInspectionAsync();
            var r2 = await _service.RunInspectionAsync();

            Assert.True(r1.Success);
            Assert.True(r2.Success);
            Assert.NotNull(r1.Data);
            Assert.NotNull(r2.Data);
            Assert.Equal(1, r1.Data!.Sequence);
            Assert.Equal(2, r2.Data!.Sequence);
            Assert.NotNull(r2.Data.Image);
            Assert.Equal(640, r2.Data.Image.Width);
            Assert.Equal(480, r2.Data.Image.Height);
            Assert.True(r2.Data.Elapsed >= TimeSpan.Zero);
        }

        [Fact]
        public async Task Shutdown后_检测拒绝()
        {
            await _service.LoadSolutionAsync("sol");
            _service.Shutdown();

            var result = await _service.RunInspectionAsync();

            Assert.False(result.Success);
        }
    }
}
