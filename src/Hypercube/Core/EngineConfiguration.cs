namespace Hypercube.Core;

/// <summary>Disk spill format used when cardinality exceeds memory limits.</summary>
public enum SpillBackendKind
{
    /// <summary>Embedded LiteDB document store (default).</summary>
    LiteDb,

    /// <summary>Columnar Parquet files optimized for metric scans.</summary>
    Parquet
}

/// <summary>Time-window aggregation strategy.</summary>
public enum WindowStrategy
{
    /// <summary>No windowing; aggregates until <see cref="RollupEngine{T}.Clear"/>.</summary>
    Continuous,

    /// <summary>Non-overlapping fixed windows.</summary>
    Tumbling,

    /// <summary>Overlapping windows that slide forward.</summary>
    Sliding,

    /// <summary>Gap-based sessions; a new window opens after inactivity.</summary>
    Session
}

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

/// <summary>
/// Version 1.1 lifecycle configuration for <see cref="RollupEngine{T}"/>.
/// </summary>
public sealed class EngineConfiguration
{
    /// <summary>Maximum in-memory keys per dimension before spill.</summary>
    public int MaxKeysPerDimension { get; init; } = 100_000;

    /// <summary>Evict dimension keys not touched within this TTL. <c>null</c> disables scavenging.</summary>
    public TimeSpan? DimensionTimeToLive { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Event-time windowing strategy.</summary>
    public WindowConfiguration Windowing { get; init; } = new();

    /// <summary>Emit OpenTelemetry-compatible <see cref="System.Diagnostics.Metrics.Meter"/> instruments.</summary>
    public bool EnableDiagnostics { get; init; } = true;

    /// <summary>Disk spill backend format.</summary>
    public SpillBackendKind SpillBackend { get; init; } = SpillBackendKind.LiteDb;

    /// <summary>Directory for spill artifacts.</summary>
    public string SpillDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "spill");

    /// <summary>LRU cap for live spilled values in RAM. <c>0</c> is unlimited.</summary>
    public int MaxLiveCacheKeys { get; init; }

    /// <summary>Maximum concurrent in-flight ingestion operations.</summary>
    public int MaxInFlightAdds { get; init; }

    /// <summary>Allowed lateness for watermark enforcement. <c>null</c> disables watermarks.</summary>
    public TimeSpan? AllowedLateness { get; init; }

    /// <summary>Clock used for timestamps, TTL scavenging, and snapshots.</summary>
    public IClock Clock { get; init; } = SystemClock.Instance;

    /// <summary>Creates configuration from legacy <see cref="RollupEngineOptions"/>.</summary>
    public static EngineConfiguration FromLegacy(RollupEngineOptions options) => new()
    {
        MaxKeysPerDimension = options.MaxKeysPerDimension,
        SpillDirectory = options.SpillDirectory,
        MaxLiveCacheKeys = options.MaxLiveCacheKeys,
        MaxInFlightAdds = options.MaxInFlightAdds,
        AllowedLateness = options.AllowedLateness
    };

    /// <summary>Maps to legacy options for backward-compatible APIs.</summary>
    public RollupEngineOptions ToLegacyOptions() => new()
    {
        MaxKeysPerDimension = MaxKeysPerDimension,
        SpillDirectory = SpillDirectory,
        MaxLiveCacheKeys = MaxLiveCacheKeys,
        MaxInFlightAdds = MaxInFlightAdds,
        AllowedLateness = AllowedLateness
    };
}
