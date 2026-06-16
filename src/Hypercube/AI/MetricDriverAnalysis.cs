namespace Hypercube.AI;

/// <summary>
/// Driver analysis for one metric across two snapshots.
/// </summary>
public sealed record MetricDriverAnalysis(
    string Metric,
    double TotalPrevious,
    double TotalCurrent,
    double TotalDelta,
    IReadOnlyList<DriverContribution> TopContributors);
