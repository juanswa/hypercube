namespace Hypercube.AI;

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
}
