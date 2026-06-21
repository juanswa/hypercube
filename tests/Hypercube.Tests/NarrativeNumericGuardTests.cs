using global::Hypercube.AI;
using global::Hypercube.AI.Onnx;
using global::Hypercube.Industry;
using Hypercube.Models;

namespace Hypercube.Tests;

public sealed class NarrativeNumericGuardTests
{
    [Fact]
    public void IsGrounded_AcceptsAllowedRawAndPercentageForms()
    {
        var allowed = new[] { 0.048 };

        Assert.True(NarrativeNumericGuard.IsGrounded("Delivery rate was 0.048.", allowed));
        Assert.True(NarrativeNumericGuard.IsGrounded("Delivery rate was 4.8%.", allowed));
    }

    [Fact]
    public void IsGrounded_RejectsFabricatedNumbers()
    {
        var allowed = new[] { 0.048 };

        Assert.False(NarrativeNumericGuard.IsGrounded("Delivery rate was 12.3%.", allowed));
    }

    [Fact]
    public void OnnxLocalAiEngine_FallsBackWhenModelMissing()
    {
        using var engine = new OnnxLocalAiEngine(modelPath: null);
        var snapshot = new SummarySnapshot(
            DateTimeOffset.UtcNow,
            [
                new SummaryRow("carrier", "mtn", new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.98,
                    ["failure_rate"] = 0.02
                })
            ]);
        var analysis = new AiAnalysisResult
        {
            RecommendedInsights = { "MTN looks steady." }
        };

        var narrative = engine.GenerateNarrative(snapshot, analysis);

        Assert.NotEmpty(narrative);
        Assert.DoesNotContain("deterministic rules applied", narrative, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OnnxSendReportNarrator_GroundsModelOutputWhenModelPresent()
    {
        var modelPath = Environment.GetEnvironmentVariable("HYPERCUBE_ONNX_MODEL_PATH");
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return;
        }

        var subject = new TestSubject("sender-123", "MTN", "Standard", "sms", "ZA", "100k+");
        var analysis = new SendReportAnalysis(
            subject,
            new AiAnalysisResult(),
            [
                new Observation(
                    "carrier_message_type",
                    "MTN|Promotional",
                    "failure_rate",
                    0.30,
                    0.03,
                    null,
                    null,
                    null,
                    0,
                    0.27,
                    ObservationKind.SelfAnomaly,
                    true,
                    false)
            ]);

        using var generator = new OnnxTextGenerator(modelPath);
        var narrator = new OnnxSendReportNarrator(generator, _ => "fallback");

        var output = narrator.Generate(analysis);

        Assert.NotEmpty(output);
        Assert.True(NarrativeNumericGuard.IsGrounded(output, PromptTemplateCompiler.AllowedNumbers(analysis)));
    }

    private sealed record TestSubject(
        string Id,
        string Carrier,
        string Tier,
        string Vertical,
        string Country,
        string VolumeBand) : ISubject;
}
