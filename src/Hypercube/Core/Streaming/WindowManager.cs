namespace Hypercube.Core;

/// <summary>
/// Computes window-qualified dimension keys from event timestamps.
/// </summary>
public static class WindowManager
{
    /// <summary>
    /// Applies the configured window strategy to a base dimension key.
    /// </summary>
    public static string QualifyKey(string baseKey, DateTimeOffset eventTime, WindowConfiguration windowing)
    {
        if (windowing.Strategy == WindowStrategy.Continuous)
        {
            return baseKey;
        }

        var windowStart = ComputeWindowStart(eventTime, windowing);
        return $"{baseKey}@{windowStart:yyyyMMddHHmmss}";
    }

    /// <summary>
    /// Returns the UTC start instant of the window containing <paramref name="eventTime"/>.
    /// </summary>
    public static DateTimeOffset ComputeWindowStart(DateTimeOffset eventTime, WindowConfiguration windowing)
    {
        var utc = eventTime.ToUniversalTime();
        return windowing.Strategy switch
        {
            WindowStrategy.Tumbling => AlignToWindow(utc, windowing.WindowSize),
            WindowStrategy.Sliding => AlignToWindow(
                utc,
                windowing.SlideInterval ?? TimeSpan.FromTicks(windowing.WindowSize.Ticks / 2)),
            WindowStrategy.Session => AlignToSession(utc, windowing.SessionGap),
            _ => utc
        };
    }

    private static DateTimeOffset AlignToWindow(DateTimeOffset eventTime, TimeSpan windowSize)
    {
        if (windowSize <= TimeSpan.Zero)
        {
            return eventTime;
        }

        var ticks = eventTime.UtcTicks - (eventTime.UtcTicks % windowSize.Ticks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static DateTimeOffset AlignToSession(DateTimeOffset eventTime, TimeSpan sessionGap)
    {
        if (sessionGap <= TimeSpan.Zero)
        {
            return eventTime;
        }

        var bucketTicks = sessionGap.Ticks;
        var ticks = eventTime.UtcTicks - (eventTime.UtcTicks % bucketTicks);
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }
}
