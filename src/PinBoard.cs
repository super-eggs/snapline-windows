using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal sealed class PinBoard : IDisposable
    {
        private readonly List<PinnedImageWindow> images = new List<PinnedImageWindow>();
        private readonly Action<BitmapSource> copyImage;
        private readonly Action<string> copyColor;
        private PinnedImageWindow lastClosed;
        private readonly bool exposeWindows;
        internal bool HasImages { get { return images.Count > 0; } }
        internal IEnumerable<PinnedImageWindow> Images { get { return images; } }
        internal PinBoard(Action<BitmapSource> copyImage, Action<string> copyColor, bool exposeWindows = false) { this.copyImage = copyImage; this.copyColor = copyColor; this.exposeWindows = exposeWindows; }
        internal PinnedImageWindow Show(BitmapSource image, uint? sequence = null)
        {
            if (sequence.HasValue)
            {
                var existing = images.FirstOrDefault(x => x.ClipboardSequence == sequence);
                if (existing != null) { existing.Show(); existing.Activate(); return existing; }
                if (lastClosed != null && lastClosed.ClipboardSequence == sequence) return RestoreLast();
            }
            if (images.Count >= 32) throw new NotSupportedException("最多同时保留 32 张贴图，请先关闭不需要的贴图。");
            var window = new PinnedImageWindow(image, copyImage, copyColor) { ClipboardSequence = sequence, ShowInTaskbar = exposeWindows };
            window.RequestClose += Close;
            images.Add(window); window.Show(); window.Activate(); return window;
        }
        internal PinnedImageWindow RestoreLast()
        {
            if (lastClosed == null) return null;
            if (images.Count >= 32) throw new NotSupportedException("请先关闭一张贴图，再恢复上一张。");
            var window = lastClosed; lastClosed = null; images.Add(window); window.Show(); window.Activate(); return window;
        }
        internal void Close(PinnedImageWindow window, bool destroy)
        {
            images.Remove(window);
            if (destroy) { if (lastClosed == window) lastClosed = null; window.Destroy(); return; }
            if (lastClosed != null && lastClosed != window) lastClosed.Destroy();
            window.HideTools(); window.Hide(); lastClosed = window;
        }
        internal void ToggleAll()
        {
            bool hide = images.Any(x => x.IsVisible);
            foreach (var window in images)
            {
                if (hide) { window.HideTools(); window.Hide(); }
                else window.Show();
            }
        }
        internal static BitmapSource FromText(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Length > 16000) throw new NotSupportedException("剪贴板文字超过 16000 字符，请缩短后再贴图。");
            Color color; bool swatch = PixelLens.TryParseColor(value, out color);
            string text = swatch ? PixelLens.Format(color, false) + "\n" + PixelLens.Format(color, true) : value;
            Brush foreground = swatch ? color.R * .299 + color.G * .587 + color.B * .114 > 145 ? Brushes.Black : Brushes.White : Ui.Text;
            var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(Ui.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), swatch ? 18 : 16, foreground, 1) { MaxTextWidth = swatch ? 280 : 600 };
            int width = swatch ? 320 : Math.Max(120, (int)Math.Ceiling(formatted.Width) + 40), height = swatch ? 180 : Math.Max(80, (int)Math.Ceiling(formatted.Height) + 40);
            if (width > 16384 || height > 16384 || (long)width * height > 40000000) throw new NotSupportedException("文字贴图过大或过长，请缩短后重试。");
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(swatch ? new SolidColorBrush(color) : Ui.Surface, null, new Rect(0, 0, width, height));
                dc.DrawText(formatted, new Point(20, swatch ? 60 : 20));
            }
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
        }
        public void Dispose()
        {
            foreach (var window in images.ToArray()) window.Destroy(); images.Clear();
            if (lastClosed != null) { lastClosed.Destroy(); lastClosed = null; }
        }
    }
}
