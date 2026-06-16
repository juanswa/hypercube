namespace Hypercube.AI;

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
