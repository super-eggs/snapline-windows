using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class CaptureTests
    {
        private static byte[] Pixels(BitmapSource source)
        {
            var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
        }
        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        internal static void Run(string root, string artifacts, Action<bool, string> check)
        {
            VerifyClipboardRetry(check);
            BitmapSource original = App.DemoImage(0);
            var canvas = new InkCanvas { Width = original.PixelWidth, Height = original.PixelHeight, Background = new ImageBrush(original) { Stretch = Stretch.Fill } };
            var tools = new AnnotationTools(canvas, original);
            byte[] baseline = Pixels(Preview.RenderAnnotation(canvas));
            foreach (AnnotationTool tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Ellipse, AnnotationTool.Line, AnnotationTool.Arrow })
            {
                tools.AddMark(tool, new Point(50, 50), new Point(200, 140));
                check(!Pixels(Preview.RenderAnnotation(canvas)).SequenceEqual(baseline), "annotation renders " + tool + " in original pixels");
                tools.Undo(); check(Pixels(Preview.RenderAnnotation(canvas)).SequenceEqual(baseline), "undo removes " + tool + " completely");
                tools.Redo(); check(tools.HasAnnotations, "redo restores " + tool); tools.Undo();
            }
            tools.AddMark(AnnotationTool.Rectangle, new Point(10, 10), new Point(130, 60), true);
            var square = (System.Windows.Shapes.Path)canvas.Children[0];
            check(square.Data.Bounds.Width == square.Data.Bounds.Height, "Shift constrains rectangle to square"); tools.Undo();
            tools.AddText(new Point(30, 80), "中文标注\n第二行");
            check(canvas.Children.OfType<TextBlock>().Single().Text.Contains("第二行") && !Pixels(Preview.RenderAnnotation(canvas)).SequenceEqual(baseline), "Chinese multiline text commits and renders");
            tools.Undo(); check(!tools.HasAnnotations, "undo text uses shared annotation history");
            tools.AddText(new Point(20, 30), "   "); check(!tools.HasAnnotations, "empty text creates no annotation");
            tools.AddMark(AnnotationTool.Rectangle, new Point(50, 50), new Point(200, 140));
            tools.Undo(); tools.AddMark(AnnotationTool.Arrow, new Point(50, 50), new Point(200, 140)); tools.Redo();
            check(canvas.Children.Count == 1, "new edit after undo discards redo branch"); tools.Undo();
            // A high frequency pattern makes pixelation measurable rather than relying on a flat demo background.
            byte[] pattern = new byte[96 * 96 * 4];
            for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
            { int offset = (y * 96 + x) * 4; pattern[offset] = (byte)(x * 2); pattern[offset + 1] = (byte)(y * 2); pattern[offset + 2] = (byte)((x + y) % 2 * 255); pattern[offset + 3] = 255; }
            var sample = BitmapSource.Create(96, 96, 96, 96, PixelFormats.Bgra32, null, pattern, 384); sample.Freeze();
            var mosaicCanvas = new InkCanvas { Width = 96, Height = 96, Background = new ImageBrush(sample) };
            var mosaic = new AnnotationTools(mosaicCanvas, sample);
            byte[] mosaicBefore = Pixels(Preview.RenderAnnotation(mosaicCanvas));
            mosaicCanvas.LayoutTransform = new ScaleTransform(2, 2);
            mosaic.AddMark(AnnotationTool.Mosaic, new Point(12, 12), new Point(72, 72));
            mosaicCanvas.LayoutTransform = Transform.Identity;
            byte[] mosaicAfter = Pixels(Preview.RenderAnnotation(mosaicCanvas));
            check(!mosaicAfter.SequenceEqual(mosaicBefore) && mosaicAfter.Take(96 * 12 * 4).SequenceEqual(mosaicBefore.Take(96 * 12 * 4)), "mosaic pixelates only its region even when preview was zoomed");
            mosaic.Undo(); check(Pixels(Preview.RenderAnnotation(mosaicCanvas)).SequenceEqual(mosaicBefore), "mosaic undo restores every original pixel");
            mosaic.Redo(); check(!Pixels(Preview.RenderAnnotation(mosaicCanvas)).SequenceEqual(mosaicBefore), "mosaic redo restores redaction");
            var positioned = new Canvas { Width = 320, Height = 240 };
            positioned.Children.Add(mosaicCanvas); Canvas.SetLeft(mosaicCanvas, 70); Canvas.SetTop(mosaicCanvas, 40);
            positioned.Measure(new Size(320, 240)); positioned.Arrange(new Rect(0, 0, 320, 240)); positioned.UpdateLayout();
            mosaic.AddMark(AnnotationTool.Mosaic, new Point(20, 20), new Point(60, 60));
            Vector layoutOffset = VisualTreeHelper.GetOffset(mosaicCanvas);
            check(layoutOffset.X == 70 && layoutOffset.Y == 40, "mosaic rendering restores the live selection offset in its parent canvas");
            Preview.RenderAnnotation(mosaicCanvas); layoutOffset = VisualTreeHelper.GetOffset(mosaicCanvas);
            check(layoutOffset.X == 70 && layoutOffset.Y == 40, "saving annotation leaves live canvas position unchanged");
            var previewPixels = new byte[320 * 240 * 4];
            for (int y = 0; y < 240; y++) for (int x = 0; x < 320; x++)
            {
                int offset = (y * 320 + x) * 4;
                previewPixels[offset] = (byte)x; previewPixels[offset + 1] = (byte)y;
                previewPixels[offset + 2] = 160; previewPixels[offset + 3] = 255;
            }
            var previewSource = BitmapSource.Create(320, 240, 192, 144, PixelFormats.Bgra32, null, previewPixels, 320 * 4);
            var previewScreen = new ScreenCapture { Image = previewSource, Bounds = new System.Drawing.Rectangle(-320, -240, 320, 240) };
            previewScreen.Windows.Add(new CaptureTarget(IntPtr.Zero, new Rect(90, 80, 120, 100)));
            var previewCapture = new CaptureWindow(previewScreen, delegate { return Task.FromResult(true); });
            try
            {
                previewCapture.Lens.Visibility = Visibility.Collapsed;
                Func<BitmapSource> renderPreview = delegate
                {
                    var surface = (FrameworkElement)previewCapture.Content;
                    surface.Measure(new Size(320, 240)); surface.Arrange(new Rect(0, 0, 320, 240)); surface.UpdateLayout();
                    var image = new RenderTargetBitmap(320, 240, 96, 96, PixelFormats.Pbgra32); image.Render(surface); return image;
                };
                previewCapture.UpdateHover(new Point(120, 110));
                var previewRender = renderPreview(); var shownPixels = Pixels(previewRender);
                int inside = (110 * 320 + 120) * 4, outside = (170 * 320 + 40) * 4;
                check(shownPixels[inside] == 120 && shownPixels[inside + 1] == 110 && shownPixels[inside + 2] == 160,
                    "revealed capture preview preserves exact source pixels with non-square bitmap DPI");
                check(shownPixels[outside] == 20 && shownPixels[outside + 1] == 83 && shownPixels[outside + 2] == 78 && shownPixels[outside + 3] == 255,
                    "cached capture backdrop stays opaque and preserves original dimming strength");
                File.WriteAllBytes(Path.Combine(artifacts, "capture-preview-mask.png"), Store.Encode(previewRender));
                previewScreen.Windows.Clear(); previewScreen.Windows.Add(new CaptureTarget(IntPtr.Zero, new Rect(160, 100, 100, 100)));
                previewCapture.UpdateHover(new Point(180, 120)); shownPixels = Pixels(renderPreview());
                check(shownPixels[inside] == 59 && shownPixels[inside + 1] == 54 && shownPixels[inside + 2] == 78,
                    "moving the revealed capture region restores the old area to its cached dimmed pixels");
            }
            finally { previewCapture.Close(); }
            var clipped = ScreenCapture.PixelRect(new Rect(-8.5, 20.2, 100, 1000), 640, 400);
            check(clipped.X == 0 && clipped.Y == 20 && clipped.Width == 92 && clipped.Height == 380, "physical-pixel crop rounds outward and clips desktop boundaries");
            check(ScreenCapture.PixelRect(Rect.Empty, 640, 400).Width == 0, "empty selection produces no crop");
            var screen = new ScreenCapture { Image = original, Bounds = new System.Drawing.Rectangle(-640, -100, 640, 400) };
            int completions = 0;
            var capture = new CaptureWindow(screen, delegate { completions++; return Task.FromResult(true); });
            try
            {
                capture.SetSelection(new Rect(15.5, 25.5, 300, 200));
                var image = capture.RenderSelection();
                var expected = new CroppedBitmap(original, new Int32Rect(15, 25, 301, 201));
                check(image.PixelWidth == 301 && image.PixelHeight == 201 && Pixels(image).SequenceEqual(Pixels(expected)), "capture crop preserves source pixels with negative desktop origin");
                capture.Annotations.AddMark(AnnotationTool.Arrow, new Point(20, 20), new Point(150, 100));
                var annotated = capture.RenderSelection();
                check(annotated.PixelWidth == 301 && !Pixels(annotated).SequenceEqual(Pixels(expected)), "capture exports annotations without dim mask border or toolbar");
                File.WriteAllBytes(Path.Combine(artifacts, "capture-annotation.png"), Store.Encode(annotated));
                check(completions == 0, "selection and annotation do not save or change clipboard before completion");
            }
            finally { capture.Close(); }
            VerifyCompactToolbar(original, artifacts, check);
            var settings = new Store(Path.Combine(root, "capture-settings"), false);
            settings.Settings.CaptureHotkey = "Ctrl+Alt+A"; settings.SaveSettings();
            check(new Store(settings.Root, false).Settings.CaptureHotkey == "Ctrl+Alt+A", "custom capture shortcut persists independently of collection settings");
            var probe = new Window { Width = 64, Height = 64, ShowActivated = false, ShowInTaskbar = false };
            new WindowInteropHelper(probe).EnsureHandle();
            try
            {
                int fired = 0;
                using (var hotkey = new CaptureHotkey(probe, delegate { fired++; }))
                {
                    // These uncommon combinations avoid reserving the user's F1 or app shortcuts in tests.
                    check(hotkey.Register("Ctrl+Alt+Shift+F23"), "native capture shortcut registers on an isolated window");
                    IntPtr handle = new WindowInteropHelper(probe).Handle;
                    Native.SendMessage(handle, 0x0312, new IntPtr(2), IntPtr.Zero);
                    check(fired == 1, "WM_HOTKEY routes to capture callback");
                    bool conflict = Native.RegisterHotKey(handle, 8, 0x4007, 0x87); // Ctrl+Alt+Shift+F24
                    try
                    {
                        check(conflict && !hotkey.Register("Ctrl+Alt+Shift+F24") && hotkey.Registered && hotkey.Gesture == "Ctrl+Alt+Shift+F23", "occupied replacement preserves original capture shortcut");
                    }
                    finally { if (conflict) Native.UnregisterHotKey(handle, 8); }
                    check(!hotkey.Register("A") && hotkey.Registered, "invalid bare-letter shortcut cannot replace working binding");
                    check(hotkey.Register("Ctrl+Alt+Shift+F24"), "shortcut can change after conflict is released");
                    Native.SendMessage(handle, 0x0312, new IntPtr(2), IntPtr.Zero);
                    check(fired == 1, "stale shortcut id is ignored after replacement");
                    Native.SendMessage(handle, 0x0312, new IntPtr(3), IntPtr.Zero);
                    check(fired == 2, "replacement shortcut routes new native id");
                }
                check(Native.RegisterHotKey(new WindowInteropHelper(probe).Handle, 9, 0x4007, 0x87), "disposing capture shortcut releases native registration");
                Native.UnregisterHotKey(new WindowInteropHelper(probe).Handle, 9);
            }
            finally { probe.Close(); }
        }
        private static void VerifyClipboardRetry(Action<bool, string> check)
        {
            const int cannotOpenClipboard = unchecked((int)0x800401D0);
            int attempts = 0;
            Task<bool> recovered = ClipboardImages.TryCopyAsync(delegate
            {
                if (++attempts < 3) throw new COMException("simulated busy clipboard", cannotOpenClipboard);
            });
            CollectionTests.Wait(recovered);
            check(recovered.Result && attempts == 3, "capture copy succeeds after the clipboard becomes available");

            attempts = 0;
            Task<bool> exhausted = ClipboardImages.TryCopyAsync(delegate
            {
                attempts++;
                throw new COMException("simulated busy clipboard", cannotOpenClipboard);
            });
            CollectionTests.Wait(exhausted);
            check(!exhausted.Result && attempts == 3, "capture copy reports failure after two clipboard retries");

            attempts = 0;
            Task<bool> failed = ClipboardImages.TryCopyAsync(delegate
            {
                attempts++;
                throw new UnauthorizedAccessException();
            });
            CollectionTests.Wait(failed);
            check(!failed.Result && attempts == 1, "capture copy reports clipboard access denial without retries");
        }
        private static void VerifyCompactToolbar(BitmapSource original, string artifacts, Action<bool, string> check)
        {
            foreach (int width in new[] { 1280, 320 })
            {
                var image = new TransformedBitmap(original, new ScaleTransform(width / (double)original.PixelWidth, 800.0 / original.PixelHeight)); image.Freeze();
                var screen = new ScreenCapture { Image = image, Bounds = new System.Drawing.Rectangle(0, 0, width, 800) };
                var capture = new CaptureWindow(screen, delegate { return Task.FromResult(true); }, delegate { }, delegate { });
                try
                {
                    capture.SetSelection(new Rect(width == 1280 ? 250 : 40, 180, width == 1280 ? 760 : 200, 200));
                    var surface = (Canvas)capture.Content;
                    surface.Measure(new Size(width, 800)); surface.Arrange(new Rect(0, 0, width, 800)); surface.UpdateLayout();
                    var toolbar = surface.Children.OfType<Border>().Single(border => border.Child is WrapPanel);
                    var buttons = Descendants<Button>(toolbar).ToArray();
                    string[] expected = { "hand", "pen", "rectangle", "ellipse", "line", "arrow", "text", "mosaic", "eraser", "undo", "redo", "color", "width", "picker", "reselect", "save", "pin", "close", "check" };
                    check(buttons.Length == expected.Length && expected.All(tag => buttons.Count(button => Equals(button.Tag, tag)) == 1)
                        && buttons.All(button => button.Content is System.Windows.Shapes.Path && !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button))),
                        "compact capture toolbar retains every action as an accessible vector button at " + width);
                    check(buttons.All(button =>
                    {
                        Rect bounds = button.TransformToAncestor(toolbar).TransformBounds(new Rect(button.RenderSize));
                        return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= toolbar.ActualWidth + .1 && bounds.Bottom <= toolbar.ActualHeight + .1 && bounds.Width >= 31 && bounds.Height >= 31;
                    }), "compact capture buttons stay inside the toolbar with 32px hit areas at " + width);
                    if (width == 1280) check(toolbar.ActualHeight <= 44 && toolbar.ActualWidth <= 660, "desktop capture toolbar fits every action into one compact row");
                    var render = new RenderTargetBitmap(width, 800, 96, 96, PixelFormats.Pbgra32); render.Render(surface);
                    var crop = new CroppedBitmap(render, new Int32Rect((int)Canvas.GetLeft(toolbar), (int)Canvas.GetTop(toolbar), (int)Math.Ceiling(toolbar.ActualWidth), (int)Math.Ceiling(toolbar.ActualHeight)));
                    File.WriteAllBytes(Path.Combine(artifacts, "capture-toolbar-" + width + ".png"), Store.Encode(crop));
                    if (width == 1280)
                    {
                        capture.Show(); capture.UpdateLayout();
                        var colorButton = buttons.Single(button => Equals(button.Tag, "color"));
                        colorButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); CollectionTests.Wait(Task.Delay(60));
                        var swatches = PresentationSource.CurrentSources.OfType<HwndSource>().Where(source => source.RootVisual != null)
                            .Select(source => Descendants<Button>(source.RootVisual).Where(button => Equals(button.Tag, "color")).ToArray())
                            .Single(group => group.Length == 6);
                        swatches.Single(button => Equals(button.ToolTip, "标注颜色：蓝色")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        var ink = Descendants<InkCanvas>(surface).Single();
                        check(ink.DefaultDrawingAttributes.Color == Colors.DodgerBlue, "compact color popup preserves all six swatches and applies the selected color");
                        var widths = buttons.Single(button => Equals(button.Tag, "width"));
                        widths.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); CollectionTests.Wait(Task.Delay(30));
                        var menu = PresentationSource.CurrentSources.OfType<HwndSource>().Where(source => source.RootVisual != null)
                            .SelectMany(source => Descendants<ContextMenu>(source.RootVisual)).Single(context => context.Items.OfType<MenuItem>().Count() == 4);
                        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Tag, 8.0)).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); menu.IsOpen = false;
                        check(ink.DefaultDrawingAttributes.Width == 8 && menu.Items.OfType<MenuItem>().Select(item => (double)item.Tag).SequenceEqual(new[] { 2.0, 3, 5, 8 }),
                            "compact stroke-width button preserves every existing width option");
                    }
                }
                finally { capture.Close(); }
            }
        }
        internal static void VerifyDesktopPixels()
        {
            var fixture = new Window { Title = "Snapline 截图像素验证", Width = 160, Height = 160, Left = 200, Top = 200,
                Background = new SolidColorBrush(Color.FromRgb(37, 128, 219)), WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                Topmost = true, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                fixture.Show(); fixture.UpdateLayout();
                CollectionTests.Wait(Task.Delay(250));
                Native.Rect bounds; Native.GetWindowRect(new WindowInteropHelper(fixture).Handle, out bounds);
                // Read only a small patch of this opaque synthetic test window.
                var image = ScreenCapture.CapturePixels(new System.Drawing.Rectangle(bounds.Left + 30, bounds.Top + 30, 16, 16));
                byte[] pixels = Pixels(image);
                if (image.PixelWidth != 16 || image.PixelHeight != 16 || pixels[0] != 219 || pixels[1] != 128 || pixels[2] != 37 || pixels[3] != 255)
                    throw new InvalidOperationException("原生桌面截图的像素、透明度或物理尺寸不正确。");
            }
            finally { fixture.Close(); }
        }
    }
}
