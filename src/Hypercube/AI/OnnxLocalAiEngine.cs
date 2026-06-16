using Hypercube.Models;

namespace Hypercube.AI;

/// <summary>
/// Placeholder ONNX-backed implementation of <see cref="ILocalAiEngine"/>.
/// Currently delegates to deterministic rules until ONNX bindings are configured.
/// </summary>
public sealed class OnnxLocalAiEngine : ILocalAiEngine
{
    private readonly RuleBasedLocalAiEngine _fallback = new();

    /// <summary>
    /// Creates an ONNX engine. When <paramref name="modelPath"/> is supplied it must exist on disk.
    /// Inference is not yet wired; analysis falls back to deterministic rules.
    /// </summary>
    /// <param name="modelPath">Optional path to an ONNX model file.</param>
    public OnnxLocalAiEngine(string? modelPath = null)
    {
        if (!string.IsNullOrWhiteSpace(modelPath) && !File.Exists(modelPath))
        {
            throw new FileNotFoundException("ONNX model file was not found.", modelPath);
        }

        ModelPath = modelPath;
    }

    /// <summary>Configured ONNX model path, if any.</summary>
    public string? ModelPath { get; }

    /// <inheritdoc />
    public AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5) =>
        _fallback.AnalyzeSummary(snapshot, previousSnapshot, topN);

    /// <inheritdoc />
    public string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        var narrative = _fallback.GenerateNarrative(snapshot, analysis);
        return string.IsNullOrWhiteSpace(ModelPath)
            ? $"{narrative} [ONNX model not configured; deterministic rules applied.]"
            : $"{narrative} [ONNX model `{ModelPath}` registered; deterministic rules applied until inference is enabled.]";
    }
}
