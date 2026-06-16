namespace Hypercube.Core;

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
