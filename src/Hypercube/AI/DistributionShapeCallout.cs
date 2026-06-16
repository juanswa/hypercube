namespace Hypercube.AI;

/// <summary>
/// Opinionated callout when a mean is a poor summary of the underlying distribution.
/// </summary>
/// <param name="MetricName">Base digest metric name.</param>
/// <param name="Mean">Digest mean.</param>
/// <param name="Median">Estimated p50.</param>
/// <param name="P95">Estimated 95th percentile.</param>
/// <param name="P99">Estimated 99th percentile.</param>
/// <param name="Skew">Detected skew direction.</param>
/// <param name="MeanMisleading">True when mean and median diverge materially.</param>
/// <param name="HeavyTail">True when the upper tail is disproportionately long.</param>
/// <param name="Summary">Human-readable shape summary.</param>
public sealed record DistributionShapeCallout(
    string MetricName,
    double Mean,
    double Median,
    double P95,
    double P99,
    DistributionSkew Skew,
    bool MeanMisleading,
    bool HeavyTail,
    string Summary);
