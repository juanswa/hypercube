namespace Hypercube.AI;

/// <summary>
/// Detected parent/child trend reversal consistent with Simpson's paradox.
/// </summary>
/// <param name="ParentCellId">Dimension or grouping identifier.</param>
/// <param name="PooledRateDelta">Change in the count-weighted pooled signal-rate.</param>
/// <param name="ChildRateDeltas">Per-sibling signal-rate deltas.</param>
/// <param name="Explanation">Human-readable summary of the reversal.</param>
public sealed record SimpsonParadoxSignal(
    string ParentCellId,
    double PooledRateDelta,
    IReadOnlyList<(string ChildCellId, double RateDelta)> ChildRateDeltas,
    string Explanation);
