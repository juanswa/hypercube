namespace Hypercube.AI;

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
