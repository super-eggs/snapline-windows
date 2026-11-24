using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal sealed class CaptureTarget
    {
        internal readonly IntPtr Handle;
        internal readonly Rect Bounds;
        internal CaptureTarget(IntPtr handle, Rect bounds) { Handle = handle; Bounds = bounds; }
    }

    internal sealed class ScreenCapture
    {
        internal BitmapSource Image;
        internal BitmapSource DimmedImage;
        internal System.Drawing.Rectangle Bounds;
        internal readonly List<CaptureTarget> Windows = new List<CaptureTarget>();
        internal static ScreenCapture Take()
        {
            var capture = new ScreenCapture { Bounds = Forms.SystemInformation.VirtualScreen };
            var bounds = capture.Bounds;
            if ((long)bounds.Width * bounds.Height > 160000000) throw new NotSupportedException("桌面总像素超过截图上限（1.6 亿像素）。");
            EnumWindows(delegate(IntPtr hwnd, IntPtr param)
            {
                Native.Rect rect; int cloaked;
                if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || hwnd == Native.GetShellWindow()) return true;
                if (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                if (DwmGetWindowAttribute(hwnd, 9, out rect, Marshal.SizeOf(typeof(Native.Rect))) != 0 && !Native.GetWindowRect(hwnd, out rect)) return true;
                var region = new Rect(rect.Left - bounds.Left, rect.Top - bounds.Top, Math.Max(0, rect.Right - rect.Left), Math.Max(0, rect.Bottom - rect.Top));
                region.Intersect(new Rect(0, 0, bounds.Width, bounds.Height));
                if (!region.IsEmpty && region.Width > 1 && region.Height > 1) capture.Windows.Add(new CaptureTarget(hwnd, region));
                return true;
            }, IntPtr.Zero);
            capture.Image = CapturePixels(bounds);
            capture.DimmedImage = DimDesktop(capture.Image);
            return capture;
        }
        internal static BitmapSource DimDesktop(BitmapSource image)
        {
            BitmapSource pixels = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = image.PixelWidth * 4, rows = Math.Min(32, image.PixelHeight);
            var buffer = new byte[stride * rows];
            var result = new WriteableBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Bgra32, null);
            for (int y = 0; y < image.PixelHeight; y += rows)
            {
                var rect = new Int32Rect(0, y, image.PixelWidth, Math.Min(rows, image.PixelHeight - y));
                pixels.CopyPixels(rect, buffer, stride, 0);
                for (int i = 0; i < rect.Height * stride; i += 4)
                {
                    int factor = buffer[i + 3] * 125;
                    buffer[i] = (byte)((buffer[i] * factor + 32512) / 65025);
                    buffer[i + 1] = (byte)((buffer[i + 1] * factor + 32512) / 65025);
                    buffer[i + 2] = (byte)((buffer[i + 2] * factor + 32512) / 65025);
                    buffer[i + 3] = 255;
                }
                result.WritePixels(rect, buffer, stride, 0);
            }
            result.Freeze(); return result;
        }
        internal static BitmapSource CapturePixels(System.Drawing.Rectangle bounds)
        {
            using (var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppRgb))
            {
                using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, System.Drawing.CopyPixelOperation.SourceCopy);
                IntPtr handle = bitmap.GetHbitmap();
                try
                {
                    var image = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); image.Freeze(); return image;
                }
                finally { DeleteObject(handle); }
            }
        }
        internal static Int32Rect PixelRect(Rect rect, int width, int height)
        {
            if (rect.IsEmpty) return new Int32Rect();
            int left = Math.Max(0, Math.Min(width, (int)Math.Floor(rect.Left))), top = Math.Max(0, Math.Min(height, (int)Math.Floor(rect.Top)));
            int right = Math.Max(left, Math.Min(width, (int)Math.Ceiling(rect.Right))), bottom = Math.Max(top, Math.Min(height, (int)Math.Ceiling(rect.Bottom)));
            return new Int32Rect(left, top, right - left, bottom - top);
        }
        private delegate bool WindowCallback(IntPtr hwnd, IntPtr param);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr param);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Native.Rect value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
