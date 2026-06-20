namespace Hypercube.Industry;

/// <summary>
/// Industry-specific plugin that wires together schemas, seasonality, benchmarks, and narration.
/// </summary>
/// <typeparam name="TEvent">The raw event type for this industry (e.g. SmsEvent).</typeparam>
public interface IIndustryPlugin<TEvent>
{
    /// <summary>Stable industry key, e.g. "sms".</summary>
    string IndustryKey { get; }

    /// <summary>Schema for per-account (subject-level) rollup.</summary>
    RollupSchema<TEvent> BuildSubjectSchema();

    /// <summary>Schema for cross-subject benchmark rollup (cohort distributions).</summary>
    RollupSchema<SubjectAggregate> BuildBenchmarkSchema();

    /// <summary>Categorises a raw event into its send category (OTP / Transactional / Promotional).</summary>
    string CategorizeSend(TEvent e);

    /// <summary>Returns the directional preference for a metric.</summary>
    MetricDirection DirectionOf(string metric);

    /// <summary>Holiday calendar for the target region.</summary>
    IHolidayCalendar Calendar { get; }

    /// <summary>Learned seasonality profile.</summary>
    ISeasonalityProfile Seasonality { get; }

    /// <summary>Peer benchmark provider.</summary>
    IBenchmarkProvider Benchmarks { get; }

    /// <summary>Narrative templates and selection logic.</summary>
    INarrativeTemplates Narrative { get; }
}