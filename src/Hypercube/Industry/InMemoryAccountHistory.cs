namespace Hypercube.Industry;

/// <summary>
/// In-memory implementation of <see cref="IAccountHistory"/> for testing and demo.
/// </summary>
public sealed class InMemoryAccountHistory : IAccountHistory
{
    private readonly Dictionary<string, List<SummarySnapshot>> _history = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Appends a snapshot for the given subject.
    /// </summary>
    public void Append(string subjectId, SummarySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!_history.TryGetValue(subjectId, out var list))
        {
            list = new List<SummarySnapshot>();
            _history[subjectId] = list;
        }

        list.Add(snapshot);
    }

    /// <summary>
    /// Returns the most recent N windows for the given subject.
    /// </summary>
    public IReadOnlyList<SummarySnapshot> RecentWindows(string subjectId, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (!_history.TryGetValue(subjectId, out var list) || list.Count == 0)
        {
            return [];
        }

        var take = Math.Min(count, list.Count);
        return [.. list.Skip(list.Count - take)];
    }
}