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
            AddLookup(c, mt, st, "delivery_rate", segment);
            AddLookup(c, mt, st, "opt_out_rate", segment);
            AddLookup(c, "*", st, "delivery_rate", segment);
            AddLookup(c, "*", st, "opt_out_rate", segment);
            AddLookup("*", mt, st, "delivery_rate", segment);
            AddLookup("*", mt, st, "opt_out_rate", segment);
        }
    }

    private void AddLookup(string carrier, string messageType, string senderTier, string metric, BenchmarkSegment segment)
    {
        _lookup[(carrier, messageType, senderTier, metric)] = segment;
    }

    /// <summary>
    /// Looks up a benchmark band for the given subject, dimension, cell, and metric.
    /// </summary>
    public BenchmarkBand? Lookup(ISubject subject, string dimension, string cellKey, string metric)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(metric);

        var senderTier = subject.Tier.ToLowerInvariant();
        var metricKey = metric.ToLowerInvariant();
        var (carrier, messageType) = ResolveCarrierAndMessageType(dimension, cellKey);
        if (carrier is null || messageType is null)
        {
            return null;
        }

        var key = (carrier, messageType, senderTier, metricKey);
        if (!_lookup.TryGetValue(key, out var segment))
        {
            return null;
        }

        var percentiles = metricKey switch
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

    private static (string? Carrier, string? MessageType) ResolveCarrierAndMessageType(string dimension, string cellKey)
    {
        return dimension switch
        {
            "carrier" => (cellKey.ToLowerInvariant(), "*"),
            "message_type" => ("*", cellKey.ToLowerInvariant()),
            "carrier_message_type" => ResolveCarrierMessageTypeCell(cellKey),
            _ => (null, null)
        };
    }

    private static (string? Carrier, string? MessageType) ResolveCarrierMessageTypeCell(string cellKey)
    {
        var parts = cellKey.Split('|', 2);
        return parts.Length == 2
            ? (parts[0].ToLowerInvariant(), parts[1].ToLowerInvariant())
            : (null, null);
    }
}