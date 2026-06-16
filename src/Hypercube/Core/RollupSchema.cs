namespace Hypercube.Core;

/// <summary>
/// Immutable rollup configuration: which dimensions to slice by, which metrics to compute,
/// and which metric is considered primary for downstream analysis.
/// </summary>
/// <typeparam name="T">The event type being aggregated.</typeparam>
public sealed class RollupSchema<T>
{
    internal RollupSchema(
        IReadOnlyList<DimensionDefinition<T>> dimensions,
        IReadOnlyList<MetricDefinition<T>> metrics,
        string primaryMetric)
    {
        if (dimensions.Count == 0)
        {
            throw new InvalidOperationException("At least one dimension must be configured.");
        }

        if (metrics.Count == 0)
        {
            throw new InvalidOperationException("At least one metric must be configured.");
        }

        Dimensions = dimensions;
        Metrics = metrics;
        PrimaryMetric = primaryMetric;
        MetricValueOffsets = BuildMetricOffsets(metrics);
        MetricValueSlotCount = MetricValueOffsets.Length == 0
            ? 0
            : MetricValueOffsets[^1] + metrics[^1].SlotCount;
    }

    /// <summary>All dimensions that each ingested item fans out across.</summary>
    public IReadOnlyList<DimensionDefinition<T>> Dimensions { get; }

    /// <summary>All metrics computed per dimension key.</summary>
    public IReadOnlyList<MetricDefinition<T>> Metrics { get; }

    /// <summary>
    /// Metric name used as the primary measure in insight and driver analysis.
    /// </summary>
    public string PrimaryMetric { get; }

    /// <summary>Offset into <see cref="CellAggregateState.MetricValues"/> for each metric index.</summary>
    internal int[] MetricValueOffsets { get; }

    /// <summary>Total number of scalar slots required for all configured metrics.</summary>
    internal int MetricValueSlotCount { get; }

    private static int[] BuildMetricOffsets(IReadOnlyList<MetricDefinition<T>> metrics)
    {
        var offsets = new int[metrics.Count];
        var cursor = 0;
        for (var i = 0; i < metrics.Count; i++)
        {
            offsets[i] = cursor;
            cursor += metrics[i].SlotCount;
        }

        return offsets;
    }
}
