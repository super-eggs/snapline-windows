using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class SelfTests
    {
        private static readonly StringBuilder log = new StringBuilder();
        private static int count;
        private static byte[] PixelBytes(BitmapSource image)
        {
            var converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; converted.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes;
        }
        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException(name);
            count++; log.AppendLine("PASS " + name);
        }
        private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root is T) yield return (T)root;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
        }
        private static void VerifyLayout(FrameworkElement content, double width, double height, string name)
        {
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            foreach (var button in Descendants<Button>(content))
            {
                DependencyObject ancestor = button; bool visible = true;
                while (ancestor != null)
                {
                    var element = ancestor as UIElement;
                    if (element != null && element.Visibility != Visibility.Visible) { visible = false; break; }
                    ancestor = VisualTreeHelper.GetParent(ancestor);
                }
                if (!visible) continue;
                Rect bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > width + .5 || bounds.Bottom > height + .5 || bounds.Height < 31)
                    throw new InvalidOperationException(name + " button clipped or too small: " + button.Content + " " + bounds);
            }
            Check(true, name + " all buttons fit viewport with 32px hit areas");
        }
        private static void SaveLayout(FrameworkElement content, double width, double height, double scale, string path, bool desktop = false)
        {
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(content);
            if (desktop)
            {
                var visual = new DrawingVisual();
                using (var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Ui.Brush(222, 229, 227), null, new Rect(0, 0, width, height));
                    drawing.DrawImage(bitmap, new Rect(0, 0, width, height));
                }
                var composite = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                composite.Render(visual); bitmap = composite;
            }
            File.WriteAllBytes(path, Store.Encode(bitmap));
        }
        private static BitmapSource ShapeImage(int width, int height, string title)
        {
            var visual = new DrawingVisual();
            using (var drawing = visual.RenderOpen())
            {
                drawing.DrawRectangle(Ui.Brush(246, 248, 250), null, new Rect(0, 0, width, height));
                drawing.DrawRectangle(Ui.Brush(206, 224, 239), null, new Rect(0, 0, width, Math.Min(height, 80)));
                drawing.DrawText(new FormattedText(title, System.Globalization.CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), 28, Ui.LightText, 1), new Point(24, 20));
                for (int y = 112; y < height - 24; y += 48)
                    drawing.DrawRoundedRectangle(Ui.Brush(220, 228, 236), null, new Rect(24, y, width - 48, 14), 7, 7);
            }
            var result = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); result.Render(visual); result.Freeze(); return result;
        }
        internal static int Run(string report)
        {
            string root = Path.Combine(Path.GetTempPath(), "Snapline-tests-" + Guid.NewGuid().ToString("N"));
            Shelf shelf = null;
            try
            {
                Directory.CreateDirectory(root);
                byte[] invisiblePixels = new byte[] { 30, 90, 210, 0, 220, 150, 40, 0 };
                var invisible = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, invisiblePixels, 8);
                var recovered = ClipboardImages.Read(delegate(string format) { return null; }, delegate { return invisible; });
                byte[] restored = PixelBytes(recovered);
                Check(restored[3] == 255 && restored[7] == 255 && restored[0] == 30 && restored[6] == 40, "clipboard zero-alpha RGB becomes visible without losing colors");
                var premultiplied = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Pbgra32, null, invisiblePixels, 8);
                restored = PixelBytes(ClipboardImages.NormalizeBitmap(premultiplied));
                Check(restored[3] == 255 && restored[0] == 30, "recover raw RGB before premultiplied-alpha conversion");
                var partial = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 0, 80, 100, 120, 128 }, 8);
                Check(ReferenceEquals(partial, ClipboardImages.NormalizeBitmap(partial)), "valid partial bitmap transparency stays unchanged");
                var emptyTransparent = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
                Check(ReferenceEquals(emptyTransparent, ClipboardImages.NormalizeBitmap(emptyTransparent)), "empty transparent bitmap is not replaced with black");
                using (var png = new MemoryStream(Store.Encode(invisible)))
                {
                    png.Position = 2;
                    var pngImage = ClipboardImages.Read(delegate(string format) { return format == "PNG" ? png : null; }, delegate { throw new InvalidOperationException("PNG should take priority"); });
                    Check(PixelBytes(pngImage)[3] == 0 && png.Position == 2, "explicit transparent PNG preserves alpha and stream position");
                }
                var pngPartial = ClipboardImages.Read(delegate(string format) { return format == "image/png" ? Store.Encode(partial) : null; }, delegate { throw new InvalidOperationException("PNG-only image must be collected"); });
                Check(PixelBytes(pngPartial)[7] == 128, "PNG-only clipboard supports genuine partial transparency");
                var malformed = ClipboardImages.Read(delegate(string format) { return format == "PNG" ? new byte[] { 1, 2, 3 } : null; }, delegate { return invisible; });
                Check(PixelBytes(malformed)[3] == 255, "invalid PNG falls back to recoverable bitmap");
                Check(ClipboardImages.Read(delegate(string format) { return null; }, delegate { return null; }) == null, "non-image clipboard produces no capture");
                var clipboardStore = new Store(Path.Combine(root, "clipboard-data")); clipboardStore.Add(recovered);
                var clipboardReloaded = new Store(clipboardStore.Root);
                Check(PixelBytes(Store.ReadImage(clipboardReloaded.Items[0].Path))[3] == 255, "repaired clipboard capture stays visible after PNG persistence and restart");
                var imageView = new Image { Source = clipboardReloaded.Items[0].Image, Width = 100, Height = 50, Stretch = Stretch.Fill };
                imageView.Measure(new Size(100, 50)); imageView.Arrange(new Rect(0, 0, 100, 50));
                var visibleRender = new RenderTargetBitmap(100, 50, 96, 96, PixelFormats.Pbgra32); visibleRender.Render(imageView);
                byte[] viewBytes = PixelBytes(visibleRender);
                Check(viewBytes[(25 * 100 + 25) * 4 + 3] == 255, "repaired screenshot thumbnail renders visibly in WPF");
                var oldStore = new Store(Path.Combine(root, "legacy-invisible")); oldStore.Add(invisible);
                var oldImage = Store.ReadImage(oldStore.Items[0].Path);
                Check(PixelBytes(oldImage)[3] == 0, "reproduce legacy invisible capture after saving bitmap with zero alpha");
                oldStore.Replace(oldStore.Items[0], ClipboardImages.NormalizeBitmap(oldImage));
                Check(PixelBytes(Store.ReadImage(oldStore.Items[0].Path))[3] == 255, "explicit repair recovers hidden RGB from an already saved app copy");
                var store = new Store(Path.Combine(root, "data"));
                var first = App.DemoImage(0);
                Check(store.Add(first), "add screenshot");
                Check(!store.Add(first) && store.Items.Count == 1, "deduplicate identical screenshot");
                var persisted = new Store(store.Root);
                Check(persisted.Items.Count == 1 && !persisted.Add(first), "restart restores screenshot and fingerprint");
                string original = Path.Combine(root, "source.png"); File.WriteAllBytes(original, Store.Encode(App.DemoImage(1)));
                byte[] sourceBytes = File.ReadAllBytes(original);
                Check(store.Import(original), "import source image");
                var shot = store.Items[0];
                var data = Shelf.FileData(shot);
                Check(((string[])data.GetData(DataFormats.FileDrop))[0] == shot.Path && data.GetDataPresent(DataFormats.Bitmap), "drag payload includes copy path and image");
                store.Replace(shot, App.DemoImage(2));
                Check(Store.Hash(File.ReadAllBytes(original)) == Store.Hash(sourceBytes), "annotation preserves original source");
                var reloaded = new Store(store.Root);
                Check(reloaded.Items[0].Fingerprint == shot.Fingerprint, "annotation persists across restart");
                store.Remove(shot);
                Check(!File.Exists(shot.Path) && File.Exists(original), "remove deletes only app copy");
                var outsider = new Shot { Path = original }; store.Items.Add(outsider); store.Remove(outsider); store.Items.Remove(outsider);
                Check(File.Exists(original), "reject deletion outside inbox");
                store.Settings.ClipboardEnabled = false; store.Settings.WatchFolder = root; store.SaveSettings();
                reloaded = new Store(store.Root);
                Check(!reloaded.Settings.ClipboardEnabled && reloaded.Settings.WatchFolder == root, "settings round trip");
                Check(reloaded.Settings.Layer == WindowLayer.Top, "new layer setting defaults to existing always-on-top behavior");
                foreach (WindowLayer layer in Enum.GetValues(typeof(WindowLayer)))
                {
                    store.SetLayer(layer);
                    Check(new Store(store.Root).Settings.Layer == layer, "window layer persists across restart: " + layer);
                }
                store.SetLayer(WindowLayer.Top);
                var legacyRoot = Path.Combine(root, "legacy-settings"); Directory.CreateDirectory(legacyRoot);
                File.WriteAllText(Path.Combine(legacyRoot, "settings.json"), "{\"ClipboardEnabled\":false,\"WatchEnabled\":false,\"WatchFolder\":\"\"}");
                var legacySettings = new Store(legacyRoot).Settings;
                Check(legacySettings.Layer == WindowLayer.Top && !legacySettings.ClipboardEnabled, "legacy settings preserve collection preferences and default layer to top");
                string folder = Path.Combine(root, "watch"); Directory.CreateDirectory(folder);
                File.Copy(original, Path.Combine(folder, "existing.png"));
                int collected = 0, errors = 0;
                using (var collector = new FolderCollector(folder, delegate(BitmapSource image) { collected++; }, delegate(string message) { errors++; }))
                {
                    collector.Scan(); collector.Scan(); Check(collected == 0, "watch skips existing library");
                    string next = Path.Combine(folder, "new.png"); File.Copy(original, next);
                    collector.Scan(); Check(collected == 0, "watch waits for stable file");
                    collector.Scan(); collector.Scan(); Check(collected == 1, "watch collects new image once");
                    File.WriteAllText(Path.Combine(folder, "broken.png"), "invalid");
                    for (int i = 0; i < 14; i++) collector.Scan();
                    Check(errors == 1 && collected == 1, "corrupt file has bounded retries");
                    File.Delete(next); collector.Scan(); File.Copy(original, next); collector.Scan(); collector.Scan();
                    Check(collected == 2, "watch supports deleted then recreated file");
                }
                string absent = Path.Combine(root, "created-later"); int firstCapture = 0;
                using (var collector = new FolderCollector(absent, delegate(BitmapSource image) { firstCapture++; }, delegate(string message) { }))
                {
                    collector.Scan(); Directory.CreateDirectory(absent); File.Copy(original, Path.Combine(absent, "first.png"));
                    collector.Scan(); collector.Scan();
                    Check(firstCapture == 1, "initially absent screenshot directory collects first capture");
                }
                var ink = new InkCanvas { Width = first.PixelWidth, Height = first.PixelHeight, Background = new ImageBrush(first) };
                var points = new StylusPointCollection(); points.Add(new StylusPoint(20, 20)); points.Add(new StylusPoint(180, 100));
                ink.Strokes.Add(new Stroke(points) { DrawingAttributes = new DrawingAttributes { Color = Colors.Red, Width = 8, Height = 8 } });
                var edited = Preview.RenderAnnotation(ink);
                Check(edited.PixelWidth == first.PixelWidth && edited.PixelHeight == first.PixelHeight && Store.Hash(Store.Encode(edited)) != Store.Hash(Store.Encode(first)), "annotation render preserves dimensions and adds ink");
                store.Add(App.DemoImage(1)); store.Add(App.DemoImage(2));
                shelf = new Shelf(store, true);
                DateTime opened = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
                shelf.BeginReveal(true, opened);
                Check(!shelf.AutoHideDue(false, opened.AddSeconds(1)) && !shelf.AutoHideDue(false, opened.AddMinutes(10)), "manual reveal stays visible with pointer outside");
                shelf.BeginReveal(false, opened);
                Check(!shelf.AutoHideDue(false, opened.AddMilliseconds(500)) && !shelf.AutoHideDue(false, opened.AddMilliseconds(1900)), "hover reveal has opening grace period");
                Check(!shelf.AutoHideDue(false, opened.AddSeconds(2)) && !shelf.AutoHideDue(false, opened.AddMilliseconds(2900)) && shelf.AutoHideDue(false, opened.AddMilliseconds(3100)), "hover reveal hides only after continuous pointer absence");
                shelf.BeginReveal(false, opened);
                shelf.AutoHideDue(false, opened.AddSeconds(2));
                Check(!shelf.AutoHideDue(true, opened.AddMilliseconds(2900)) && !shelf.AutoHideDue(false, opened.AddSeconds(3)) && !shelf.AutoHideDue(false, opened.AddMilliseconds(3900)), "pointer reentry cancels pending hide");
                shelf.BeginReveal(false, opened); shelf.Busy = true;
                Check(!shelf.AutoHideDue(false, opened.AddMinutes(1)), "menu dialog and drag suppress automatic hiding");
                shelf.Busy = false;
                DateTime resumed = DateTime.UtcNow;
                Check(!shelf.AutoHideDue(false, resumed.AddMilliseconds(500)), "closing menu or dialog starts new grace period");
                var pin = shelf.PinButton;
                shelf.BeginReveal(false, opened); pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!shelf.AutoHideDue(false, opened.AddMinutes(1)), "pin click prevents automatic hiding");
                pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!shelf.AutoHideDue(false, opened.AddMinutes(1)) && shelf.AutoHideDue(false, opened.AddMinutes(1).AddMilliseconds(1100)), "unpin starts a fresh leave interval");
                shelf.BeginReveal(true, opened);
                shelf.BeginReveal(false, opened.AddSeconds(5));
                Check(!shelf.AutoHideDue(false, opened.AddSeconds(7)) && shelf.AutoHideDue(false, opened.AddMilliseconds(8100)), "new hover reveal resets prior manual mode");
                var content = (FrameworkElement)shelf.Content;
                content.Measure(new Size(shelf.Width, shelf.Height)); content.Arrange(new Rect(0, 0, shelf.Width, shelf.Height)); content.UpdateLayout();
                var render = new RenderTargetBitmap((int)shelf.Width, (int)shelf.Height, 96, 96, PixelFormats.Pbgra32); render.Render(content);
                byte[] renderedPixels = new byte[(int)shelf.Width * (int)shelf.Height * 4]; render.CopyPixels(renderedPixels, (int)shelf.Width * 4, 0);
                Check(Enumerable.Range(0, (int)shelf.Width * (int)shelf.Height).Count(i => renderedPixels[i * 4 + 3] > 0) > 40000, "shelf render contains visible controls and cards");
                Check(renderedPixels[(200 * (int)shelf.Width + 100) * 4 + 3] == 0, "desktop remains transparent in open space between controls and cards");
                string preview = Path.Combine(Path.GetDirectoryName(report), "shelf-preview.png"); File.WriteAllBytes(preview, Store.Encode(render));
                // A sample desktop-colored background is only for the preview export.
                // The actual window and the main PNG keep their transparent pixels.
                var desktopPreview = new DrawingVisual();
                using (var drawing = desktopPreview.RenderOpen())
                {
                    drawing.DrawRectangle(Ui.Brush(222, 229, 227), null, new Rect(0, 0, shelf.Width, shelf.Height));
                    drawing.DrawImage(render, new Rect(0, 0, shelf.Width, shelf.Height));
                }
                var desktopRender = new RenderTargetBitmap((int)shelf.Width, (int)shelf.Height, 96, 96, PixelFormats.Pbgra32);
                desktopRender.Render(desktopPreview);
                File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(report), "shelf-on-sample-desktop.png"), Store.Encode(desktopRender));
                Check(File.Exists(preview), "WPF shelf renders to preview artifact");
                string artifacts = Path.GetDirectoryName(report);
                foreach (int width in new[] { 2560, 1920, 1100, 720, 480, 320 })
                {
                    shelf.ApplyLayout(width);
                    VerifyLayout(content, shelf.Width, shelf.Height, "shelf " + width);
                    SaveLayout(content, shelf.Width, shelf.Height, 1, Path.Combine(artifacts, "shelf-" + width + ".png"));
                }
                shelf.ApplyLayout(480);
                VerifyLayout(content, shelf.Width, shelf.Height, "narrow continuous scrolling");
                Check(!Descendants<Button>(content).Any(button => Equals(button.Content, "上一页") || Equals(button.Content, "下一页")), "pagination controls removed");
                Check(Descendants<Image>(content).Count() == store.Items.Count && shelf.MaxScroll > 0, "all captures remain mounted in one continuous row");
                shelf.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                Check(shelf.ScrollOffset == 0 && shelf.ScrollTarget > 0, "vertical wheel starts a smooth horizontal target without jumping");
                shelf.StepScroll(16);
                Check(shelf.ScrollOffset > 0 && shelf.ScrollOffset < shelf.ScrollTarget, "scroll animation has intermediate positions");
                for (int i = 0; i < 30; i++) shelf.StepScroll(16);
                Check(Math.Abs(shelf.ScrollOffset - shelf.ScrollTarget) < .3, "scroll animation settles on target");
                var canvas = Descendants<Canvas>(content).Single();
                var anchorView = canvas.Children.OfType<Border>().First(view => Canvas.GetLeft(view) + view.Width > 16);
                var anchorSource = Descendants<Image>(anchorView).Single().Source;
                double anchorBefore = Canvas.GetLeft(anchorView);
                store.Add(ShapeImage(440, 280, "新截图"));
                VerifyLayout(content, shelf.Width, shelf.Height, "capture inserted while browsing");
                var anchorAfterView = canvas.Children.OfType<Border>().Single(view => ReferenceEquals(Descendants<Image>(view).Single().Source, anchorSource));
                Check(Math.Abs(Canvas.GetLeft(anchorAfterView) - anchorBefore) < .01, "incoming capture preserves the same visible image and screen position");
                shelf.ScrollBy(100000);
                for (int i = 0; i < 50; i++) shelf.StepScroll(16);
                Check(Math.Abs(shelf.ScrollOffset - shelf.MaxScroll) < .3 && !shelf.ScrollRightButton.IsEnabled, "right edge clamps without overscroll");
                foreach (var view in canvas.Children.OfType<Border>())
                {
                    double x = Canvas.GetLeft(view);
                    if (x + view.Width < 0 || x > shelf.Width) continue;
                    Check(Math.Abs(Canvas.GetTop(view) - Clothesline.DropAt((x + view.Width / 2) / shelf.Width, shelf.Width)) < .01, "visible clip follows fixed rope after scrolling");
                }
                shelf.ScrollLeftButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(shelf.ScrollTarget < shelf.ScrollOffset, "edge arrow moves continuously toward left");
                shelf.ScrollBy(-100000);
                for (int i = 0; i < 50; i++) shelf.StepScroll(16);
                Check(shelf.ScrollOffset == 0 && !shelf.ScrollLeftButton.IsEnabled, "left edge clamps without overscroll");
                store.Add(ShapeImage(450, 290, "最新截图"));
                Check(shelf.ScrollOffset == 0, "incoming capture stays at newest end when not browsing old images");
                shelf.ApplyLayout(1100);
                foreach (double scale in new[] { 1.5, 2.0 })
                {
                    SaveLayout(content, shelf.Width, shelf.Height, scale, Path.Combine(artifacts, "shelf-dpi-" + (int)(96 * scale) + ".png"));
                    Check(true, "shelf DPI " + (int)(96 * scale) + " render exports successfully");
                }
                var emptyShelf = new Shelf(new Store(Path.Combine(root, "empty")), true);
                try
                {
                    emptyShelf.ApplyLayout(480); var emptyContent = (FrameworkElement)emptyShelf.Content;
                    VerifyLayout(emptyContent, emptyShelf.Width, emptyShelf.Height, "empty shelf");
                    Check(Descendants<Button>(emptyContent).Any(button => Equals(button.Content, "导入图片")), "empty shelf offers import action");
                    SaveLayout(emptyContent, emptyShelf.Width, emptyShelf.Height, 1, Path.Combine(artifacts, "shelf-empty.png"));
                }
                finally { emptyShelf.Dispose(); emptyShelf.Close(); }
                var shapeStore = new Store(Path.Combine(root, "shapes"));
                shapeStore.Add(ShapeImage(1000, 640, "横向截图"));
                shapeStore.Add(ShapeImage(1800, 120, "细长截图"));
                shapeStore.Add(ShapeImage(360, 780, "竖屏截图"));
                shapeStore.Add(ShapeImage(500, 500, "方形截图"));
                var shapeShelf = new Shelf(shapeStore, true);
                try
                {
                    shapeShelf.ApplyLayout(2560); var shapeContent = (FrameworkElement)shapeShelf.Content;
                    VerifyLayout(shapeContent, shapeShelf.Width, shapeShelf.Height, "full screen mixed screenshot shapes");
                    var rope = Descendants<Clothesline>(shapeContent).Single();
                    Check(shapeShelf.Width == 2560 && Math.Abs(rope.ActualWidth - 2560) < 1 && Math.Abs(rope.TransformToAncestor(shapeContent).Transform(new Point()).X) < 1, "rope spans complete screen width without old maximum or side padding");
                    Check(!Descendants<TextBlock>(shapeContent).Any(text => text.Visibility == Visibility.Visible && (text.Text.Contains("最近") || text.Text.Contains("单击复制") || text.Text == "截图晾衣绳")), "persistent explanatory labels removed");
                    Check(shapeShelf.ScrollLeftButton.Visibility == Visibility.Collapsed && shapeShelf.ScrollRightButton.Visibility == Visibility.Collapsed, "edge arrows stay hidden when captures fit");
                    foreach (Shot shape in shapeStore.Items)
                    {
                        Size size = shapeShelf.ImageSize(shape);
                        Check(size.Width <= 180.01 && size.Height <= 120.01, "compact thumbnail bounds " + shape.Image.PixelWidth + "x" + shape.Image.PixelHeight);
                        Check(Math.Abs(size.Width / size.Height - (double)shape.Image.PixelWidth / shape.Image.PixelHeight) < .001, "thumbnail preserves aspect ratio " + shape.Image.PixelWidth + "x" + shape.Image.PixelHeight);
                    }
                    SaveLayout(shapeContent, shapeShelf.Width, shapeShelf.Height, 1, Path.Combine(artifacts, "shelf-shapes-on-sample-desktop.png"), true);
                    shapeShelf.ApplyLayout(480);
                    VerifyLayout(shapeContent, shapeShelf.Width, shapeShelf.Height, "narrow mixed screenshot shapes");
                    for (int i = 0; i < 12; i++) shapeStore.Add(ShapeImage(640, 400, "截图 " + (i + 1)));
                    shapeShelf.ApplyLayout(1100);
                    VerifyLayout(shapeContent, shapeShelf.Width, shapeShelf.Height, "dense continuous screenshot row");
                    SaveLayout(shapeContent, shapeShelf.Width, shapeShelf.Height, 1, Path.Combine(artifacts, "shelf-scroll-preview.png"), true);
                    shapeShelf.ScrollBy(440);
                    for (int i = 0; i < 40; i++) shapeShelf.StepScroll(16);
                    SaveLayout(shapeContent, shapeShelf.Width, shapeShelf.Height, 1, Path.Combine(artifacts, "shelf-scrolled-preview.png"), true);
                    Check(((LinearGradientBrush)Descendants<Canvas>(shapeContent).Single().OpacityMask).GradientStops[0].Color.A == 0, "overflow edge fades indicate hidden captures");
                    shapeShelf.ApplyLayout(2560);
                    Check(shapeShelf.ScrollOffset <= shapeShelf.MaxScroll && shapeShelf.ScrollTarget <= shapeShelf.MaxScroll, "resizing clamps scroll range safely");
                }
                finally { shapeShelf.Dispose(); shapeShelf.Close(); }
                var previewStore = new Store(Path.Combine(root, "preview"));
                previewStore.Add(ShapeImage(1000, 640, "图片预览"));
                var editor = new Preview(previewStore, previewStore.Items[0], true);
                try
                {
                    var editorContent = (FrameworkElement)editor.Content;
                    // Content has a 20px outer margin, as in the actual Window layout.
                    VerifyLayout(editorContent, 420, 600, "narrow annotation editor");
                    SaveLayout(editorContent, 420, 600, 1, Path.Combine(artifacts, "editor-narrow.png"));
                    VerifyLayout(editorContent, 1100, 760, "wide preview viewport"); editor.FitImage(); editorContent.UpdateLayout();
                    Check(Descendants<Button>(editorContent).Where(button => !Equals(button.Tag, "")).All(button => button.Content is System.Windows.Shapes.Path), "preview actions use vector icons instead of text labels");
                    Check(Descendants<Button>(editorContent).All(button => !string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(button))), "icon controls retain accessible operation names");
                    var zoomControl = Descendants<Slider>(editorContent).Single();
                    zoomControl.Value = Math.Log(.5);
                    Check(Math.Abs(editor.Zoom - .5) < .0001, "bottom zoom slider adjusts image scale");
                    Check(Descendants<Button>(editorContent).Any(button => Equals(button.Content, "50%")), "zoom percentage reflects slider change");
                    editor.FitImage();
                    Check(editor.Zoom > 0 && editor.Zoom <= 1, "preview fits image without enlarging small originals");
                    var editCanvas = Descendants<InkCanvas>(editorContent).Single();
                    editCanvas.Strokes.Add(new Stroke(new StylusPointCollection(new[] { new StylusPoint(5, 5), new StylusPoint(20, 20) })));
                    var unrotated = editor.RenderEdited();
                    Descendants<Button>(editorContent).Single(button => Equals(button.Tag, "plus")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(editor.Zoom > 0, "preview zoom button routed event works");
                    editor.ChangeZoom(3);
                    var viewer = Descendants<ScrollViewer>(editorContent).Single(); editorContent.UpdateLayout();
                    Check(viewer.ExtentWidth > viewer.ViewportWidth || viewer.ExtentHeight > viewer.ViewportHeight, "magnified preview offers scrollable image extent");
                    var zoomOnly = editor.RenderEdited();
                    Check(zoomOnly.PixelWidth == unrotated.PixelWidth && zoomOnly.PixelHeight == unrotated.PixelHeight && Store.Encode(zoomOnly).SequenceEqual(Store.Encode(unrotated)), "zoom does not change saved dimensions or annotation pixels");
                    editor.Rotate(90);
                    var rotated = editor.RenderEdited();
                    var expectedRotation = new TransformedBitmap(unrotated, new RotateTransform(90));
                    Check(rotated.PixelWidth == unrotated.PixelHeight && rotated.PixelHeight == unrotated.PixelWidth, "quarter turn swaps saved image dimensions");
                    Check(PixelBytes(rotated).SequenceEqual(PixelBytes(expectedRotation)), "rotation saves image and annotations together at original resolution");
                    editor.Rotate(-90);
                    Check(editor.Rotation == 0 && Store.Encode(editor.RenderEdited()).SequenceEqual(Store.Encode(unrotated)), "reverse rotation restores orientation and pixels");
                    editor.Rotate(-90); Check(editor.Rotation == 270, "left rotation normalizes to 270 degrees");
                    editor.Rotate(90); editor.ChangeZoom(100); Check(editor.Zoom == 8, "zoom upper limit is 800 percent");
                    editor.ChangeZoom(.001); Check(editor.Zoom == .05, "zoom lower limit is five percent for normal images");
                    editor.FitImage(); editor.UpdateLayout();
                    SaveLayout(editorContent, 1100, 760, 1, Path.Combine(artifacts, "preview-editor.png"));
                    VerifyLayout(editorContent, 1100, 760, "wide preview editor");
                    editor.Width = 420; editor.Height = 600; editor.UpdateLayout(); editor.FitImage();
                    VerifyLayout(editorContent, 420, 560, "resized preview editor");
                    Check(zoomControl.Visibility == Visibility.Collapsed, "narrow preview hides slider while keeping zoom controls");
                    Descendants<Button>(editorContent).Single(button => Equals(button.Tag, "edit")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(editCanvas.EditingMode == InkCanvasEditingMode.None, "collapsing annotation tools restores drag browsing mode");
                    previewStore.SetLayer(WindowLayer.Bottom);
                    Check(!editor.Topmost && Descendants<Button>(editorContent).Any(button => Equals(button.ToolTip, "窗口层级：始终置底")), "preview responds immediately to shared bottom layer setting");
                    previewStore.SetLayer(WindowLayer.Top); Check(editor.Topmost, "preview restores always-on-top dynamically");
                    VerifyLayout(editorContent, 1100, 760, "minimal image preview"); editor.FitImage(); editorContent.UpdateLayout();
                    SaveLayout(editorContent, 1100, 760, 1, Path.Combine(artifacts, "preview-viewer.png"));
                }
                finally { editor.Close(); }
                var bounded = new Store(Path.Combine(root, "bounded"));
                for (int i = 0; i < Store.Limit + 1; i++)
                {
                    byte[] pixels = new byte[] { (byte)i, 24, 60, 255 };
                    bounded.Add(BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4));
                }
                Check(bounded.Items.Count == Store.Limit && Directory.GetFiles(bounded.Inbox, "*.png").Length == Store.Limit + 1, "bounded shelf retains older files");
                log.AppendLine("RESULT: " + count + " checks passed");
                return 0;
            }
            catch (Exception ex) { log.AppendLine("FAIL " + ex); return 1; }
            finally
            {
                if (shelf != null) { shelf.Dispose(); shelf.Close(); }
                Directory.CreateDirectory(Path.GetDirectoryName(report)); File.WriteAllText(report, log.ToString(), Encoding.UTF8);
                // This unique directory was created by this test only.
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
