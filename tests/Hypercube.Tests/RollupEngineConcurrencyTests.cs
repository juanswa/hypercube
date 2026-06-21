using System.Threading;
using System.Threading.Tasks;
using Hypercube.State;

namespace Hypercube.Tests;

public sealed class RollupEngineConcurrencyTests
{
    [Fact]
    public void TryAdd_IsThreadSafe_WithParquetSpillBackend()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Count()
            .Sum(e => e.Points)
            .Build();

        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-parquet-concurrent-{Guid.NewGuid():N}");
        using var engine = new RollupEngine<TeamActivity>(schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 2,
            SpillBackend = SpillBackendKind.Parquet,
            SpillDirectory = spillDir,
            MaxLiveCacheKeys = 0
        });

        var timestamp = DateTimeOffset.UtcNow;
        Parallel.For(0, 400, i =>
            engine.TryAdd(new TeamActivity(timestamp, $"team-{i % 8}", 1, Acknowledged: false)));

        engine.FlushSpill();
        var snapshot = engine.DeriveSnapshot();
        Assert.Equal(8, snapshot.Rows.Count);
        Assert.Equal(400, snapshot.Rows.Sum(static row => row.Count));
        Assert.All(snapshot.Rows, row => Assert.Equal(50, row.Count));
    }

    [Fact]
    public void RollupEngine_WithBoundedHotCache_ParquetSpillBackend_PreservesConcurrentSameKeyUpdates()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Count()
            .Build();

        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-parquet-hotcache-{Guid.NewGuid():N}");
        using var engine = new RollupEngine<TeamActivity>(schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 1,
            SpillBackend = SpillBackendKind.Parquet,
            SpillDirectory = spillDir,
            MaxLiveCacheKeys = 1
        });

        var timestamp = DateTimeOffset.UtcNow;
        Parallel.Invoke(
            () => engine.Add(new TeamActivity(timestamp, "key", 1, Acknowledged: false)),
            () => engine.Add(new TeamActivity(timestamp, "other", 1, Acknowledged: false)),
            () => engine.Add(new TeamActivity(timestamp, "key", 1, Acknowledged: false)));

        engine.FlushSpill();
        var snapshot = engine.DeriveSnapshot();
        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal(2, snapshot.Rows.Single(row => row.Key == "key").Count);
    }

    [Fact]
    public void RollupEngine_WithBoundedLiveCache_LiteDbSpillBackend_PreservesConcurrentSameKeyUpdates()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Count()
            .Build();

        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-litedb-hotcache-{Guid.NewGuid():N}");
        using var engine = new RollupEngine<TeamActivity>(schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 2,
            SpillBackend = SpillBackendKind.LiteDb,
            SpillDirectory = spillDir,
            MaxLiveCacheKeys = 1
        });

        var timestamp = DateTimeOffset.UtcNow;
        Parallel.Invoke(
            () => engine.Add(new TeamActivity(timestamp, "key", 1, Acknowledged: false)),
            () => engine.Add(new TeamActivity(timestamp, "other", 1, Acknowledged: false)),
            () => engine.Add(new TeamActivity(timestamp, "key", 1, Acknowledged: false)));

        engine.FlushSpill();
        var snapshot = engine.DeriveSnapshot();
        Assert.Equal(2, snapshot.Rows.Count);
        Assert.Equal(2, snapshot.Rows.Single(row => row.Key == "key").Count);
    }

    [Fact]
    public void Dispose_FlushesParquetSpillBackends()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Count()
            .Build();

        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-parquet-dispose-{Guid.NewGuid():N}");
        var engine = new RollupEngine<TeamActivity>(schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 1,
            SpillBackend = SpillBackendKind.Parquet,
            SpillDirectory = spillDir
        });

        engine.Add(new TeamActivity(DateTimeOffset.UtcNow, "alpha", 1, Acknowledged: false));
        engine.Add(new TeamActivity(DateTimeOffset.UtcNow, "beta", 1, Acknowledged: false));
        engine.FlushSpill();
        engine.Dispose();

        Assert.NotEmpty(Directory.EnumerateFiles(spillDir, "*.parquet"));
    }

    [Fact]
    public void Dispose_FlushesDirtyCells_LiteDbSpillBackend()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Count()
            .Build();

        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-litedb-dispose-{Guid.NewGuid():N}");
        var ts = DateTimeOffset.UtcNow;

        var engine = new RollupEngine<TeamActivity>(schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 1,
            SpillBackend = SpillBackendKind.LiteDb,
            SpillDirectory = spillDir,
            MaxLiveCacheKeys = 0
        });

        engine.Add(new TeamActivity(ts, "alpha", 1, Acknowledged: false));
        engine.Add(new TeamActivity(ts, "beta", 1, Acknowledged: false));  // triggers spill of alpha (count=1) to disk
        engine.Add(new TeamActivity(ts, "alpha", 1, Acknowledged: false)); // mutates spilled alpha -> count=2, dirty in RAM only

        engine.Dispose();                    // MUST flush dirty alpha to disk

        // Reopen the spilled file directly; assert the dirty mutation survived.
        var dbPath = Directory.EnumerateFiles(spillDir, "*.db").Single();
        using var backend = new LiteDbBackend<CellAggregateState>(dbPath, "dimension_team");
        Assert.True(backend.TryGet("alpha", out var state));
        Assert.Equal(2d, state!.MetricValues[0]); // Count metric, slot 0 — 2 alpha adds, not 1
    }

    [Fact]
    public void GetOrAdd_ConcurrentSameKey_NoDuplicateKeyException_ExactlyOneRowPersisted()
    {
        const int threadCount = 64;
        // Use an in-memory LiteDB connection to avoid file-handle races in test cleanup.
        using var backend = new LiteDbBackend<string>(":memory:", "test_collection");

        Parallel.For(0, threadCount, _ =>
        {
            backend.GetOrAdd("shared-key", () => "shared-value");
        });

        Assert.Equal(1, backend.Count);
        Assert.Equal("shared-value", backend.GetOrAdd("shared-key", () => "should-not-be-called"));
    }

    private sealed record TeamActivity(
        DateTimeOffset Timestamp,
        string Team,
        double Points,
        bool Acknowledged);
}
