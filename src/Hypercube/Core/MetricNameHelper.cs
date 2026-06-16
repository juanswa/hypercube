namespace Hypercube.Core;

/// <summary>
/// Naming conventions for metrics derived from percentile digests.
/// </summary>
public static class MetricNameHelper
{
    public static string Mean(string metric) => $"{metric}_mean";
    public static string Percentile(string metric, int percentile) => $"{metric}_p{percentile}";
    public static string UniqueCount(string metric) => $"{metric}_unique";
}
