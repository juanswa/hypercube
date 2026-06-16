namespace Hypercube.Core;

/// <summary>
/// Supported aggregation operations for rollup metrics.
/// </summary>
public enum AggregationKind
{
    /// <summary>Increment a counter once per ingested item.</summary>
    Count,

    /// <summary>Sum a numeric field across ingested items.</summary>
    Sum,

    /// <summary>Track the minimum value of a numeric field.</summary>
    Min,

    /// <summary>Track the maximum value of a numeric field.</summary>
    Max,

    /// <summary>Increment a counter when a predicate evaluates to <c>true</c>.</summary>
    CountWhen,

    /// <summary>Streaming percentile digest over a numeric field.</summary>
    TDigest,

    /// <summary>Running arithmetic mean of a numeric field (sum and count tracked internally).</summary>
    Average,

    /// <summary>Approximate distinct count via HyperLogLog over a string entity key.</summary>
    HyperLogLog
}
