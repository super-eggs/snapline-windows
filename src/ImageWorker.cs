using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Snapline
{
    // Clipboard access requires STA. Paths are queued; full images are only held by the active operation.
    internal sealed class ImageWorker : IDisposable
    {
        private readonly BlockingCollection<Action> work = new BlockingCollection<Action>();
        internal ImageWorker()
        {
            var thread = new Thread(delegate()
            {
                Action action;
                while (work.TryTake(out action, Timeout.Infinite))
                {
                    action();
                    // A blocking iterator retains its last Current while idle, including image results.
                    action = null;
                }
                work.Dispose();
            }) { IsBackground = true, Name = "Snapline images" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
        internal Task<T> Run<T>(Func<T> action)
        {
            var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            work.Add(delegate
            {
                try { result.SetResult(action()); }
                catch (Exception ex) { result.SetException(ex); }
            });
            return result.Task;
        }
        public void Dispose() { work.CompleteAdding(); }
    }
}
