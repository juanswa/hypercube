namespace Hypercube.AI;

/// <summary>
/// Builds a full statistical story for a single rollup cell on demand.
/// </summary>
public static class CellExplainer
{
    /// <summary>
    /// Explains a cell's position, trend, distribution shape, and related co-movements.
    /// </summary>
    /// <param name="cellId">Cell identifier in <c>dimension:key</c> form.</param>
    /// <param name="current">Current snapshot.</param>
    /// <param name="previous">Optional prior snapshot for trend and driver context.</param>
    /// <param name="history">Optional ordered history for co-movement and EWMA context.</param>
    /// <param name="distributionMetric">Optional digest metric base name (for example <c>latency</c>).</param>
    public static CellExplanation Explain(
        string cellId,
        SummarySnapshot current,
        SummarySnapshot? previous = null,
        IReadOnlyList<SummarySnapshot>? history = null,
        string? distributionMetric = null)
    {
        if (!current.TryGetRow(cellId, out var row))
        {
            throw new KeyNotFoundException($"Cell '{cellId}' was not found in the current snapshot.");
        }

        var siblings = current.RowsByDimension[row.Dimension];
        var primaryValue = current.PrimaryValue(row);
        var siblingValues = siblings.Select(current.PrimaryValue).Order().ToList();
        var siblingRank = PercentileRank(primaryValue, siblingValues);
        var dimensionTotal = siblings.Sum(current.PrimaryValue);
        var share = dimensionTotal == 0 ? 0 : primaryValue / dimensionTotal;

        double? previousValue = null;
        double? trendDelta = null;
        double? ewmaBaseline = null;
        double? zScore = null;
        DriverContribution? driver = null;

        if (previous is not null)
        {
            previousValue = previous.TryGetRow(cellId, out var previousRow)
                ? previous.PrimaryValue(previousRow)
                : 0;
            trendDelta = primaryValue - previousValue;
            ewmaBaseline = (0.35 * primaryValue) + (0.65 * previousValue.Value);

            if (previous.RowsByDimension.TryGetValue(row.Dimension, out var previousSiblings) &&
                previousSiblings.Count > 1)
            {
                var (mean, stdDev) = MeanAndPopulationStdDev(previousSiblings.Select(previous.PrimaryValue));
                zScore = stdDev <= 1e-9 ? 0 : (primaryValue - mean) / stdDev;
            }

            var drivers = DeterministicInsightEngine.AnalyzeDrivers(previous, current, topN: siblings.Count);
            driver = drivers.TopContributors.FirstOrDefault(d =>
                CellId.Equals(d.CellId, cellId));
        }

        DistributionShapeCallout? shape = distributionMetric is null
            ? null
            : DistributionShapeEngine.AnalyzeRow(row, distributionMetric);

        var coMovements = history is { Count: >= 3 }
            ? CoMovementEngine.DiscoverForCell(cellId, history)
            : [];

        var bullets = BuildNarrative(
            cellId,
            primaryValue,
            siblingRank,
            share,
            trendDelta,
            ewmaBaseline,
            zScore,
            driver,
            shape,
            coMovements);

        return new CellExplanation(
            cellId,
            row.Dimension,
            row.Key,
            primaryValue,
            siblingRank,
            share,
            previousValue,
            trendDelta,
            ewmaBaseline,
            zScore,
            driver,
            shape,
            coMovements,
            bullets);
    }

    private static List<string> BuildNarrative(
        string cellId,
        double primaryValue,
        double siblingRank,
        double share,
        double? trendDelta,
        double? ewmaBaseline,
        double? zScore,
        DriverContribution? driver,
        DistributionShapeCallout? shape,
        IReadOnlyList<CoMovementPair> coMovements)
    {
        var bullets = new List<string>
        {
            $"{cellId} has primary value {primaryValue:0.##}, ranking at the {siblingRank:P0} percentile among siblings and representing {share:P1} of its dimension."
        };

        if (trendDelta is not null)
        {
            bullets.Add($"Trend vs previous snapshot: {trendDelta.Value:+0.##;-0.##;0} (EWMA baseline {ewmaBaseline:0.##}).");
        }

        if (zScore is not null && Math.Abs(zScore.Value) >= 1.5)
        {
            bullets.Add($"Historical z-score on the primary metric is {zScore.Value:0.##}, indicating an outlier vs the prior sibling distribution.");
        }

        if (driver is not null && Math.Abs(driver.Delta) > 0)
        {
            bullets.Add($"Driver contribution: delta {driver.Delta:+0.##;-0.##;0} ({driver.ShareOfTotalDelta:P1} of dimension-wide change).");
        }

        if (shape is not null)
        {
            bullets.Add(shape.Summary);
        }

        foreach (var pair in coMovements.Take(2))
        {
            bullets.Add(pair.Explanation);
        }

        return bullets;
    }

    private static double PercentileRank(double value, List<double> orderedValues)
    {
        if (orderedValues.Count == 0)
        {
            return 0;
        }

        var below = orderedValues.Count(v => v < value);
        return (double)below / orderedValues.Count;
    }

    private static (double Mean, double StdDev) MeanAndPopulationStdDev(IEnumerable<double> values)
    {
        double mean = 0;
        double sumSquaredDeviations = 0;
        long count = 0;

        foreach (var value in values)
        {
            count++;
            var delta = value - mean;
            mean += delta / count;
            sumSquaredDeviations += delta * (value - mean);
        }

        if (count == 0)
        {
            return (0, 0);
        }

        return (mean, count == 1 ? 0 : Math.Sqrt(sumSquaredDeviations / count));
    }
}
