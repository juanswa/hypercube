using Hypercube.Models;

namespace Hypercube.AI;

/// <summary>
/// Structured output from local AI analysis of rollup snapshots.
/// </summary>
public sealed class AiAnalysisResult
{
    /// <summary>Anomaly score per cell identifier. Higher means more anomalous.</summary>
    public Dictionary<string, double> AnomalyScores { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Trend or ratio score per cell identifier.
    /// In the rule-based engine this is signal-count divided by total count.
    /// </summary>
    public Dictionary<string, double> TrendScores { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Human-readable warning flags raised during analysis.</summary>
    public List<string> Flags { get; init; } = [];

    /// <summary>Short actionable insight statements.</summary>
    public List<string> RecommendedInsights { get; init; } = [];

    /// <summary>Top ranked interesting cells from deterministic analysis.</summary>
    public List<InterestingCellInsight> TopInterestingCells { get; init; } = [];

    /// <summary>Detected Simpson-style paradox signals between snapshots.</summary>
    public List<SimpsonParadoxSignal> SimpsonSignals { get; init; } = [];
}

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

/// <summary>
/// Deterministic, rule-based implementation of <see cref="ILocalAiEngine"/>.
/// Suitable for offline use without ML models.
/// </summary>
public sealed class RuleBasedLocalAiEngine : ILocalAiEngine
{
    /// <inheritdoc />
    public AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5)
    {
        var result = new AiAnalysisResult();

        var topInsights = DeterministicInsightEngine.RankInterestingCells(snapshot, previousSnapshot, topN);
        result.TopInterestingCells.AddRange(topInsights);
        foreach (var insight in topInsights)
        {
            result.AnomalyScores[insight.CellId] = insight.Score;
            if (insight.Kind is InsightKind.ZScoreOutlier or InsightKind.DeviationFromExpectation)
            {
                result.Flags.Add($"{insight.Kind}: {insight.Explanation}");
            }
        }

        foreach (var row in snapshot.Rows.Where(r => r.Count > 0))
        {
            var signalRate = (double)row.SignalCount / row.Count;
            result.TrendScores[$"{row.Dimension}:{row.Key}"] = signalRate;
        }

        if (previousSnapshot is not null)
        {
            var simpsonSignals = DeterministicInsightEngine.DetectSimpsonsParadox(previousSnapshot, snapshot);
            result.SimpsonSignals.AddRange(simpsonSignals);
            foreach (var signal in simpsonSignals)
            {
                result.Flags.Add($"Simpson signal: {signal.Explanation}");
            }
        }

        if (result.TopInterestingCells.Count > 0)
        {
            var top = result.TopInterestingCells[0];
            result.RecommendedInsights.Add($"Most interesting cell is {top.CellId} ({top.Kind}) score={top.Score:0.##}.");
        }
        else
        {
            result.RecommendedInsights.Add("No major anomalies detected in current snapshot.");
        }

        return result;
    }

    /// <inheritdoc />
    public string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        if (snapshot.Rows.Count == 0)
        {
            return "No data available for analysis.";
        }

        var top = snapshot.Rows.OrderByDescending(r => r.Count).Take(3).ToList();
        var topSummary = string.Join(", ", top.Select(t => $"{t.Dimension}/{t.Key}: {t.Count} events"));

        var narrativeParts = new List<string>
        {
            $"Snapshot generated at {snapshot.GeneratedAt:u}. Top dimensions: {topSummary}."
        };

        if (analysis.TopInterestingCells.Count > 0)
        {
            var interesting = analysis.TopInterestingCells.Take(3)
                .Select(i => $"{i.CellId} ({i.Kind}, score={i.Score:0.##})");
            narrativeParts.Add($"Interesting cells: {string.Join("; ", interesting)}.");
        }

        if (analysis.SimpsonSignals.Count > 0)
        {
            narrativeParts.Add($"Simpson-style reversals detected: {analysis.SimpsonSignals.Count}.");
        }

        if (analysis.TrendScores.Count > 0)
        {
            var weakestSignalRate = analysis.TrendScores.OrderBy(kv => kv.Value).First();
            narrativeParts.Add($"Lowest signal-rate cell: {weakestSignalRate.Key} ({weakestSignalRate.Value:P1}).");
        }

        if (analysis.Flags.Count > 0)
        {
            narrativeParts.Add($"Flags: {string.Join(" | ", analysis.Flags.Take(3))}.");
        }

        return string.Join(" ", narrativeParts);
    }

    /// <summary>
    /// Renders an ASCII terminal report with sparklines and optional digest histograms.
    /// </summary>
    /// <param name="snapshot">Snapshot that was analyzed.</param>
    /// <param name="analysis">Analysis output from <see cref="AnalyzeSummary"/>.</param>
    /// <param name="history">Optional prior snapshots for trend sparklines.</param>
    /// <param name="digestMetric">Optional digest metric for distribution histograms.</param>
    public string RenderTerminalReport(
        SummarySnapshot snapshot,
        AiAnalysisResult analysis,
        IReadOnlyList<SummarySnapshot>? history = null,
        string? digestMetric = null) =>
        Visualization.SnapshotTerminalReport.Render(snapshot, analysis, history, digestMetric);
}
