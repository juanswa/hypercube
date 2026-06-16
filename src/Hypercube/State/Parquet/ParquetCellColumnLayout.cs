using Parquet.Schema;

namespace Hypercube.State.Parquet;

internal static class ParquetCellColumns
{
    public const string KeyColumn = "key";
    public const string LastAccessColumn = "last_access_ticks";
    public const string ScalarColumnPrefix = "v_";
    public const string SketchColumnPrefix = "sk_";
}

/// <summary>
/// Maps a <see cref="RollupSchema{T}"/> to columnar Parquet fields for cell spill storage.
/// </summary>
internal sealed class ParquetCellColumnLayout<T>
{
    public ParquetCellColumnLayout(RollupSchema<T> schema)
    {
        Schema = schema;
        ScalarSlotCount = schema.MetricValueSlotCount;
        SketchMetricIndices = [.. schema.Metrics
            .Select((metric, index) => (metric, index))
            .Where(static pair => pair.metric.Kind is AggregationKind.TDigest or AggregationKind.HyperLogLog)
            .Select(static pair => pair.index)];

        var fields = new List<Field>
        {
            new DataField<string>(ParquetCellColumns.KeyColumn),
            new DataField<long>(ParquetCellColumns.LastAccessColumn)
        };

        ScalarFields = new DataField<double>[ScalarSlotCount];
        for (var slot = 0; slot < ScalarSlotCount; slot++)
        {
            ScalarFields[slot] = new DataField<double>($"{ParquetCellColumns.ScalarColumnPrefix}{slot}");
            fields.Add(ScalarFields[slot]);
        }

        var sketchFields = new Dictionary<int, DataField<byte[]>>();
        foreach (var metricIndex in SketchMetricIndices)
        {
            var field = new DataField<byte[]>($"{ParquetCellColumns.SketchColumnPrefix}{metricIndex}");
            sketchFields[metricIndex] = field;
            fields.Add(field);
        }

        SketchFields = sketchFields;

        ParquetSchema = new ParquetSchema(fields);
    }

    public RollupSchema<T> Schema { get; }

    public ParquetSchema ParquetSchema { get; }

    public int ScalarSlotCount { get; }

    public DataField<double>[] ScalarFields { get; }

    public IReadOnlyList<int> SketchMetricIndices { get; }

    public IReadOnlyDictionary<int, DataField<byte[]>> SketchFields { get; }

    /// <summary>
    /// Resolves which metric indices are required to satisfy a projection.
    /// <c>null</c> means all metrics.
    /// </summary>
    public HashSet<int> ResolveMetricIndices(IReadOnlySet<string>? metricProjection)
    {
        if (metricProjection is null)
        {
            return [.. Enumerable.Range(0, Schema.Metrics.Count)];
        }

        var indices = new HashSet<int>();
        for (var i = 0; i < Schema.Metrics.Count; i++)
        {
            var metric = Schema.Metrics[i];
            if (metricProjection.Contains(metric.Name))
            {
                indices.Add(i);
                continue;
            }

            switch (metric.Kind)
            {
                case AggregationKind.TDigest:
                    if (metricProjection.Contains(MetricNameHelper.Mean(metric.Name)) ||
                        metricProjection.Contains(MetricNameHelper.Percentile(metric.Name, 50)) ||
                        metricProjection.Contains(MetricNameHelper.Percentile(metric.Name, 95)) ||
                        metricProjection.Contains(MetricNameHelper.Percentile(metric.Name, 99)))
                    {
                        indices.Add(i);
                    }

                    break;

                case AggregationKind.HyperLogLog:
                    if (metricProjection.Contains(MetricNameHelper.UniqueCount(metric.Name)))
                    {
                        indices.Add(i);
                    }

                    break;
            }
        }

        return indices;
    }

    /// <summary>
    /// Returns Parquet data fields required for a metric projection (always includes key).
    /// </summary>
    public IReadOnlyList<DataField> ResolveReadFields(IReadOnlySet<string>? metricProjection, bool includeLastAccess)
    {
        var metricIndices = ResolveMetricIndices(metricProjection);
        var fields = new List<DataField>(2 + ScalarSlotCount + SketchFields.Count)
        {
            (DataField)ParquetSchema.DataFields.First(static field => field.Name == ParquetCellColumns.KeyColumn)
        };

        if (includeLastAccess)
        {
            fields.Add((DataField)ParquetSchema.DataFields.First(static field => field.Name == ParquetCellColumns.LastAccessColumn));
        }

        for (var slot = 0; slot < ScalarSlotCount; slot++)
        {
            if (metricIndices.Any(index => SlotBelongsToMetric(index, slot)))
            {
                fields.Add(ScalarFields[slot]);
            }
        }

        foreach (var metricIndex in SketchMetricIndices)
        {
            if (metricIndices.Contains(metricIndex))
            {
                fields.Add(SketchFields[metricIndex]);
            }
        }

        return fields;
    }

    public static bool IsLegacyPayloadSchema(ParquetSchema schema) =>
        schema.DataFields.Any(static field => field.Name == "payload");

    private bool SlotBelongsToMetric(int metricIndex, int slot)
    {
        var offset = Schema.MetricValueOffsets[metricIndex];
        var slotCount = Schema.Metrics[metricIndex].SlotCount;
        return slot >= offset && slot < offset + slotCount;
    }
}
