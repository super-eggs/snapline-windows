using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class SnipasteToolsTests
    {
        private static byte[] Pixels(BitmapSource source)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
        }
        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        internal static void Run(string root, string artifacts, Action<bool, string> check)
        {
            byte[] bytes = { 10, 20, 30, 255, 40, 50, 60, 255, 70, 80, 90, 255, 100, 110, 120, 255 };
            var sample = BitmapSource.Create(2, 2, 192, 144, PixelFormats.Bgra32, null, bytes, 8); sample.Freeze();
            var lens = new PixelLens(); lens.SetImage(sample); lens.Sample(new Point(1.9, .9));
            check(lens.PixelX == 1 && lens.PixelY == 0 && lens.ColorText == "#3C3228", "color picker uses exact source pixels independently of bitmap DPI");
            lens.ToggleFormat(); check(lens.ColorText == "RGB(60, 50, 40)", "Shift color format preserves exact RGB channels");
            lens.Sample(new Point(-5, 9)); check(lens.PixelX == 0 && lens.PixelY == 1 && lens.Color.R == 90, "magnifier sampling clamps to image edges without shifting target pixel");
            var lensRender = new RenderTargetBitmap(186, 224, 96, 96, PixelFormats.Pbgra32);
            lens.Measure(new Size(186, 224)); lens.Arrange(new Rect(0, 0, 186, 224)); lensRender.Render(lens);
            var lensPixels = Pixels(lensRender); int center = (90 * 186 + 92) * 4;
            check(lensPixels[center] == 70 && lensPixels[center + 1] == 80 && lensPixels[center + 2] == 90, "magnifier center cell shows selected edge pixel without interpolation");
            File.WriteAllBytes(Path.Combine(artifacts, "pixel-lens.png"), Store.Encode(lensRender));
            var patchBytes = new byte[31 * 23 * 4];
            for (int y = 0; y < 23; y++)
                for (int x = 0; x < 31; x++)
                {
                    int offset = (y * 31 + x) * 4;
                    patchBytes[offset] = (byte)(x * 7); patchBytes[offset + 1] = (byte)(y * 9);
                    patchBytes[offset + 2] = 120; patchBytes[offset + 3] = 255;
                }
            lens.SetImage(BitmapSource.Create(31, 23, 96, 96, PixelFormats.Bgra32, null, patchBytes, 31 * 4));
            foreach (Point point in new[] { new Point(12, 10), new Point(0, 0), new Point(30, 22), new Point(30, 0), new Point(0, 22) })
            {
                lens.Sample(point); lens.UpdateLayout(); lensRender.Clear(); lensRender.Render(lens);
                var rendered = Pixels(lensRender);
                check(rendered[center] == point.X * 7 && rendered[center + 1] == point.Y * 9 && rendered[center + 2] == 120,
                    "magnifier preserves exact center pixel at " + point);
            }
            lens.Sample(new Point(30, 22)); lens.UpdateLayout(); lensRender.Clear(); lensRender.Render(lens);
            var edgePixels = Pixels(lensRender); int outside = (100 * 186 + 103) * 4;
            check(edgePixels[outside] == 255 && edgePixels[outside + 1] == 255 && edgePixels[outside + 2] == 255,
                "magnifier leaves pixels beyond image edge empty after moving between samples");
            lens.SetImage(sample); lens.Sample(new Point(0, 0));
            check(lens.Color == Color.FromRgb(30, 20, 10), "changing magnifier source refreshes an unchanged cursor coordinate");
            Color color;
            check(PixelLens.TryParseColor("#2580db", out color) && color == Color.FromRgb(37, 128, 219), "F3 recognizes copied hexadecimal color");
            check(PixelLens.TryParseColor("#abc", out color) && color == Color.FromRgb(170, 187, 204), "short hexadecimal color expands correctly");
            check(PixelLens.TryParseColor("RGB(37, 128, 219)", out color) && color.B == 219, "F3 recognizes copied RGB color");
            check(!PixelLens.TryParseColor("RGB(300, 0, 0)", out color) && !PixelLens.TryParseColor("rgb(1,2,3", out color) && !PixelLens.TryParseColor("1,2,3)", out color), "malformed or out-of-range color text is not misread as a color");
            var swatch = PinBoard.FromText("#2580DB");
            check(swatch.PixelWidth == 320 && swatch.PixelHeight == 180 && Pixels(swatch).Take(4).SequenceEqual(new byte[] { 219, 128, 37, 255 }), "F3 color card has exact opaque source color and readable labels");
            var text = PinBoard.FromText("多行贴图\n第二行");
            check(text != null && text.IsFrozen && text.PixelHeight >= 80, "F3 plain Chinese text converts to a standalone frozen image");
            check(PinBoard.FromText(" \n ") == null, "empty clipboard text does not open a pin");
            bool textBounded = false; try { PinBoard.FromText(new string('a', 16001)); } catch (NotSupportedException) { textBounded = true; }
            check(textBounded, "clipboard text conversion is bounded before allocating image");
            bool heightBounded = false; try { PinBoard.FromText("开头" + new string('\n', 2000) + "结尾"); } catch (NotSupportedException) { heightBounded = true; }
            check(heightBounded, "very tall clipboard text is rejected before exceeding render-target dimensions");
            BitmapSource original = App.DemoImage(1);
            using (var board = new PinBoard(delegate { }, delegate { }))
            {
                var pin = board.Show(original, 142);
                check(pin.Topmost && pin.IsVisible && pin.WindowStyle == WindowStyle.None, "F3 opens a borderless always-on-top image window");
                check(ReferenceEquals(pin, board.Show(original, 142)) && board.Images.Count() == 1, "repeated F3 for unchanged clipboard raises existing pin without duplication");
                var clean = pin.RenderImage();
                pin.ChangeZoom(2); pin.ChangeOpacity(.35);
                var zoomed = pin.RenderImage();
                File.WriteAllBytes(Path.Combine(artifacts, "pin-original.png"), Store.Encode(clean));
                File.WriteAllBytes(Path.Combine(artifacts, "pin-zoom-export.png"), Store.Encode(zoomed));
                check(Pixels(zoomed).SequenceEqual(Pixels(clean)), "pin zoom and window opacity do not alter copied image pixels");
                pin.ChangeZoom(100); check(pin.Zoom == 8, "pin zoom has 800 percent upper bound");
                pin.ChangeZoom(.001); check(pin.Zoom == .05, "pin zoom has five percent lower bound");
                pin.ChangeOpacity(0); check(pin.Opacity == .1, "pin opacity cannot become completely invisible");
                pin.ChangeOpacity(2); check(pin.Opacity == 1, "pin opacity upper bound is opaque");
                pin.Rotate(90);
                var rotated = pin.RenderImage(); var expected = new TransformedBitmap(clean, new RotateTransform(90));
                check(rotated.PixelWidth == clean.PixelHeight && rotated.PixelHeight == clean.PixelWidth && Pixels(rotated).SequenceEqual(Pixels(expected)), "pin rotation copies original-resolution rotated pixels");
                pin.Rotate(-90); pin.Flip(true);
                check(Pixels(pin.RenderImage()).SequenceEqual(Pixels(new TransformedBitmap(clean, new ScaleTransform(-1, 1)))), "pin horizontal flip affects output at original resolution");
                pin.Flip(true); pin.Flip(false);
                check(Pixels(pin.RenderImage()).SequenceEqual(Pixels(new TransformedBitmap(clean, new ScaleTransform(1, -1)))), "pin vertical flip affects output at original resolution");
                pin.Flip(false); pin.Annotations.AddMark(AnnotationTool.Arrow, new Point(20, 20), new Point(180, 100));
                check(!Pixels(pin.RenderImage()).SequenceEqual(Pixels(clean)), "pinned screenshot supports the shared annotation tools");
                pin.Annotations.Undo(); check(Pixels(pin.RenderImage()).SequenceEqual(Pixels(clean)), "pinned annotation undo restores source pixels");
                board.ToggleAll(); check(!pin.IsVisible && board.Images.Count() == 1, "Shift+F3 hides pins without closing or destroying them");
                board.ToggleAll(); check(pin.IsVisible, "Shift+F3 restores hidden pins");
                pin.Close(); check(!pin.IsVisible && !board.Images.Any(), "closing a pin removes it from active group but keeps recovery");
                check(ReferenceEquals(pin, board.Show(original, 142)) && pin.IsVisible, "F3 restores last closed pin for unchanged clipboard");
                board.Close(pin, true); check(!board.Images.Any() && board.RestoreLast() == null, "destroyed pin cannot be restored from closed-pin history");
            }
            int pinned = 0; BitmapSource pinnedImage = null;
            var screen = new ScreenCapture { Image = original, Bounds = new System.Drawing.Rectangle(0, 0, original.PixelWidth, original.PixelHeight) };
            string copiedColor = null;
            var capture = new CaptureWindow(screen, delegate { return Task.FromResult(true); }, delegate(string value) { copiedColor = value; }, delegate(BitmapSource image) { pinned++; pinnedImage = image; });
            try
            {
                capture.UpdateLens(new Point(10, 10));
                check(capture.Lens.Visibility == Visibility.Collapsed && capture.Lens.PixelX == 0 && capture.Lens.PixelY == 0,
                    "capture starts without showing or sampling the color picker");
                var picker = Descendants<Button>((DependencyObject)capture.Content).Single(button => Equals(button.Tag, "picker"));
                check(!picker.IsEnabled, "color picker toolbar action is unavailable before confirming a selection");
                capture.SetSelection(new Rect(20, 30, 200, 100)); capture.UpdateLens(new Point(40, 50));
                check(picker.IsEnabled && capture.Lens.Visibility == Visibility.Collapsed && capture.Lens.PixelX == 0,
                    "confirming and adjusting a selection does not start pixel sampling");
                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(capture.Lens.Visibility == Visibility.Visible && capture.Lens.PixelX == 40 && capture.Lens.PixelY == 50,
                    "toolbar color picker starts exact source sampling only when explicitly enabled");
                var input = PresentationSource.FromVisual(capture) ?? new HwndSource(new HwndSourceParameters("Snapline picker test") { Width = 1, Height = 1 });
                try
                {
                    capture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, Environment.TickCount, Key.C) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    check(copiedColor == capture.Lens.ColorText && capture.Selection == new Rect(20, 30, 200, 100),
                        "color picker copies a color without completing or changing the screenshot selection");
                    capture.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, input, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    check(capture.Lens.Visibility == Visibility.Collapsed && picker.IsEnabled && capture.Selection == new Rect(20, 30, 200, 100)
                        && !Descendants<TextBlock>((DependencyObject)capture.Content).Any(label => label.Text.Contains("Esc 退出取色")),
                        "Escape exits color picking while keeping the selected screenshot open");
                }
                finally { if (PresentationSource.FromVisual(capture) == null) ((HwndSource)input).Dispose(); }
                capture.UpdateLens(new Point(45, 55));
                check(capture.Lens.PixelX == 40, "exiting the picker stops sampling while the mouse continues moving");
                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var pen = Descendants<Button>(capture.Annotations.Toolbar).Single(button => Equals(button.Tag, "pen"));
                pen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(capture.Lens.Visibility == Visibility.Collapsed && capture.Annotations.Tool == AnnotationTool.Pen,
                    "switching to annotation tools exits the color picker and preserves the chosen tool");
                picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); picker.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                check(capture.Lens.Visibility == Visibility.Collapsed, "color picker toolbar button toggles sampling off");
                capture.PinSelection(); check(pinned == 1 && pinnedImage.PixelWidth == 200 && pinnedImage.PixelHeight == 100, "Ctrl+T exports selected pixels directly to pin callback without clipboard dependency");
            }
            finally { capture.Close(); }
            var settings = new Store(Path.Combine(root, "pin-settings"), false);
            settings.Settings.PinHotkey = "Ctrl+Alt+P"; settings.Settings.TogglePinsHotkey = "Ctrl+Shift+P"; settings.SaveSettings();
            var restored = new Store(settings.Root, false);
            check(restored.Settings.PinHotkey == "Ctrl+Alt+P" && restored.Settings.TogglePinsHotkey == "Ctrl+Shift+P", "paste and group-toggle hotkeys persist independently of screenshot settings");
            var hotkeyWindow = new Window(); new WindowInteropHelper(hotkeyWindow).EnsureHandle();
            try
            {
                int snips = 0, pastes = 0;
                using (var snip = new CaptureHotkey(hotkeyWindow, delegate { snips++; }))
                using (var paste = new CaptureHotkey(hotkeyWindow, delegate { pastes++; }, 4))
                {
                    check(snip.Register("Ctrl+Alt+Shift+F23") && paste.Register("Ctrl+Alt+Shift+F22"), "independent global capture and paste bindings can coexist on one native window");
                    var handle = new WindowInteropHelper(hotkeyWindow).Handle;
                    Native.SendMessage(handle, 0x0312, new IntPtr(4), IntPtr.Zero);
                    check(pastes == 1 && snips == 0, "paste hotkey routes only to pin callback");
                    Native.SendMessage(handle, 0x0312, new IntPtr(2), IntPtr.Zero);
                    check(pastes == 1 && snips == 1, "capture hotkey remains independent of paste callback");
                }
            }
            finally { hotkeyWindow.Close(); }
        }
    }
}
