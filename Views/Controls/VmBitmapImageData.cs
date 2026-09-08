using System;
using System.Drawing;
using System.Drawing.Imaging;
using VMControls.RenderInterface;

namespace UiTopMachine.Views.Controls
{
    /// <summary>
    /// Bitmap → VmRenderControl 图像数据适配器：
    /// 实现 VMControls 的 IImageData 接口（Width/Height/Buffer/PixelFormat），
    /// 把托管 Bitmap 的像素拷贝为 24 位 RGB 缓冲，供 VmRenderControl.ImageSource 渲染。
    /// 混合架构（v1.26）：检测在桥接进程（PNG 回传），渲染控件在本进程仅吃纯图像数据，
    /// 不触碰 VM 引擎对象（net10.0 直引引擎已实证原生崩溃）。
    /// </summary>
    internal sealed class VmBitmapImageData : IImageData
    {
        private VmBitmapImageData(int width, int height, byte[] buffer)
        {
            Width = width;
            Height = height;
            Buffer = buffer;
        }

        /// <inheritdoc />
        public int Width { get; }

        /// <inheritdoc />
        public int Height { get; }

        /// <inheritdoc />
        public byte[] Buffer { get; set; } = Array.Empty<byte>();

        /// <inheritdoc />
        public string MemoryAddress => string.Empty;

        /// <inheritdoc />
        public string PixelFormat => "RGB24";

        /// <summary>
        /// 从 Bitmap 构建适配器：像素统一转为 24 位 RGB 逐行缓冲（失败返回 null，调用方回退 PictureBox）
        /// </summary>
        public static VmBitmapImageData? TryCreate(Image image)
        {
            if (image is not Bitmap bitmap || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return null;
            }

            try
            {
                var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var converted = bitmap.PixelFormat == System.Drawing.Imaging.PixelFormat.Format24bppRgb
                    ? bitmap
                    : bitmap.Clone(rect, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                try
                {
                    var locked = converted.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                    try
                    {
                        var rowBytes = locked.Width * 3;
                        var buffer = new byte[rowBytes * locked.Height];
                        for (int y = 0; y < locked.Height; y++)
                        {
                            var rowStart = locked.Scan0 + y * locked.Stride;
                            System.Runtime.InteropServices.Marshal.Copy(rowStart, buffer, y * rowBytes, rowBytes);
                        }

                        return new VmBitmapImageData(locked.Width, locked.Height, buffer);
                    }
                    finally
                    {
                        converted.UnlockBits(locked);
                    }
                }
                finally
                {
                    if (!ReferenceEquals(converted, bitmap))
                    {
                        converted.Dispose();
                    }
                }
            }
            catch
            {
                return null; // 像素转换失败不致命，调用方保留上一张显示
            }
        }
    }
}