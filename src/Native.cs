using System;
using System.Runtime.InteropServices;

namespace Snapline
{
    internal static class Native
    {
        internal static string LayerName(WindowLayer layer)
        {
            return layer == WindowLayer.Top ? "始终置顶" : layer == WindowLayer.Bottom ? "始终置底" : "普通层级";
        }
        internal static void ApplyLayer(System.Windows.Window window, WindowLayer layer)
        {
            window.Topmost = layer == WindowLayer.Top;
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, new IntPtr(layer == WindowLayer.Top ? -1 : layer == WindowLayer.Bottom ? 1 : -2), 0, 0, 0, 0, 0x0013);
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern IntPtr GetShellWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder name, int length);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
        internal static string ScreenshotFolder()
        {
            Guid id = new Guid("B7BEDE81-DF94-4682-A7D8-57A52620B86F");
            IntPtr path;
            if (SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out path) == 0)
            {
                try { return Marshal.PtrToStringUni(path); }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
        }
        internal static bool Fullscreen(System.Windows.Forms.Screen screen, IntPtr ours)
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero || hwnd == ours || hwnd == GetShellWindow()) return false;
            var name = new System.Text.StringBuilder(64);
            GetClassName(hwnd, name, name.Capacity);
            if (name.ToString() == "WorkerW" || name.ToString() == "Progman") return false;
            Rect r;
            if (!GetWindowRect(hwnd, out r)) return false;
            var b = screen.Bounds;
            return r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
        }
    }
}
