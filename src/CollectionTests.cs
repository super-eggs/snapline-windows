using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Snapline
{
    internal static class CollectionTests
    {
        internal static void Wait(Task task)
        {
            if (!task.IsCompleted)
            {
                var frame = new DispatcherFrame();
                var dispatcher = Dispatcher.CurrentDispatcher;
                var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                timeout.Tick += delegate { frame.Continue = false; };
                task.ContinueWith(delegate { dispatcher.BeginInvoke(new Action(delegate { frame.Continue = false; })); });
                timeout.Start(); Dispatcher.PushFrame(frame); timeout.Stop();
                if (!task.IsCompleted) throw new TimeoutException("图片收集测试超时。");
            }
            task.GetAwaiter().GetResult();
        }
        internal static void Run(string root, Action<bool, string> check)
        {
            string folder = Path.Combine(root, "ack-watch"); Directory.CreateDirectory(folder);
            string source = Path.Combine(folder, "new.png");
            int attempts = 0, errors = 0;
            var saved = new TaskCompletionSource<bool>();
            using (var collector = new FolderCollector(folder, delegate(string path) { attempts++; return saved.Task; }, delegate { errors++; }))
            {
                Wait(collector.ScanAsync()); File.WriteAllBytes(source, Store.Encode(App.DemoImage(0)));
                Wait(collector.ScanAsync());
                Task pending = collector.ScanAsync();
                Wait(collector.ScanAsync());
                check(!pending.IsCompleted && attempts == 1, "watch waits for save acknowledgement and skips overlapping scans");
                saved.SetException(new IOException("simulated disk full")); Wait(pending);
                saved = new TaskCompletionSource<bool>(); saved.SetResult(false);
                Wait(collector.ScanAsync()); Wait(collector.ScanAsync());
                check(attempts == 2 && errors == 0, "failed save remains pending; confirmed duplicate is acknowledged once");
            }
            string retryFolder = Path.Combine(root, "cooldown-watch"); Directory.CreateDirectory(retryFolder);
            bool available = false; attempts = 0; errors = 0;
            using (var collector = new FolderCollector(retryFolder, delegate(string path)
            {
                attempts++;
                if (!available) throw new UnauthorizedAccessException("simulated unavailable destination");
                return Task.FromResult(true);
            }, delegate { errors++; }))
            {
                DateTime now = DateTime.UtcNow;
                Wait(collector.ScanAsync(now)); File.WriteAllBytes(Path.Combine(retryFolder, "retry.png"), Store.Encode(App.DemoImage(0)));
                Wait(collector.ScanAsync(now));
                for (int i = 0; i < 14; i++) Wait(collector.ScanAsync(now));
                check(attempts == 12 && errors == 1, "temporary failures enter cooldown after twelve attempts and report once");
                available = true; Wait(collector.ScanAsync(now.AddSeconds(59)));
                check(attempts == 12, "temporary failure cooldown does not busy-loop");
                Wait(collector.ScanAsync(now.AddMinutes(1))); Wait(collector.ScanAsync(now.AddMinutes(2)));
                check(attempts == 13, "watch automatically recovers after cooldown without losing failed file");
            }
            var store = new Store(Path.Combine(root, "async-data"), false);
            try
            {
                bool uiEvents = true, staWorker = false;
                var dispatcher = Dispatcher.CurrentDispatcher;
                store.Changed += delegate { uiEvents &= dispatcher.CheckAccess(); };
                string first = Path.Combine(root, "async-first.png");
                File.WriteAllBytes(first, Store.Encode(App.DemoImage(0)));
                Task<bool> a = store.ImportAsync(first), b = store.ImportAsync(first);
                Wait(Task.WhenAll(a, b));
                check(a.Result && !b.Result && store.Items.Count == 1 && Directory.GetFiles(store.Inbox, "*.png").Length == 1,
                    "queued simultaneous captures deduplicate before writing a second copy");
                Task<bool> capture = store.AddAsync(delegate
                {
                    staWorker = Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && !dispatcher.CheckAccess();
                    return App.DemoImage(1);
                });
                Wait(capture);
                check(staWorker && uiEvents, "image reading and encoding use background STA while store changes stay on UI dispatcher");
                check(store.Items.All(x => x.Image.IsFrozen && !(x.Image is TransformedBitmap)), "thumbnails are frozen detached pixel buffers");
                var restored = new Store(store.Root, false);
                try
                {
                    Task loading = restored.LoadAsync(); Task<bool> duplicate = restored.ImportAsync(first);
                    Wait(Task.WhenAll(loading, duplicate));
                    check(!duplicate.Result && restored.Items.Count == 2, "startup loading precedes queued collection and restores persisted fingerprints");
                }
                finally { Wait(restored.StopAsync()); }
                Task<bool> invalid = store.ImportAsync(Path.Combine(root, "missing.png"));
                Task<bool> afterFailure = store.AddAsync(delegate { return App.DemoImage(2); });
                try { Wait(invalid); check(false, "missing input must fail"); } catch (FileNotFoundException) { }
                Wait(afterFailure);
                check(afterFailure.Result && store.Items.Count == 3, "failed queue operation does not prevent later captures");
                using (var release = new ManualResetEventSlim())
                {
                    var reading = new TaskCompletionSource<bool>();
                    bool snapped = false, snappedBeforeQueuedImport = false;
                    Task blocker = store.AddAsync(delegate
                    {
                        reading.SetResult(true);
                        if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("snapshot test gate");
                        return null;
                    });
                    try
                    {
                        Wait(reading.Task);
                        Task queued = store.AddAsync(delegate { snappedBeforeQueuedImport = snapped; return null; });
                        Task snapshot = store.AddAsync(delegate { snapped = true; return null; }, true);
                        release.Set(); Wait(Task.WhenAll(blocker, queued, snapshot));
                        check(snappedBeforeQueuedImport, "clipboard snapshot is read before queued file imports while persistence remains serialized");
                    }
                    finally { release.Set(); }
                }
                Task<bool> draining = store.AddAsync(delegate { return BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 1, 2, 3, 255 }, 4); });
                Task<bool> last = store.AddAsync(delegate { return BitmapSource.Create(1, 1, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 4, 5, 6, 255 }, 4); });
                Task stopped = store.StopAsync();
                Wait(stopped);
                check(draining.IsCompleted && draining.Result && last.IsCompleted && last.Result && store.Items.Count == 5,
                    "shutdown persists all accepted queued captures before stopping image worker");
                try { Wait(store.ImportAsync(first)); check(false, "stopped queue must reject new work"); }
                catch (InvalidOperationException) { check(true, "shutdown rejects new collection after draining"); }
            }
            finally { Wait(store.StopAsync()); }
            var retryStore = new Store(Path.Combine(root, "retry-store"), false);
            try
            {
                Directory.Delete(retryStore.Inbox); File.WriteAllText(retryStore.Inbox, "block destination");
                int reads = 0;
                var recovered = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                recovered.Tick += delegate
                {
                    recovered.Stop(); File.Delete(retryStore.Inbox); Directory.CreateDirectory(retryStore.Inbox);
                };
                recovered.Start();
                try
                {
                    Task<bool> retry = retryStore.AddAsync(delegate { reads++; return App.DemoImage(0); });
                    Wait(retry);
                    check(retry.Result && reads == 1 && retryStore.Items.Count == 1 && Directory.GetFiles(retryStore.Inbox, "*.tmp").Length == 0,
                        "real destination failure retries the same snapshot and leaves no temporary files after recovery");
                }
                finally { recovered.Stop(); }
            }
            finally { Wait(retryStore.StopAsync()); }
            var pendingStore = new Store(Path.Combine(root, "pending-clipboard"), false);
            try
            {
                Directory.Delete(pendingStore.Inbox); File.WriteAllText(pendingStore.Inbox, "block destination");
                int reads = 0;
                Task<bool> failed = pendingStore.AddAsync(delegate { reads++; return App.DemoImage(0); }, true);
                try { Wait(failed); check(false, "persistent write failure must be reported"); }
                catch (IOException) { }
                check(pendingStore.HasPendingClipboard && pendingStore.Items.Count == 0 && reads == 1,
                    "exhausted clipboard write retries retain encoded snapshot without acknowledging collection");
                File.Delete(pendingStore.Inbox); Directory.CreateDirectory(pendingStore.Inbox);
                Task<bool> recovered = pendingStore.RetryClipboardAsync(); Wait(recovered);
                check(recovered.Result && !pendingStore.HasPendingClipboard && reads == 1 && pendingStore.Items.Count == 1,
                    "clipboard snapshot recovers after storage outage without rereading changing clipboard");
                pendingStore.Remove(pendingStore.Items[0]);
                Directory.Delete(pendingStore.Inbox); File.WriteAllText(pendingStore.Inbox, "block destination again");
                failed = pendingStore.AddAsync(delegate { return App.DemoImage(1); }, true);
                try { Wait(failed); check(false, "second persistent write failure must be reported"); }
                catch (IOException) { }
                File.Delete(pendingStore.Inbox); Directory.CreateDirectory(pendingStore.Inbox);
                Wait(pendingStore.AddAsync(delegate { return App.DemoImage(1); }));
                recovered = pendingStore.RetryClipboardAsync(); Wait(recovered);
                check(!recovered.Result && !pendingStore.HasPendingClipboard && pendingStore.Items.Count == 1 && Directory.GetFiles(pendingStore.Inbox, "*.png").Length == 1,
                    "pending clipboard retry deduplicates an image already recovered through another source");
            }
            finally { Wait(pendingStore.StopAsync()); }
            string liveFolder = Path.Combine(root, "timer-watch"); Directory.CreateDirectory(liveFolder);
            var liveStore = new Store(Path.Combine(root, "timer-store"), false);
            try
            {
                var committed = new TaskCompletionSource<bool>();
                var dispatcher = Dispatcher.CurrentDispatcher;
                liveStore.Changed += delegate { committed.TrySetResult(dispatcher.CheckAccess()); };
                using (var collector = new FolderCollector(liveFolder, delegate(string path)
                {
                    return (Task<bool>)dispatcher.Invoke(new Func<Task<bool>>(delegate { return liveStore.ImportAsync(path); }));
                }, delegate(string message) { committed.TrySetException(new IOException(message)); }))
                {
                    collector.Start(); File.Copy(source, Path.Combine(liveFolder, "first-after-start.png"));
                    Wait(committed.Task);
                    check(committed.Task.Result && liveStore.Items.Count == 1 && File.Exists(liveStore.Items[0].Path),
                        "real folder timer and dispatcher integration persist first new capture and update UI after save");
                }
            }
            finally { Wait(liveStore.StopAsync()); }
        }
    }
}
