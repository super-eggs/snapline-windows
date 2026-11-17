using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    // Samples source pixels, independently of display scaling, dim masks and annotations.
    internal sealed class PixelLens : FrameworkElement
    {
        private BitmapSource source;
        private BitmapSource pixels;
        private Int32Rect region;
        private readonly byte[] pixel = new byte[4];
        private bool sampled;
        internal int PixelX { get; private set; }
        internal int PixelY { get; private set; }
        internal Color Color { get; private set; }
        internal bool RgbFormat { get; private set; }
        internal string ColorText { get { return Format(Color, RgbFormat); } }
        internal PixelLens() { Width = 186; Height = 224; IsHitTestVisible = false; RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor); }
        internal void SetImage(BitmapSource image)
        {
            source = image;
            pixels = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            sampled = false;
            Sample(new Point());
        }
        internal void Sample(Point point)
        {
            int px = Math.Max(0, Math.Min(source.PixelWidth - 1, (int)Math.Floor(point.X)));
            int py = Math.Max(0, Math.Min(source.PixelHeight - 1, (int)Math.Floor(point.Y)));
            if (sampled && px == PixelX && py == PixelY) return;
            PixelX = px; PixelY = py; sampled = true;
            pixels.CopyPixels(new Int32Rect(PixelX, PixelY, 1, 1), pixel, 4, 0);
            Color = Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
            int left = Math.Max(0, PixelX - 7), top = Math.Max(0, PixelY - 7);
            region = new Int32Rect(left, top, Math.Min(source.PixelWidth - left, 15), Math.Min(source.PixelHeight - top, 15));
            InvalidateVisual();
        }
        internal void ToggleFormat() { RgbFormat = !RgbFormat; InvalidateVisual(); }
        internal void Clear() { source = pixels = null; sampled = false; InvalidateVisual(); }
        internal static string Format(Color color, bool rgb)
        {
            return rgb ? string.Format(CultureInfo.InvariantCulture, "RGB({0}, {1}, {2})", color.R, color.G, color.B)
                : string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
        internal static bool TryParseColor(string text, out Color color)
        {
            color = Colors.Transparent;
            if (text == null || text.Length > 80) return false;
            string value = text.Trim();
            if (Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"))
            {
                if (value.Length == 4) value = "#" + value[1] + value[1] + value[2] + value[2] + value[3] + value[3];
                int hex = int.Parse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                color = Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex); return true;
            }
            var match = Regex.Match(value, @"^(?:rgb\(\s*)?(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*\))?$", RegexOptions.IgnoreCase);
            if (!match.Success || value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) != value.EndsWith(")", StringComparison.Ordinal)) return false;
            int r = int.Parse(match.Groups[1].Value), g = int.Parse(match.Groups[2].Value), b = int.Parse(match.Groups[3].Value);
            if (r > 255 || g > 255 || b > 255) return false;
            color = Color.FromRgb((byte)r, (byte)g, (byte)b); return true;
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (source == null) return;
            dc.DrawRoundedRectangle(Ui.Surface, new Pen(Ui.Edge, 1), new Rect(.5, .5, Width - 1, Height - 1), 8, 8);
            var crop = new CroppedBitmap(source, region);
            // Keep the sampled pixel at the crosshair even at desktop edges.
            double x = 10 + (7 - PixelX + region.X) * 11, y = 8 + (7 - PixelY + region.Y) * 11;
            dc.PushClip(new RectangleGeometry(new Rect(10, 8, 165, 165)));
            dc.DrawRectangle(Brushes.White, null, new Rect(10, 8, 165, 165));
            dc.DrawImage(crop, new Rect(x, y, region.Width * 11, region.Height * 11));
            var grid = new Pen(new SolidColorBrush(Color.FromArgb(65, 100, 100, 100)), .5);
            for (int i = 0; i <= 15; i++)
            {
                dc.DrawLine(grid, new Point(10 + i * 11, 8), new Point(10 + i * 11, 173));
                dc.DrawLine(grid, new Point(10, 8 + i * 11), new Point(175, 8 + i * 11));
            }
            dc.DrawRectangle(null, new Pen(Brushes.Black, 3), new Rect(87, 85, 11, 11));
            dc.DrawRectangle(null, new Pen(Brushes.White, 1), new Rect(87, 85, 11, 11));
            dc.Pop();
            dc.DrawRectangle(new SolidColorBrush(Color), new Pen(Ui.Muted, 1), new Rect(11, 182, 17, 17));
            DrawText(dc, ColorText, new Point(36, 182), 12, Ui.Text);
            DrawText(dc, string.Format("{0}, {1}  ·  C 复制 / Shift 切换", PixelX, PixelY), new Point(11, 205), 10, Ui.Muted);
        }
        private static void DrawText(DrawingContext dc, string value, Point point, double size, Brush brush)
        {
            dc.DrawText(new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight, new Typeface(Ui.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), size, brush, 1), point);
        }
    }
}
