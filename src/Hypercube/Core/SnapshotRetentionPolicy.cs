namespace Hypercube.Core;

/// <summary>
/// Policy for pruning historical spill artifacts.
/// </summary>
public sealed class SnapshotRetentionPolicy
{
    /// <summary>Delete spill files older than this age. <c>null</c> disables age pruning.</summary>
    public TimeSpan? MaxAge { get; init; }

    /// <summary>Keep at most this many spill database files per directory. <c>0</c> disables count pruning.</summary>
    public int MaxSpillFiles { get; init; }
}
