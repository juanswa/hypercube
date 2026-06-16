namespace Hypercube.Core;

/// <summary>
/// Maintains spill-directory retention for long-running rollup deployments.
/// </summary>
public static class SnapshotRetentionManager
{
    /// <summary>
    /// Prunes spill database files according to <paramref name="policy"/>.
    /// </summary>
    /// <param name="spillDirectory">Directory containing dimension spill <c>.db</c> files.</param>
    /// <param name="policy">Retention limits to apply.</param>
    /// <returns>Number of files deleted.</returns>
    public static int PruneSpillDirectory(string spillDirectory, SnapshotRetentionPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spillDirectory);
        ArgumentNullException.ThrowIfNull(policy);

        if (!Directory.Exists(spillDirectory))
        {
            return 0;
        }

        var files = Directory
            .EnumerateFiles(spillDirectory, "*.db", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        var deleted = 0;
        var cutoff = policy.MaxAge is { } maxAge
            ? DateTimeOffset.UtcNow - maxAge
            : (DateTimeOffset?)null;

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var tooOld = cutoff is not null && file.LastWriteTimeUtc < cutoff.Value.UtcDateTime;
            var overCount = policy.MaxSpillFiles > 0 && i >= policy.MaxSpillFiles;
            if (!tooOld && !overCount)
            {
                continue;
            }

            file.Delete();
            deleted++;
        }

        return deleted;
    }
}
