using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal static class MemoryTests
    {
        internal static void Run(string root, Action<bool, string> check)
        {
            using (var worker = new ImageWorker())
            {
                var payload = SubmitPayload(worker);
                CollectionTests.Wait(Task.Delay(50));
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                check(!payload.IsAlive, "idle image worker releases completed task captures without needing another task");
                var next = worker.Run(delegate { return 42; }); CollectionTests.Wait(next);
                check(next.Result == 42, "image worker remains usable after releasing its last task");
            }
            int width = 4101, height = 9;
            var bytes = new byte[width * height * 4];
            for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = (byte)(i / 4); bytes[i + 1] = 37; bytes[i + 2] = 151; }
            foreach (var format in new[] { PixelFormats.Bgra32, PixelFormats.Pbgra32 })
            {
                var source = BitmapSource.Create(width, height, 144, 192, format, null, bytes, width * 4);
                var repaired = ClipboardImages.NormalizeBitmap(source);
                var actual = new byte[bytes.Length]; repaired.CopyPixels(actual, width * 4, 0);
                bool correct = true;
                for (int i = 0; i < actual.Length; i += 4)
                    correct &= actual[i] == bytes[i] && actual[i + 1] == bytes[i + 1] && actual[i + 2] == bytes[i + 2] && actual[i + 3] == 255;
                check(correct && repaired.IsFrozen && repaired.DpiX == 144 && repaired.DpiY == 192,
                    "bounded clipboard scan repairs every pixel across horizontal and vertical block tails: " + format);
            }
            bytes[bytes.Length - 1] = 128;
            var partial = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4);
            check(ReferenceEquals(partial, ClipboardImages.NormalizeBitmap(partial)), "nonzero alpha in the final clipboard block preserves genuine transparency everywhere");

            var image = App.DemoImage(0);
            var canvas = new InkCanvas { Width = image.PixelWidth, Height = image.PixelHeight, Background = new ImageBrush(image) };
            var annotations = new AnnotationTools(canvas, image);
            annotations.AddMark(AnnotationTool.Mosaic, new Point(12, 12), new Point(72, 72));
            var mosaic = (BitmapSource)canvas.Children.OfType<Image>().Single().Source;
            check(mosaic.IsFrozen && mosaic.PixelWidth == 5 && mosaic.PixelHeight == 5
                && !(mosaic is TransformedBitmap) && !(mosaic is CroppedBitmap), "mosaic history owns only its tiny pixel patch rather than a full-image transform chain");

            var store = new Store(Path.Combine(root, "memory-history"), false);
            store.Add(BitmapSource.Create(1200, 800, 96, 96, PixelFormats.Bgra32, null, new byte[1200 * 800 * 4], 1200 * 4));
            store.Add(BitmapSource.Create(600, 1200, 96, 96, PixelFormats.Bgra32, null, new byte[600 * 1200 * 4], 600 * 4));
            var restored = new Store(store.Root);
            check(restored.Items.All(shot => shot.Image.IsFrozen && Math.Max(shot.Image.PixelWidth, shot.Image.PixelHeight) == 420), "history decodes portrait and landscape images directly at thumbnail resolution");
            var original = Store.ReadImage(store.Items[0].Path);
            check(original.PixelWidth == 600 && original.PixelHeight == 1200, "thumbnail-only history loading preserves original stored image resolution");
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference SubmitPayload(ImageWorker worker)
        {
            var bytes = new byte[1024 * 1024];
            var weak = new WeakReference(bytes);
            CollectionTests.Wait(worker.Run(delegate { GC.KeepAlive(bytes); return true; }));
            return weak;
        }
    }
}
