namespace Hypercube.AI;

/// <summary>
/// Driver attribution evaluated independently for multiple metrics.
/// </summary>
public sealed record MultiMetricDriverAnalysisResult(
    IReadOnlyList<MetricDriverAnalysis> Metrics);
