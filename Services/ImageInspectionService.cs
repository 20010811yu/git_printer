using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using UiTopMachine.Models;
using UiTopMachine.Services.Interfaces;

namespace UiTopMachine.Services
{
    /// <summary>
    /// 图像视觉检测服务 Mock 实现（占位，仿参考程序 VisionMaster 流程结构）：
    /// 方案加载模拟延时后成功；检测运行用 GDI+ 生成模拟检测图（约 20% 概率 NG）。
    /// 真机接入：替换为实现类（内部封装 VmSolution.Load / VmProcedure.SyncRun / 结果图导出），
    /// 注册处一行切换，VM/View 无需改动
    /// </summary>
    public class ImageInspectionService : IImageInspectionService
    {
        private const int SimulatedLoadDelayMs = 800;
        private const int SimulatedRunDelayMs = 300;
        private const int MockImageWidth = 640;
        private const int MockImageHeight = 480;

        private readonly object _lock = new();
        private readonly Random _random = new();
        private int _sequence;

        /// <inheritdoc />
        public event EventHandler? SolutionLoaded;

        /// <inheritdoc />
        public bool IsSolutionLoaded { get; private set; }

        /// <inheritdoc />
        public string ProcedureName { get; } = "Testing";

        /// <inheritdoc />
        public async Task<Result<bool>> LoadSolutionAsync(string solutionPath)
        {
            if (IsSolutionLoaded)
            {
                return Result<bool>.OK(true); // 幂等：方案已加载
            }

            if (string.IsNullOrWhiteSpace(solutionPath))
            {
                return Result<bool>.Fail("方案路径为空，无法加载");
            }

            // 模拟方案加载耗时（真实实现：VmSolution.Load + OnSolutionLoadEndEvent 回调确认）
            await Task.Delay(SimulatedLoadDelayMs);

            lock (_lock)
            {
                IsSolutionLoaded = true;
            }

            SolutionLoaded?.Invoke(this, EventArgs.Empty);
            return Result<bool>.OK(true);
        }

        /// <inheritdoc />
        public async Task<Result<ImageInspectionResult>> RunInspectionAsync()
        {
            if (!IsSolutionLoaded)
            {
                return Result<ImageInspectionResult>.Fail("检测方案未加载，请先加载方案");
            }

            var sw = Stopwatch.StartNew();

            // 模拟采集与流程执行耗时
            await Task.Delay(SimulatedRunDelayMs);

            var sequence = Interlocked.Increment(ref _sequence);
            bool isOk = _random.NextDouble() >= 0.2; // 约 20% NG
            var image = RenderMockImage(isOk, sequence);

            sw.Stop();
            return Result<ImageInspectionResult>.OK(new ImageInspectionResult
            {
                Image = image,
                IsOk = isOk,
                Sequence = sequence,
                Elapsed = sw.Elapsed
            });
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            lock (_lock)
            {
                IsSolutionLoaded = false;
            }
        }

        /// <summary>
        /// 生成模拟检测图：深灰底 + 检测框 + 十字定位线 + 结论大字（OK 绿 / NG 红）+ 随机缺陷标记
        /// </summary>
        private Bitmap RenderMockImage(bool isOk, int sequence)
        {
            var bitmap = new Bitmap(MockImageWidth, MockImageHeight);
            using var g = Graphics.FromImage(bitmap);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            g.Clear(Color.FromArgb(43, 43, 43));

            // 检测框
            using (var framePen = new Pen(Color.FromArgb(0, 174, 255), 2f))
            {
                g.DrawRectangle(framePen, 60, 60, MockImageWidth - 120, MockImageHeight - 120);
            }

            // 十字定位线
            using (var crossPen = new Pen(Color.FromArgb(90, 200, 120), 1f))
            {
                g.DrawLine(crossPen, MockImageWidth / 2f, 40, MockImageWidth / 2f, MockImageHeight - 40);
                g.DrawLine(crossPen, 40, MockImageHeight / 2f, MockImageWidth - 40, MockImageHeight / 2f);
            }

            // 随机缺陷标记（NG 时画 2~4 个红圈）
            if (!isOk)
            {
                using var defectPen = new Pen(Color.Red, 2f);
                int defects = _random.Next(2, 5);
                for (int i = 0; i < defects; i++)
                {
                    int size = _random.Next(20, 45);
                    int x = _random.Next(80, MockImageWidth - 80 - size);
                    int y = _random.Next(80, MockImageHeight - 80 - size);
                    g.DrawEllipse(defectPen, x, y, size, size);
                }
            }

            // 结论大字
            string verdict = isOk ? "OK" : "NG";
            Color verdictColor = isOk ? Color.Lime : Color.Red;
            using (var verdictFont = new Font("Arial", 42f, FontStyle.Bold))
            using (var verdictBrush = new SolidBrush(verdictColor))
            {
                var size = g.MeasureString(verdict, verdictFont);
                g.DrawString(verdict, verdictFont, verdictBrush,
                    (MockImageWidth - size.Width) / 2f, (MockImageHeight - size.Height) / 2f);
            }

            // 角标信息
            using var infoFont = new Font("Consolas", 12f);
            using var infoBrush = new SolidBrush(Color.Gainsboro);
            g.DrawString($"SEQ {sequence:000}  {DateTime.Now:HH:mm:ss.fff}", infoFont, infoBrush, 12, 8);
            g.DrawString("MOCK INSPECTION", infoFont, infoBrush, 12, MockImageHeight - 26);

            return bitmap;
        }
    }
}
