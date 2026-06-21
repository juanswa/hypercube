using Hypercube.Industry;
using Hypercube.Tui.Demo;

namespace Hypercube.Tests;

public sealed class SmsCampaignGeneratorTests
{
    [Fact]
    public void Generate_RequestedCount_MatchesTotalMessages()
    {
        var generator = new SmsCampaignGenerator(seed: 42);
        var subject = new TestSubject("sender-demo", "MTN", "Standard", "sms", "ZA", "100k+");

        var events = generator.Generate(subject, 10_000, TestTimestamps.AtMinutes(0), TimeSpan.FromDays(7)).ToList();

        Assert.Equal(10_000, events.Count);
        Assert.Equal(10_000L, events.Sum(e => e.Total));
    }

    [Fact]
    public void Generate_ProducesNonDeliveredStatuses()
    {
        var generator = new SmsCampaignGenerator(seed: 42);
        var subject = new TestSubject("sender-demo", "MTN", "Standard", "sms", "ZA", "100k+");

        var events = generator.Generate(subject, 100_000, TestTimestamps.AtMinutes(0), TimeSpan.FromDays(7)).ToList();

        Assert.True(events.Sum(e => e.FailedTotal) > 0, "Expected at least some non-delivered attempted messages.");
        Assert.True(events.Sum(e => e.Rejectd) > 0, "Expected at least some REJECTD outcomes.");
        Assert.True(events.Sum(e => e.Expired) > 0, "Expected at least some EXPIRED outcomes.");
        Assert.True(events.Sum(e => e.Spam) > 0, "Expected at least some SPAM outcomes.");
    }

    [Fact]
    public void Generate_BaselineMix_IsCloseToConfiguredRatios()
    {
        var generator = new SmsCampaignGenerator(seed: 42);
        var subject = new TestSubject("sender-demo", "MTN", "Standard", "sms", "ZA", "100k+");

        var events = generator.Generate(subject, 300_000, TestTimestamps.AtMinutes(0), TimeSpan.FromDays(7)).ToList();
        var boundary = events.Count * 2 / 3;
        var baseline = events.Take(boundary).ToList();

        var sent = baseline.Sum(e => e.Total);
        Assert.True(sent > 0);

        var expiredShare = baseline.Sum(e => e.Expired) / (double)sent;
        var spamShare = baseline.Sum(e => e.Spam) / (double)sent;

        Assert.InRange(expiredShare, 0.04, 0.06);
        Assert.InRange(spamShare, 0.003, 0.007);
    }

    [Fact]
    public void Generate_AnomalySegment_HasHigherRejectRateInFinalThird()
    {
        var generator = new SmsCampaignGenerator(seed: 42);
        var subject = new TestSubject("sender-demo", "MTN", "Standard", "sms", "ZA", "100k+");

        var events = generator.Generate(subject, 200_000, TestTimestamps.AtMinutes(0), TimeSpan.FromDays(7)).ToList();
        var boundary = events.Count * 2 / 3;

        var baseline = events
            .Take(boundary)
            .Where(e => e.Carrier == "MTN" && e.MessageType == "Promotional")
            .ToList();
        var anomaly = events
            .Skip(boundary)
            .Where(e => e.Carrier == "MTN" && e.MessageType == "Promotional")
            .ToList();

        Assert.NotEmpty(baseline);
        Assert.NotEmpty(anomaly);

        var baselineAttempted = baseline.Sum(e => e.Attempted);
        var anomalyAttempted = anomaly.Sum(e => e.Attempted);
        Assert.True(baselineAttempted > 0);
        Assert.True(anomalyAttempted > 0);

        var baselineRejectRate = baseline.Sum(e => e.Rejectd) / (double)baselineAttempted;
        var anomalyRejectRate = anomaly.Sum(e => e.Rejectd) / (double)anomalyAttempted;

        Assert.True(anomalyRejectRate > baselineRejectRate, $"Expected anomaly reject rate ({anomalyRejectRate:F4}) to exceed baseline ({baselineRejectRate:F4}).");
    }

    private sealed record TestSubject(
        string Id,
        string Carrier,
        string Tier,
        string Vertical,
        string Country,
        string VolumeBand) : ISubject;
}
