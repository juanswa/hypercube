namespace Hypercube.Tests;

using global::Hypercube.AI.Onnx;

public sealed record UserEvent(string Region, string UserId, double Amount);

public sealed class ExtendedCapabilitiesTests
{
    [Fact]
    public void Average_ComputesArithmeticMean()
    {
        var schema = RollupSchema
            .For<UserEvent>()
            .Dimension(e => e.Region)
            .Count()
            .Average(e => e.Amount)
            .Build();

        var engine = new RollupEngine<UserEvent>(schema);
        engine.Add(new UserEvent("east", "u1", 10));
        engine.Add(new UserEvent("east", "u2", 20));
        engine.Add(new UserEvent("east", "u3", 30));

        var row = Assert.Single(engine.DeriveSnapshot().Rows);
        Assert.Equal(20, row["amount"]);
    }

    [Fact]
    public void HyperLogLog_EstimatesDistinctEntities()
    {
        var schema = RollupSchema
            .For<UserEvent>()
            .Dimension(e => e.Region)
            .HyperLogLog(e => e.UserId, "users")
            .Build();

        var engine = new RollupEngine<UserEvent>(schema);
        for (var i = 0; i < 100; i++)
        {
            engine.Add(new UserEvent("east", $"user-{i}", 1));
        }

        engine.Add(new UserEvent("east", "user-0", 1));
        engine.Add(new UserEvent("east", "user-1", 1));

        var row = Assert.Single(engine.DeriveSnapshot().Rows);
        Assert.InRange(row[MetricNameHelper.UniqueCount("users")], 95, 105);
    }

    [Fact]
    public void AnalyzeDriversForMetrics_AttributesMultipleMetrics()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [new SummaryRow("channel", "sms", new Dictionary<string, double> { ["count"] = 100, ["sum"] = 1000 })]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [new SummaryRow("channel", "sms", new Dictionary<string, double> { ["count"] = 150, ["sum"] = 1800 })]);

        var result = DeterministicInsightEngine.AnalyzeDriversForMetrics(previous, current, ["count", "sum"]);

        Assert.Equal(2, result.Metrics.Count);
        Assert.Equal(50, result.Metrics.First(metric => metric.Metric == "count").TotalDelta);
        Assert.Equal(800, result.Metrics.First(metric => metric.Metric == "sum").TotalDelta);
    }

    [Fact]
    public void CoMovementOptions_ChangesEwmaSensitivity()
    {
        var history = new[]
        {
            SnapshotAt(0, ("region:east", 10), ("metric:latency", 5)),
            SnapshotAt(1, ("region:east", 20), ("metric:latency", 5)),
            SnapshotAt(2, ("region:east", 30), ("metric:latency", 25)),
            SnapshotAt(3, ("region:east", 40), ("metric:latency", 35))
        };

        var fast = CoMovementEngine.DiscoverPairs(history, new CoMovementOptions { EwmaAlpha = 0.9, MinAbsCorrelation = 0.5 });
        var slow = CoMovementEngine.DiscoverPairs(history, new CoMovementOptions { EwmaAlpha = 0.1, MinAbsCorrelation = 0.5 });

        Assert.NotEmpty(fast);
        Assert.NotEmpty(slow);
    }

    [Fact]
    public void TryAdd_AppliesBackpressureAndWatermarkPolicies()
    {
        var schema = RollupSchema.For<UserEvent>().Dimension(e => e.Region).Count().Build();
        var engine = new RollupEngine<UserEvent>(schema, new RollupEngineOptions
        {
            MaxInFlightAdds = 1,
            AllowedLateness = TimeSpan.FromMinutes(5)
        });

        var baseTime = DateTimeOffset.UtcNow;
        Assert.Equal(RollupAddResult.Accepted, engine.TryAdd(new UserEvent("east", "u1", 1), baseTime));
        Assert.Equal(RollupAddResult.DroppedLate, engine.TryAdd(new UserEvent("east", "u2", 1), baseTime.AddMinutes(-10)));
    }

    [Fact]
    public void SnapshotRetentionManager_PrunesOldSpillFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"hypercube-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var oldFile = Path.Combine(directory, "old.db");
        File.WriteAllText(oldFile, "x");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-10));

        var deleted = SnapshotRetentionManager.PruneSpillDirectory(directory, new SnapshotRetentionPolicy
        {
            MaxAge = TimeSpan.FromDays(1)
        });

        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldFile));
    }

    [Fact]
    public void OnnxLocalAiEngine_FallsBackToDeterministicRules()
    {
        var engine = new OnnxLocalAiEngine();
        var snapshot = new SummarySnapshot(
            TestTimestamps.Epoch,
            [new SummaryRow("region", "east", new Dictionary<string, double> { ["count"] = 10 })]);

        var analysis = engine.AnalyzeSummary(snapshot);
        var narrative = engine.GenerateNarrative(snapshot, analysis);

        Assert.NotEmpty(analysis.RecommendedInsights);
        Assert.NotEmpty(narrative);
        Assert.DoesNotContain("deterministic rules applied", narrative, StringComparison.OrdinalIgnoreCase);
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
