using Hypercube.Industry;
using Hypercube.Industry.Sms;
using Hypercube.Models;
using System.Collections.Generic;

namespace Hypercube.Tests;

public sealed class CampaignReportTests
{
    [Fact]
    public void BuildCampaignReport_FromDateRangeAndHistory_CreatesClassifiedObservations()
    {
        // Arrange: subject and plugin
        var subject = new TestSubject(
            Id: "sender-123",
            Carrier: "Vodacom",
            Tier: "Standard",
            Vertical: "sms",
            Country: "ZA",
            VolumeBand: "10k-100k");

        var plugin = new SmsIndustryPlugin();
        var schema = plugin.BuildSubjectSchema();
        var engine = new RollupEngine<SmsEvent>(schema);
        var history = new InMemoryAccountHistory();

        // Historical snapshot: prior window showing stable delivery/failure rates
        var priorSnapshot = new SummarySnapshot(
            TestTimestamps.AtMinutes(-60),
            [
                new SummaryRow("carrier", "Vodacom", new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.96,
                    ["failure_rate"] = 0.04
                }),
                new SummaryRow("message_type", "Transactional", new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.98,
                    ["failure_rate"] = 0.02
                })
            ]);

        history.Append(subject.Id, priorSnapshot);

        // Act: ingest current window data for the date range
        var currentTime = TestTimestamps.AtMinutes(0);
        engine.Add(new SmsEvent(subject.Id, subject.Carrier, "Transactional", currentTime.AddMinutes(1), 480, 6, 6, 5, 3, 0));
        engine.Add(new SmsEvent(subject.Id, subject.Carrier, "Transactional", currentTime.AddMinutes(2), 470, 10, 10, 7, 3, 0));

        var snapshot = engine.DeriveSnapshot();
        var report = plugin.BuildCampaignReport(subject, snapshot, history, currentTime, currentTime.AddHours(1));

        // Assert: observations are produced and snapped into a campaign report
        Assert.NotNull(report);
        Assert.Equal(subject, report.Subject);
        Assert.Equal(currentTime, report.WindowStart);
        Assert.Equal(currentTime.AddHours(1), report.WindowEnd);
        Assert.NotEmpty(report.Analysis.Observations);
        Assert.NotNull(plugin.Benchmarks.Lookup(subject, "carrier", "Vodacom", "delivery_rate"));
        Assert.NotNull(plugin.Benchmarks.Lookup(subject, "message_type", "Transactional", "delivery_rate"));
        Assert.NotNull(plugin.Benchmarks.Lookup(subject, "carrier_message_type", "Vodacom|Transactional", "delivery_rate"));

        var carrierDelivery = Assert.Single(report.Analysis.Observations, o => o.Dimension == "carrier" && o.Metric == "delivery_rate");
        Assert.Equal(0.95, carrierDelivery.Actual, 3);
        Assert.InRange(carrierDelivery.Actual, 0d, 1d);   // guards the mean-of-counts regression
        Assert.True(carrierDelivery.IsMaterial);
        Assert.Equal(0.96, carrierDelivery.SelfExpected!.Value, 3);

        var failureObservation = Assert.Single(report.Analysis.Observations, o => o.Dimension == "carrier" && o.Metric == "failure_rate");
        Assert.Equal(0.05, failureObservation.Actual, 3);
    }

    [Fact]
    public void SmsIndustryPlugin_BuildCampaignReport_ReturnsPluginSpecificReport()
    {
        var subject = new TestSubject(
            Id: "sender-456",
            Carrier: "MTN",
            Tier: "Enterprise",
            Vertical: "sms",
            Country: "ZA",
            VolumeBand: "100k+");

        var plugin = new SmsIndustryPlugin();
        var engine = new RollupEngine<SmsEvent>(plugin.BuildSubjectSchema());
        var history = new InMemoryAccountHistory();
        var priorSnapshot = new SummarySnapshot(
            TestTimestamps.AtMinutes(-120),
            [
                new SummaryRow("carrier", "MTN", new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.96,
                    ["failure_rate"] = 0.04
                })
            ]);

        history.Append(subject.Id, priorSnapshot);

        var currentTime = TestTimestamps.AtMinutes(0);
        engine.Add(new SmsEvent(subject.Id, subject.Carrier, "OTP", currentTime.AddMinutes(5), 950, 15, 15, 12, 8, 0));
        var snapshot = engine.DeriveSnapshot();
        var report = plugin.BuildCampaignReport(subject, snapshot, history, currentTime, currentTime.AddMinutes(60));

        Assert.Equal("sender-456", report.Subject.Id);
        Assert.Equal("sms", report.Subject.Vertical);
        Assert.Equal(1, report.Analysis.Observations.Count(o => o.Dimension == "carrier" && o.Metric == "delivery_rate"));
        Assert.Equal(currentTime, report.WindowStart);
        Assert.Equal(currentTime.AddMinutes(60), report.WindowEnd);
    }

    [Fact]
    public void CampaignReport_WithNarrativeTemplates_GeneratesSummaryAndObservationTexts()
    {
        // Arrange: build a campaign report with observations
        var subject = new TestSubject(
            Id: "sender-789",
            Carrier: "Vodacom",
            Tier: "Standard",
            Vertical: "sms",
            Country: "ZA",
            VolumeBand: "50k-100k");

        var plugin = new SmsIndustryPlugin();
        var engine = new RollupEngine<SmsEvent>(plugin.BuildSubjectSchema());
        var history = new InMemoryAccountHistory();
        var priorSnapshot = new SummarySnapshot(
            TestTimestamps.AtMinutes(-60),
            [
                new SummaryRow("carrier", "Vodacom", new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.97,
                    ["failure_rate"] = 0.03
                })
            ]);

        history.Append(subject.Id, priorSnapshot);

        var currentTime = TestTimestamps.AtMinutes(0);
        engine.Add(new SmsEvent(subject.Id, subject.Carrier, "Promotional", currentTime.AddMinutes(10), 920, 25, 20, 22, 13, 0));

        var cancelledEvent = new SmsEvent(subject.Id, subject.Carrier, "Promotional", currentTime.AddMinutes(20), 90, 0, 0, 5, 5, 20);
        engine.Add(cancelledEvent);
        var snapshot = engine.DeriveSnapshot();
        var report = plugin.BuildCampaignReport(subject, snapshot, history, currentTime, currentTime.AddHours(1));

        // Act: generate narrative summary and render observations
        var narrative = plugin.Narrative;
        var summary = narrative.Summary(report.Analysis);
        var observationTexts = report.Analysis.Observations
            .Select(o => narrative.Render(o))
            .ToList();

        // Assert: narrative production succeeds and produces meaningful text
        Assert.NotNull(summary);
        Assert.NotEmpty(summary);
        Assert.Contains("sender-789", summary);
        Assert.Contains("observations", summary);

        Assert.NotEmpty(observationTexts);
        Assert.True(observationTexts.Count > 0, "Should have multiple observations across dimensions and metrics");
        Assert.True(observationTexts.Any(o => o.Contains("Delivery rate")), "Should include delivery rate observations");
    }

    [Fact]
    public void Cancelled_ExcludedFromRateDenominator()
    {
        var subject = new TestSubject("sender-cancel", "MTN", "Standard", "sms", "ZA", "10k-100k");
        var plugin = new SmsIndustryPlugin();
        var engine = new RollupEngine<SmsEvent>(plugin.BuildSubjectSchema());

        var now = TestTimestamps.AtMinutes(0);
        engine.Add(new SmsEvent(subject.Id, subject.Carrier, "OTP", now, 80, 5, 5, 5, 5, 20));

        var snapshot = engine.DeriveSnapshot();
        var carrier = Assert.Single(snapshot.Rows.Where(r => r.Dimension == "carrier"));

        Assert.Equal(120d, carrier["sent"], 3);
        Assert.Equal(20d, carrier["cancelled"], 3);
        Assert.Equal(0.8, carrier["delivery_rate"], 3);
        Assert.Equal(0.2, carrier["failure_rate"], 3);
    }

    private sealed record TestSubject(
        string Id,
        string Carrier,
        string Tier,
        string Vertical,
        string Country,
        string VolumeBand) : ISubject;
}
