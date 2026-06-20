namespace Hypercube.Industry;

/// <summary>
/// One rate-point per subject per cohort cell — the benchmark engine's input type.
/// </summary>
/// <param name="SubjectId">Sender identifier.</param>
/// <param name="Cohort">Cohort key identifying the peer group.</param>
/// <param name="Metric">Metric name, e.g. "delivery_rate".</param>
/// <param name="Rate">Computed rate value for this subject in this cohort cell.</param>
/// <param name="Volume">Event volume in this cohort cell (used for the per-metric volume floor).</param>
public sealed record SubjectAggregate(
    string SubjectId,
    CohortKey Cohort,
    string Metric,
    double Rate,
    long Volume);