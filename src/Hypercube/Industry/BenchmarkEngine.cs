namespace Hypercube.Industry;

/// <summary>
/// Computes peer-cohort percentile bands from <see cref="SubjectAggregate"/> records.
/// </summary>
public sealed class BenchmarkEngine : IBenchmarkProvider
{
    private readonly int _minPeerCount;
    private readonly Dictionary<(CohortKey Cohort, string Metric), TDigestState> _digests = new();
    private readonly Dictionary<(CohortKey Cohort, string Metric), int> _counts = new();

    /// <summary>
    /// Creates a new engine with the given k-anonymity floor.
    /// </summary>
    /// <param name="minPeerCount">Minimum number of subjects required to emit a band. Defaults to 5.</param>
    public BenchmarkEngine(int minPeerCount = 5)
    {
        if (minPeerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(minPeerCount));
        }

        _minPeerCount = minPeerCount;
    }

    /// <summary>
    /// Ingests one subject aggregate and updates the corresponding cohort×metric digest.
    /// </summary>
    public void Ingest(SubjectAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        var key = (aggregate.Cohort, aggregate.Metric);
        if (!_digests.TryGetValue(key, out var digest))
        {
            digest = new TDigestState();
            _digests[key] = digest;
            _counts[key] = 0;
        }

        digest.Add(aggregate.Rate);
        _counts[key]++;
    }

    /// <summary>
    /// Ingests a batch of aggregates.
    /// </summary>
    public void IngestRange(IEnumerable<SubjectAggregate> aggregates)
    {
        foreach (var aggregate in aggregates)
        {
            Ingest(aggregate);
        }
    }

    /// <summary>
    /// Returns the benchmark band for the given cohort and metric, or null if k-anonymity is not met.
    /// </summary>
    public BenchmarkBand? GetBand(CohortKey cohort, string metric)
    {
        var key = (cohort, metric);
        if (!_digests.TryGetValue(key, out var digest))
        {
            return null;
        }

        if (_counts.TryGetValue(key, out var count) && count < _minPeerCount)
        {
            return null;
        }

        return new BenchmarkBand(
            P25: digest.Quantile(0.25),
            Median: digest.Quantile(0.50),
            P75: digest.Quantile(0.75),
            PeerCount: count,
            Resolved: cohort,
            P90: digest.Quantile(0.90));
    }

    /// <summary>
    /// IBenchmarkProvider implementation — maps subject + cell to a cohort key.
    /// </summary>
    public BenchmarkBand? Lookup(ISubject subject, string dimension, string cellKey, string metric)
    {
        ArgumentNullException.ThrowIfNull(subject);
        // Default mapping: use subject properties + cellKey as carrier.
        // Industry plugins should replace this with a dimension-aware mapper.
        var cohort = new CohortKey(
            subject.Tier,
            subject.Vertical,
            cellKey,
            "all",
            "all",
            "all");
        return GetBand(cohort, metric);
    }
}