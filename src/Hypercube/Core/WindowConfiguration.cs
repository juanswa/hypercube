namespace Hypercube.Core;

/// <summary>Windowing parameters for event-time ingestion.</summary>
public sealed class WindowConfiguration
{
    /// <summary>Window strategy applied during ingestion.</summary>
    public WindowStrategy Strategy { get; init; } = WindowStrategy.Continuous;

    /// <summary>Window size for tumbling and sliding strategies.</summary>
    public TimeSpan WindowSize { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Slide interval for sliding windows. Defaults to half of <see cref="WindowSize"/>.</summary>
    public TimeSpan? SlideInterval { get; init; }

    /// <summary>Gap that closes a session window.</summary>
    public TimeSpan SessionGap { get; init; } = TimeSpan.FromMinutes(30);
}
