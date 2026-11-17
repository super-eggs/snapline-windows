using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Snapline
{
    // A bounded, stable-file scanner also survives dropped FileSystemWatcher events.
    internal sealed class FolderCollector : IDisposable
    {
        private readonly string folder;
        private readonly Func<string, Task<bool>> received;
        private readonly Action<string> error;
        private readonly HashSet<string> known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Pending> pending = new Dictionary<string, Pending>(StringComparer.OrdinalIgnoreCase);
        private Timer timer;
        private bool initialized;
        private volatile bool disposed;
        private int scanning;
        private sealed class Pending
        {
            internal long Length;
            internal DateTime Modified, RetryAfter;
            internal int Attempts;
            internal bool Exhausted;
        }
        internal static bool IsImage(string path)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif" || extension == ".tif" || extension == ".tiff";
        }
        internal FolderCollector(string path, Func<string, Task<bool>> received, Action<string> error)
        {
            folder = path; this.received = received; this.error = error;
        }
        internal void Start()
        {
            // Establish the existing-file baseline before returning; this scan only enumerates paths.
            ScanAsync().GetAwaiter().GetResult();
            timer = new Timer(async delegate { await ScanAsync(); }, null, 700, 700);
        }
        internal async Task ScanAsync(DateTime? scanTime = null)
        {
            if (disposed || Interlocked.CompareExchange(ref scanning, 1, 0) != 0) return;
            try
            {
                if (disposed) return;
                DateTime now = scanTime ?? DateTime.UtcNow;
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
                        if (disposed) return;
                        if (known.Contains(file)) continue;
                        var info = new FileInfo(file);
                        Pending candidate;
                        if (!pending.TryGetValue(file, out candidate))
                        {
                            pending[file] = new Pending { Length = info.Length, Modified = info.LastWriteTimeUtc };
                            continue;
                        }
                        if (candidate.Length != info.Length || candidate.Modified != info.LastWriteTimeUtc)
                        {
                            candidate.Length = info.Length; candidate.Modified = info.LastWriteTimeUtc;
                            candidate.Attempts = 0; candidate.Exhausted = false; candidate.RetryAfter = DateTime.MinValue;
                            continue;
                        }
                        if (info.Length == 0 || candidate.Exhausted || now < candidate.RetryAfter) continue;
                        try
                        {
                            // Added and duplicate are both acknowledged only after the store finishes.
                            await received(file);
                            known.Add(file); pending.Remove(file);
                        }
                        catch (Exception ex)
                        {
                            bool temporary = ex is IOException || ex is UnauthorizedAccessException;
                            if (candidate.Attempts < 12 && ++candidate.Attempts == 12)
                            {
                                error(temporary ? "有一张图片仍未保存，请检查磁盘空间与权限；每分钟继续尝试。" : "有一张图片无法读取，可稍后手动导入。");
                            }
                            if (candidate.Attempts >= 12)
                            {
                                candidate.Exhausted = !temporary;
                                candidate.RetryAfter = now.AddMinutes(1);
                            }
                        }
                    }
                }
                catch (IOException) { error("截图文件夹暂时不可读，正在等待恢复。"); }
                catch (UnauthorizedAccessException) { error("无法访问截图文件夹，请在托盘中重新选择。"); }
            }
            finally { Interlocked.Exchange(ref scanning, 0); }
        }
        public void Dispose() { disposed = true; if (timer != null) timer.Dispose(); }
    }
}
