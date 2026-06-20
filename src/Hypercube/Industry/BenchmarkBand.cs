namespace Hypercube.Industry;

/// <summary>
/// A percentile band from the peer cohort distribution.
/// </summary>
/// <param name="P25">25th percentile of the rate distribution.</param>
/// <param name="Median">50th percentile (median).</param>
/// <param name="P75">75th percentile of the rate distribution.</param>
/// <param name="P90">90th percentile of the rate distribution.</param>
/// <param name="PeerCount">Number of peer subjects in this cohort cell. Must be ≥ k (k-anonymity floor).</param>
/// <param name="Resolved">The cohort key this band was resolved from (after any fallback ladder broadening).</param>
public sealed record BenchmarkBand(
    double P25,
    double Median,
    double P75,
    int PeerCount,
    CohortKey Resolved,
    double P90 = 0.0);
