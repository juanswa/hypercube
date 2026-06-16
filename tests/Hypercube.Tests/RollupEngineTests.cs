using Hypercube.Core;
using Hypercube.Models;
using Xunit;

namespace Hypercube.Tests;

public sealed record TeamActivity(
    DateTimeOffset Timestamp,
    string Team,
    double Points,
    bool Acknowledged);

public sealed class RollupEngineTests
{
    private static readonly RollupSchema<TeamActivity> Schema = RollupSchema
        .For<TeamActivity>()
        .Dimension(e => e.Team)
        .Count()
        .Sum(e => e.Points)
        .CountWhen(e => e.Acknowledged, "signal")
        .Build();

    [Fact]
    public void Add_AppliesConfiguredDimensionsAndMetrics()
    {
        var sut = new RollupEngine<TeamActivity>(Schema, new RollupEngineOptions
        {
            MaxKeysPerDimension = 10,
            SpillDirectory = Path.Combine(Path.GetTempPath(), "hypercube-tests")
        });

        sut.Add(new TeamActivity(DateTimeOffset.UtcNow, "Core", 5, Acknowledged: false));
        sut.Add(new TeamActivity(DateTimeOffset.UtcNow, "Core", 7, Acknowledged: false));
        sut.Add(new TeamActivity(DateTimeOffset.UtcNow, "Core", 0, Acknowledged: true));

        var snapshot = sut.DeriveSnapshot();
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal("team", row.Dimension);
        Assert.Equal("core", row.Key);
        Assert.Equal(3, row.Count);
        Assert.Equal(1, row.SignalCount);
        Assert.Equal(12, row["points"]);
    }

    [Fact]
    public void Add_FansOutAcrossMultipleDimensions()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Dimension("status", e => e.Acknowledged ? "acknowledged" : "pending")
            .Count()
            .Build();

        var sut = new RollupEngine<TeamActivity>(schema);
        sut.Add(new TeamActivity(DateTimeOffset.UtcNow, "Core", 1, Acknowledged: true));
        sut.Add(new TeamActivity(DateTimeOffset.UtcNow, "Core", 1, Acknowledged: false));

        var snapshot = sut.DeriveSnapshot();
        Assert.Equal(3, snapshot.Rows.Count);
        Assert.Contains(snapshot.Rows, r => r.Dimension == "team" && r.Key == "core" && r.Count == 2);
        Assert.Contains(snapshot.Rows, r => r.Dimension == "status" && r.Key == "acknowledged" && r.Count == 1);
        Assert.Contains(snapshot.Rows, r => r.Dimension == "status" && r.Key == "pending" && r.Count == 1);
    }

    [Fact]
    public void Add_ContinuesAfterSpillToDisk()
    {
        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-spill-{Guid.NewGuid():N}");
        var sut = new RollupEngine<TeamActivity>(Schema, new RollupEngineOptions
        {
            MaxKeysPerDimension = 2,
            SpillDirectory = spillDir
        });

        for (var i = 0; i < 5; i++)
        {
            sut.Add(new TeamActivity(DateTimeOffset.UtcNow, $"team-{i}", 1, Acknowledged: false));
        }

        var snapshot = sut.DeriveSnapshot();
        Assert.Equal(5, snapshot.Rows.Count);
        Assert.All(snapshot.Rows, row => Assert.Equal(1, row.Count));
        Assert.True(Directory.EnumerateFiles(spillDir, "*.db").Any());
    }

    [Fact]
    public void Add_IsThreadSafeForSameCell()
    {
        var sut = new RollupEngine<TeamActivity>(Schema);
        var timestamp = DateTimeOffset.UtcNow;

        Parallel.For(0, 500, _ =>
            sut.Add(new TeamActivity(timestamp, "Core", 1, Acknowledged: false)));

        var row = Assert.Single(sut.DeriveSnapshot().Rows);
        Assert.Equal(500, row.Count);
        Assert.Equal(500, row["points"]);
    }

    [Fact]
    public void Add_IsThreadSafeForPercentileDigestDuringSpill()
    {
        var schema = RollupSchema
            .For<TeamActivity>()
            .Dimension(e => e.Team)
            .Count()
            .PercentileDigest(e => e.Points, "points")
            .Build();

        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-tdigest-spill-{Guid.NewGuid():N}");
        var sut = new RollupEngine<TeamActivity>(schema, new RollupEngineOptions
        {
            MaxKeysPerDimension = 2,
            SpillDirectory = spillDir
        });

        Parallel.For(0, 300, i =>
            sut.Add(new TeamActivity(DateTimeOffset.UtcNow, $"team-{i % 5}", i, Acknowledged: false)));

        var snapshot = sut.DeriveSnapshot();
        Assert.Equal(5, snapshot.Rows.Count);
        Assert.All(snapshot.Rows, row => Assert.Equal(60, row.Count));
        Assert.All(snapshot.Rows, row => Assert.True(row[MetricNameHelper.Mean("points")] > 0));
        Assert.True(Directory.EnumerateFiles(spillDir, "*.db").Any());
    }
}
