namespace Hypercube.Industry;

/// <summary>
/// Provides recent historical snapshots for a subject's account.
/// </summary>
public interface IAccountHistory
{
    /// <summary>
    /// Returns the most recent N windows for the given subject.
    /// </summary>
    IReadOnlyList<SummarySnapshot> RecentWindows(string subjectId, int count);
}