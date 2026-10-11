using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Windows.Media.Imaging;

namespace Snapline
{
    internal enum WindowLayer { Top = 0, Normal = 1, Bottom = 2 }
    [DataContract] internal sealed class Settings
    {
        [DataMember] public bool ClipboardEnabled = true;
        [DataMember] public bool WatchEnabled = true;
        [DataMember] public string WatchFolder = Native.ScreenshotFolder();
        [DataMember] public WindowLayer Layer = WindowLayer.Top;
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
        internal readonly string Root;
        internal readonly string Inbox;
        internal readonly List<Shot> Items = new List<Shot>();
        internal Settings Settings;
        internal event Action Changed;
        internal event Action LayerChanged;
        internal void SetLayer(WindowLayer layer)
        {
            Settings.Layer = layer; SaveSettings();
            if (LayerChanged != null) LayerChanged();
        }
        internal event Action<string> Error;
        internal Store(string root)
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
                }
                catch (Exception) { Settings = new Settings(); }
            }
            foreach (string file in Directory.GetFiles(Inbox, "*.png").OrderByDescending(File.GetLastWriteTimeUtc).Take(Limit))
            {
                try
                {
                    BitmapSource image = ReadImage(file);
                    Items.Add(new Shot { Path = file, Image = Thumbnail(image), Fingerprint = Hash(Encode(image)), Created = File.GetLastWriteTime(file) });
                }
                catch (Exception) { }
            }
        }
        internal static BitmapSource ReadImage(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > 80 * 1024 * 1024) throw new NotSupportedException("图片超过 80 MB，请选择较小的图片。");
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 40000000) throw new NotSupportedException("图片像素过大。");
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
            var thumb = new TransformedBitmap(image, new System.Windows.Media.ScaleTransform(scale, scale));
            thumb.Freeze();
            return thumb;
        }
        internal bool Import(string path) { return Add(ReadImage(path)); }
        internal bool Add(BitmapSource image)
        {
            if (image == null) return false;
            if ((long)image.PixelWidth * image.PixelHeight > 40000000) throw new NotSupportedException("图片像素过大。");
            byte[] bytes = Encode(image);
            string fingerprint = Hash(bytes);
            if (Items.Any(x => x.Fingerprint == fingerprint)) return false;
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            string destination = System.IO.Path.Combine(Inbox, name);
            File.WriteAllBytes(destination + ".tmp", bytes);
            File.Move(destination + ".tmp", destination);
            Items.Insert(0, new Shot { Path = destination, Image = Thumbnail(image), Fingerprint = fingerprint, Created = DateTime.Now });
            // Keep a bounded shelf, but never silently delete old captures.
            while (Items.Count > Limit) Items.RemoveAt(Items.Count - 1);
            Notify();
            return true;
        }
        internal void Remove(Shot shot)
        {
            // Only the app-owned copy can be removed; imported source files are untouched.
            if (!Items.Contains(shot) || !Owns(shot.Path)) return;
            File.Delete(shot.Path);
            Items.Remove(shot);
            Notify();
        }
        internal bool Owns(string path)
        {
            return string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), Inbox, StringComparison.OrdinalIgnoreCase);
        }
        internal void Replace(Shot shot, BitmapSource edited)
        {
            if (!Items.Contains(shot) || !Owns(shot.Path)) return;
            byte[] bytes = Encode(edited);
            string temp = shot.Path + ".edit";
            File.WriteAllBytes(temp, bytes);
            File.Replace(temp, shot.Path, null);
            shot.Image = Thumbnail(edited);
            shot.Fingerprint = Hash(bytes);
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
