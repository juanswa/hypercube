namespace Hypercube.Industry;

/// <summary>
/// Deterministic join that produces <see cref="SendReportAnalysis"/>.
/// All comparison and classification lives here; the narrator never computes.
/// </summary>
public static class SendReportObservationEngine
{
    private const double DefaultMinMateriality = 0.005;
    private const int DefaultHistoryWindowCount = 1; // most recent window for intrinsic + co-movement

    /// <summary>
    /// Builds a complete send-report analysis for the given subject and current snapshot.
    /// </summary>
    /// <param name="subject">The sending account being analyzed.</param>
    /// <param name="current">Current rollup snapshot.</param>
    /// <param name="history">Account history provider.</param>
    /// <param name="plugin">Industry plugin supplying seasonality, benchmarks, and direction map.</param>
    /// <param name="minMateriality">Minimum absolute deviation to flag as material. Defaults to 0.005 (0.5 pp).</param>
    public static SendReportAnalysis Build(
        ISubject subject,
        SummarySnapshot current,
        IAccountHistory history,
        IIndustryPlugin<object> plugin,
        double minMateriality = DefaultMinMateriality)
    {
        // 1. Compute intrinsic analysis against the most recent history window.
        var recentWindows = history.RecentWindows(subject.Id, DefaultHistoryWindowCount);
        var topCells = recentWindows.Count > 0
            ? DeterministicInsightEngine.RankInterestingCells(current, recentWindows[0], topN: 10)
            : DeterministicInsightEngine.RankInterestingCells(current);
        var intrinsic = new AiAnalysisResult { TopInterestingCells = [.. topCells] };

        // 2. Build co-movement context from history (used for SharedCause detection).
        var coMovementHistory = history.RecentWindows(subject.Id, 12).ToList();

        // 3. Classify every (dimension, cell, metric) observation.
        var observations = new List<Observation>();

        foreach (var row in current.Rows)
        {
            foreach (var metric in row.Metrics.Keys)
            {
                var actual = row[metric];
                var direction = plugin.DirectionOf(metric);

                // Self-seasonality baseline.
                var dow = current.GeneratedAt.LocalDateTime.DayOfWeek;
                var hour = current.GeneratedAt.LocalDateTime.Hour;
                var isHoliday = plugin.Calendar.IsHoliday(DateOnly.FromDateTime(current.GeneratedAt.LocalDateTime));
                var selfExpected = plugin.Seasonality.ExpectedSelf(subject.Id, metric, dow, hour, isHoliday);

                // Cohort benchmark.
                var cohortBand = plugin.Benchmarks.Lookup(subject, row.Dimension, row.Key, metric);

                // Deviation vs the best available baseline.
                var baseline = selfExpected ?? cohortBand?.Median ?? 0d;
                var deviation = actual - baseline;

                // Materiality.
                var isMaterial = Math.Abs(deviation) >= minMateriality;

                // Classification.
                var kind = Classify(
                    actual,
                    selfExpected,
                    cohortBand,
                    deviation,
                    isMaterial,
                    minMateriality,
                    row.Dimension,
                    row.Key,
                    metric,
                    subject,
                    plugin,
                    coMovementHistory);

                // Favourability (null for Neutral metrics and WithinNormal).
                bool? isFavorable = null;
                if (direction != MetricDirection.Neutral && kind != ObservationKind.WithinNormal)
                {
                    isFavorable = direction == MetricDirection.HigherIsBetter
                        ? deviation > 0
                        : deviation < 0;
                }

                observations.Add(new Observation(
                    Dimension: row.Dimension,
                    CellKey: row.Key,
                    Metric: metric,
                    Actual: actual,
                    SelfExpected: selfExpected,
                    CohortMedian: cohortBand?.Median,
                    CohortP25: cohortBand?.P25,
                    CohortP75: cohortBand?.P75,
                    CohortPeerCount: cohortBand?.PeerCount ?? 0,
                    Deviation: deviation,
                    Kind: kind,
                    IsMaterial: isMaterial,
                    IsFavorable: isFavorable));
            }
        }

        return new SendReportAnalysis(subject, intrinsic, observations);
    }

    private static ObservationKind Classify(
        double actual,
        double? selfExpected,
        BenchmarkBand? cohortBand,
        double deviation,
        bool isMaterial,
        double minMateriality,
        string dimension,
        string cellKey,
        string metric,
        ISubject subject,
        IIndustryPlugin<object> plugin,
        IReadOnlyList<SummarySnapshot> coMovementHistory)
    {
        // 1. SeasonalExpected: actual ≈ selfExpected (seasonality explains the movement).
        if (selfExpected.HasValue && Math.Abs(actual - selfExpected.Value) < minMateriality)
        {
            return ObservationKind.SeasonalExpected;
        }

        // 2. SharedCause: cell co-moves with peers in the same direction.
        if (coMovementHistory.Count >= 3)
        {
            var coMovements = CoMovementEngine.DiscoverForCell(
                cellId: $"{dimension}:{cellKey}",
                history: coMovementHistory,
                maxLag: 3,
                topN: 5,
                minAbsCorrelation: 0.6);

            if (coMovements.Count > 0 && deviation != 0)
            {
                // At least one strong positive co-movement → external driver.
                return ObservationKind.SharedCause;
            }
        }

        // 3. Cohort-based classification (requires a benchmark band).
        if (cohortBand is null)
        {
            // No peer data — fall back to self-only classification.
            if (!selfExpected.HasValue)
            {
                return ObservationKind.WithinNormal; // nothing to compare against.
            }

            return Math.Abs(deviation) < minMateriality
                ? ObservationKind.WithinNormal
                : ObservationKind.SelfAnomaly;
        }

        // Within cohort band?
        var withinCohort = actual >= cohortBand.P25 && actual <= cohortBand.P75;

        // Within self band? (self-expected within materiality floor).
        var withinSelf = !selfExpected.HasValue || Math.Abs(actual - selfExpected.Value) < minMateriality;

        if (withinSelf && withinCohort)
        {
            return ObservationKind.WithinNormal;
        }

        if (!withinSelf && withinCohort)
        {
            return ObservationKind.SelfAnomaly;
        }

        if (actual < cohortBand.P25)
        {
            return ObservationKind.BelowPeers;
        }

        if (actual > cohortBand.P75)
        {
            return ObservationKind.AbovePeers;
        }

        // Fallback (should not reach here if logic is complete).
        return ObservationKind.WithinNormal;
    }
}