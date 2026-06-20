namespace Hypercube.Industry;

/// <summary>
/// Uniquely identifies a cohort cell for benchmark lookups.
/// </summary>
/// <param name="Tier">Account tier, e.g. "Enterprise".</param>
/// <param name="Vertical">Industry vertical, e.g. "retail".</param>
/// <param name="Carrier">SMS carrier, e.g. "Vodacom".</param>
/// <param name="MessageType">Send category: "OTP", "Transactional", or "Promotional".</param>
/// <param name="DayBucket">Day-of-week bucket, e.g. "weekday" or "weekend".</param>
/// <param name="HourBucket">Hour-of-day bucket, e.g. "02" (zero-padded hour).</param>
public sealed record CohortKey(
    string Tier,
    string Vertical,
    string Carrier,
    string MessageType,
    string DayBucket,
    string HourBucket);