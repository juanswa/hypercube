namespace Hypercube.Core.Streaming;

/// <summary>
/// Result of attempting to ingest an item into <see cref="RollupEngine{T}"/>.
/// </summary>
public enum RollupAddResult
{
    /// <summary>The item was accepted and aggregated.</summary>
    Accepted,

    /// <summary>The item was rejected because in-flight capacity was exceeded.</summary>
    Backpressure,

    /// <summary>The item was dropped because it arrived after the configured watermark lateness.</summary>
    DroppedLate
}
