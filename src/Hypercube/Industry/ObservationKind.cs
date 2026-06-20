namespace Hypercube.Industry;

/// <summary>
/// Classification of a single observed deviation in a send-report.
/// Set deterministically by <see cref="SendReportObservationEngine"/>; never by the narrator.
/// </summary>
public enum ObservationKind
{
    /// <summary>
    /// Inside both self-history and cohort bands — no action needed, usually omitted from narrative.
    /// </summary>
    WithinNormal,

    /// <summary>
    /// Deviates from this account's own historical baseline, but within cohort norms.
    /// </summary>
    SelfAnomaly,

    /// <summary>
    /// Below the cohort p25 — under-performing vs peers.
    /// </summary>
    BelowPeers,

    /// <summary>
    /// Above the cohort p75 — over-performing vs peers.
    /// </summary>
    AbovePeers,

    /// <summary>
    /// Moved, but consistent with learned seasonality (e.g. holiday dip) — do not alarm.
    /// </summary>
    SeasonalExpected,

    /// <summary>
    /// Moved in the same direction as the cohort — external (network/time) effect, not the sender.
    /// </summary>
    SharedCause
}