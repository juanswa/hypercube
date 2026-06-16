using Hypercube.Core.Sketches;

namespace Hypercube.Core;

/// <summary>
/// Applies schema metrics to <see cref="CellAggregateState"/> and materializes snapshot values.
/// </summary>
internal static class CellAggregator<T>
{
    public static CellAggregateState CreateState(RollupSchema<T> schema)
    {
        var metricCount = schema.Metrics.Count;
        var activeSketches = new object?[metricCount];
        for (var i = 0; i < metricCount; i++)
        {
            activeSketches[i] = schema.Metrics[i].Kind switch
            {
                AggregationKind.TDigest => new TDigestState(),
                AggregationKind.HyperLogLog => new HyperLogLogState(),
                _ => null
            };
        }

        return new CellAggregateState
        {
            MetricValues = CreateInitialValues(schema),
            SketchStates = new byte[metricCount][],
            ActiveSketches = activeSketches
        };
    }

    public static void Apply(T item, RollupSchema<T> schema, CellAggregateState state)
    {
        var metrics = schema.Metrics;
        lock (state.Sync)
        {
            for (var i = 0; i < metrics.Count; i++)
            {
                ApplyMetric(item, metrics[i], schema, state, i);
            }
        }
    }

    public static CellAggregateState SnapshotForPersistence(CellAggregateState state) =>
        CellAggregateStateSerializer.Snapshot(state);

    public static Dictionary<string, double> ToValues(
        RollupSchema<T> schema,
        CellAggregateState state,
        IReadOnlySet<string>? metricProjection = null) =>
        ToValuesCore(schema, state, metricProjection);

    private static Dictionary<string, double> ToValuesCore(
        RollupSchema<T> schema,
        CellAggregateState state,
        IReadOnlySet<string>? metricProjection)
    {
        var metrics = schema.Metrics;
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        lock (state.Sync)
        {
            for (var i = 0; i < metrics.Count; i++)
            {
                var metric = metrics[i];
                if (metricProjection is not null && !MetricIncluded(metricProjection, metric))
                {
                    continue;
                }

                var offset = schema.MetricValueOffsets[i];
                switch (metric.Kind)
                {
                    case AggregationKind.TDigest:
                    {
                        var digest = GetOrCreateDigest(state, i);
                        if (IncludeMetricName(metricProjection, MetricNameHelper.Mean(metric.Name)))
                        {
                            values[MetricNameHelper.Mean(metric.Name)] = digest.Mean;
                        }

                        if (IncludeMetricName(metricProjection, MetricNameHelper.Percentile(metric.Name, 50)))
                        {
                            values[MetricNameHelper.Percentile(metric.Name, 50)] = digest.Quantile(0.50);
                        }

                        if (IncludeMetricName(metricProjection, MetricNameHelper.Percentile(metric.Name, 95)))
                        {
                            values[MetricNameHelper.Percentile(metric.Name, 95)] = digest.Quantile(0.95);
                        }

                        if (IncludeMetricName(metricProjection, MetricNameHelper.Percentile(metric.Name, 99)))
                        {
                            values[MetricNameHelper.Percentile(metric.Name, 99)] = digest.Quantile(0.99);
                        }

                        break;
                    }

                    case AggregationKind.HyperLogLog:
                        values[MetricNameHelper.UniqueCount(metric.Name)] = GetOrCreateHyperLogLog(state, i).Estimate();
                        break;

                    case AggregationKind.Average:
                    {
                        var count = state.MetricValues[offset + 1];
                        values[metric.Name] = count == 0 ? 0 : state.MetricValues[offset] / count;
                        break;
                    }

                    default:
                        values[metric.Name] = ReadValue(metric.Kind, state.MetricValues[offset]);
                        break;
                }
            }
        }

        return values;
    }

    private static double[] CreateInitialValues(RollupSchema<T> schema)
    {
        var values = new double[schema.MetricValueSlotCount];
        for (var i = 0; i < schema.Metrics.Count; i++)
        {
            var metric = schema.Metrics[i];
            var offset = schema.MetricValueOffsets[i];
            values[offset] = metric.Kind switch
            {
                AggregationKind.Min => double.PositiveInfinity,
                AggregationKind.Max => double.NegativeInfinity,
                _ => 0
            };
        }

        return values;
    }

    private static void ApplyMetric(
        T item,
        MetricDefinition<T> metric,
        RollupSchema<T> schema,
        CellAggregateState state,
        int index)
    {
        var offset = schema.MetricValueOffsets[index];
        switch (metric.Kind)
        {
            case AggregationKind.Count:
                state.MetricValues[offset]++;
                break;

            case AggregationKind.CountWhen:
                if (metric.Predicate!(item))
                {
                    state.MetricValues[offset]++;
                }

                break;

            case AggregationKind.Sum:
                state.MetricValues[offset] += metric.ValueSelector!(item);
                break;

            case AggregationKind.Min:
                state.MetricValues[offset] = Math.Min(state.MetricValues[offset], metric.ValueSelector!(item));
                break;

            case AggregationKind.Max:
                state.MetricValues[offset] = Math.Max(state.MetricValues[offset], metric.ValueSelector!(item));
                break;

            case AggregationKind.Average:
                state.MetricValues[offset] += metric.ValueSelector!(item);
                state.MetricValues[offset + 1]++;
                break;

            case AggregationKind.TDigest:
                GetOrCreateDigest(state, index).Add(metric.ValueSelector!(item));
                break;

            case AggregationKind.HyperLogLog:
                GetOrCreateHyperLogLog(state, index).Add(Sanitizers.Normalize(metric.StringSelector!(item)));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(metric), metric.Kind, "Unsupported aggregation kind.");
        }
    }

    private static TDigestState GetOrCreateDigest(CellAggregateState state, int index)
    {
        EnsureActiveSketchArray(state, index);
        if (state.ActiveSketches[index] is TDigestState existing)
        {
            return existing;
        }

        var data = state.SketchStates.Length > index ? state.SketchStates[index] : [];
        var digest = TDigestState.Deserialize(data ?? []);
        state.ActiveSketches[index] = digest;
        return digest;
    }

    private static HyperLogLogState GetOrCreateHyperLogLog(CellAggregateState state, int index)
    {
        EnsureActiveSketchArray(state, index);
        if (state.ActiveSketches[index] is HyperLogLogState existing)
        {
            return existing;
        }

        var data = state.SketchStates.Length > index ? state.SketchStates[index] : [];
        var sketch = HyperLogLogState.Deserialize(data ?? []);
        state.ActiveSketches[index] = sketch;
        return sketch;
    }

    private static void EnsureActiveSketchArray(CellAggregateState state, int index)
    {
        if (state.ActiveSketches is null || state.ActiveSketches.Length <= index)
        {
            var newLength = Math.Max(state.ActiveSketches?.Length ?? 0, index + 1);
            var expanded = new object?[newLength];
            state.ActiveSketches?.CopyTo(expanded, 0);
            state.ActiveSketches = expanded;
        }
    }

    private static double ReadValue(AggregationKind kind, double raw) => kind switch
    {
        AggregationKind.Min when double.IsPositiveInfinity(raw) => 0,
        AggregationKind.Max when double.IsNegativeInfinity(raw) => 0,
        _ => raw
    };

    private static bool MetricIncluded(IReadOnlySet<string> metricProjection, MetricDefinition<T> metric)
    {
        if (metricProjection.Contains(metric.Name))
        {
            return true;
        }

        return metric.Kind switch
        {
            AggregationKind.TDigest =>
                metricProjection.Contains(MetricNameHelper.Mean(metric.Name)) ||
                metricProjection.Contains(MetricNameHelper.Percentile(metric.Name, 50)) ||
                metricProjection.Contains(MetricNameHelper.Percentile(metric.Name, 95)) ||
                metricProjection.Contains(MetricNameHelper.Percentile(metric.Name, 99)),
            AggregationKind.HyperLogLog =>
                metricProjection.Contains(MetricNameHelper.UniqueCount(metric.Name)),
            _ => false
        };
    }

    private static bool IncludeMetricName(IReadOnlySet<string>? metricProjection, string metricName) =>
        metricProjection is null || metricProjection.Contains(metricName);
}
