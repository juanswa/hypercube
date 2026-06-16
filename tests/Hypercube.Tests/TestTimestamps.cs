namespace Hypercube.Tests;

/// <summary>
/// Fixed timestamps for snapshot and history helpers.
/// Ingest tests may still use wall-clock time; ordering tests should use these.
/// </summary>
internal static class TestTimestamps
{
    public static readonly DateTimeOffset Epoch = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public static DateTimeOffset AtMinutes(int minutes) => Epoch.AddMinutes(minutes);
}
