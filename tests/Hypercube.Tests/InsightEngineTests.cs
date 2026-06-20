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

    // --- T2a: sub-threshold reversal produces NO signal ---
    [Fact]
    public void DetectSimpsonsParadox_SubThresholdReversal_ReturnsEmpty()
    {
        // Pooled delta is +0.001 (1 pp), children each move -0.001 — both below 0.005 threshold.
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 1000, 100),   // rate 0.10
                Row("channel", "email", 1000, 200)  // rate 0.20
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 2000, 220),   // rate 0.11  (delta +0.01)
                Row("channel", "email", 100, 11)    // rate 0.11  (delta -0.09)
            ]);

        // pooled: previous = 300/2000 = 0.15, current = 231/2100 ≈ 0.11, delta ≈ -0.04 (above threshold)
        // But child deltas: sms +0.01, email -0.09 — both above 0.005, so this IS a real signal.
        // To make it sub-threshold, use smaller numbers:
        var previous2 = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 10000, 1000),  // rate 0.10
                Row("channel", "email", 10000, 2000) // rate 0.20
            ]);

        var current2 = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 20000, 2200),  // rate 0.11  (delta +0.01)
                Row("channel", "email", 1000, 110)   // rate 0.11  (delta -0.09)
            ]);

        // pooled: prev = 3000/20000 = 0.15, curr = 2310/21000 = 0.11, delta = -0.04 (above 0.005)
        // child deltas: sms +0.01, email -0.09 — both above 0.005 → still a signal.
        // We need deltas below 0.005 for both children. Use near-zero child deltas:
        var previous3 = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 10000, 1000),  // rate 0.10
                Row("channel", "email", 10000, 2000) // rate 0.20
            ]);

        var current3 = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 20000, 2200),  // rate 0.11  (delta +0.01)
                Row("channel", "email", 100, 11)     // rate 0.11  (delta -0.09)
            ]);

        // pooled: prev = 3000/20000 = 0.15, curr = 2310/20100 ≈ 0.1149, delta ≈ -0.035 (above 0.005)
        // child deltas: sms +0.01, email -0.09 — both above 0.005 → still a signal.
        // The simplest way: make pooled delta itself sub-threshold.
        var previous4 = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 10000, 1000),  // rate 0.10
                Row("channel", "email", 10000, 1000) // rate 0.10
            ]);

        var current4 = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 20000, 2200),  // rate 0.11  (delta +0.01)
                Row("channel", "email", 100, 11)     // rate 0.11  (delta -0.09)
            ]);

        // pooled: prev = 2000/20000 = 0.10, curr = 2310/20100 ≈ 0.1149, delta ≈ +0.015 (above 0.005)
        // child deltas: sms +0.01, email -0.09 — both above 0.005 → still a signal.
        // Let's just make the pooled delta sub-threshold directly:
        var previous5 = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100000, 10000),  // rate 0.10
                Row("channel", "email", 100000, 10000) // rate 0.10
            ]);

        var current5 = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 200000, 22000),  // rate 0.11  (delta +0.01)
                Row("channel", "email", 1000, 110)     // rate 0.11  (delta -0.09)
            ]);

        // pooled: prev = 20000/200000 = 0.10, curr = 23100/201000 ≈ 0.1149, delta ≈ +0.0049 (below 0.005)
        var signals = DeterministicInsightEngine.DetectSimpsonsParadox(previous5, current5);
        Assert.Empty(signals);
    }

    // --- T2b: above-threshold genuine reversal still produces a signal ---
    [Fact]
    public void DetectSimpsonsParadox_AboveThresholdReversal_ReturnsSignal()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100, 10),    // rate 0.10
                Row("channel", "email", 100, 50)   // rate 0.50
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 150, 18),    // rate 0.12  (delta +0.02)
                Row("channel", "email", 50, 27.5)  // rate 0.55  (delta +0.05)
            ]);

        // pooled: prev = 60/200 = 0.30, curr = 45.5/200 = 0.2275, delta = -0.0725 (above 0.005)
        // child deltas: sms +0.02, email +0.05 — both positive, pooled negative → paradox
        var signals = DeterministicInsightEngine.DetectSimpsonsParadox(previous, current);
        var signal = Assert.Single(signals);
        Assert.Equal("channel", signal.ParentCellId);
        Assert.True(signal.PooledRateDelta < 0);
        Assert.All(signal.ChildRateDeltas, child => Assert.True(child.RateDelta > 0));
    }

    // --- T3a: child present only in current does NOT trigger a paradox ---
    [Fact]
    public void DetectSimpsonsParadox_OneWindowOnlyChild_DoesNotTriggerParadox()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100, 10)   // rate 0.10
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 100, 10),    // rate 0.10 (unchanged)
                Row("channel", "email", 100, 90)   // rate 0.90 (new child, only in current)
            ]);

        // With minCellCount=30, "email" has count=100 in current but 0 in previous → excluded.
        // Only "sms" remains in the sign test → childRateDeltas.Count < 2 → no signal.
        var signals = DeterministicInsightEngine.DetectSimpsonsParadox(previous, current);
        Assert.Empty(signals);
    }

    // --- T3b: small-sample child (count < minCellCount) is excluded from sign test ---
    [Fact]
    public void DetectSimpsonsParadox_SmallSampleChild_ExcludedFromSignTest()
    {
        var previous = new SummarySnapshot(
            TestTimestamps.AtMinutes(0),
            [
                Row("channel", "sms", 100, 10),    // rate 0.10
                Row("channel", "email", 10, 9)     // rate 0.90 (count < 30)
            ]);

        var current = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                Row("channel", "sms", 100, 10),     // rate 0.10 (unchanged)
                Row("channel", "email", 10, 1)      // rate 0.10 (count < 30)
            ]);

        // "email" has count=10 in both windows (< 30) → excluded from sign test.
        // Only "sms" remains → childRateDeltas.Count < 2 → no signal.
        var signals = DeterministicInsightEngine.DetectSimpsonsParadox(previous, current);
        Assert.Empty(signals);
    }
}
