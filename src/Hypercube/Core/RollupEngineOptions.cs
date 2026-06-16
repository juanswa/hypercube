namespace Hypercube.Core;

/// <summary>
/// Runtime options that control memory use and spill behavior for <see cref="RollupEngine{T}"/>.
/// </summary>
public sealed class RollupEngineOptions
{
    /// <summary>
    /// Maximum number of distinct keys kept in memory per dimension before spilling to disk.
    /// Default is <c>100_000</c>.
    /// </summary>
    public int MaxKeysPerDimension { get; init; } = 100_000;

    /// <summary>
    /// Directory where LiteDB spill files are written when cardinality exceeds
    /// <see cref="MaxKeysPerDimension"/>. Created automatically if it does not exist.
    /// </summary>
    public string SpillDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "spill");

    /// <summary>
    /// Maximum live cell states cached in RAM per spilled dimension. Evicted keys remain on disk
    /// and are reloaded on next access. <c>0</c> means unlimited.
    /// </summary>
    public int MaxLiveCacheKeys { get; init; }

    /// <summary>
    /// Maximum concurrent in-flight <see cref="RollupEngine{T}.TryAdd"/> operations.
    /// When exceeded, callers receive <see cref="RollupAddResult.Backpressure"/>. <c>0</c> disables the limit.
    /// </summary>
    public int MaxInFlightAdds { get; init; }

    /// <summary>
    /// Allowed lateness for event-time ingestion. <c>null</c> disables watermark enforcement.
    /// </summary>
    public TimeSpan? AllowedLateness { get; init; }
}
