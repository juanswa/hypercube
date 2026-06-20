namespace Hypercube.Industry;

/// <summary>
/// No-op seasonality profile that always returns null.
/// The observation engine must handle absent seasonality gracefully.
/// </summary>
public sealed class NullSeasonalityProfile : ISeasonalityProfile
{
    /// <summary>Singleton instance.</summary>
    public static NullSeasonalityProfile Instance { get; } = new();

    private NullSeasonalityProfile()
    {
    }

    public double? ExpectedSelf(string subjectId, string metric, DayOfWeek dow, int hour, bool isHoliday) => null;

    public double? ExpectedCohort(CohortKey cohort, string metric, DayOfWeek dow, int hour, bool isHoliday) => null;
}