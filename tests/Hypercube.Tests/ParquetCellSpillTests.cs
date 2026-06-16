namespace Hypercube.Tests;

public sealed class ParquetCellSpillTests
{
    private static readonly RollupSchema<SpillProbeEvent> Schema = RollupSchema
        .For<SpillProbeEvent>()
        .Dimension(e => e.Region)
        .Count()
        .Sum(e => e.Amount, "amount_sum")
        .PercentileDigest(e => e.Amount, "amount")
        .Build();

    [Fact]
    public async Task ParquetCellSpillBackend_WritesColumnarSchema()
    {
        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-columnar-{Guid.NewGuid():N}");
        var engine = new RollupEngine<SpillProbeEvent>(Schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 1,
            SpillBackend = SpillBackendKind.Parquet,
            SpillDirectory = spillDir
        });

        engine.Add(new SpillProbeEvent("east", 10));
        engine.Add(new SpillProbeEvent("west", 20));
        engine.FlushSpill();

        var path = Directory.EnumerateFiles(spillDir, "*.parquet").Single();
        await using var stream = File.OpenRead(path);
        using var reader = await ParquetReader.CreateAsync(stream);
        var fieldNames = reader.Schema.DataFields.Select(static field => field.Name).ToArray();

        Assert.Contains("key", fieldNames);
        Assert.Contains("last_access_ticks", fieldNames);
        Assert.Contains("v_0", fieldNames);
        Assert.Contains("v_1", fieldNames);
        Assert.Contains("sk_2", fieldNames);
        Assert.DoesNotContain("payload", fieldNames);
    }

    [Fact]
    public void DeriveSnapshot_WithMetricProjection_ReturnsOnlyRequestedMetrics()
    {
        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-projection-{Guid.NewGuid():N}");
        var engine = new RollupEngine<SpillProbeEvent>(Schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 1,
            SpillBackend = SpillBackendKind.Parquet,
            SpillDirectory = spillDir,
            MaxLiveCacheKeys = 0
        });

        engine.Add(new SpillProbeEvent("east", 10));
        engine.Add(new SpillProbeEvent("west", 20));
        engine.Add(new SpillProbeEvent("north", 30));

        engine.FlushSpill();
        var projected = engine.DeriveSnapshot(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "count" });
        Assert.Equal(3, projected.Rows.Count);
        Assert.All(projected.Rows, row =>
        {
            Assert.Single(row.Metrics);
            Assert.True(row.Metrics.ContainsKey("count"));
            Assert.False(row.Metrics.ContainsKey("amount_sum"));
            Assert.False(row.Metrics.ContainsKey(MetricNameHelper.Mean("amount")));
        });

        var full = engine.DeriveSnapshot();
        var east = Assert.Single(full.Rows, row => row.Key == "east");
        Assert.True(east.Metrics.ContainsKey("amount_sum"));
        Assert.True(east.Metrics.ContainsKey(MetricNameHelper.Mean("amount")));
    }

    [Fact]
    public void ParquetCellSpillBackend_ProjectedRead_SkipsDigestMetricsWhenNotRequested()
    {
        var spillDir = Path.Combine(Path.GetTempPath(), $"hypercube-projection-read-{Guid.NewGuid():N}");
        var engine = new RollupEngine<SpillProbeEvent>(Schema, new EngineConfiguration
        {
            MaxKeysPerDimension = 1,
            SpillBackend = SpillBackendKind.Parquet,
            SpillDirectory = spillDir,
            MaxLiveCacheKeys = 0
        });

        engine.Add(new SpillProbeEvent("east", 42));
        engine.Add(new SpillProbeEvent("west", 7));
        engine.FlushSpill();

        var path = Directory.EnumerateFiles(spillDir, "*.parquet").Single();
        using var backend = new ParquetCellSpillBackend<SpillProbeEvent>(path, Schema, maxHotKeys: 0);
        var projected = backend
            .EnumerateProjected(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "count", "amount_sum" })
            .ToList();

        Assert.Equal(2, projected.Count);
        var east = Assert.Single(projected, row => row.Key == "east");
        Assert.Equal(1, east.Metrics["count"]);
        Assert.Equal(42, east.Metrics["amount_sum"]);
        Assert.DoesNotContain(MetricNameHelper.Mean("amount"), east.Metrics.Keys);
    }

    private sealed record SpillProbeEvent(string Region, double Amount);
}
