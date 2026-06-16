using System.Globalization;
using System.Text;

namespace Hypercube.Visualization;

/// <summary>
/// ASCII terminal graphics for rollup metrics, trends, and distribution shapes.
/// </summary>
public static class TerminalVisualizer
{
  // ASCII-only blocks render reliably across Windows and Unix terminals.
    private static readonly char[] SparkBlocks = [' ', '.', ':', '-', '=', '+', '*', '#', '@'];

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
        int maxBarWidth = 40,
        bool compact = false)
    {
        if (buckets.Count == 0)
        {
            return string.Empty;
        }

        var maxVal = buckets.Values.Max();
        var lines = new List<string>();
        if (!compact)
        {
            lines.Add(string.Empty);
            lines.Add($"Distribution Shape: {metricName}");
            lines.Add(new string('-', maxBarWidth + 20));
        }

        var labelWidth = compact ? 5 : 8;
        foreach (var (label, value) in buckets)
        {
            var barLength = maxVal > 0
                ? (int)Math.Round(value / maxVal * maxBarWidth)
                : 0;
            var bar = new string('#', barLength);
            var valueText = value.ToString(compact ? "0.##" : "F2", CultureInfo.InvariantCulture);
            lines.Add($"{label.PadRight(labelWidth)} {bar} [{valueText}]");
        }

        if (!compact)
        {
            lines.Add(new string('-', maxBarWidth + 20));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Shortens a dimension key for terminal display by removing window suffixes.
    /// </summary>
    public static string FormatDimensionKey(string key)
    {
        var separator = key.IndexOf('@');
        return separator > 0 ? key[..separator] : key;
    }

    /// <summary>
    /// Shortens a canonical cell id (<c>dimension:key</c>) for terminal display.
    /// </summary>
    public static string FormatCellId(string cellId)
    {
        var separator = cellId.IndexOf(':');
        if (separator <= 0 || separator >= cellId.Length - 1)
        {
            return cellId;
        }

        return $"{cellId[..separator]}/{FormatDimensionKey(cellId[(separator + 1)..])}";
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

