namespace Hypercube.Industry;

/// <summary>
/// A single classified observation in a send report.
/// All numeric fields and classifications are set by <see cref="SendReportObservationEngine"/>.
/// </summary>
/// <param name="Dimension">Dimension name, e.g. "carrier" or "carrier|dow|hod".</param>
/// <param name="CellKey">Cell key within the dimension, e.g. "Vodacom" or "Vodacom|Sat|02".</param>
/// <param name="Metric">Metric name, e.g. "delivery_rate" or "failure_rate".</param>
/// <param name="Actual">Observed value in the current window.</param>
/// <param name="SelfExpected">Expected value from seasonality (self-history). Null if unavailable.</param>
/// <param name="CohortMedian">Median of the peer cohort distribution. Null if no benchmark available.</param>
/// <param name="CohortP25">25th percentile of the peer cohort. Null if no benchmark available.</param>
/// <param name="CohortP75">75th percentile of the peer cohort. Null if no benchmark available.</param>
/// <param name="CohortPeerCount">Number of peers in the resolved cohort cell. 0 if no benchmark.</param>
/// <param name="Deviation">Signed deviation from the relevant baseline (self-expected or cohort median).</param>
/// <param name="Kind">Classification of this observation.</param>
/// <param name="IsMaterial">Whether the deviation passes the materiality floor.</param>
/// <param name="IsFavorable">Direction-aware favourability. Null for Neutral metrics or WithinNormal.</param>
public sealed record Observation(
    string Dimension,
    string CellKey,
    string Metric,
    double Actual,
    double? SelfExpected,
    double? CohortMedian,
    double? CohortP25,
    double? CohortP75,
    int CohortPeerCount,
    double Deviation,
    ObservationKind Kind,
    bool IsMaterial,
    bool? IsFavorable);