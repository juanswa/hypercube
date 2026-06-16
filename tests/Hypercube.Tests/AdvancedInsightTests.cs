namespace Hypercube.Tests;

public sealed record LatencyEvent(string Region, double LatencyMs);

public sealed class AdvancedInsightTests
{
    [Fact]
    public void PercentileDigest_EmitsMeanAndPercentiles()
    {
        var schema = RollupSchema
            .For<LatencyEvent>()
            .Dimension(e => e.Region)
            .Count()
            .PercentileDigest(e => e.LatencyMs, "latency")
            .Build();

        var engine = new RollupEngine<LatencyEvent>(schema);
        foreach (var value in new[] { 10, 12, 14, 16, 18, 100 })
        {
            engine.Add(new LatencyEvent("east", value));
        }

        var row = Assert.Single(engine.DeriveSnapshot().Rows);
        Assert.True(row["latency_mean"] > row[MetricNameHelper.Percentile("latency", 50)]);
        Assert.True(row[MetricNameHelper.Percentile("latency", 95)] >= row[MetricNameHelper.Percentile("latency", 50)]);
    }

    [Fact]
    public void DistributionShapeEngine_FlagsMisleadingMean()
    {
        var row = new SummaryRow("region", "east", new Dictionary<string, double>
        {
            [MetricNameHelper.Mean("latency")] = 40,
            [MetricNameHelper.Percentile("latency", 50)] = 12,
            [MetricNameHelper.Percentile("latency", 95)] = 55,
            [MetricNameHelper.Percentile("latency", 99)] = 120
        });

        var callout = DistributionShapeEngine.AnalyzeRow(row, "latency");

        Assert.NotNull(callout);
        Assert.Equal(DistributionSkew.RightSkewed, callout!.Skew);
        Assert.True(callout.MeanMisleading);
        Assert.Contains("right-skewed", callout.Summary);
        Assert.Contains("alerting", callout.Summary);
    }

    [Fact]
    public void CoMovementEngine_DetectsLeadLagRelationship()
    {
        var history = new[]
        {
            SnapshotAt(0, ("region:east", 10), ("metric:latency", 5)),
            SnapshotAt(1, ("region:east", 20), ("metric:latency", 5)),
            SnapshotAt(2, ("region:east", 30), ("metric:latency", 25)),
            SnapshotAt(3, ("region:east", 40), ("metric:latency", 35))
        };

        var pairs = CoMovementEngine.DiscoverPairs(history, maxLag: 2, topN: 5, minAbsCorrelation: 0.5);

        Assert.Contains(pairs, pair =>
            pair.Correlation > 0.5 &&
            (pair.CellId == "region:east" || pair.OtherCellId == "region:east"));
    }

    [Fact]
    public void CellExplainer_BuildsFullContext()
    {
        var previous = SnapshotAt(0, ("channel:sms", 100), ("channel:email", 100));
        var current = SnapshotAt(5, ("channel:sms", 500), ("channel:email", 90));
        current = current with
        {
            Rows = [.. current.Rows.Select(row =>
                row.Key == "sms"
                    ? row with
                    {
                        Metrics = new Dictionary<string, double>(row.Metrics)
                        {
                            [MetricNameHelper.Mean("latency")] = 40,
                            [MetricNameHelper.Percentile("latency", 50)] = 12,
                            [MetricNameHelper.Percentile("latency", 95)] = 55,
                            [MetricNameHelper.Percentile("latency", 99)] = 120
                        }
                    }
                    : row)]
        };

        var explanation = CellExplainer.Explain(
            "channel:sms",
            current,
            previous,
            history: [previous, current],
            distributionMetric: "latency");

        Assert.Equal("channel:sms", explanation.CellId);
        Assert.Equal(500, explanation.PrimaryValue);
        Assert.True(explanation.ShareOfDimension > 0.5);
        Assert.NotNull(explanation.DistributionShape);
        Assert.NotEmpty(explanation.NarrativeBullets);
    }

    private static SummarySnapshot SnapshotAt(int minutes, params (string CellId, double Value)[] cells)
    {
        var rows = cells.Select(cell =>
        {
            var split = cell.CellId.Split(':', 2);
            return new SummaryRow(split[0], split[1], new Dictionary<string, double> { ["count"] = cell.Value });
        });

        return new SummarySnapshot(TestTimestamps.AtMinutes(minutes), [.. rows]);
    }
}
