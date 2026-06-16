namespace Hypercube.AI;

/// <summary>
/// Categories of deterministic insights produced from rollup snapshots.
/// </summary>
public enum InsightKind
{
    /// <summary>Cell primary metric deviates from a uniform share within its dimension.</summary>
    DeviationFromExpectation,

    /// <summary>Cell count is an outlier relative to the previous window distribution.</summary>
    ZScoreOutlier,

    /// <summary>Cell count shifted materially from its exponentially weighted moving average.</summary>
    EwmaTrendShift,

    /// <summary>Pooled signal-rate and sibling signal-rates moved in opposite directions.</summary>
    SimpsonsParadox
}
