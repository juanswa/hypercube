namespace Hypercube.AI;

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
