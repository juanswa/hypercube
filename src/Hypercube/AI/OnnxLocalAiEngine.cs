namespace Hypercube.AI;

/// <summary>
/// Placeholder ONNX-backed implementation of <see cref="ILocalAiEngine"/>.
/// Currently delegates to deterministic rules until ONNX bindings are configured.
/// </summary>
public sealed class OnnxLocalAiEngine(string? modelPath = null) : ILocalAiEngine
{
    private readonly RuleBasedLocalAiEngine _fallback = new();

    /// <summary>Optional path to an ONNX model file when configured.</summary>
    public string? ModelPath { get; } = ValidateModelPath(modelPath);

    /// <inheritdoc />
    public AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5) =>
        _fallback.AnalyzeSummary(snapshot, previousSnapshot, topN);

    /// <inheritdoc />
    public string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        var narrative = _fallback.GenerateNarrative(snapshot, analysis);
        return ModelPath is null
            ? $"{narrative} deterministic rules applied."
            : narrative;
    }

    private static string? ValidateModelPath(string? modelPath)
    {
        if (!string.IsNullOrWhiteSpace(modelPath) && !File.Exists(modelPath))
        {
            throw new FileNotFoundException("ONNX model file was not found.", modelPath);
        }

        return modelPath;
    }
}
