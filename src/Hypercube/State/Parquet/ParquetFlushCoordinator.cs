namespace Hypercube.State.Parquet;

/// <summary>Coalesces repeated flush requests into a single background operation.</summary>
internal sealed class ParquetFlushCoordinator
{
    private readonly Lock _gate = new();
    private readonly Action _flushCore;
    private bool _flushQueued;

    public ParquetFlushCoordinator(Action flushCore) => _flushCore = flushCore;

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
                _flushCore();
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
    public void FlushNow() => _flushCore();
}
