namespace Hypercube.Models;

/// <summary>
/// A single aggregated cell in a rollup snapshot.
/// Each row represents one dimension key (for example <c>channel:sms</c>) and the
/// metric values computed for that cell.
/// </summary>
/// <param name="Dimension">The dimension name (normalized to lowercase).</param>
/// <param name="Key">The dimension key value (normalized to lowercase).</param>
/// <param name="Metrics">Named metric values produced by the rollup schema.</param>
public sealed record SummaryRow(
    string Dimension,
    string Key,
    IReadOnlyDictionary<string, double> Metrics)
{
    /// <summary>
    /// Gets a metric value by name. Returns <c>0</c> when the metric is not present.
    /// </summary>
    /// <param name="metric">The metric name configured in <see cref="Core.RollupSchema{T}"/>.</param>
    public double this[string metric] =>
        Metrics.TryGetValue(metric, out var value) ? value : 0;

    /// <summary>
    /// Convenience accessor for the <c>count</c> metric.
    /// </summary>
    public double Count => this["count"];

    /// <summary>
    /// Convenience accessor for the <c>signal</c> metric (typically a conditional counter).
    /// </summary>
    public double SignalCount => this["signal"];
}
