namespace Hypercube.Tests;

public sealed class InsightEngineTests
{
    private static SummaryRow Row(string dimension, string key, double count, double signal = 0, double sum = 0) =>
        new(dimension, key, new Dictionary<string, double>
        {
            ["count"] = count,
            ["signal"] = signal,
            ["sum"] = sum > 0 ? sum : count
        });

    [Fact]
    public void RankInterestingCells_ReturnsTopSignals()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100, 10),
                Row("channel", "email", 100, 20),
                Row("status", "failed", 20)
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 500, 20),
                Row("channel", "email", 90, 30),
                Row("status", "failed", 10)
            ]);

        var ranked = DeterministicInsightEngine.RankInterestingCells(current, previous, topN: 3);

        Assert.Equal(3, ranked.Count);
        Assert.Contains(ranked, x => x.CellId == "channel:sms");
    }

    [Fact]
    public void AnalyzeDrivers_AttributesTopDeltaContributors()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100, 10),
                Row("channel", "email", 100, 20)
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 40, 5),
                Row("channel", "email", 95, 18)
            ]);

        var result = DeterministicInsightEngine.AnalyzeDrivers(previous, current, topN: 2);
        var top = Assert.Single(result.TopContributors, x => x.CellId == "channel:sms");

        Assert.Equal(-65, result.TotalDelta);
        Assert.Equal(-60, top.Delta);
    }

    [Fact]
    public void RuleBasedEngine_GeneratesDeterministicNarrative()
    {
        var engine = new RuleBasedLocalAiEngine();
        var snapshot = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("status", "delivered", 84, 40),
                Row("status", "failed", 16, 2)
            ]);

        AiAnalysisResult? analysis = engine.AnalyzeSummary(snapshot);
        var narrative = engine.GenerateNarrative(snapshot, analysis);

        Assert.Contains("Busiest right now", narrative);
        Assert.NotEmpty(analysis.RecommendedInsights);
    }

    [Fact]
    public void DetectSimpsonsParadox_FlagsRateReversalWhenWeightsShift()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100, 10),
                Row("channel", "email", 100, 50)
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 150, 18),
                Row("channel", "email", 50, 27.5)
            ]);

        var signals = DeterministicInsightEngine.DetectSimpsonsParadox(previous, current);

        var signal = Assert.Single(signals);
        Assert.Equal("channel", signal.ParentCellId);
        Assert.True(signal.PooledRateDelta < 0);
        Assert.All(signal.ChildRateDeltas, child => Assert.True(child.RateDelta > 0));
    }
}
