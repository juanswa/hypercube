namespace Hypercube.Tests;

public sealed record WindowedEvent(string Region, DateTimeOffset Timestamp, double Amount);

public sealed class EngineV11Tests
{
    [Fact]
    public void WindowManager_QualifiesTumblingKeys()
    {
        var windowing = new WindowConfiguration
        {
            Strategy = WindowStrategy.Tumbling,
            WindowSize = TimeSpan.FromHours(1)
        };

        var key = WindowManager.QualifyKey(
            "east",
            new DateTimeOffset(2026, 6, 16, 10, 15, 0, TimeSpan.Zero),
            windowing);

        Assert.StartsWith("east@", key);
        Assert.Contains("2026061610", key);
    }

    [Fact]
    public void RollupEngine_ScavengeStaleDimensions_RemovesInactiveKeys()
    {
        var schema = RollupSchema
            .For<WindowedEvent>()
            .Dimension(e => e.Region)
            .Count()
            .Build();

        var engine = new RollupEngine<WindowedEvent>(schema, new EngineConfiguration
        {
            DimensionTimeToLive = TimeSpan.FromMilliseconds(1)
        });

        engine.Add(new WindowedEvent("east", DateTimeOffset.UtcNow, 1));
        Thread.Sleep(5);

        var removed = engine.ScavengeStaleDimensions();
        Assert.Equal(1, removed);
        Assert.Empty(engine.DeriveSnapshot().Rows);
    }

    [Fact]
    public void TryAdd_AppliesTumblingWindowWhenEventTimeProvided()
    {
        var schema = RollupSchema
            .For<WindowedEvent>()
            .Dimension(e => e.Region)
            .Count()
            .Build();

        var engine = new RollupEngine<WindowedEvent>(schema, new EngineConfiguration
        {
            Windowing = new WindowConfiguration
            {
                Strategy = WindowStrategy.Tumbling,
                WindowSize = TimeSpan.FromHours(1)
            }
        });

        var t0 = new DateTimeOffset(2026, 6, 16, 10, 0, 0, TimeSpan.Zero);
        var t1 = t0.AddHours(2);
        engine.TryAdd(new WindowedEvent("east", t0, 1), t0);
        engine.TryAdd(new WindowedEvent("east", t1, 1), t1);

        Assert.Equal(2, engine.DeriveSnapshot().Rows.Count);
    }

    [Fact]
    public void ParquetSnapshotExporter_WritesLongFormatFile()
    {
        var snapshot = new SummarySnapshot(
            TestTimestamps.Epoch,
            [new SummaryRow("region", "east", new Dictionary<string, double> { ["count"] = 10, ["sum"] = 20 })]);

        var path = Path.Combine(Path.GetTempPath(), $"hypercube-parquet-{Guid.NewGuid():N}.parquet");
        try
        {
            ParquetSnapshotExporter.Export(snapshot, path);
            Assert.True(new FileInfo(path).Length > 0);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void CachedLocalAiEngine_ReusesSimilarSnapshotAnalysis()
    {
        var inner = new RuleBasedLocalAiEngine();
        var cache = new SimilarityInferenceCache(0.95);
        var engine = new CachedLocalAiEngine(inner, cache);

        var snapshot = new SummarySnapshot(
            TestTimestamps.Epoch,
            [new SummaryRow("region", "east", new Dictionary<string, double> { ["count"] = 100 })]);

        var first = engine.AnalyzeSummary(snapshot);
        var second = engine.AnalyzeSummary(snapshot with { GeneratedAt = snapshot.GeneratedAt.AddMinutes(1) });

        Assert.Equal(first.RecommendedInsights.Count, second.RecommendedInsights.Count);
    }

    [Fact]
    public void PromptTemplateCompiler_ProducesCompactPrompt()
    {
        var snapshot = new SummarySnapshot(
            TestTimestamps.Epoch,
            [new SummaryRow("region", "east", new Dictionary<string, double> { ["count"] = 42 })]);

        var prompt = new PromptTemplateCompiler().Compile(snapshot);

        Assert.Contains("[system]", prompt);
        Assert.Contains("region:east", prompt);
        Assert.Contains("count=42", prompt);
    }
}
