using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media.Imaging;

namespace Snapline
{
    // A bounded, stable-file scanner also survives dropped FileSystemWatcher events.
    internal sealed class FolderCollector : IDisposable
    {
        private readonly string folder;
        private readonly Action<BitmapSource> received;
        private readonly Action<string> error;
        private readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        private Timer timer;
        private bool initialized, disposed;
        private readonly object gate = new object();
        private sealed class Pending { internal long Length; internal DateTime Modified; internal int Attempts; }
        internal static bool IsImage(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif" || extension == ".tif" || extension == ".tiff";
        }
        internal FolderCollector(string path, Action<BitmapSource> received, Action<string> error)
        {
            folder = path; this.received = received; this.error = error;
        }
        internal void Start() { Scan(); timer = new Timer(delegate { Scan(); }, null, 700, 700); }
        internal void Scan()
        {
            lock (gate)
            {
                if (disposed) return;
                // An initially absent Screenshots directory must not swallow its
                // first screenshot when Windows creates the directory later.
                if (!Directory.Exists(folder)) { initialized = true; return; }
                try
                {
                    var files = Directory.GetFiles(folder).Where(IsImage).ToArray();
                    if (!initialized)
                    {
                        foreach (string file in files) known.Add(file);
                        initialized = true;
                        return; // Never ingest a user's existing image library on startup.
                    }
                    var present = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
                    known.RemoveWhere(x => !present.Contains(x));
                    foreach (string removed in pending.Keys.Where(x => !present.Contains(x)).ToArray()) pending.Remove(removed);
                    foreach (string file in files)
                    {
                        if (known.Contains(file)) continue;
                        var info = new FileInfo(file);
                        Pending candidate;
                        if (!pending.TryGetValue(file, out candidate))
                        {
                            pending[file] = new Pending { Length = info.Length, Modified = info.LastWriteTimeUtc };
                            continue;
                        }
                        if (candidate.Length != info.Length || candidate.Modified != info.LastWriteTimeUtc || info.Length == 0)
                        {
                            candidate.Length = info.Length; candidate.Modified = info.LastWriteTimeUtc;
                            continue;
                        }
                        try
                        {
                            BitmapSource image = Store.ReadImage(file);
                            received(image);
                            known.Add(file); pending.Remove(file);
                        }
                        catch (Exception)
                        {
                            if (++candidate.Attempts >= 12)
                            {
                                known.Add(file); pending.Remove(file);
                                error("有一张图片无法读取，可稍后手动导入。");
                            }
                        }
                    }
                }
                catch (IOException) { error("截图文件夹暂时不可读，正在等待恢复。"); }
                catch (UnauthorizedAccessException) { error("无法访问截图文件夹，请在托盘中重新选择。"); }
            }
        }
        public void Dispose() { lock (gate) { disposed = true; if (timer != null) timer.Dispose(); } }
    }
}
