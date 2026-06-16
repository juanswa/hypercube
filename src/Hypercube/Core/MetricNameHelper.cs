namespace Hypercube.Core;

/// <summary>
/// Naming conventions for metrics derived from percentile digests.
/// </summary>
public static class MetricNameHelper
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> MeanCache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Metric, int Percentile), string> PercentileCache =
        new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> UniqueCountCache =
        new(StringComparer.OrdinalIgnoreCase);

    public static string Mean(string metric) =>
        MeanCache.GetOrAdd(metric, static name => $"{name}_mean");

    public static string Percentile(string metric, int percentile) =>
        PercentileCache.GetOrAdd((metric, percentile), static key => $"{key.Metric}_p{key.Percentile}");

    public static string UniqueCount(string metric) =>
        UniqueCountCache.GetOrAdd(metric, static name => $"{name}_unique");
}
