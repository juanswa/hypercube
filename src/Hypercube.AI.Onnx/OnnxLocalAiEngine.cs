using Hypercube.AI;
using Hypercube.Models;

namespace Hypercube.AI.Onnx;

public sealed class OnnxLocalAiEngine : ILocalAiEngine, IDisposable
{
    private readonly RuleBasedLocalAiEngine _fallback = new();
    private readonly PromptTemplateCompiler _compiler = new();
    private readonly OnnxTextGenerator? _generator;
    private readonly bool _ownsGenerator;

    public OnnxLocalAiEngine(string? modelPath = null)
    {
        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            try
            {
                _generator = new OnnxTextGenerator(modelPath);
            }
            catch
            {
                _generator = null;
            }
        }
    }

    public OnnxLocalAiEngine(OnnxTextGenerator generator, bool ownsGenerator = false)
    {
        _generator = generator;
        _ownsGenerator = ownsGenerator;
    }

    public AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5) =>
        _fallback.AnalyzeSummary(snapshot, previousSnapshot, topN);

    public string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        var deterministic = _fallback.GenerateNarrative(snapshot, analysis);
        if (_generator is null)
        {
            return deterministic;
        }

        try
        {
            var grounded = _compiler.Compile(snapshot, analysis);
            var output = _generator.Generate(GroundingPrompts.ReportNarrator, grounded);
            return NarrativeNumericGuard.IsGrounded(output, PromptTemplateCompiler.AllowedNumbers(snapshot))
                ? output
                : deterministic;
        }
        catch
        {
            return deterministic;
        }
    }

    public void Dispose()
    {
        if (_ownsGenerator)
        {
            _generator?.Dispose();
        }
    }
}
