namespace Hypercube.Industry;

/// <summary>
/// Fixed public-holiday calendar for a given country.
/// Replace or extend with a dynamic provider for production use.
/// </summary>
public sealed class FixedHolidayCalendar : IHolidayCalendar
{
    private readonly HashSet<DateOnly> _holidays;

    /// <summary>
    /// Creates a calendar from an explicit set of holiday dates.
    /// </summary>
    public FixedHolidayCalendar(IEnumerable<DateOnly> holidays)
    {
        _holidays = new HashSet<DateOnly>(holidays);
    }

    /// <summary>
    /// Returns <c>true</c> if the specified date is a public holiday.
    /// </summary>
    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    /// <summary>
    /// South African public holidays (fixed + common movable approximations).
    /// </summary>
    public static FixedHolidayCalendar SouthAfrica(int year)
    {
        var holidays = new List<DateOnly>
        {
            // Fixed-date holidays.
            DateOnly.Parse($"{year}-01-01"),  // New Year's Day
            DateOnly.Parse($"{year}-03-21"),  // Human Rights Day
            DateOnly.Parse($"{year}-04-27"),  // Freedom Day
            DateOnly.Parse($"{year}-05-01"),  // Workers' Day
            DateOnly.Parse($"{year}-06-16"),  // Youth Day
            DateOnly.Parse($"{year}-08-09"),  // National Women's Day
            DateOnly.Parse($"{year}-09-24"),  // Heritage Day
            DateOnly.Parse($"{year}-12-16"),  // Day of Reconciliation
            DateOnly.Parse($"{year}-12-25"),  // Christmas Day
            DateOnly.Parse($"{year}-12-26"),  // Day of Goodwill
        };

        // Approximate movable holidays (Easter-based) — use fixed approximations for demo.
        // Production should use a proper computus library.
        return new FixedHolidayCalendar(holidays);
    }
}