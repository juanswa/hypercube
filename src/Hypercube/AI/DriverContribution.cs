namespace Hypercube.AI;

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
