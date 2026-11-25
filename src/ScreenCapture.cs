using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
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
        private const uint SourceCopy = 0x00CC0020;
        internal BitmapSource Image;
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
            return capture;
        }
        internal static BitmapSource CapturePixels(System.Drawing.Rectangle bounds)
        {
            int stride = checked(bounds.Width * 4);
            int size = checked(stride * bounds.Height);
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader)),
                Width = bounds.Width, Height = -bounds.Height, Planes = 1, BitCount = 32
            };
            IntPtr desktop = GetDC(IntPtr.Zero);
            if (desktop == IntPtr.Zero) throw new Win32Exception();
            IntPtr target = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                target = CreateCompatibleDC(desktop);
                if (target == IntPtr.Zero) throw new Win32Exception();
                IntPtr pixels;
                bitmap = CreateDIBSection(desktop, ref header, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) throw new Win32Exception();
                previous = SelectObject(target, bitmap);
                if (previous == IntPtr.Zero) throw new Win32Exception();
                if (!BitBlt(target, 0, 0, bounds.Width, bounds.Height, desktop, bounds.Left, bounds.Top, SourceCopy))
                    throw new Win32Exception();
                if (!GdiFlush()) throw new Win32Exception("无法读取桌面像素。");
                // BI_RGB's fourth byte is unused. WPF copies the opaque BGR pixels before the DIB is released.
                var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, pixels, size, stride);
                image.Freeze();
                return image;
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(target, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (target != IntPtr.Zero) DeleteDC(target);
                ReleaseDC(IntPtr.Zero, desktop);
            }
        }
        internal static Int32Rect PixelRect(Rect rect, int width, int height)
        {
            if (rect.IsEmpty) return new Int32Rect();
            int left = Math.Max(0, Math.Min(width, (int)Math.Floor(rect.Left))), top = Math.Max(0, Math.Min(height, (int)Math.Floor(rect.Top)));
            int right = Math.Max(left, Math.Min(width, (int)Math.Ceiling(rect.Right))), bottom = Math.Max(top, Math.Min(height, (int)Math.Ceiling(rect.Bottom)));
            return new Int32Rect(left, top, right - left, bottom - top);
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, ImageSize;
            internal int XPelsPerMeter, YPelsPerMeter;
            internal uint ColorsUsed, ColorsImportant;
        }
        private delegate bool WindowCallback(IntPtr hwnd, IntPtr param);
        [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr param);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern bool BitBlt(IntPtr target, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Native.Rect value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
