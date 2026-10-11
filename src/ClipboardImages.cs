using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class ClipboardImages
    {
        internal static BitmapSource Read(Func<string, object> pngData, Func<BitmapSource> bitmapData)
        {
            // PNG carries explicit alpha; prefer it over ambiguous clipboard bitmaps.
            foreach (string format in new[] { "PNG", "image/png" })
            {
                object data = pngData(format);
                if (data == null) continue;
                try
                {
                    byte[] bytes = data as byte[];
                    var stream = data as Stream;
                    if (bytes != null)
                    {
                        if (bytes.Length > 80 * 1024 * 1024) continue;
                        using (var memory = new MemoryStream(bytes, false)) return DecodePng(memory);
                    }
                    if (stream != null && stream.CanSeek)
                    {
                        if (stream.Length > 80 * 1024 * 1024) continue;
                        long position = stream.Position;
                        try { stream.Position = 0; return DecodePng(stream); }
                        finally { stream.Position = position; }
                    }
                }
                catch (IOException) { }
                catch (FormatException) { }
                catch (NotSupportedException) { }
                catch (ArgumentException) { }
            }
            return NormalizeBitmap(bitmapData());
        }
        private static BitmapSource DecodePng(Stream stream)
        {
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            BitmapSource frame = decoder.Frames[0];
            if ((long)frame.PixelWidth * frame.PixelHeight > 40000000) throw new NotSupportedException("图片像素过大。");
            frame.Freeze(); return frame;
        }
        internal static BitmapSource NormalizeBitmap(BitmapSource image)
        {
            if (image == null) return null;
            if ((long)image.PixelWidth * image.PixelHeight > 40000000) throw new NotSupportedException("图片像素过大。");
            // Some bitmap producers write RGB but leave every alpha byte zero.
            // Inspect raw pixels before conversion can discard that hidden RGB.
            BitmapSource pixels = image;
            if (image.Format != PixelFormats.Bgra32 && image.Format != PixelFormats.Pbgra32)
                pixels = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            int stride = checked(image.PixelWidth * 4);
            byte[] bytes = new byte[checked(stride * image.PixelHeight)];
            pixels.CopyPixels(bytes, stride, 0);
            bool hasRgb = false;
            for (int i = 0; i < bytes.Length; i += 4)
            {
                if (bytes[i + 3] != 0) return image; // Preserve valid partial transparency.
                hasRgb |= bytes[i] != 0 || bytes[i + 1] != 0 || bytes[i + 2] != 0;
            }
            if (!hasRgb) return image; // No hidden picture data to recover.
            for (int i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
            double dpiX = image.DpiX > 0 ? image.DpiX : 96, dpiY = image.DpiY > 0 ? image.DpiY : 96;
            var restored = BitmapSource.Create(image.PixelWidth, image.PixelHeight, dpiX, dpiY, PixelFormats.Bgra32, null, bytes, stride);
            restored.Freeze(); return restored;
        }
    }
}
