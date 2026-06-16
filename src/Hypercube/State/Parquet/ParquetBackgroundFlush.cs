using System.Collections.Concurrent;
using System.Diagnostics;

namespace Hypercube.State.Parquet;

/// <summary>
/// Shared single-threaded queue for coalesced Parquet spill flushes.
/// Uses a dedicated background thread (not the thread pool) to avoid pool starvation under sustained load.
/// </summary>
internal static class ParquetBackgroundFlush
{
    private static readonly Lazy<FlushWorker> Worker = new(() => new FlushWorker());

    /// <summary>Queues a flush action on the dedicated spill thread.</summary>
    public static void Schedule(Action flushAction) => Worker.Value.Schedule(flushAction);

    private sealed class FlushWorker
    {
        private readonly BlockingCollection<Action> _queue = [];
        private readonly Thread _thread;

        public FlushWorker()
        {
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "Hypercube.ParquetFlush"
            };
            _thread.Start();
        }

        public void Schedule(Action flushAction)
        {
            _queue.Add(flushAction);
        }

        private void Run()
        {
            foreach (var action in _queue.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (IOException ex)
                {
                    Trace.TraceWarning("Parquet spill flush IO issue: {0}", ex.Message);
                }
                catch (Exception ex)
                {
                    Trace.TraceError("Parquet spill flush failed: {0}", ex);
                }
            }
        }
    }
}
