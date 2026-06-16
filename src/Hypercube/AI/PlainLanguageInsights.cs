namespace Hypercube.AI;

/// <summary>
/// Turns rollup metrics and deterministic insights into layman-friendly explanations.
/// </summary>
public static class PlainLanguageInsights
{
    /// <summary>Short legend for the dimension cells table columns.</summary>
    public const string CellsTableLegend =
        "Count = total events · Signal = confirmed/acknowledged · P95 = slow tail (95% finish faster)";

    /// <summary>
    /// Writes a plain-language executive summary of the current snapshot.
    /// </summary>
    public static string WriteExecutiveSummary(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        if (snapshot.Rows.Count == 0)
        {
            return "No events have arrived yet. Once data flows in, this panel will explain what stands out.";
        }

        var parts = new List<string>
        {
            DescribeTrafficLeaders(snapshot)
        };

        var topInsight = analysis.TopInterestingCells
            .OrderByDescending(static i => i.Score)
            .FirstOrDefault();

        if (topInsight is not null)
        {
            parts.Add(DescribeTopConcernBrief(topInsight));
        }
        else
        {
            parts.Add("Nothing looks unusual right now — volumes are fairly balanced across categories.");
        }

        if (analysis.SimpsonSignals.Count > 0)
        {
            parts.Add(
                "Heads up: the overall success rate moved one way, but individual sub-groups moved the opposite way. " +
                "That usually means traffic shifted between categories, not that performance truly improved or worsened everywhere.");
        }

        if (analysis.TrendScores.Count > 0)
        {
            var weakest = analysis.TrendScores.OrderBy(static kv => kv.Value).First();
            var (dimension, key) = ParseCellId(weakest.Key);
            parts.Add(
                $"Lowest confirmation rate: {FriendlyCategory(dimension, key)} at {weakest.Value:P0} " +
                $"({DescribeConfirmationRate(weakest.Value)}).");
        }

        return string.Join(" ", parts);
    }

    /// <summary>Short alert title for the insights panel.</summary>
    public static string WriteAlertHeadline(InterestingCellInsight insight) =>
        insight.Kind switch
        {
            InsightKind.DeviationFromExpectation => "Busier than peers",
            InsightKind.EwmaTrendShift => "Sudden change",
            InsightKind.ZScoreOutlier => "Unusual volume",
            InsightKind.SimpsonsParadox => "Misleading average",
            _ => "Worth a look"
        };

    /// <summary>Full plain-language alert body.</summary>
    public static string WriteAlertBody(InterestingCellInsight insight)
    {
        var (dimension, key) = ParseCellId(insight.CellId);
        var where = FriendlyCategory(dimension, key);

        return insight.Kind switch
        {
            InsightKind.DeviationFromExpectation => DescribeDeviation(insight, where),
            InsightKind.EwmaTrendShift => DescribeTrendShift(insight, where),
            InsightKind.ZScoreOutlier => DescribeZScore(insight, where),
            InsightKind.SimpsonsParadox =>
                $"{where} looks odd when you zoom out: group totals and individual pieces are telling different stories.",
            _ => $"{where}: {SimplifyTechnicalExplanation(insight.Explanation)}"
        };
    }

    /// <summary>Explains latency percentiles without statistics jargon.</summary>
    public static string WriteLatencySummary(DistributionShapeCallout shape)
    {
        var parts = new List<string>
        {
            $"Typical response time is about {shape.Median:0} ms (half of requests are faster). " +
            $"Most requests stay under {shape.P95:0} ms, but the slowest 5% stretch higher."
        };

        if (shape.HeavyTail)
        {
            parts.Add(
                $"A few very slow requests pull the average up to {shape.Mean:0} ms. " +
                "For speed goals, trust the 95th percentile — not the average.");
        }
        else if (shape.MeanMisleading)
        {
            parts.Add("The average and typical time differ enough that the average can be misleading.");
        }
        else
        {
            parts.Add("Response times look fairly consistent — no dramatic long tail right now.");
        }

        return string.Join(" ", parts);
    }

    private static string DescribeTrafficLeaders(SummarySnapshot snapshot)
    {
        var leaders = snapshot.Rows
            .OrderByDescending(snapshot.PrimaryValue)
            .Take(2)
            .Select(row => $"{FriendlyCategory(row.Dimension, row.Key)} ({row.Count:0} events)")
            .ToList();

        if (leaders.Count == 0)
        {
            return "Traffic is still ramping up.";
        }

        if (leaders.Count == 1)
        {
            return $"Most activity right now is in {leaders[0]}.";
        }

        return $"Busiest right now: {leaders[0]} and {leaders[1]}.";
    }

    private static string DescribeTopConcernBrief(InterestingCellInsight insight)
    {
        var (dimension, key) = ParseCellId(insight.CellId);
        var where = FriendlyCategory(dimension, key);

        return insight.Kind switch
        {
            InsightKind.DeviationFromExpectation => $"Worth watching: {where} is busier than similar categories.",
            InsightKind.EwmaTrendShift => $"Worth watching: {where} just sped up or slowed down.",
            InsightKind.ZScoreOutlier => $"Worth watching: {where} has unusual traffic compared with recent history.",
            _ => $"Worth watching: {where}."
        };
    }

    private static string DescribeTopConcern(InterestingCellInsight insight)
    {
        var (dimension, key) = ParseCellId(insight.CellId);
        var where = FriendlyCategory(dimension, key);

        return insight.Kind switch
        {
            InsightKind.DeviationFromExpectation =>
                $"Main thing to watch: {where} is busier than its peers ({WriteAlertBody(insight)})",
            InsightKind.EwmaTrendShift =>
                $"Main thing to watch: {where} just changed pace ({WriteAlertBody(insight)})",
            InsightKind.ZScoreOutlier =>
                $"Main thing to watch: {where} has an unusual spike in volume ({WriteAlertBody(insight)})",
            _ => $"Main thing to watch: {WriteAlertBody(insight)}"
        };
    }

    private static string DescribeDeviation(InterestingCellInsight insight, string where)
    {
        if (TryParseObservedExpected(insight.Explanation, out var observed, out var expected))
        {
            var direction = observed >= expected ? "more" : "fewer";
            var delta = Math.Abs(observed - expected);
            return $"{where} has {direction} events than similar categories (~{observed:0} vs ~{expected:0}, about {delta:0} off).";
        }

        return $"{where} stands out from other {where.Split(' ').FirstOrDefault() ?? "categories"} — {SimplifyTechnicalExplanation(insight.Explanation)}";
    }

    private static string DescribeTrendShift(InterestingCellInsight insight, string where)
    {
        if (TryParseEwma(insight.Explanation, out var current, out var baseline))
        {
            var direction = current >= baseline ? "up" : "down";
            return $"{where} is trending {direction} compared with a moment ago ({current:0} vs recent ~{baseline:0} events).";
        }

        return $"{where} recently changed compared with the last few seconds.";
    }

    private static string DescribeZScore(InterestingCellInsight insight, string where) =>
        $"{where} has much more (or much less) traffic than its recent normal.";

    private static string DescribeConfirmationRate(double rate) =>
        rate switch
        {
            >= 0.9 => "very high",
            >= 0.75 => "healthy",
            >= 0.5 => "moderate",
            _ => "low — worth investigating"
        };

    private static string FriendlyCategory(string dimension, string key)
    {
        var shortKey = StripWindowSuffix(key);
        return dimension.ToLowerInvariant() switch
        {
            "channel" => $"the {shortKey} channel",
            "region" => $"the {shortKey} region",
            "status" => $"{shortKey} status",
            _ => $"{dimension} '{shortKey}'"
        };
    }

    private static (string Dimension, string Key) ParseCellId(string cellId)
    {
        var separator = cellId.IndexOf(':');
        if (separator <= 0 || separator >= cellId.Length - 1)
        {
            return (cellId, string.Empty);
        }

        return (cellId[..separator], cellId[(separator + 1)..]);
    }

    private static string StripWindowSuffix(string key)
    {
        var separator = key.IndexOf('@');
        return separator > 0 ? key[..separator] : key;
    }

    private static string SimplifyTechnicalExplanation(string explanation) =>
        explanation
            .Replace("standardized residual", "how far off it is", StringComparison.OrdinalIgnoreCase)
            .Replace("EWMA shift", "recent change", StringComparison.OrdinalIgnoreCase)
            .Replace("uniform-within-dimension expected", "what similar categories average", StringComparison.OrdinalIgnoreCase)
            .Replace("primary metric", "event count", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseObservedExpected(string explanation, out double observed, out double expected)
    {
        observed = 0;
        expected = 0;
        var observedIndex = explanation.IndexOf("Observed ", StringComparison.OrdinalIgnoreCase);
        var expectedIndex = explanation.IndexOf("expected ", StringComparison.OrdinalIgnoreCase);
        if (observedIndex < 0 || expectedIndex < 0)
        {
            return false;
        }

        var observedSlice = explanation[(observedIndex + "Observed ".Length)..];
        var observedEnd = observedSlice.IndexOf(' ');
        if (observedEnd <= 0 || !double.TryParse(observedSlice[..observedEnd], out observed))
        {
            return false;
        }

        var expectedSlice = explanation[(expectedIndex + "expected ".Length)..];
        var expectedEnd = expectedSlice.IndexOf(';');
        if (expectedEnd < 0)
        {
            expectedEnd = expectedSlice.IndexOf(',');
        }

        if (expectedEnd < 0)
        {
            expectedEnd = expectedSlice.Length;
        }

        return double.TryParse(expectedSlice[..expectedEnd].Trim(), out expected);
    }

    private static bool TryParseEwma(string explanation, out double current, out double baseline)
    {
        current = 0;
        baseline = 0;
        var currentIndex = explanation.IndexOf("current=", StringComparison.OrdinalIgnoreCase);
        var baselineIndex = explanation.IndexOf("baseline=", StringComparison.OrdinalIgnoreCase);
        if (currentIndex < 0 || baselineIndex < 0)
        {
            return false;
        }

        var currentSlice = explanation[(currentIndex + "current=".Length)..];
        var currentEnd = currentSlice.IndexOf(',');
        if (currentEnd < 0 || !double.TryParse(currentSlice[..currentEnd].Trim(), out current))
        {
            return false;
        }

        var baselineSlice = explanation[(baselineIndex + "baseline=".Length)..];
        var baselineEnd = baselineSlice.IndexOf(')');
        if (baselineEnd < 0)
        {
            baselineEnd = baselineSlice.Length;
        }

        return double.TryParse(baselineSlice[..baselineEnd].Trim().TrimEnd('.'), out baseline);
    }
}
