namespace Hypercube.Industry;

/// <summary>
/// Determines whether a given date is a public holiday for seasonality baselines.
/// </summary>
public interface IHolidayCalendar
{
    /// <summary>
    /// Returns <c>true</c> if the specified date is a public holiday.
    /// </summary>
    bool IsHoliday(DateOnly date);
}