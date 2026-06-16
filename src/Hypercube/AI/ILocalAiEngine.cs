namespace Hypercube.AI;

/// <summary>
/// Local, offline AI contract for analyzing rollup snapshots and generating narratives.
/// Implementations may use ONNX/GGML models or deterministic rules.
/// </summary>
public interface ILocalAiEngine
{
    /// <summary>
    /// Analyzes a snapshot, optionally comparing against a previous snapshot.
    /// </summary>
    /// <param name="snapshot">Current rollup snapshot.</param>
    /// <param name="previousSnapshot">Optional prior snapshot for temporal analysis.</param>
    /// <param name="topN">Maximum number of top interesting cells to retain.</param>
    AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5);

    /// <summary>
    /// Produces a human-readable narrative from a snapshot and its analysis result.
    /// </summary>
    /// <param name="snapshot">Snapshot that was analyzed.</param>
    /// <param name="analysis">Analysis output from <see cref="AnalyzeSummary"/>.</param>
    string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis);
}
