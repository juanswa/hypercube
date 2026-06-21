namespace Hypercube.Industry.Sms;

/// <summary>
/// Raw SMS send event — plugin-scoped, not part of the core model.
/// </summary>
/// <param name="SenderId">Account/sub-account identifier.</param>
/// <param name="Carrier">Network carrier, e.g. "Vodacom".</param>
/// <param name="MessageType">Send category: "OTP", "Transactional", or "Promotional".</param>
/// <param name="Timestamp">Event time of the send.</param>
/// <param name="Delivered">Count of delivered messages.</param>
/// <param name="Expired">Count of expired messages.</param>
/// <param name="Undeliv">Count of permanently undeliverable messages.</param>
/// <param name="Rejectd">Count of carrier/route rejections.</param>
/// <param name="Spam">Count of carrier spam-filtered messages.</param>
/// <param name="Cancelled">Count of messages cancelled before attempt.</param>
public sealed record SmsEvent(
    string SenderId,
    string Carrier,
    string MessageType,
    DateTimeOffset Timestamp,
    long Delivered,
    long Expired,
    long Undeliv,
    long Rejectd,
    long Spam,
    long Cancelled)
{
    public long Total => Delivered + Expired + Undeliv + Rejectd + Spam + Cancelled;

    public long Attempted => Total - Cancelled;

    public long FailedTotal => Expired + Undeliv + Rejectd + Spam;
}

/// <summary>
/// SMS industry plugin wiring schemas, seasonality, benchmarks, and narration.
/// </summary>
public sealed class SmsIndustryPlugin : IIndustryPlugin<SmsEvent>
{
    private const string DeliveryRateMetric = "delivery_rate";
    private const string FailureRateMetric = "failure_rate";
    private const string RejectRateMetric = "rejectd_rate";
    private const string SpamRateMetric = "spam_rate";
    private const string SentMetric = "sent";
    private const string DeliveredMetric = "delivered";
    private const string ExpiredMetric = "expired";
    private const string UndelivMetric = "undeliv";
    private const string RejectdMetric = "rejectd";
    private const string SpamMetric = "spam";
    private const string CancelledMetric = "cancelled";

    /// <summary>Stable industry key.</summary>
    public string IndustryKey => "sms";

    /// <summary>Holiday calendar for South Africa.</summary>
    public IHolidayCalendar Calendar { get; } = FixedHolidayCalendar.SouthAfrica(DateTime.UtcNow.Year);

    /// <summary>No-op seasonality until learned profiles are wired.</summary>
    public ISeasonalityProfile Seasonality { get; } = NullSeasonalityProfile.Instance;

    /// <summary>Peer benchmark provider backed by embedded industry-standard data.</summary>
    public IBenchmarkProvider Benchmarks { get; } = new StaticIndustryBenchmarkProvider(LoadEmbeddedData());

    /// <summary>SMS-specific narrative templates.</summary>
    public INarrativeTemplates Narrative { get; } = new SmsNarrativeTemplates();

    /// <summary>
    /// Builds the per-account (subject-level) rollup schema.
    /// Dimensions: carrier, message type, day-of-week bucket, hour-of-day bucket.
    /// Metrics: delivery_rate, failure_rate.
    /// </summary>
    public RollupSchema<SmsEvent> BuildSubjectSchema()
    {
        var builder = RollupSchema.For<SmsEvent>()
            .Dimension("carrier", e => e.Carrier)
            .Dimension("message_type", e => e.MessageType)
            .Dimension("carrier_message_type", e => $"{e.Carrier}|{e.MessageType}")
            .Dimension("dow", e => DayBucket(e.Timestamp))
            .Dimension("hod", e => HourBucket(e.Timestamp));

        builder = builder
            .Ratio(e => (double)e.Delivered,   e => (double)e.Attempted, DeliveryRateMetric)
            .Ratio(e => (double)e.FailedTotal, e => (double)e.Attempted, FailureRateMetric)
            .Ratio(e => (double)e.Rejectd,     e => (double)e.Attempted, RejectRateMetric)
            .Ratio(e => (double)e.Spam,        e => (double)e.Attempted, SpamRateMetric)
            .Sum(e => (double)e.Total, SentMetric)
            .Sum(e => (double)e.Delivered, DeliveredMetric)
            .Sum(e => (double)e.Expired, ExpiredMetric)
            .Sum(e => (double)e.Undeliv, UndelivMetric)
            .Sum(e => (double)e.Rejectd, RejectdMetric)
            .Sum(e => (double)e.Spam, SpamMetric)
            .Sum(e => (double)e.Cancelled, CancelledMetric)
            .PrimaryMetric(DeliveryRateMetric);

        return builder.Build();
    }

    /// <summary>
    /// Builds the cross-subject benchmark rollup schema.
    /// Dimensions: carrier, message type, day-of-week bucket, hour-of-day bucket.
    /// Metrics: delivery_rate, failure_rate.
    /// </summary>
    public RollupSchema<SubjectAggregate> BuildBenchmarkSchema()
    {
        var builder = RollupSchema.For<SubjectAggregate>()
            .Dimension("carrier", a => a.Cohort.Carrier)
            .Dimension("message_type", a => a.Cohort.MessageType)
            .Dimension("dow", a => a.Cohort.DayBucket)
            .Dimension("hod", a => a.Cohort.HourBucket);

        builder = builder
            .PercentileDigest(a => a.Rate, DeliveryRateMetric)
            .PercentileDigest(a => a.Rate, FailureRateMetric)
            .PercentileDigest(a => a.Rate, RejectRateMetric)
            .PercentileDigest(a => a.Rate, SpamRateMetric)
            .PrimaryMetric(DeliveryRateMetric);

        return builder.Build();
    }

    /// <summary>
    /// Builds an SMS campaign report for the given snapshot window.
    /// </summary>
    public CampaignReport BuildCampaignReport(
        ISubject subject,
        SummarySnapshot snapshot,
        IAccountHistory history,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        double minMateriality = 0.005)
    {
        var analysis = SendReportObservationEngine.Build(subject, snapshot, history, this, minMateriality);
        return new CampaignReport(subject, windowStart, windowEnd, snapshot, analysis);
    }

    /// <summary>Categorises a raw SMS event into its send category (already present on the event).</summary>
    public string CategorizeSend(SmsEvent e) => e.MessageType;

    /// <summary>Returns the directional preference for a metric.</summary>
    public MetricDirection DirectionOf(string metric) => metric switch
    {
        DeliveryRateMetric => MetricDirection.HigherIsBetter,
        FailureRateMetric => MetricDirection.LowerIsBetter,
        RejectRateMetric => MetricDirection.LowerIsBetter,
        SpamRateMetric => MetricDirection.LowerIsBetter,
        SentMetric => MetricDirection.Neutral,
        DeliveredMetric => MetricDirection.Neutral,
        ExpiredMetric => MetricDirection.Neutral,
        UndelivMetric => MetricDirection.Neutral,
        RejectdMetric => MetricDirection.Neutral,
        SpamMetric => MetricDirection.Neutral,
        CancelledMetric => MetricDirection.Neutral,
        _ => MetricDirection.Neutral
    };

    private static string DayBucket(DateTimeOffset ts) => ts.LocalDateTime.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday ? "weekend" : "weekday";

    private static string HourBucket(DateTimeOffset ts) => ts.LocalDateTime.Hour.ToString("D2");

    private static IndustryBenchmarkData LoadEmbeddedData()
    {
        return new IndustryBenchmarkData
        {
            Meta = new BenchmarkMeta
            {
                Source = "self_derived_aggregate",
                Description = "Computed from this organization's own aggregate send data across all senders. NOT an external published industry standard - no reliable source exists at this granularity. Recompute periodically (e.g. nightly/weekly) as new data arrives.",
                SegmentedBy = new List<string> { "carrier", "message_type", "sender_tier" },
                GeneratedFromRows = 29360
            },
            Segments = new List<BenchmarkSegment>
            {
                new BenchmarkSegment { Carrier = "CellC", MessageType = "OTP", SenderTier = "Trial", SampleSizeHours = 637, DeliveryRate = new MetricPercentiles { Median = 1.0, P25 = 1.0, P75 = 1.0, P10 = 1.0, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "CellC", MessageType = "Promotional", SenderTier = "Trial", SampleSizeHours = 1105, DeliveryRate = new MetricPercentiles { Median = 0.95, P25 = 0.9344, P75 = 1.0, P10 = 0.9167, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "CellC", MessageType = "Transactional", SenderTier = "Trial", SampleSizeHours = 828, DeliveryRate = new MetricPercentiles { Median = 1.0, P25 = 1.0, P75 = 1.0, P10 = 0.9, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "OTP", SenderTier = "Enterprise", SampleSizeHours = 1176, DeliveryRate = new MetricPercentiles { Median = 0.9664, P25 = 0.9412, P75 = 0.9796, P10 = 0.907, P90 = 0.991 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "OTP", SenderTier = "Standard", SampleSizeHours = 3428, DeliveryRate = new MetricPercentiles { Median = 0.9627, P25 = 0.91, P75 = 1.0, P10 = 0.8768, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "OTP", SenderTier = "Trial", SampleSizeHours = 728, DeliveryRate = new MetricPercentiles { Median = 1.0, P25 = 1.0, P75 = 1.0, P10 = 1.0, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "Promotional", SenderTier = "Enterprise", SampleSizeHours = 1176, DeliveryRate = new MetricPercentiles { Median = 0.953, P25 = 0.9252, P75 = 0.963, P10 = 0.899, P90 = 0.975 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.00219, P10 = 0.0, P90 = 0.003 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "Promotional", SenderTier = "Standard", SampleSizeHours = 3407, DeliveryRate = new MetricPercentiles { Median = 0.9494, P25 = 0.8993, P75 = 1.0, P10 = 0.8679, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "Promotional", SenderTier = "Trial", SampleSizeHours = 1105, DeliveryRate = new MetricPercentiles { Median = 0.9722, P25 = 0.9412, P75 = 1.0, P10 = 0.9078, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "Transactional", SenderTier = "Enterprise", SampleSizeHours = 1176, DeliveryRate = new MetricPercentiles { Median = 0.9598, P25 = 0.9325, P75 = 0.9709, P10 = 0.901, P90 = 0.982 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "Transactional", SenderTier = "Standard", SampleSizeHours = 3466, DeliveryRate = new MetricPercentiles { Median = 0.954, P25 = 0.9032, P75 = 1.0, P10 = 0.875, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "MTN", MessageType = "Transactional", SenderTier = "Trial", SampleSizeHours = 728, DeliveryRate = new MetricPercentiles { Median = 1.0, P25 = 1.0, P75 = 1.0, P10 = 1.0, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Telkom", MessageType = "OTP", SenderTier = "Standard", SampleSizeHours = 1169, DeliveryRate = new MetricPercentiles { Median = 0.9636, P25 = 0.9556, P75 = 1.0, P10 = 0.9487, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Telkom", MessageType = "Promotional", SenderTier = "Standard", SampleSizeHours = 1129, DeliveryRate = new MetricPercentiles { Median = 0.9565, P25 = 0.9464, P75 = 1.0, P10 = 0.938, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Telkom", MessageType = "Transactional", SenderTier = "Standard", SampleSizeHours = 1169, DeliveryRate = new MetricPercentiles { Median = 0.9589, P25 = 0.95, P75 = 1.0, P10 = 0.9422, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Vodacom", MessageType = "OTP", SenderTier = "Enterprise", SampleSizeHours = 1176, DeliveryRate = new MetricPercentiles { Median = 0.9825, P25 = 0.9753, P75 = 0.9888, P10 = 0.9712, P90 = 0.994 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Vodacom", MessageType = "OTP", SenderTier = "Standard", SampleSizeHours = 1101, DeliveryRate = new MetricPercentiles { Median = 1.0, P25 = 0.9815, P75 = 1.0, P10 = 0.9714, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Vodacom", MessageType = "Promotional", SenderTier = "Enterprise", SampleSizeHours = 1160, DeliveryRate = new MetricPercentiles { Median = 0.975, P25 = 0.9667, P75 = 1.0, P10 = 0.9615, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Vodacom", MessageType = "Promotional", SenderTier = "Standard", SampleSizeHours = 1176, DeliveryRate = new MetricPercentiles { Median = 0.9724, P25 = 0.9655, P75 = 1.0, P10 = 0.9612, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.00218, P10 = 0.0, P90 = 0.00315 } },
                new BenchmarkSegment { Carrier = "Vodacom", MessageType = "Transactional", SenderTier = "Enterprise", SampleSizeHours = 1176, DeliveryRate = new MetricPercentiles { Median = 0.9775, P25 = 0.9709, P75 = 0.9848, P10 = 0.9667, P90 = 0.988 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } },
                new BenchmarkSegment { Carrier = "Vodacom", MessageType = "Transactional", SenderTier = "Standard", SampleSizeHours = 1144, DeliveryRate = new MetricPercentiles { Median = 0.9821, P25 = 0.9714, P75 = 1.0, P10 = 0.9643, P90 = 1.0 }, OptOutRate = new MetricPercentiles { Median = 0.0, P25 = 0.0, P75 = 0.0, P10 = 0.0, P90 = 0.0 } }
            }
        };
    }
}
