using System.Collections.Concurrent;
using Hypercube.Models;

namespace Hypercube.AI;

/// <summary>
/// Discovers cells that co-move or move inversely across snapshot history using EWMA-smoothed series.
/// </summary>
public static class CoMovementEngine
{
    /// <summary>
    /// Finds the strongest co-moving cell pairs, including optional lead/lag offsets.
    /// </summary>
    public static IReadOnlyList<CoMovementPair> DiscoverPairs(
        IReadOnlyList<SummarySnapshot> history,
        int maxLag = 3,
        int topN = 10,
        double minAbsCorrelation = 0.7) =>
        DiscoverPairs(history, new CoMovementOptions
        {
            MaxLag = maxLag,
            TopN = topN,
            MinAbsCorrelation = minAbsCorrelation
        });

    /// <summary>
    /// Finds co-moving pairs using configurable EWMA and correlation thresholds.
    /// </summary>
    public static IReadOnlyList<CoMovementPair> DiscoverPairs(
        IReadOnlyList<SummarySnapshot> history,
        CoMovementOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (history.Count < 3)
        {
            return [];
        }

        var cellIds = history
            .SelectMany(snapshot => snapshot.Rows.Select(CellId.From))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var seriesByCell = cellIds.ToDictionary(
            cellId => cellId,
            cellId => BuildEwmaSeries(history, cellId, options.EwmaAlpha),
            StringComparer.OrdinalIgnoreCase);

        var activeCellIds = cellIds
            .Where(cellId => !IsEffectivelyZeroSeries(seriesByCell[cellId]))
            .ToList();

        if (activeCellIds.Count < 2)
        {
            return [];
        }

        var pairs = new ConcurrentBag<CoMovementPair>();
        Parallel.For(0, activeCellIds.Count, i =>
        {
            var leftId = activeCellIds[i];
            var leftSeries = seriesByCell[leftId];
            for (var j = i + 1; j < activeCellIds.Count; j++)
            {
                var rightId = activeCellIds[j];
                var rightSeries = seriesByCell[rightId];
                var (correlation, lag) = BestLagCorrelation(leftSeries, rightSeries, options.MaxLag);
                if (Math.Abs(correlation) < options.MinAbsCorrelation)
                {
                    continue;
                }

                var direction = correlation >= 0 ? "move together" : "move inversely";
                var lagText = lag == 0
                    ? "in the same window"
                    : lag > 0
                        ? $"{leftId} leads {rightId} by {lag} snapshot(s)"
                        : $"{rightId} leads {leftId} by {-lag} snapshot(s)";

                pairs.Add(new CoMovementPair(
                    leftId,
                    rightId,
                    correlation,
                    lag,
                    $"{leftId} and {rightId} {direction} (r={correlation:0.##}); {lagText}."));
            }
        });

        return [.. pairs
            .OrderByDescending(pair => Math.Abs(pair.Correlation))
            .Take(Math.Max(1, options.TopN))];
    }

    /// <summary>
    /// Returns co-movement pairs involving a specific cell.
    /// </summary>
    public static IReadOnlyList<CoMovementPair> DiscoverForCell(
        string cellId,
        IReadOnlyList<SummarySnapshot> history,
        int maxLag = 3,
        int topN = 5,
        double minAbsCorrelation = 0.6)
    {
        return [.. DiscoverPairs(history, maxLag, topN: history.Count * 2, minAbsCorrelation)
            .Where(pair =>
                pair.CellId.Equals(cellId, StringComparison.OrdinalIgnoreCase) ||
                pair.OtherCellId.Equals(cellId, StringComparison.OrdinalIgnoreCase))
            .Take(topN)];
    }

    private static double[] BuildEwmaSeries(IReadOnlyList<SummarySnapshot> history, string cellId, double ewmaAlpha)
    {
        var raw = history
            .Select(snapshot =>
            {
                var row = snapshot.Rows.FirstOrDefault(r => CellId.From(r).Equals(cellId, StringComparison.OrdinalIgnoreCase));
                return row is null ? 0 : snapshot.PrimaryValue(row);
            })
            .ToArray();

        if (raw.Length == 0)
        {
            return raw;
        }

        var ewma = new double[raw.Length];
        ewma[0] = raw[0];
        for (var i = 1; i < raw.Length; i++)
        {
            ewma[i] = (ewmaAlpha * raw[i]) + ((1 - ewmaAlpha) * ewma[i - 1]);
        }

        return ewma;
    }

    private static bool IsEffectivelyZeroSeries(double[] series)
    {
        foreach (var value in series)
        {
            if (Math.Abs(value) > 1e-12)
            {
                return false;
            }
        }

        return true;
    }

    private static (double Correlation, int Lag) BestLagCorrelation(double[] left, double[] right, int maxLag)
    {
        var bestCorrelation = 0d;
        var bestLag = 0;

        for (var lag = -maxLag; lag <= maxLag; lag++)
        {
            var correlation = LaggedCorrelation(left, right, lag);
            if (Math.Abs(correlation) > Math.Abs(bestCorrelation))
            {
                bestCorrelation = correlation;
                bestLag = lag;
            }
        }

        return (bestCorrelation, bestLag);
    }

    private static double LaggedCorrelation(double[] left, double[] right, int lag)
    {
        var alignedLeft = new List<double>();
        var alignedRight = new List<double>();

        for (var i = 0; i < left.Length; i++)
        {
            var j = i + lag;
            if (j < 0 || j >= right.Length)
            {
                continue;
            }

            alignedLeft.Add(left[i]);
            alignedRight.Add(right[j]);
        }

        if (alignedLeft.Count < 2)
        {
            return 0;
        }

        return Pearson(alignedLeft, alignedRight);
    }

    private static double Pearson(List<double> xs, List<double> ys)
    {
        var n = xs.Count;
        var meanX = xs.Average();
        var meanY = ys.Average();
        double numerator = 0;
        double denomX = 0;
        double denomY = 0;

        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - meanX;
            var dy = ys[i] - meanY;
            numerator += dx * dy;
            denomX += dx * dx;
            denomY += dy * dy;
        }

        if (denomX <= 1e-12 || denomY <= 1e-12)
        {
            return 0;
        }

        return numerator / Math.Sqrt(denomX * denomY);
    }
}
