using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using Accessibility;

namespace Snapline
{
    internal static class ControlDetector
    {
        private static readonly Guid AccessibleId = new Guid("618736E0-3C3D-11CF-810C-00AA00389B71");

        // Query the recorded underlying HWND: point lookup on the desktop would hit our capture overlay.
        internal static List<Rect> Find(CaptureTarget target, Point point, System.Drawing.Rectangle desktop)
        {
            var rectangles = new List<Rect>();
            if (target.Handle == IntPtr.Zero) return Normalize(rectangles, target.Bounds, point, desktop);
            int initialized = CoInitializeEx(IntPtr.Zero, 0);
            if (initialized < 0) Marshal.ThrowExceptionForHR(initialized);
            try
            {
                int x = desktop.Left + (int)point.X, y = desktop.Top + (int)point.Y;
                IntPtr window = target.Handle;
                // HWND children are needed for native controls; accessible children need not have an HWND.
                for (int depth = 0; depth < 32; depth++)
                {
                    var client = new Native.Point { X = x, Y = y };
                    if (!ScreenToClient(window, ref client)) break;
                    IntPtr child = ChildWindowFromPointEx(window, client, 5); // Skip invisible/transparent windows.
                    if (child == IntPtr.Zero || child == window) break;
                    window = child;
                }
                Guid iid = AccessibleId;
                IAccessible current;
                if (AccessibleObjectFromWindow(window, 0, ref iid, out current) < 0 || current == null)
                    return Normalize(rectangles, target.Bounds, point, desktop);
                var clock = Stopwatch.StartNew();
                int childId = 0;
                for (int depth = 0; depth < 32 && clock.ElapsedMilliseconds < 250; depth++)
                {
                    object hit = current.accHitTest(x, y);
                    var next = hit as IAccessible;
                    if (next == null && hit is int && (int)hit != 0)
                    {
                        childId = (int)hit;
                        try { next = current.get_accChild(childId) as IAccessible; }
                        catch (Exception ex)
                        {
                            // Simple MSAA children have an ID and bounds but may not expose an object.
                            if (!(ex is COMException || ex is NotImplementedException || ex is ArgumentException || ex is InvalidCastException)) throw;
                        }
                    }
                    if (next == null || ReferenceEquals(current, next)) break;
                    current = next; childId = 0;
                }
                for (int depth = 0; current != null && depth < 32; depth++)
                {
                    ReadBounds(current, childId, rectangles);
                    if (clock.ElapsedMilliseconds >= 250) break;
                    if (childId != 0) childId = 0;
                    else current = current.accParent as IAccessible;
                }
            }
            catch (Exception ex)
            {
                // Providers may disappear, deny access or omit MSAA members. Window selection still works.
                if (!(ex is COMException || ex is UnauthorizedAccessException || ex is NotImplementedException || ex is ArgumentException || ex is InvalidCastException)) throw;
            }
            finally { CoUninitialize(); }
            return Normalize(rectangles, target.Bounds, point, desktop);
        }

        private static void ReadBounds(IAccessible accessible, int childId, List<Rect> rectangles)
        {
            try
            {
                int x, y, width, height;
                object state = accessible.get_accState(childId);
                if (state is int && ((int)state & 0x18000) != 0) return; // Invisible or offscreen.
                accessible.accLocation(out x, out y, out width, out height, childId);
                if (width > 1 && height > 1) rectangles.Add(new Rect(x, y, width, height));
            }
            catch (Exception ex)
            {
                if (!(ex is COMException || ex is NotImplementedException || ex is ArgumentException || ex is InvalidCastException)) throw;
            }
        }

        internal static List<Rect> Normalize(IEnumerable<Rect> rectangles, Rect window, Point point, System.Drawing.Rectangle desktop)
        {
            var result = new List<Rect>();
            foreach (Rect value in rectangles)
            {
                if (value.IsEmpty) continue;
                Rect rect = value; rect.Offset(-desktop.Left, -desktop.Top); rect.Intersect(window);
                var pixels = ScreenCapture.PixelRect(rect, desktop.Width, desktop.Height);
                if (pixels.Width < 2 || pixels.Height < 2) continue;
                rect = new Rect(pixels.X, pixels.Y, pixels.Width, pixels.Height);
                if (!rect.Contains(point) || rect == window) continue;
                if (result.Count > 0 && (rect == result[result.Count - 1] || !rect.Contains(result[result.Count - 1]))) continue;
                result.Add(rect);
            }
            result.Add(window);
            return result;
        }

        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint model);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IAccessible accessible);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref Native.Point point);
        [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr hwnd, Native.Point point, uint flags);
    }
}
