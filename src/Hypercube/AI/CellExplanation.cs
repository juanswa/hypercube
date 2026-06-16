namespace Hypercube.AI;

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
