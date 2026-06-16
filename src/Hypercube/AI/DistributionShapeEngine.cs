namespace Hypercube.AI;

/// <summary>
/// Surfaces distribution-shape insights from percentile digest metrics on summary rows.
/// </summary>
public static class DistributionShapeEngine
{
    private const double SkewRatioThreshold = 1.25;
    private const double HeavyTailRatioThreshold = 2.5;

    /// <summary>
    /// Analyzes digest-derived percentiles for a row and returns a shape callout when data exists.
    /// </summary>
    public static DistributionShapeCallout? AnalyzeRow(SummaryRow row, string digestMetric)
    {
        var meanKey = MetricNameHelper.Mean(digestMetric);
        if (!row.Metrics.ContainsKey(meanKey))
        {
            return null;
        }

        var mean = row[meanKey];
        var median = row[MetricNameHelper.Percentile(digestMetric, 50)];
        var p95 = row[MetricNameHelper.Percentile(digestMetric, 95)];
        var p99 = row[MetricNameHelper.Percentile(digestMetric, 99)];

        if (mean <= 0 && median <= 0)
        {
            return null;
        }

        var skew = ClassifySkew(mean, median);
        var meanMisleading = IsMeanMisleading(mean, median);
        var heavyTail = median > 0 && (p99 / Math.Max(median, 1e-9)) >= HeavyTailRatioThreshold;
        var summary = BuildSummary(mean, median, p95, skew, meanMisleading, heavyTail);

        return new DistributionShapeCallout(
            digestMetric,
            mean,
            median,
            p95,
            p99,
            skew,
            meanMisleading,
            heavyTail,
            summary);
    }

    /// <summary>
    /// Scans all rows and returns shape callouts for rows with misleading means or heavy tails.
    /// </summary>
    public static IReadOnlyList<DistributionShapeCallout> FindNotableShapes(
        SummarySnapshot snapshot,
        string digestMetric)
    {
        return [.. snapshot.Rows
            .Select(row => AnalyzeRow(row, digestMetric))
            .Where(callout => callout is not null)
            .Cast<DistributionShapeCallout>()
            .Where(callout => callout.MeanMisleading || callout.HeavyTail)
            .OrderByDescending(callout => Math.Abs(callout.Mean - callout.Median))];
    }

    private static DistributionSkew ClassifySkew(double mean, double median)
    {
        if (mean <= 0 || median <= 0)
        {
            return DistributionSkew.Unknown;
        }

        var ratio = mean / median;
        if (ratio >= SkewRatioThreshold)
        {
            return DistributionSkew.RightSkewed;
        }

        if (ratio <= 1 / SkewRatioThreshold)
        {
            return DistributionSkew.LeftSkewed;
        }

        return DistributionSkew.Symmetric;
    }

    private static bool IsMeanMisleading(double mean, double median) =>
        median > 0 && (mean / median >= SkewRatioThreshold || mean / median <= 1 / SkewRatioThreshold);

    private static string BuildSummary(
        double mean,
        double median,
        double p95,
        DistributionSkew skew,
        bool meanMisleading,
        bool heavyTail)
    {
        var parts = new List<string>
        {
            $"mean={mean:0.##}, median={median:0.##}, p95={p95:0.##}"
        };

        if (meanMisleading && skew == DistributionSkew.RightSkewed)
        {
            parts.Add(
                $"mean ({mean:0.##}) exceeds median ({median:0.##}); right-skewed — use median or p95 for alerting");
        }
        else if (meanMisleading && skew == DistributionSkew.LeftSkewed)
        {
            parts.Add(
                $"mean ({mean:0.##}) is below median ({median:0.##}); left-skewed — use median for alerting");
        }

        if (heavyTail)
        {
            parts.Add("heavy upper tail — prefer p95 or p99 for SLO thresholds");
        }

        return string.Join("; ", parts) + ".";
    }
}
