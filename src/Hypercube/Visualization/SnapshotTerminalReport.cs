using System.Text;
using Hypercube.AI;

namespace Hypercube.Visualization;

/// <summary>
/// Renders rollup snapshots and insight analysis as terminal-friendly ASCII reports.
/// </summary>
public static class SnapshotTerminalReport
{
    /// <summary>
    /// Builds a multi-section terminal report for a snapshot and optional analysis context.
    /// </summary>
    /// <param name="snapshot">Current rollup snapshot.</param>
    /// <param name="analysis">Optional AI analysis output.</param>
    /// <param name="history">Optional prior snapshots for per-cell sparklines.</param>
    /// <param name="digestMetric">Optional digest metric name for distribution histograms.</param>
    public static string Render(
        SummarySnapshot snapshot,
        AiAnalysisResult? analysis = null,
        IReadOnlyList<SummarySnapshot>? history = null,
        string? digestMetric = null)
    {
        var report = new StringBuilder();
        report.AppendLine("=== HYPERCUBE SNAPSHOT REPORT ===");
        report.AppendLine($"Generated: {snapshot.GeneratedAt:u}");
        report.AppendLine($"Primary metric: {snapshot.PrimaryMetric}");
        report.AppendLine($"Cells: {snapshot.Rows.Count}");
        report.AppendLine();

        HistorySeriesIndex? historyIndex = history is { Count: >= 2 }
            ? TerminalVisualizer.BuildHistorySeriesIndex(history)
            : null;

        foreach (var row in snapshot.Rows.OrderByDescending(snapshot.PrimaryValue))
        {
            report.AppendLine(
                $"Cell: {row.Dimension} / {row.Key} (count: {row.Count:0.##}, primary: {snapshot.PrimaryValue(row):0.##})");

            if (historyIndex is not null &&
                historyIndex.TryGetSeries(row.Dimension, row.Key, out var series))
            {
                var sparkline = TerminalVisualizer.RenderSparkline(series);
                if (sparkline.Length > 0)
                {
                    report.AppendLine($"  Trend: {sparkline}");
                }
            }

            if (!string.IsNullOrWhiteSpace(digestMetric))
            {
                var buckets = TerminalVisualizer.ExtractDigestBuckets(row, digestMetric);
                if (buckets.Count > 0)
                {
                    report.AppendLine(
                        TerminalVisualizer.RenderHistogram($"{digestMetric} ({row.Dimension}:{row.Key})", buckets));
                }
            }
        }

        if (analysis is not null)
        {
            report.AppendLine();
            report.AppendLine("--- Insight Highlights ---");
            foreach (var insight in analysis.RecommendedInsights)
            {
                report.AppendLine($"* {insight}");
            }

            if (analysis.TopInterestingCells.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Top interesting cells:");
                foreach (var insight in analysis.TopInterestingCells.Take(5))
                {
                    report.AppendLine($"  {insight.CellId} [{insight.Kind}] score={insight.Score:0.##}");
                }
            }

            if (analysis.SimpsonSignals.Count > 0)
            {
                report.AppendLine();
                report.AppendLine($"Simpson signals: {analysis.SimpsonSignals.Count}");
            }
        }

        return report.ToString().TrimEnd();
    }

    /// <summary>
    /// Writes a rendered report to standard output.
    /// </summary>
    public static void Print(
        SummarySnapshot snapshot,
        AiAnalysisResult? analysis = null,
        IReadOnlyList<SummarySnapshot>? history = null,
        string? digestMetric = null)
    {
        Console.WriteLine(Render(snapshot, analysis, history, digestMetric));
    }
}
