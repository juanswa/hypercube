namespace Hypercube.Industry;

/// <summary>
/// Provides expected-value baselines from learned seasonality, never hardcoded constants.
/// </summary>
public interface ISeasonalityProfile
{
    /// <summary>
    /// Returns the expected value for a subject's own history at the given time bucket.
    /// </summary>
    double? ExpectedSelf(string subjectId, string metric, DayOfWeek dow, int hour, bool isHoliday);

    /// <summary>
    /// Returns the expected value for a cohort at the given time bucket.
    /// </summary>
    double? ExpectedCohort(CohortKey cohort, string metric, DayOfWeek dow, int hour, bool isHoliday);
}