namespace Hypercube.Industry.Sms;

/// <summary>
/// Static benchmark provider backed by embedded industry-standard data.
/// </summary>
public sealed class StaticIndustryBenchmarkProvider : IBenchmarkProvider
{
    private readonly IndustryBenchmarkData _data;
    private readonly Dictionary<(string Carrier, string MessageType, string SenderTier, string Metric), BenchmarkSegment> _lookup;

    /// <summary>
    /// Creates a provider from parsed benchmark data.
    /// </summary>
    public StaticIndustryBenchmarkProvider(IndustryBenchmarkData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _lookup = new Dictionary<(string, string, string, string), BenchmarkSegment>();
        foreach (var segment in data.Segments)
        {
            var c = segment.Carrier.ToLowerInvariant();
            var mt = segment.MessageType.ToLowerInvariant();
            var st = segment.SenderTier.ToLowerInvariant();
            _lookup.Add((c, mt, st, "delivery_rate"), segment);
            _lookup.Add((c, mt, st, "opt_out_rate"), segment);
        }
    }

    /// <summary>
    /// Looks up a benchmark band for the given subject, dimension, cell, and metric.
    /// </summary>
    public BenchmarkBand? Lookup(ISubject subject, string dimension, string cellKey, string metric)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(metric);

        // cellKey is the carrier in the SMS plugin's carrier dimension.
        // Match against the benchmark data by carrier + message_type + sender_tier + metric.
        var carrier = cellKey;
        var messageType = dimension switch
        {
            "message_type" => cellKey,
            _ => "all"
        };

        // For the SMS plugin, the dimension name tells us what cellKey represents.
        // carrier dimension: cellKey = carrier name
        // message_type dimension: cellKey = message type
        // We need to look up by all three dimensions, but Lookup only gives us one cellKey at a time.
        // Fallback: match on carrier only, ignore message_type and tier for now.
        // The observation engine will call this per (dimension, cell, metric), so we return
        // the best available band for the carrier + metric combination.

        var senderTier = subject.Tier.ToLowerInvariant();
        var key = (carrier.ToLowerInvariant(), messageType.ToLowerInvariant(), senderTier, metric.ToLowerInvariant());
        if (_lookup.TryGetValue(key, out var segment))
        {
            var percentiles = metric.ToLowerInvariant() switch
            {
                "delivery_rate" => segment.DeliveryRate,
                "opt_out_rate" => segment.OptOutRate,
                _ => null
            };

            if (percentiles is null)
            {
                return null;
            }

            var cohort = new CohortKey(
                subject.Tier,
                subject.Vertical,
                carrier,
                messageType,
                "all",
                "all");

            return new BenchmarkBand(
                P25: percentiles.P25,
                Median: percentiles.Median,
                P75: percentiles.P75,
                PeerCount: segment.SampleSizeHours,
                Resolved: cohort,
                P90: percentiles.P90);
        }

        return null;
    }
}