using Hypercube.Models;

namespace Hypercube.AI;

/// <summary>
/// Deterministic, offline insight algorithms over rollup snapshots.
/// No external AI services are required.
/// </summary>
public static class DeterministicInsightEngine
{
    /// <summary>
    /// Ranks the most interesting cells in the current snapshot.
    /// Uses within-dimension surprise scores always; adds historical z-score and EWMA insights when
    /// a previous snapshot is supplied.
    /// </summary>
    /// <param name="current">Snapshot to analyze.</param>
    /// <param name="previous">Optional prior snapshot for temporal comparisons.</param>
    /// <param name="topN">Maximum number of insights to return. At least one is returned when data exists.</param>
    /// <returns>Insights ordered by descending score.</returns>
    public static IReadOnlyList<InterestingCellInsight> RankInterestingCells(
        SummarySnapshot current,
        SummarySnapshot? previous = null,
        int topN = 10)
    {
        var insights = new List<InterestingCellInsight>();
        var rows = current.Rows;

        if (rows.Count == 0)
        {
            return insights;
        }

        AddWithinDimensionSurpriseInsights(current, insights);

        if (previous is not null)
        {
            AddHistoricalInsights(current, previous, insights);
        }

        return [.. insights
            .OrderByDescending(x => x.Score)
            .Take(Math.Max(1, topN))];
    }

    /// <summary>
    /// Attributes the total change in the snapshot primary metric to individual cells.
    /// </summary>
    /// <param name="previous">Baseline snapshot.</param>
    /// <param name="current">Comparison snapshot.</param>
    /// <param name="topN">Maximum contributors to return by absolute delta.</param>
    public static DriverAnalysisResult AnalyzeDrivers(
        SummarySnapshot previous,
        SummarySnapshot current,
        int topN = 5)
    {
        var prevMap = previous.Rows.ToDictionary(
            CellId.From,
            previous.PrimaryValue,
            StringComparer.OrdinalIgnoreCase);
        var currMap = current.Rows.ToDictionary(
            CellId.From,
            current.PrimaryValue,
            StringComparer.OrdinalIgnoreCase);

        var allCellIds = prevMap.Keys.Concat(currMap.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var contributions = new List<DriverContribution>();

        var totalPrevious = prevMap.Values.Sum();
        var totalCurrent = currMap.Values.Sum();
        var totalDelta = totalCurrent - totalPrevious;

        foreach (var cellId in allCellIds)
        {
            prevMap.TryGetValue(cellId, out var previousValue);
            currMap.TryGetValue(cellId, out var currentValue);
            var delta = currentValue - previousValue;
            var share = totalDelta == 0 ? 0 : delta / totalDelta;

            contributions.Add(new DriverContribution(
                cellId,
                previousValue,
                currentValue,
                delta,
                share));
        }

        return new DriverAnalysisResult(totalPrevious, totalCurrent, totalDelta, [.. contributions
            .OrderByDescending(x => Math.Abs(x.Delta))
            .Take(Math.Max(1, topN))]);
    }

    /// <summary>
    /// Attributes changes across multiple metrics simultaneously.
    /// </summary>
    /// <param name="previous">Baseline snapshot.</param>
    /// <param name="current">Comparison snapshot.</param>
    /// <param name="metrics">Metric names to evaluate. Defaults to all metrics present in either snapshot.</param>
    /// <param name="topN">Maximum contributors per metric.</param>
    public static MultiMetricDriverAnalysisResult AnalyzeDriversForMetrics(
        SummarySnapshot previous,
        SummarySnapshot current,
        IReadOnlyList<string>? metrics = null,
        int topN = 5)
    {
        var metricNames = metrics ?? previous.Rows
            .SelectMany(row => row.Metrics.Keys)
            .Concat(current.Rows.SelectMany(row => row.Metrics.Keys))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var analyses = new List<MetricDriverAnalysis>(metricNames.Count);
        foreach (var metric in metricNames)
        {
            var previousSnapshot = previous with { PrimaryMetric = metric };
            var currentSnapshot = current with { PrimaryMetric = metric };
            var result = AnalyzeDrivers(previousSnapshot, currentSnapshot, topN);
            analyses.Add(new MetricDriverAnalysis(
                metric,
                result.TotalPrevious,
                result.TotalCurrent,
                result.TotalDelta,
                result.TopContributors));
        }

        return new MultiMetricDriverAnalysisResult(analyses);
    }

    /// <summary>
    /// Detects Simpson's-paradox-style rate reversals within a dimension.
    /// Compares each sibling cell's signal-rate delta against the count-weighted pooled rate delta.
    /// </summary>
    /// <param name="previous">Baseline snapshot.</param>
    /// <param name="current">Comparison snapshot.</param>
    /// <param name="countMetric">Denominator metric for rates. Defaults to <c>count</c>.</param>
    /// <param name="rateNumeratorMetric">Numerator metric for rates. Defaults to <c>signal</c>.</param>
    public static IReadOnlyList<SimpsonParadoxSignal> DetectSimpsonsParadox(
        SummarySnapshot previous,
        SummarySnapshot current,
        string countMetric = "count",
        string rateNumeratorMetric = "signal")
    {
        var previousByCell = previous.Rows.ToDictionary(CellId.From, x => x, StringComparer.OrdinalIgnoreCase);
        var currentByCell = current.Rows.ToDictionary(CellId.From, x => x, StringComparer.OrdinalIgnoreCase);

        var results = new List<SimpsonParadoxSignal>();
        foreach (var dimensionGroup in current.Rows.GroupBy(r => r.Dimension, StringComparer.OrdinalIgnoreCase))
        {
            var dimension = dimensionGroup.Key;
            var childIds = dimensionGroup
                .Select(CellId.From)
                .Concat(previous.Rows.Where(r => r.Dimension.Equals(dimension, StringComparison.OrdinalIgnoreCase)).Select(CellId.From))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (childIds.Count < 2)
            {
                continue;
            }

            var (Count, Numerator) = SumRateComponents(childIds, previousByCell, countMetric, rateNumeratorMetric);
            var currentTotals = SumRateComponents(childIds, currentByCell, countMetric, rateNumeratorMetric);
            if (Count <= 0 || currentTotals.Count <= 0)
            {
                continue;
            }

            var pooledRatePrevious = Numerator / Count;
            var pooledRateCurrent = currentTotals.Numerator / currentTotals.Count;
            var pooledRateDelta = pooledRateCurrent - pooledRatePrevious;
            if (Math.Abs(pooledRateDelta) < 1e-9)
            {
                continue;
            }

            var childRateDeltas = new List<(string ChildCellId, double RateDelta)>();
            foreach (var childId in childIds)
            {
                previousByCell.TryGetValue(childId, out var previousRow);
                currentByCell.TryGetValue(childId, out var currentRow);
                var previousRate = Rate(previousRow, countMetric, rateNumeratorMetric);
                var currentRate = Rate(currentRow, countMetric, rateNumeratorMetric);
                var delta = currentRate - previousRate;
                if (Math.Abs(delta) >= 1e-9)
                {
                    childRateDeltas.Add((childId, delta));
                }
            }

            if (childRateDeltas.Count < 2)
            {
                continue;
            }

            var childSigns = childRateDeltas.Select(x => Math.Sign(x.RateDelta)).Where(s => s != 0).Distinct().ToList();
            if (childSigns.Count != 1)
            {
                continue;
            }

            var childSign = childSigns[0];
            if (childSign == Math.Sign(pooledRateDelta))
            {
                continue;
            }

            var explanation =
                $"Dimension {dimension}: sibling signal-rates moved {(childSign > 0 ? "up" : "down")} " +
                $"while the pooled rate moved {(pooledRateDelta > 0 ? "up" : "down")} " +
                $"({pooledRatePrevious:P1} -> {pooledRateCurrent:P1}), consistent with a weight-shift paradox.";

            results.Add(new SimpsonParadoxSignal(
                dimension,
                pooledRateDelta,
                childRateDeltas,
                explanation));
        }

        return results;
    }

    private static void AddWithinDimensionSurpriseInsights(
        SummarySnapshot snapshot,
        List<InterestingCellInsight> insights)
    {
        foreach (var dimensionGroup in snapshot.Rows.GroupBy(x => x.Dimension, StringComparer.OrdinalIgnoreCase))
        {
            var rows = dimensionGroup.ToList();
            var total = rows.Sum(snapshot.PrimaryValue);
            if (total == 0 || rows.Count == 0)
            {
                continue;
            }

            var expectedUniform = total / rows.Count;
            foreach (var row in rows)
            {
                var observed = snapshot.PrimaryValue(row);
                var residual = (observed - expectedUniform) / Math.Sqrt(expectedUniform + 1.0);
                var score = Math.Abs(residual);

                insights.Add(new InterestingCellInsight(
                    InsightKind.DeviationFromExpectation,
                    CellId.From(row),
                    score,
                    $"Observed {observed:0.##} vs uniform-within-dimension expected {expectedUniform:0.##}; standardized residual={residual:0.##}."));
            }
        }
    }

    private static void AddHistoricalInsights(
        SummarySnapshot currentSnapshot,
        SummarySnapshot previousSnapshot,
        List<InterestingCellInsight> insights)
    {
        var previousRows = previousSnapshot.Rows;
        var currentRows = currentSnapshot.Rows;
        var prevByCell = previousRows.ToDictionary(
            CellId.From,
            previousSnapshot.PrimaryValue,
            StringComparer.OrdinalIgnoreCase);
        var prevAverage = previousRows.Count == 0 ? 0 : previousRows.Average(previousSnapshot.PrimaryValue);
        var prevStdDev = StdDev(previousRows.Select(previousSnapshot.PrimaryValue));

        foreach (var row in currentRows)
        {
            var cellId = CellId.From(row);
            var currentValue = currentSnapshot.PrimaryValue(row);
            prevByCell.TryGetValue(cellId, out var previousValue);

            var z = prevStdDev <= 0.000001 ? 0 : (currentValue - prevAverage) / prevStdDev;
            var ewma = (0.35 * currentValue) + (0.65 * previousValue);
            var ewmaShift = currentValue - ewma;

            insights.Add(new InterestingCellInsight(
                InsightKind.ZScoreOutlier,
                cellId,
                Math.Abs(z),
                $"Historical z-score={z:0.##} on primary metric using previous-window distribution."));

            insights.Add(new InterestingCellInsight(
                InsightKind.EwmaTrendShift,
                cellId,
                Math.Abs(ewmaShift),
                $"EWMA shift={ewmaShift:0.##} (current={currentValue:0.##}, baseline={ewma:0.##})."));
        }
    }

    private static (double Count, double Numerator) SumRateComponents(
        IReadOnlyList<string> childIds,
        Dictionary<string, SummaryRow> rowsByCell,
        string countMetric,
        string rateNumeratorMetric)
    {
        double count = 0;
        double numerator = 0;
        foreach (var childId in childIds)
        {
            if (!rowsByCell.TryGetValue(childId, out var row))
            {
                continue;
            }

            count += row[countMetric];
            numerator += row[rateNumeratorMetric];
        }

        return (count, numerator);
    }

    private static double Rate(SummaryRow? row, string countMetric, string rateNumeratorMetric)
    {
        if (row is null)
        {
            return 0;
        }

        var denominator = row[countMetric];
        return denominator == 0 ? 0 : row[rateNumeratorMetric] / denominator;
    }

    private static double StdDev(IEnumerable<double> source)
    {
        var values = source as double[] ?? source.ToArray();
        if (values.Length == 0)
        {
            return 0;
        }

        var avg = values.Average();
        double sumSquared = 0;
        foreach (var value in values)
        {
            var delta = value - avg;
            sumSquared += delta * delta;
        }

        return Math.Sqrt(sumSquared / values.Length);
    }
}
