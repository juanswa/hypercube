namespace Hypercube.State.Parquet;

/// <summary>Coalesces repeated flush requests into a single background operation.</summary>
internal sealed class ParquetFlushCoordinator(Action flushCore)
{
    private readonly System.Threading.Lock _gate = new();

    private bool _flushQueued;

    /// <summary>Schedules a background flush when one is not already queued.</summary>
    public void RequestFlush()
    {
        lock (_gate)
        {
            if (_flushQueued)
            {
                return;
            }

            _flushQueued = true;
        }

        ParquetBackgroundFlush.Schedule(() =>
        {
            try
            {
                flushCore();
            }
            finally
            {
                lock (_gate)
                {
                    _flushQueued = false;
                }
            }
        });
    }

    /// <summary>Performs a synchronous flush, bypassing the background queue.</summary>
    public void FlushNow() => flushCore();
}
