namespace Hypercube.Industry;

/// <summary>
/// Looks up peer cohort percentile bands for a subject's metric observation.
/// </summary>
public interface IBenchmarkProvider
{
    /// <summary>
    /// Returns the benchmark band for the given subject, dimension, cell, and metric.
    /// Returns <c>null</c> if the cohort has fewer than k peers (k-anonymity suppression).
    /// </summary>
    BenchmarkBand? Lookup(ISubject subject, string dimension, string cellKey, string metric);
}