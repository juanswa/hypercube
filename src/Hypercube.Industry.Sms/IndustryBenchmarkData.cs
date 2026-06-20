namespace Hypercube.Industry.Sms;

/// <summary>
/// Represents the industry-standard benchmark data format (self-derived aggregates).
/// </summary>
public sealed class IndustryBenchmarkData
{
    /// <summary>Metadata about the benchmark source.</summary>
    public BenchmarkMeta Meta { get; init; } = new();

    /// <summary>Segmented benchmark data.</summary>
    public List<BenchmarkSegment> Segments { get; init; } = new();
}

/// <summary>
/// Metadata about the benchmark source.
/// </summary>
public sealed class BenchmarkMeta
{
    /// <summary>Source of the data.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Description of how the data was generated.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Dimensions the data is segmented by.</summary>
    public List<string> SegmentedBy { get; init; } = new();

    /// <summary>Total number of rows used to generate the benchmark.</summary>
    public int GeneratedFromRows { get; init; }
}

/// <summary>
/// A single benchmark segment for a specific carrier/message_type/tier combination.
/// </summary>
public sealed class BenchmarkSegment
{
    /// <summary>Network carrier.</summary>
    public string Carrier { get; init; } = string.Empty;

    /// <summary>Message type (OTP, Transactional, Promotional).</summary>
    public string MessageType { get; init; } = string.Empty;

    /// <summary>Sender tier (Enterprise, Standard, Trial).</summary>
    public string SenderTier { get; init; } = string.Empty;

    /// <summary>Sample size in hours.</summary>
    public int SampleSizeHours { get; init; }

    /// <summary>Delivery rate percentiles.</summary>
    public MetricPercentiles DeliveryRate { get; init; } = new();

    /// <summary>Opt-out rate percentiles.</summary>
    public MetricPercentiles OptOutRate { get; init; } = new();
}

/// <summary>
/// Percentile data for a metric.
/// </summary>
public sealed class MetricPercentiles
{
    /// <summary>Median (50th percentile).</summary>
    public double Median { get; init; }

    /// <summary>25th percentile.</summary>
    public double P25 { get; init; }

    /// <summary>75th percentile.</summary>
    public double P75 { get; init; }

    /// <summary>10th percentile.</summary>
    public double P10 { get; init; }

    /// <summary>90th percentile.</summary>
    public double P90 { get; init; }
}
