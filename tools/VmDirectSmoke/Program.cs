using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VMControls.RenderInterface;

namespace VmDirectSmoke
{
    /// <summary>
    /// 冒烟验证（用后即弃）：混合方案核心链路——
    /// 主程序（net10.0）不加载 VM SDK 引擎，仅直引 VMControls 渲染控件（纯托管），
    /// 自实现 IImageData 包装 PNG 像素，喂 VmRenderControl.ImageSource 渲染。
    /// 通过 → 「桥接进程出图 + VmRenderControl 显示」混合方案可行（内置平移/缩放）
    /// </summary>
    internal static class Program
    {
        private const string VmLibsDir = @"D:\visionmaster\VisionMaster4.4.0\Applications\myLibs";
        private const string TestPng = @"D:\GitRepo\tools\VmVisionBridge\bin\Debug\net48\grab\grab_1.png";

        [STAThread]
        private static int Main()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== VmRenderControl 混合方案冒烟（net10.0 + ImageSource）===");

            // VMControls 依赖兜底解析：先 myLibs 同目录，再 GAC 物理路径（.NET 10 不读 GAC，手动补解析）
            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                var name = new AssemblyName(args.Name).Name;

                var myLibs = Path.Combine(VmLibsDir, name + ".dll");
                if (File.Exists(myLibs))
                {
                    return Assembly.LoadFrom(myLibs);
                }

                var gacRoot = @"C:\Windows\Microsoft.Net\assembly\GAC_MSIL";
                var gacDir = Path.Combine(gacRoot, name);
                if (Directory.Exists(gacDir))
                {
                    var dll = Directory.GetFiles(gacDir, name + ".dll", SearchOption.AllDirectories);
                    if (dll.Length > 0)
                    {
                        Console.WriteLine("    [resolver] GAC 加载: " + name);
                        return Assembly.LoadFrom(dll[0]);
                    }
                }

                return null;
            };

            try
            {
                // ① 实例化 VmRenderControl（冒烟一已证实 net10.0 可实例化）
                Console.WriteLine("[1] 创建 VmRenderControl…");
                using var host = new Form();
                var render = new VMControls.Winform.Release.VmRenderControl
                {
                    Dock = DockStyle.Fill
                };
                host.Controls.Add(render);
                host.CreateControl();
                Console.WriteLine("    OK");

                // ② 读 PNG → 24bpp 像素 Buffer + 尺寸 → 自实现 IImageData
                Console.WriteLine("[2] PNG → IImageData 包装…");
                using var bitmap = new Bitmap(TestPng);
                var data = new BitmapImageData
                {
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    PixelFormat = "RGB24",
                    Buffer = ToRgb24Buffer(bitmap)
                };
                Console.WriteLine($"    OK（{data.Width}x{data.Height}，buffer {data.Buffer.Length}B）");

                // ③ 喂 ImageSource 渲染（不经过 ModuleSource/VmProcedure）
                Console.WriteLine("[3] 设置 ImageSource…");
                render.ImageSource = data;
                Console.WriteLine("    OK");

                // ④ 强制重绘 + 消息循环数帧（暴露渲染线程/原生依赖问题）
                Console.WriteLine("[4] 渲染数帧…");
                render.Refresh();
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 1500)
                {
                    Application.DoEvents();
                }

                render.Refresh();
                while (sw.ElapsedMilliseconds < 3000)
                {
                    Application.DoEvents();
                }

                Console.WriteLine("=== 混合方案全链路通过：桥接保留 + VmRenderControl 显示可行 ===");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAIL " + ex.GetType().Name + ": " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.WriteLine("    Inner: " + ex.InnerException.Message);
                }

                Console.WriteLine("=== 混合方案验证失败：回退 PictureBox ===");
                return 1;
            }
        }

        /// <summary>
        /// Bitmap 转 24 位 RGB 像素缓冲（自底向上行序按 VM 惯例；如渲染翻转再改 TopDown）
        /// </summary>
        private static byte[] ToRgb24Buffer(Bitmap bitmap)
        {
            var bmp = bitmap;
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var converted = bmp.PixelFormat == PixelFormat.Format24bppRgb
                ? bmp
                : bmp.Clone(rect, PixelFormat.Format24bppRgb);
            var locked = converted.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                var buffer = new byte[Math.Abs(locked.Stride) * locked.Height];
                var pos = 0;
                for (int y = 0; y < locked.Height; y++)
                {
                    var rowStart = locked.Scan0 + y * locked.Stride;
                    System.Runtime.InteropServices.Marshal.Copy(rowStart, buffer, pos, locked.Width * 3);
                    pos += locked.Width * 3;
                }

                return buffer;
            }
            finally
            {
                converted.UnlockBits(locked);
                if (!ReferenceEquals(converted, bmp))
                {
                    converted.Dispose();
                }
            }
        }

        /// <summary>
        /// IImageData 最小实现：包装像素缓冲（属性为接口要求）
        /// </summary>
        private sealed class BitmapImageData : IImageData
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public byte[] Buffer { get; set; } = Array.Empty<byte>();
            public string MemoryAddress => string.Empty;
            public string PixelFormat { get; set; } = "RGB24";
        }
    }
}