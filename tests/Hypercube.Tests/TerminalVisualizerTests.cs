using Hypercube.AI;
using Hypercube.Core;
using Hypercube.Models;
using Hypercube.Visualization;
using Xunit;

namespace Hypercube.Tests;

public sealed class TerminalVisualizerTests
{
    [Fact]
    public void RenderSparkline_ProducesBlockCharacters()
    {
        var sparkline = TerminalVisualizer.RenderSparkline([1, 3, 5, 2, 8]);

        Assert.Equal(5, sparkline.Length);
        Assert.All(sparkline, ch => Assert.Contains(ch, " ▂▃▄▅▆▇█"));
        Assert.True(sparkline[^1] >= sparkline[0]);
    }

    [Fact]
    public void RenderSparkline_ReturnsEmptyForNoData()
    {
        Assert.Equal(string.Empty, TerminalVisualizer.RenderSparkline([]));
    }

    [Fact]
    public void RenderHistogram_IncludesBucketsAndBars()
    {
        var output = TerminalVisualizer.RenderHistogram(
            "latency",
            new Dictionary<string, double>
            {
                ["p50"] = 42.1,
                ["p95"] = 145.0,
                ["p99"] = 320.12
            },
            maxBarWidth: 20);

        Assert.Contains("Distribution Shape: latency", output);
        Assert.Contains("p50", output);
        Assert.Contains("█", output);
        Assert.Contains("320.12", output);
    }

    [Fact]
    public void ExtractDigestBuckets_ReadsPercentileMetrics()
    {
        var row = new SummaryRow("region", "east", new Dictionary<string, double>
        {
            [MetricNameHelper.Mean("latency")] = 40,
            [MetricNameHelper.Percentile("latency", 50)] = 12,
            [MetricNameHelper.Percentile("latency", 95)] = 55,
            [MetricNameHelper.Percentile("latency", 99)] = 120
        });

        var buckets = TerminalVisualizer.ExtractDigestBuckets(row, "latency");

        Assert.Equal(40, buckets["mean"]);
        Assert.Equal(12, buckets["p50"]);
        Assert.Equal(55, buckets["p95"]);
        Assert.True(buckets["p75"] > buckets["p50"]);
    }

    [Fact]
    public void SnapshotTerminalReport_IncludesTrendSparklineAndInsights()
    {
        var history = new[]
        {
            Snapshot(0, ("channel", "sms", 10)),
            Snapshot(1, ("channel", "sms", 20)),
            Snapshot(2, ("channel", "sms", 35))
        };

        var current = history[^1];
        var engine = new RuleBasedLocalAiEngine();
        var analysis = engine.AnalyzeSummary(current, history[^2]);

        var report = SnapshotTerminalReport.Render(
            current,
            analysis,
            history,
            digestMetric: null);

        Assert.Contains("HYPERCUBE SNAPSHOT REPORT", report);
        Assert.Contains("Trend:", report);
        Assert.Contains("Insight Highlights", report);
    }

    [Fact]
    public void ExtractCellSeries_UsesDimensionAndKeyWithoutSplitting()
    {
        var history = new[]
        {
            Snapshot(0, ("channel", "sms", 10)),
            Snapshot(1, ("channel", "sms", 25))
        };

        var series = TerminalVisualizer.ExtractCellSeries("channel", "sms", history);

        Assert.Equal([10, 25], series);
    }

    [Fact]
    public void BuildHistorySeriesIndex_ReusesPreallocatedBuffers()
    {
        var history = new[]
        {
            Snapshot(0, ("channel", "sms", 10), ("channel", "email", 4)),
            Snapshot(1, ("channel", "sms", 20), ("channel", "email", 8))
        };

        var index = TerminalVisualizer.BuildHistorySeriesIndex(history);

        Assert.True(index.TryGetSeries("channel", "sms", out var sms));
        Assert.Equal([10, 20], sms.ToArray());
        Assert.True(index.TryGetSeries("channel", "email", out var email));
        Assert.Equal([4, 8], email.ToArray());
    }

    [Fact]
    public void RuleBasedEngine_RenderTerminalReport_DelegatesToSnapshotReport()
    {
        var snapshot = Snapshot(0, ("region", "east", 10));
        var engine = new RuleBasedLocalAiEngine();
        var analysis = engine.AnalyzeSummary(snapshot);

        var report = engine.RenderTerminalReport(snapshot, analysis);

        Assert.Contains("region", report);
        Assert.Contains("Insight Highlights", report);
    }

    private static SummarySnapshot Snapshot(int minute, params (string Dimension, string Key, double Value)[] cells)
    {
        var rows = cells.Select(cell =>
            new SummaryRow(cell.Dimension, cell.Key, new Dictionary<string, double> { ["count"] = cell.Value }));

        return new SummarySnapshot(DateTimeOffset.UtcNow.AddMinutes(minute), [.. rows]);
    }
}
