using System.Globalization;
using System.Text;

namespace Hypercube.Visualization;

/// <summary>
/// ASCII terminal graphics for rollup metrics, trends, and distribution shapes.
/// </summary>
public static class TerminalVisualizer
{
    private static readonly char[] SparkBlocks = [' ', ' ', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    /// <summary>
    /// Renders a single-line sparkline from a numeric series.
    /// </summary>
    public static string RenderSparkline(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return string.Empty;
        }

        var min = values[0];
        var max = values[0];
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] < min)
            {
                min = values[i];
            }

            if (values[i] > max)
            {
                max = values[i];
            }
        }

        var range = max - min;
        var sb = new StringBuilder(values.Length);
        for (var i = 0; i < values.Length; i++)
        {
            var blockIndex = range > 0
                ? (int)Math.Floor((values[i] - min) / range * (SparkBlocks.Length - 1))
                : 4;
            sb.Append(SparkBlocks[Math.Clamp(blockIndex, 0, SparkBlocks.Length - 1)]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders a single-line sparkline from a materialized array.
    /// </summary>
    public static string RenderSparkline(double[] values) =>
        RenderSparkline(values.AsSpan());

    /// <summary>
    /// Renders a labeled horizontal bar chart for percentile or bucket values.
    /// </summary>
    public static string RenderHistogram(
        string metricName,
        IReadOnlyDictionary<string, double> buckets,
        int maxBarWidth = 40)
    {
        if (buckets.Count == 0)
        {
            return string.Empty;
        }

        var maxVal = buckets.Values.Max();
        var lines = new List<string>
        {
            string.Empty,
            $"Distribution Shape: {metricName}",
            new string('-', maxBarWidth + 20)
        };

        foreach (var (label, value) in buckets)
        {
            var barLength = maxVal > 0
                ? (int)Math.Round(value / maxVal * maxBarWidth)
                : 0;
            var bar = new string('█', barLength);
            lines.Add($"{label.PadRight(8)} | {bar.PadRight(maxBarWidth)} [{value.ToString("F2", CultureInfo.InvariantCulture)}]");
        }

        lines.Add(new string('-', maxBarWidth + 20));
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Builds histogram buckets from digest-derived metrics on a summary row.
    /// </summary>
    public static IReadOnlyDictionary<string, double> ExtractDigestBuckets(SummaryRow row, string digestMetric)
    {
        var meanKey = MetricNameHelper.Mean(digestMetric);
        if (!row.Metrics.ContainsKey(meanKey))
        {
            return new Dictionary<string, double>();
        }

        var p50Key = MetricNameHelper.Percentile(digestMetric, 50);
        var p95Key = MetricNameHelper.Percentile(digestMetric, 95);
        var p99Key = MetricNameHelper.Percentile(digestMetric, 99);

        return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["mean"] = row[meanKey],
            ["p50"] = row[p50Key],
            ["p75"] = InterpolatePercentile(row, digestMetric, 50, 95, 75, p50Key, p95Key),
            ["p95"] = row[p95Key],
            ["p99"] = row[p99Key]
        };
    }

    /// <summary>
    /// Indexes primary-metric series for every cell across snapshot history in a single pass.
    /// </summary>
    public static HistorySeriesIndex BuildHistorySeriesIndex(IReadOnlyList<SummarySnapshot> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return new HistorySeriesIndex(history);
    }

    /// <summary>
    /// Extracts a primary-metric time series for a cell across snapshot history.
    /// </summary>
    public static double[] ExtractCellSeries(
        string dimension,
        string key,
        IReadOnlyList<SummarySnapshot> history)
    {
        var series = new double[history.Count];
        FillCellSeries(dimension, key, history, series);
        return series;
    }

    /// <summary>
    /// Extracts a primary-metric time series for a cell across snapshot history.
    /// </summary>
    public static double[] ExtractCellSeries(string cellId, IReadOnlyList<SummarySnapshot> history)
    {
        if (!TryParseCellId(cellId, out var dimension, out var key))
        {
            return new double[history.Count];
        }

        return ExtractCellSeries(dimension, key, history);
    }

    /// <summary>
    /// Fills a pre-allocated buffer with primary-metric values for one cell across history.
    /// </summary>
    public static void FillCellSeries(
        string dimension,
        string key,
        IReadOnlyList<SummarySnapshot> history,
        Span<double> destination)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (destination.Length < history.Count)
        {
            throw new ArgumentException("Destination span must be at least as large as history.", nameof(destination));
        }

        for (var i = 0; i < history.Count; i++)
        {
            var snapshot = history[i];
            destination[i] = TryGetPrimaryValue(snapshot, dimension, key, out var value) ? value : 0;
        }
    }

    private static bool TryGetPrimaryValue(
        SummarySnapshot snapshot,
        string dimension,
        string key,
        out double value)
    {
        if (snapshot.TryGetRow(dimension, key, out var row))
        {
            value = snapshot.PrimaryValue(row);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParseCellId(string cellId, out string dimension, out string key)
    {
        var separator = cellId.IndexOf(':');
        if (separator <= 0 || separator >= cellId.Length - 1)
        {
            dimension = string.Empty;
            key = string.Empty;
            return false;
        }

        dimension = cellId[..separator];
        key = cellId[(separator + 1)..];
        return true;
    }

    private static double InterpolatePercentile(
        SummaryRow row,
        string digestMetric,
        int lowerPercentile,
        int upperPercentile,
        int targetPercentile,
        string? lowerKey = null,
        string? upperKey = null)
    {
        var lower = row[lowerKey ?? MetricNameHelper.Percentile(digestMetric, lowerPercentile)];
        var upper = row[upperKey ?? MetricNameHelper.Percentile(digestMetric, upperPercentile)];
        if (upperPercentile == lowerPercentile)
        {
            return lower;
        }

        var weight = (targetPercentile - lowerPercentile) / (double)(upperPercentile - lowerPercentile);
        return lower + ((upper - lower) * weight);
    }
}

/// <summary>
/// Pre-indexed per-cell history buffers for low-allocation terminal rendering.
/// </summary>
public sealed class HistorySeriesIndex
{
    private readonly Dictionary<CellKey, double[]> _seriesByCell;

    internal HistorySeriesIndex(IReadOnlyList<SummarySnapshot> history)
    {
        _seriesByCell = new Dictionary<CellKey, double[]>(CellKeyComparer.Instance);
        for (var i = 0; i < history.Count; i++)
        {
            var snapshot = history[i];
            foreach (var row in snapshot.Rows)
            {
                var cellKey = new CellKey(row.Dimension, row.Key);
                if (!_seriesByCell.TryGetValue(cellKey, out var series))
                {
                    series = new double[history.Count];
                    _seriesByCell[cellKey] = series;
                }

                series[i] = snapshot.PrimaryValue(row);
            }
        }
    }

    /// <summary>
    /// Returns a pre-built series for the given cell when present in history.
    /// </summary>
    public bool TryGetSeries(string dimension, string key, out ReadOnlySpan<double> series)
    {
        if (_seriesByCell.TryGetValue(new CellKey(dimension, key), out var values))
        {
            series = values;
            return true;
        }

        series = ReadOnlySpan<double>.Empty;
        return false;
    }

    private readonly record struct CellKey(string Dimension, string Key);

    private sealed class CellKeyComparer : IEqualityComparer<CellKey>
    {
        public static CellKeyComparer Instance { get; } = new();

        public bool Equals(CellKey x, CellKey y) =>
            x.Dimension.Equals(y.Dimension, StringComparison.OrdinalIgnoreCase) &&
            x.Key.Equals(y.Key, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode(CellKey obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Dimension),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key));
    }
}
