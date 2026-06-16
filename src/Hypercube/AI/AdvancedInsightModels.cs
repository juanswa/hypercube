namespace Hypercube.AI;

/// <summary>Detected skew direction for a metric distribution.</summary>
public enum DistributionSkew
{
    Unknown,
    Symmetric,
    RightSkewed,
    LeftSkewed
}

/// <summary>
/// Opinionated callout when a mean is a poor summary of the underlying distribution.
/// </summary>
/// <param name="MetricName">Base digest metric name.</param>
/// <param name="Mean">Digest mean.</param>
/// <param name="Median">Estimated p50.</param>
/// <param name="P95">Estimated 95th percentile.</param>
/// <param name="P99">Estimated 99th percentile.</param>
/// <param name="Skew">Detected skew direction.</param>
/// <param name="MeanMisleading">True when mean and median diverge materially.</param>
/// <param name="HeavyTail">True when the upper tail is disproportionately long.</param>
/// <param name="Summary">Human-readable shape summary.</param>
public sealed record DistributionShapeCallout(
    string MetricName,
    double Mean,
    double Median,
    double P95,
    double P99,
    DistributionSkew Skew,
    bool MeanMisleading,
    bool HeavyTail,
    string Summary);

/// <summary>
/// Pair of cells that co-move across snapshot history, optionally with lead/lag.
/// </summary>
/// <param name="CellId">The anchor cell.</param>
/// <param name="OtherCellId">The related cell.</param>
/// <param name="Correlation">Pearson correlation on EWMA-smoothed series (-1 to 1).</param>
/// <param name="LagSnapshots">
/// Positive when <paramref name="CellId"/> leads <paramref name="OtherCellId"/> by this many snapshots.
/// </param>
/// <param name="Explanation">Human-readable co-movement summary.</param>
public sealed record CoMovementPair(
    string CellId,
    string OtherCellId,
    double Correlation,
    int LagSnapshots,
    string Explanation);

/// <summary>
/// Full statistical context for a single rollup cell.
/// </summary>
public sealed record CellExplanation(
    string CellId,
    string Dimension,
    string Key,
    double PrimaryValue,
    double SiblingPercentileRank,
    double ShareOfDimension,
    double? PreviousValue,
    double? TrendDelta,
    double? EwmaBaseline,
    double? ZScore,
    DriverContribution? DriverContribution,
    DistributionShapeCallout? DistributionShape,
    IReadOnlyList<CoMovementPair> CoMovements,
    IReadOnlyList<string> NarrativeBullets);
