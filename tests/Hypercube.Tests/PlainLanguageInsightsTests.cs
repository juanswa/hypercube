namespace Hypercube.Tests;

public sealed class PlainLanguageInsightsTests
{
    [Fact]
    public void WriteExecutiveSummary_UsesPlainLanguage()
    {
        var snapshot = new SummarySnapshot(
            TestTimestamps.AtMinutes(5),
            [
                new SummaryRow("channel", "sms", new Dictionary<string, double> { ["count"] = 200, ["signal"] = 120 }),
                new SummaryRow("channel", "email", new Dictionary<string, double> { ["count"] = 80, ["signal"] = 40 })
            ]);

        var analysis = new RuleBasedLocalAiEngine().AnalyzeSummary(snapshot);
        var summary = PlainLanguageInsights.WriteExecutiveSummary(snapshot, analysis);

        Assert.Contains("sms channel", summary);
        Assert.DoesNotContain("z-score", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EWMA", summary);
    }

    [Fact]
    public void WriteAlertBody_TranslatesDeviationInsight()
    {
        var insight = new InterestingCellInsight(
            InsightKind.DeviationFromExpectation,
            "channel:push",
            2.4,
            "Observed 69 vs uniform-within-dimension expected 51.25; standardized residual=2.46.");

        var body = PlainLanguageInsights.WriteAlertBody(insight);

        Assert.Contains("push channel", body);
        Assert.Contains("69", body);
        Assert.Contains("peer baseline", body);
        Assert.Contains("difference", body);
        Assert.DoesNotContain("residual", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("~69 vs ~51", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteLatencySummary_AvoidsStatisticsJargon()
    {
        var callout = new DistributionShapeCallout(
            "latency",
            30,
            28,
            46,
            120,
            DistributionSkew.RightSkewed,
            true,
            true,
            "technical");

        var text = PlainLanguageInsights.WriteLatencySummary(callout);

        Assert.Contains("Typical response time", text);
        Assert.Contains("95th percentile", text);
        Assert.DoesNotContain("right-skewed", text, StringComparison.OrdinalIgnoreCase);
    }
}
