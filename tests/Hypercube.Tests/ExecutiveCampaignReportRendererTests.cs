using System.Globalization;
using Hypercube.Industry;
using Hypercube.Industry.Sms;
using Hypercube.Models;
using Hypercube.Tui.Dashboard;

namespace Hypercube.Tests;

public sealed class ExecutiveCampaignReportRendererTests
{
    [Fact]
    public void ReportUsesInvariantNumerics()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("af-ZA");
            CultureInfo.CurrentUICulture = new CultureInfo("af-ZA");

            var report = BuildReport(sent: 10_000, cancelled: 0, delivered: 9_500, expired: 100, undeliv: 100, rejectd: 250, spam: 50, anomalyRejectRate: 0.12);
            var markdown = ExecutiveCampaignReportRenderer.RenderMarkdown(report, 10_000, null, aiFallbackMode: true);

            Assert.Contains("95.0", markdown);
            Assert.DoesNotContain("95,0", markdown);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    [Fact]
    public void RenderMarkdown_IncludesTotalsAndWorstSegmentRow()
    {
        var report = BuildReport(sent: 100_000, cancelled: 500, delivered: 92_000, expired: 2_000, undeliv: 2_000, rejectd: 3_000, spam: 500, anomalyRejectRate: 0.18);
        var markdown = ExecutiveCampaignReportRenderer.RenderMarkdown(report, 100_000, null, aiFallbackMode: true);

        Assert.Contains("## 🗣️ Executive narrative", markdown);
        Assert.Contains("## 👑 Campaign totals", markdown);
        Assert.Contains("- **Sent:** 100,000", markdown);
        Assert.Contains("- **Worst:** MTN|promotional", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("## 🔀 Delivery flow (Sankey)", markdown);
        Assert.Contains("SENT 100,000", markdown);
        Assert.Contains("Balance check over sent denominator: ATTEMPTED + CANCELLED = SENT", markdown);
        Assert.Contains("Rate meaning", markdown);
        Assert.Contains("DELIVRD", markdown);
        Assert.Contains("EXPIRED", markdown);
        Assert.Contains("UNDELIV", markdown);
        Assert.Contains("REJECTD", markdown);
        Assert.Contains("SPAM", markdown);
        Assert.Contains("CANCELLED", markdown);
    }

    [Fact]
    public void FailureBreakdown_IdentifiesDominantReason()
    {
        var report = BuildReport(sent: 150_000, cancelled: 0, delivered: 132_000, expired: 4_000, undeliv: 3_000, rejectd: 9_000, spam: 2_000, anomalyRejectRate: 0.30);
        var markdown = ExecutiveCampaignReportRenderer.RenderMarkdown(report, 150_000, null, aiFallbackMode: true);

        Assert.Contains("Dominant reason: REJECTD", markdown);
        Assert.Contains("Move MTN|promotional to a backup route", markdown);
        Assert.Contains("Overall, 150,000 messages were sent", markdown);
    }

    private static CampaignReport BuildReport(double sent, double cancelled, double delivered, double expired, double undeliv, double rejectd, double spam, double anomalyRejectRate)
    {
        var attempted = sent - cancelled;
        var failed = expired + undeliv + rejectd + spam;
        var subject = new DemoSubject("sender-test", "MTN", "Standard", "sms", "ZA", "100k+");
        var rows = new List<SummaryRow>
        {
            new("carrier", "MTN", new Dictionary<string, double>
            {
                ["sent"] = sent,
                ["cancelled"] = cancelled,
                ["delivered"] = delivered,
                ["expired"] = expired,
                ["undeliv"] = undeliv,
                ["rejectd"] = rejectd,
                ["spam"] = spam,
                ["delivery_rate"] = delivered / attempted,
                ["failure_rate"] = failed / attempted,
                ["rejectd_rate"] = rejectd / attempted,
                ["spam_rate"] = spam / attempted
            }),
            new("carrier_message_type", "MTN|promotional", new Dictionary<string, double>
            {
                ["sent"] = sent * 0.4,
                ["cancelled"] = 0d,
                ["delivered"] = sent * 0.4 * (1 - anomalyRejectRate),
                ["expired"] = sent * 0.4 * 0.02,
                ["undeliv"] = sent * 0.4 * 0.02,
                ["rejectd"] = sent * 0.4 * anomalyRejectRate,
                ["spam"] = sent * 0.4 * 0.01,
                ["delivery_rate"] = 1 - anomalyRejectRate,
                ["failure_rate"] = anomalyRejectRate,
                ["rejectd_rate"] = anomalyRejectRate,
                ["spam_rate"] = 0.01
            }),
            new("carrier_message_type", "MTN|otp", new Dictionary<string, double>
            {
                ["sent"] = sent * 0.6,
                ["cancelled"] = 0d,
                ["delivered"] = sent * 0.6 * 0.97,
                ["expired"] = sent * 0.6 * 0.01,
                ["undeliv"] = sent * 0.6 * 0.01,
                ["rejectd"] = sent * 0.6 * 0.005,
                ["spam"] = sent * 0.6 * 0.005,
                ["delivery_rate"] = 0.97,
                ["failure_rate"] = 0.03,
                ["rejectd_rate"] = 0.005,
                ["spam_rate"] = 0.005
            }),
            new("hod", "08", new Dictionary<string, double> { ["delivery_rate"] = 0.99, ["sent"] = sent * 0.2, ["expired"] = sent * 0.001, ["undeliv"] = sent * 0.001, ["rejectd"] = sent * 0.001, ["spam"] = sent * 0.001 }),
            new("hod", "13", new Dictionary<string, double> { ["delivery_rate"] = 0.91, ["sent"] = sent * 0.2, ["expired"] = sent * 0.006, ["undeliv"] = sent * 0.004, ["rejectd"] = sent * 0.006, ["spam"] = sent * 0.002 }),
            new("dow", "weekday", new Dictionary<string, double> { ["delivery_rate"] = 0.95, ["sent"] = sent * 0.7, ["expired"] = sent * 0.01, ["undeliv"] = sent * 0.01, ["rejectd"] = sent * 0.015, ["spam"] = sent * 0.005 }),
            new("dow", "weekend", new Dictionary<string, double> { ["delivery_rate"] = 0.93, ["sent"] = sent * 0.3, ["expired"] = sent * 0.01, ["undeliv"] = sent * 0.01, ["rejectd"] = sent * 0.01, ["spam"] = sent * 0.01 })
        };

        var snapshot = new SummarySnapshot(DateTimeOffset.UtcNow, rows, "failure_rate");
        var analysis = new SendReportAnalysis(
            subject,
            new AiAnalysisResult(),
            new List<Observation>
            {
                new("carrier_message_type", "MTN|promotional", "rejectd_rate", anomalyRejectRate, 0.04, null, null, null, 0, anomalyRejectRate - 0.04, ObservationKind.SelfAnomaly, true, false)
            });

        return new CampaignReport(subject, DateTimeOffset.UtcNow.AddDays(-7), DateTimeOffset.UtcNow, snapshot, analysis);
    }

    private sealed record DemoSubject(
        string Id,
        string Carrier,
        string Tier,
        string Vertical,
        string Country,
        string VolumeBand) : ISubject;
}
