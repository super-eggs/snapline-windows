using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Snapline
{
    internal static class ControlDetectionTests
    {
        private static void WaitFor(Func<bool> condition)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.ElapsedMilliseconds < 5000) CollectionTests.Wait(Task.Delay(30));
        }
        private static Rect Local(System.Drawing.Rectangle rect, System.Drawing.Rectangle desktop)
        {
            return new Rect(rect.Left - desktop.Left, rect.Top - desktop.Top, rect.Width, rect.Height);
        }
        private static ScreenCapture SyntheticScreen(IntPtr handle, System.Drawing.Rectangle bounds)
        {
            var image = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgr32, null, new byte[bounds.Width * bounds.Height * 4], bounds.Width * 4);
            image.Freeze();
            var screen = new ScreenCapture { Bounds = bounds, Image = image };
            screen.Windows.Add(new CaptureTarget(handle, new Rect(0, 0, bounds.Width, bounds.Height)));
            return screen;
        }
        internal static void Run(Action<bool, string> check)
        {
            var desktop = new System.Drawing.Rectangle(-900, -200, 1200, 800);
            var window = new Rect(0, 0, 500, 400);
            var rectangles = ControlDetector.Normalize(new[]
            {
                new Rect(-880, -170, 80, 24), new Rect(-880, -170, 80, 24),
                new Rect(-850, -170, 100, 30), new Rect(-920, -220, 420, 320), Rect.Empty
            }, window, new Point(40, 40), desktop);
            check(rectangles.SequenceEqual(new[] { new Rect(20, 30, 80, 24), new Rect(0, 0, 400, 300), window }),
                "control candidates use physical desktop offsets, clip parents, and discard duplicate or non-nested bounds");
            check(ControlDetector.Find(new CaptureTarget(IntPtr.Zero, window), new Point(40, 40), desktop).SequenceEqual(new[] { window }),
                "missing accessibility target retains the whole-window candidate");

            using (var fixture = new Forms.Form { Text = "Snapline 控件识别验证", Width = 520, Height = 340, TopMost = true,
                StartPosition = Forms.FormStartPosition.Manual, Location = new System.Drawing.Point(100, 100), ShowInTaskbar = false, AutoScaleMode = Forms.AutoScaleMode.None })
            {
                var panel = new Forms.Panel { Left = 30, Top = 35, Width = 420, Height = 220, AccessibleRole = Forms.AccessibleRole.Grouping };
                var button = new Forms.Button { Left = 20, Top = 25, Width = 130, Height = 36, Text = "合成测试按钮" };
                var list = new Forms.ListBox { Left = 190, Top = 25, Width = 180, Height = 120 };
                list.Items.AddRange(new object[] { "合成列表第一项", "合成列表第二项" });
                panel.Controls.Add(button); panel.Controls.Add(list); fixture.Controls.Add(panel); fixture.Show();
                Native.Rect native; Native.GetWindowRect(fixture.Handle, out native);
                var bounds = System.Drawing.Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
                var screen = SyntheticScreen(fixture.Handle, bounds);
                Rect expected = Local(button.RectangleToScreen(button.ClientRectangle), bounds);
                Point point = new Point(expected.Left + expected.Width / 2, expected.Top + expected.Height / 2);
                var capture = new CaptureWindow(screen, delegate { return Task.FromResult(true); });
                try
                {
                    capture.Show(); capture.UpdateHover(point);
                    check(capture.Hover == screen.Windows[0].Bounds, "whole-window hover is available immediately while control lookup is pending");
                    WaitFor(delegate { return capture.Hover == expected; });
                    check(capture.Hover == expected && capture.HoverLevels >= 3, "real MSAA finds a native button and its parents beneath the visible capture overlay");
                    capture.ChangeHoverLevel(1); Rect parent = capture.Hover;
                    check(parent != expected && parent.Contains(expected), "parent selection expands to the containing native panel");
                    capture.UpdateHover(point);
                    capture.UpdateHover(new Point(point.X + 1, point.Y));
                    check(capture.Hover == parent, "stationary events and movement inside the same control preserve the chosen hierarchy level");
                    var keys = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(capture), Environment.TickCount, Key.D2) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    capture.RaiseEvent(keys);
                    check(keys.Handled && capture.Hover == expected, "2 selects the child level through the real capture key handler");
                    var stage = (Canvas)capture.Content;
                    stage.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                    check(capture.Hover == parent, "mouse wheel selects the parent level through the capture event handler");
                    for (int i = 0; i < 40; i++) capture.ChangeHoverLevel(1);
                    check(capture.Hover == screen.Windows[0].Bounds, "hierarchy stops at the captured top-level window");
                    for (int i = 0; i < 40; i++) capture.ChangeHoverLevel(-1);
                    check(capture.Hover == expected, "child hierarchy stops at the element under the pointer");
                    Rect item = Local(list.RectangleToScreen(list.GetItemRectangle(1)), bounds);
                    capture.UpdateHover(new Point(item.Left + 10, item.Top + item.Height / 2));
                    WaitFor(delegate { return capture.Hover.Width < list.Width && capture.Hover.Height <= item.Height + 2; });
                    check(capture.Hover.Contains(new Point(item.Left + 10, item.Top + item.Height / 2)) && capture.Hover.Height <= item.Height + 2,
                        "real MSAA resolves a list item represented by a child ID");
                    capture.UpdateHover(point); capture.SetSelection(new Rect(10, 10, 50, 40));
                    CollectionTests.Wait(Task.Delay(200));
                    check(capture.Selection == new Rect(10, 10, 50, 40), "pending hover lookup cannot replace a confirmed manual selection");
                }
                finally { capture.Close(); }
                screen.Windows.Clear(); screen.Windows.Add(new CaptureTarget(IntPtr.Zero, new Rect(0, 0, bounds.Width, bounds.Height)));
                capture = new CaptureWindow(screen, delegate { return Task.FromResult(true); });
                try
                {
                    capture.UpdateHover(point); capture.ChangeHoverLevel(1); capture.ChangeHoverLevel(-1);
                    check(capture.Hover == screen.Windows[0].Bounds && capture.HoverLevels == 1, "unavailable control provider keeps whole-window selection at every hierarchy key");
                }
                finally { capture.Close(); }
                var unavailable = new CaptureTarget(fixture.Handle, screen.Windows[0].Bounds);
                fixture.Close();
                var lookup = Task.Run(delegate { return ControlDetector.Find(unavailable, point, bounds); });
                CollectionTests.Wait(lookup);
                check(lookup.Result.SequenceEqual(new[] { unavailable.Bounds }), "window closed after screenshot safely falls back without a provider exception");
            }
            VerifyWpf(check);
        }

        private static void VerifyWpf(Action<bool, string> check)
        {
            var button = new Button { Content = "WPF 合成按钮", Width = 120, Height = 36, Margin = new Thickness(25) };
            var fixture = new Window { Title = "Snapline WPF 控件识别验证", Width = 340, Height = 220, Left = 120, Top = 120,
                ShowInTaskbar = false, Topmost = true, Content = new GroupBox { Header = "合成分组", Margin = new Thickness(20), Content = button } };
            CaptureWindow capture = null;
            try
            {
                fixture.Show(); fixture.UpdateLayout();
                IntPtr handle = new WindowInteropHelper(fixture).Handle;
                Native.Rect native; Native.GetWindowRect(handle, out native);
                var bounds = System.Drawing.Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
                Point start = button.PointToScreen(new Point()), end = button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight));
                var expected = new Rect(new Point(start.X - bounds.Left, start.Y - bounds.Top), new Point(end.X - bounds.Left, end.Y - bounds.Top));
                capture = new CaptureWindow(SyntheticScreen(handle, bounds), delegate { return Task.FromResult(true); });
                capture.Show(); capture.UpdateHover(new Point(expected.Left + 5, expected.Top + 5));
                WaitFor(delegate { return capture.Hover == expected; });
                check(capture.Hover == expected && capture.HoverLevels >= 2, "MSAA bridge detects a WPF button without its own HWND beneath the overlay");
            }
            finally { if (capture != null) capture.Close(); fixture.Close(); }
        }
    }
}
