namespace Hypercube.Core;

/// <summary>
/// Tracks event-time watermarks for out-of-order stream ingestion.
/// </summary>
public sealed class WatermarkTracker
{
    private DateTimeOffset _currentWatermark = DateTimeOffset.MinValue;

    /// <summary>
    /// Creates a watermark tracker with the given allowed lateness window.
    /// </summary>
    /// <param name="allowedLateness">How far behind the current watermark events may arrive.</param>
    public WatermarkTracker(TimeSpan allowedLateness)
    {
        if (allowedLateness < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(allowedLateness));
        }

        AllowedLateness = allowedLateness;
    }

    /// <summary>Maximum lateness permitted before events are dropped.</summary>
    public TimeSpan AllowedLateness { get; }

    /// <summary>The highest event time observed so far.</summary>
    public DateTimeOffset CurrentWatermark => _currentWatermark;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="eventTime"/> is older than the allowed lateness window.
    /// </summary>
    public bool IsLate(DateTimeOffset eventTime) =>
        _currentWatermark != DateTimeOffset.MinValue &&
        eventTime < _currentWatermark - AllowedLateness;

    /// <summary>Advances the watermark when <paramref name="eventTime"/> is newer.</summary>
    public void Advance(DateTimeOffset eventTime)
    {
        if (eventTime > _currentWatermark)
        {
            _currentWatermark = eventTime;
        }
    }
}
