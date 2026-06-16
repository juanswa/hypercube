using Hypercube.Models;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Hypercube.State.Parquet;

/// <summary>
/// Exports rollup snapshots to columnar Parquet for OLAP-friendly metric scans.
/// </summary>
public static class ParquetSnapshotExporter
{
    /// <summary>
    /// Writes a snapshot in long format: dimension, key, metric, value, generated_at.
    /// </summary>
    public static async Task ExportAsync(SummarySnapshot snapshot, string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var dimensions = new List<string>();
        var keys = new List<string>();
        var metrics = new List<string>();
        var values = new List<double>();
        var generatedAt = new List<DateTime>();

        foreach (var row in snapshot.Rows)
        {
            foreach (var (metric, value) in row.Metrics)
            {
                dimensions.Add(row.Dimension);
                keys.Add(row.Key);
                metrics.Add(metric);
                values.Add(value);
                generatedAt.Add(snapshot.GeneratedAt.UtcDateTime);
            }
        }

        var schema = new ParquetSchema(
            new DataField<string>("dimension"),
            new DataField<string>("key"),
            new DataField<string>("metric"),
            new DataField<double>("value"),
            new DataField<DateTime>("generated_at"));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
        await using var stream = File.Create(filePath);
        using var writer = await ParquetWriter.CreateAsync(schema, stream, cancellationToken: cancellationToken);
        using var groupWriter = writer.CreateRowGroup();

        await groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[0], dimensions.ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[1], keys.ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[2], metrics.ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[3], values.ToArray()), cancellationToken);
        await groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[4], generatedAt.ToArray()), cancellationToken);
    }

    /// <summary>Synchronous wrapper for <see cref="ExportAsync"/>.</summary>
    public static void Export(SummarySnapshot snapshot, string filePath) =>
        ExportAsync(snapshot, filePath).GetAwaiter().GetResult();
}
