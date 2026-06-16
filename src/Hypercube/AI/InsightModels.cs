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

/// <summary>
/// A scored insight about a single rollup cell.
/// </summary>
/// <param name="Kind">The insight classification.</param>
/// <param name="CellId">Cell identifier in <c>dimension:key</c> form.</param>
/// <param name="Score">Relative importance; higher means more interesting.</param>
/// <param name="Explanation">Human-readable description of why the cell was flagged.</param>
public sealed record InterestingCellInsight(
    InsightKind Kind,
    string CellId,
    double Score,
    string Explanation);

/// <summary>
/// Contribution of one cell to the overall change between two snapshots.
/// </summary>
/// <param name="CellId">Cell identifier in <c>dimension:key</c> form.</param>
/// <param name="PreviousValue">Primary metric value in the previous snapshot.</param>
/// <param name="CurrentValue">Primary metric value in the current snapshot.</param>
/// <param name="Delta"><paramref name="CurrentValue"/> minus <paramref name="PreviousValue"/>.</param>
/// <param name="ShareOfTotalDelta">
/// Fraction of total delta attributed to this cell. Zero when total delta is zero.
/// </param>
public sealed record DriverContribution(
    string CellId,
    double PreviousValue,
    double CurrentValue,
    double Delta,
    double ShareOfTotalDelta);

/// <summary>
/// Result of driver analysis between two snapshots.
/// </summary>
/// <param name="TotalPrevious">Sum of primary metric values in the previous snapshot.</param>
/// <param name="TotalCurrent">Sum of primary metric values in the current snapshot.</param>
/// <param name="TotalDelta"><paramref name="TotalCurrent"/> minus <paramref name="TotalPrevious"/>.</param>
/// <param name="TopContributors">Largest absolute deltas, ordered by magnitude.</param>
public sealed record DriverAnalysisResult(
    double TotalPrevious,
    double TotalCurrent,
    double TotalDelta,
    IReadOnlyList<DriverContribution> TopContributors);

/// <summary>
/// Driver analysis for one metric across two snapshots.
/// </summary>
public sealed record MetricDriverAnalysis(
    string Metric,
    double TotalPrevious,
    double TotalCurrent,
    double TotalDelta,
    IReadOnlyList<DriverContribution> TopContributors);

/// <summary>
/// Driver attribution evaluated independently for multiple metrics.
/// </summary>
public sealed record MultiMetricDriverAnalysisResult(
    IReadOnlyList<MetricDriverAnalysis> Metrics);

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

/// <summary>
/// Helpers for addressing rollup cells consistently across analysis components.
/// </summary>
public static class CellId
{
    /// <summary>
    /// Builds a canonical cell identifier from a summary row.
    /// </summary>
    /// <param name="row">Row containing dimension and key.</param>
    /// <returns>Identifier in <c>dimension:key</c> form.</returns>
    public static string From(SummaryRow row) => $"{row.Dimension}:{row.Key}";

    /// <summary>Case-insensitive comparer for cell identifiers.</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Compares two cell identifiers using <see cref="Comparer"/>.</summary>
    public static bool Equals(string left, string right) => Comparer.Equals(left, right);
}
