using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal enum WindowLayer { Top = 0, Normal = 1, Bottom = 2 }
    [DataContract] internal sealed class Settings
    {
        [DataMember] public bool ClipboardEnabled = true;
        [DataMember] public bool WatchEnabled = true;
        [DataMember] public string WatchFolder = Native.ScreenshotFolder();
        [DataMember] public WindowLayer Layer = WindowLayer.Top;
        [DataMember] public string CaptureHotkey = "F1";
        [DataMember] public string PinHotkey = "F3";
        [DataMember] public string TogglePinsHotkey = "Shift+F3";
        [DataMember] public bool ShelfHoverEnabled = true;

        [OnDeserializing]
        private void SetDeserializationDefaults(StreamingContext context)
        {
            ShelfHoverEnabled = true;
        }
    }
    internal sealed class Shot
    {
        public string Path;
        public BitmapSource Image;
        public string Fingerprint;
        public DateTime Created;
    }
    internal sealed class Store
    {
        internal const int Limit = 60;
        private const int FirstThumbnailBatch = 12;
        internal readonly string Root;
        internal readonly string Inbox;
        internal readonly List<Shot> Items = new List<Shot>();
        internal Settings Settings;
        private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        private ImageWorker worker;
        private Task tail = Task.FromResult(true);
        private bool stopping, historyPending;
        private sealed class Capture { internal byte[] Bytes; internal string Fingerprint; internal BitmapSource Thumbnail; }
        private Capture pendingClipboard;
        internal bool HasPendingClipboard { get { return pendingClipboard != null; } }
        internal bool ThumbnailsPending { get; private set; }
        internal event Action Changed;
        internal event Action LayerChanged;
        internal void SetLayer(WindowLayer layer)
        {
            Settings.Layer = layer; SaveSettings();
            if (LayerChanged != null) LayerChanged();
        }
        internal event Action<string> Error;
        internal Store(string root, bool load = true, bool deferHistory = false)
        {
            Root = System.IO.Path.GetFullPath(root);
            Inbox = System.IO.Path.Combine(Root, "inbox");
            Directory.CreateDirectory(Inbox);
            Settings = new Settings();
            string config = System.IO.Path.Combine(Root, "settings.json");
            if (File.Exists(config))
            {
                try
                {
                    using (var stream = File.OpenRead(config))
                        Settings = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                    if (Settings == null) Settings = new Settings();
                    if (!Enum.IsDefined(typeof(WindowLayer), Settings.Layer)) Settings.Layer = WindowLayer.Top;
                    if (string.IsNullOrWhiteSpace(Settings.CaptureHotkey)) Settings.CaptureHotkey = "F1";
                    if (string.IsNullOrWhiteSpace(Settings.PinHotkey)) Settings.PinHotkey = "F3";
                    if (string.IsNullOrWhiteSpace(Settings.TogglePinsHotkey)) Settings.TogglePinsHotkey = "Shift+F3";
                }
                catch (Exception) { Settings = new Settings(); }
            }
            historyPending = ThumbnailsPending = deferHistory;
            if (load && !deferHistory) Items.AddRange(LoadShots(true));
        }
        private List<Shot> LoadShots(bool thumbnails)
        {
            var shots = new List<Shot>();
            foreach (string file in Directory.GetFiles(Inbox, "*.png").OrderByDescending(File.GetLastWriteTimeUtc).Take(Limit))
            {
                try
                {
                    BitmapSource image = thumbnails ? ReadImage(file, true) : null;
                    string fingerprint;
                    using (var stream = File.OpenRead(file))
                    using (var hash = SHA256.Create()) fingerprint = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
                    shots.Add(new Shot { Path = file, Image = image, Fingerprint = fingerprint, Created = File.GetLastWriteTime(file) });
                }
                catch (Exception) { }
            }
            return shots;
        }
        private void EnsureWorker()
        {
            dispatcher.VerifyAccess();
            if (stopping) throw new InvalidOperationException("图片收集正在退出。");
            if (worker == null) worker = new ImageWorker();
        }
        private async Task<T> Enqueue<T>(Func<Task<T>> action)
        {
            EnsureWorker();
            Task previous = tail;
            var completed = new TaskCompletionSource<bool>();
            tail = completed.Task;
            await previous;
            try { return await action(); }
            finally { completed.SetResult(true); }
        }
        internal Task<bool> LoadAsync()
        {
            return Enqueue(async delegate
            {
                await ReadHistoryAsync(true);
                ThumbnailsPending = false; Notify(); return true;
            });
        }
        private async Task ReadHistoryAsync(bool thumbnails)
        {
            var shots = await worker.Run(delegate { return LoadShots(thumbnails); });
            Items.AddRange(shots); historyPending = false;
        }
        internal Task<bool> LoadThumbnailsAsync()
        {
            return Enqueue(async delegate
            {
                if (!ThumbnailsPending) return true;
                if (historyPending) await ReadHistoryAsync(false);
                var missing = Items.Where(shot => shot.Image == null).ToArray();
                if (missing.Length > FirstThumbnailBatch)
                {
                    await ReadThumbnailsAsync(missing.Take(FirstThumbnailBatch).ToArray());
                    Notify();
                    missing = missing.Skip(FirstThumbnailBatch).ToArray();
                }
                if (missing.Length > 0) await ReadThumbnailsAsync(missing);
                ThumbnailsPending = false; Notify(); return true;
            });
        }
        private async Task ReadThumbnailsAsync(Shot[] shots)
        {
            var images = await worker.Run(delegate
            {
                return shots.Select(shot =>
                {
                    try { return ReadImage(shot.Path, true); }
                    catch (Exception) { return null; }
                }).ToArray();
            });
            for (int i = 0; i < shots.Length; i++)
            {
                if (images[i] == null) Items.Remove(shots[i]);
                else shots[i].Image = images[i];
            }
        }
        internal Task<bool> ImportAsync(string path) { return AddAsync(delegate { return ReadImage(path); }); }
        internal Task<bool> AddAsync(Func<BitmapSource> read, bool snapshotImmediately = false)
        {
            // Snapshot a changing clipboard before waiting behind a batch of file imports.
            Task<BitmapSource> snapshot = null;
            if (snapshotImmediately)
            {
                EnsureWorker();
                snapshot = worker.Run(delegate
                {
                    var image = read();
                    if (image != null) image.Freeze();
                    return image;
                });
            }
            return Enqueue(async delegate
            {
                BitmapSource image = snapshot == null ? null : await snapshot;
                Capture capture = await worker.Run(delegate { return Prepare(snapshot == null ? read() : image); });
                if (capture == null) return false;
                Shot shot;
                try
                {
                    if (historyPending) await ReadHistoryAsync(false);
                    if (Items.Any(x => x.Fingerprint == capture.Fingerprint)) return false;
                    shot = await PersistAsync(capture);
                }
                catch (IOException) { if (snapshotImmediately) pendingClipboard = capture; throw; }
                catch (UnauthorizedAccessException) { if (snapshotImmediately) pendingClipboard = capture; throw; }
                Publish(shot); return true;
            });
        }
        internal Task<bool> RetryClipboardAsync()
        {
            return Enqueue(async delegate
            {
                Capture capture = pendingClipboard;
                if (historyPending) await ReadHistoryAsync(false);
                if (Items.Any(x => x.Fingerprint == capture.Fingerprint)) { pendingClipboard = null; return false; }
                Shot shot = await PersistAsync(capture);
                pendingClipboard = null;
                Publish(shot); return true;
            });
        }
        internal Task WhenIdleAsync() { return tail; }
        internal async Task StopAsync()
        {
            stopping = true;
            await tail;
            if (worker != null) { worker.Dispose(); worker = null; }
        }
        private async Task<Shot> PersistAsync(Capture capture)
        {
            for (int attempt = 0; ; attempt++)
            {
                try { return await worker.Run(delegate { return Persist(capture); }); }
                catch (IOException) { if (attempt == 2) throw; }
                catch (UnauthorizedAccessException) { if (attempt == 2) throw; }
                // Keep this snapshot while retrying, even if the clipboard changes meanwhile.
                await Task.Delay(700 * (attempt + 1));
            }
        }
        internal static BitmapSource ReadImage(string path) { return ReadImage(path, false); }
        private static BitmapSource ReadImage(string path, bool thumbnail)
        {
            var info = new FileInfo(path);
            if (info.Length > 80 * 1024 * 1024) throw new NotSupportedException("图片超过 80 MB，请选择较小的图片。");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, thumbnail ? BitmapCacheOption.None : BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 40000000) throw new NotSupportedException("图片像素过大。");
                if (thumbnail)
                {
                    var image = new BitmapImage();
                    image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
                    image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                    if (frame.PixelWidth >= frame.PixelHeight) image.DecodePixelWidth = Math.Min(420, frame.PixelWidth);
                    else image.DecodePixelHeight = Math.Min(420, frame.PixelHeight);
                    stream.Position = 0; image.StreamSource = stream; image.EndInit(); image.Freeze();
                    return image;
                }
                frame.Freeze();
                return frame;
            }
        }
        internal static byte[] Encode(BitmapSource image)
        {
            var converted = new System.Windows.Media.Imaging.FormatConvertedBitmap(image, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(converted));
            using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
        }
        internal static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
        private static BitmapSource Thumbnail(BitmapSource image)
        {
            double scale = Math.Min(1, 420.0 / Math.Max(image.PixelWidth, image.PixelHeight));
            BitmapSource resized = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
            if (resized.Format != System.Windows.Media.PixelFormats.Bgra32)
                resized = new FormatConvertedBitmap(resized, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int stride = resized.PixelWidth * 4;
            byte[] pixels = new byte[stride * resized.PixelHeight];
            resized.CopyPixels(pixels, stride, 0);
            // Detach from the decoder/transform so 60 thumbnails cannot retain 60 full images.
            var thumb = BitmapSource.Create(resized.PixelWidth, resized.PixelHeight, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, stride);
            thumb.Freeze(); return thumb;
        }
        internal bool Import(string path) { return Add(ReadImage(path)); }
        internal bool Add(BitmapSource image)
        {
            Capture capture = Prepare(image);
            if (capture == null || Items.Any(x => x.Fingerprint == capture.Fingerprint)) return false;
            Publish(Persist(capture)); return true;
        }
        private static Capture Prepare(BitmapSource image)
        {
            if (image == null) return null;
            if ((long)image.PixelWidth * image.PixelHeight > 40000000) throw new NotSupportedException("图片像素过大。");
            byte[] bytes = Encode(image);
            return new Capture { Bytes = bytes, Fingerprint = Hash(bytes), Thumbnail = Thumbnail(image) };
        }
        private Shot Persist(Capture capture)
        {
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            string destination = System.IO.Path.Combine(Inbox, name);
            string temp = destination + ".tmp";
            try { File.WriteAllBytes(temp, capture.Bytes); File.Move(temp, destination); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            return new Shot { Path = destination, Image = capture.Thumbnail, Fingerprint = capture.Fingerprint, Created = DateTime.Now };
        }
        private void Publish(Shot shot)
        {
            Items.Insert(0, shot);
            // Keep a bounded shelf, but never silently delete old captures.
            while (Items.Count > Limit) Items.RemoveAt(Items.Count - 1);
            Notify();
        }
        internal bool Remove(Shot shot)
        {
            // Only the app-owned copy can be removed; imported source files are untouched.
            if (!Items.Contains(shot) || !Owns(shot.Path)) return false;
            File.Delete(shot.Path);
            Items.Remove(shot);
            Notify();
            return true;
        }
        internal bool Owns(string path)
        {
            return string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), Inbox, StringComparison.OrdinalIgnoreCase);
        }
        internal void Replace(Shot shot, BitmapSource edited)
        {
            if (!Items.Contains(shot) || !Owns(shot.Path)) return;
            Capture capture = Prepare(edited);
            string temp = shot.Path + ".edit";
            File.WriteAllBytes(temp, capture.Bytes);
            File.Replace(temp, shot.Path, null);
            PublishReplacement(shot, capture);
        }
        internal Task<bool> ReplaceAsync(Shot shot, BitmapSource edited)
        {
            return Enqueue(async delegate
            {
                if (!Items.Contains(shot) || !Owns(shot.Path)) throw new IOException("截图已离开晾衣绳。");
                string path = shot.Path;
                string temp = path + ".edit-" + Guid.NewGuid().ToString("N");
                try
                {
                    Capture capture = await worker.Run(delegate
                    {
                        Capture prepared = Prepare(edited);
                        File.WriteAllBytes(temp, prepared.Bytes);
                        return prepared;
                    });
                    if (!Items.Contains(shot)) throw new IOException("截图已离开晾衣绳。");
                    File.Replace(temp, path, null);
                    PublishReplacement(shot, capture);
                    return true;
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            });
        }
        private void PublishReplacement(Shot shot, Capture capture)
        {
            shot.Image = capture.Thumbnail;
            shot.Fingerprint = capture.Fingerprint;
            Notify();
        }
        internal void SaveSettings()
        {
            string path = System.IO.Path.Combine(Root, "settings.json");
            string temp = path + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, Settings);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal void Report(string message) { if (Error != null) Error(message); }
        private void Notify() { if (Changed != null) Changed(); }
    }
}
