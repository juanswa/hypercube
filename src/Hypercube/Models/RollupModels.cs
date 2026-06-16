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

/// <summary>
/// A point-in-time view of all rollup cells and their metric values.
/// <para>
/// <see cref="RowsByDimension"/> and <see cref="TryGetRow(string, out SummaryRow)"/> indexes are built lazily from
/// <see cref="Rows"/> on first access and remain valid for the lifetime of this instance.
/// <see cref="Core.RollupEngine{T}.DeriveSnapshot"/> always produces a fresh snapshot; use
/// <c>with</c> to derive variants rather than mutating <see cref="Rows"/> in place.
/// </para>
/// </summary>
/// <param name="GeneratedAt">UTC timestamp when the snapshot was materialized.</param>
/// <param name="Rows">All dimension/key cells and their aggregated metrics.</param>
/// <param name="PrimaryMetric">
/// The metric name used as the main measure for insight and driver analysis.
/// Defaults to <c>count</c>.
/// </param>
public sealed record SummarySnapshot(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<SummaryRow> Rows,
    string PrimaryMetric = "count")
{
    private Dictionary<string, IReadOnlyList<SummaryRow>>? _rowsByDimension;
    private Dictionary<string, SummaryRow>? _rowByCellId;

    /// <summary>
    /// Rows grouped by dimension name for O(1) sibling lookups.
    /// Built lazily on first access.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SummaryRow>> RowsByDimension
    {
        get
        {
            EnsureIndexes();
            return _rowsByDimension!;
        }
    }

    /// <summary>
    /// Tries to resolve a row by canonical <c>dimension:key</c> identifier.
    /// </summary>
    public bool TryGetRow(string cellId, out SummaryRow row)
    {
        EnsureIndexes();
        return _rowByCellId!.TryGetValue(cellId, out row!);
    }

    /// <summary>
    /// Tries to resolve a row by dimension and key.
    /// </summary>
    public bool TryGetRow(string dimension, string key, out SummaryRow row) =>
        TryGetRow(FormatCellId(dimension, key), out row);

    /// <summary>
    /// Gets the configured primary metric value for a row.
    /// </summary>
    /// <param name="row">The summary row to read.</param>
    public double PrimaryValue(SummaryRow row) => row[PrimaryMetric];

    private void EnsureIndexes()
    {
        if (_rowsByDimension is not null)
        {
            return;
        }

        var byDimension = new Dictionary<string, List<SummaryRow>>(StringComparer.OrdinalIgnoreCase);
        var byCellId = new Dictionary<string, SummaryRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in Rows)
        {
            if (!byDimension.TryGetValue(row.Dimension, out var siblings))
            {
                siblings = [];
                byDimension[row.Dimension] = siblings;
            }

            siblings.Add(row);
            byCellId[FormatCellId(row.Dimension, row.Key)] = row;
        }

        _rowsByDimension = byDimension.ToDictionary(
            static pair => pair.Key,
            static pair => (IReadOnlyList<SummaryRow>)pair.Value,
            StringComparer.OrdinalIgnoreCase);
        _rowByCellId = byCellId;
    }

    private static string FormatCellId(string dimension, string key) => $"{dimension}:{key}";
}
