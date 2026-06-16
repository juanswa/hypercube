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
            result.RecommendedInsights.Add(PlainLanguageInsights.WriteAlertBody(top));
        }
        else
        {
            result.RecommendedInsights.Add("Nothing unusual detected — traffic looks steady.");
        }

        return result;
    }

    /// <inheritdoc />
    public string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis) =>
        PlainLanguageInsights.WriteExecutiveSummary(snapshot, analysis);
}
