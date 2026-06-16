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
