# Hypercube

Streaming rollup engine for .NET with deterministic insights, columnar Parquet spill, and an optional Spectre.Console live dashboard. Runs fully offline — no cloud dependencies.

**Version:** 1.1

## Features

- **Streaming rollups** — Count, sum, min/max, averages, TDigest percentiles, HyperLogLog distinct counts, and conditional counters across multiple dimensions.
- **Hot/cold tiering** — Bounded in-memory LRU per dimension; cold cells spill to LiteDB or columnar Parquet with background flush.
- **Event-time windows** — Tumbling, sliding, and session windows with watermark lateness and backpressure.
- **Deterministic insights** — Offline anomaly ranking, driver decomposition, Simpson-style paradox detection, distribution shape analysis, and co-movement discovery.
- **Local AI layer** — Rule-based engine today; ONNX hook for narrative generation. Similarity caching and diagnostics timing included.
- **Terminal dashboard** — Live sparklines, digest histograms, and background insight refresh (`Hypercube.Tui`).

## Quickstart

### Library

```csharp
using Hypercube;
using Hypercube.Core;
using Hypercube.Models;

var schema = RollupSchema
    .For<MyEvent>()
    .Dimension(e => e.Region)
    .Count()
    .Sum(e => e.Amount, "revenue")
    .PrimaryMetric("count")
    .Build();

var engine = Hypercube.CreateEngine(schema, new EngineConfiguration
{
    MaxKeysPerDimension = 100_000,
    SpillBackend = SpillBackendKind.Parquet,
    EnableDiagnostics = true
});

engine.Add(new MyEvent("east", 42.0));
var snapshot = engine.DeriveSnapshot();
```

### Live dashboard (demo)

```bash
dotnet run --project src/Hypercube.Tui
```

Optional refresh interval in milliseconds: `dotnet run --project src/Hypercube.Tui -- 250`

### SMS campaign audit (deterministic synthetic stream)

```bash
dotnet run --project src/Hypercube.Tui -- --campaign 2000000
```

Useful options:

- `--campaign <count>` — run the campaign build demo for a specific message count
- `--download-models` / `--setup-ai` — download local ONNX model weights
- `--live [refresh-ms]` — force live dashboard mode

What the SMS audit models and reports (deterministic):

- Delivery outcomes: `DELIVRD`, `EXPIRED`, `UNDELIV`, `REJECTD`, `SPAM`, `CANCELLED`
- Engagement outcomes: `Replies`, `Opt-outs`
- Engagement rates: `reply_rate` (higher is better), `opt_out_rate` (lower is better)
- Interpretation guidance:
  - Rising `opt_out_rate` versus baseline/peers is a poor-campaign signal (message quality/frequency risk)
  - High `reply_rate` is a positive CTA/engagement signal
  - Practical opt-out pressure bands: `< 1.5%` healthy, `1.5%–3.5%` watch, `> 3.5%` critical

Report output:

- Campaign audit markdown is written under the TUI runtime output folder:
  - `src/Hypercube.Tui/bin/<Configuration>/<TargetFramework>/reports/`
- Each run writes a timestamped file:
  - `campaign-audit-yyyyMMdd-HHmmss-fff.md`

### Inject your own engine into the dashboard

```csharp
using Hypercube;
using Hypercube.Core;
using Hypercube.Tui.Dashboard;
using Hypercube.Tui.Demo;

var schema = RollupSchema.For<DemoEvent>() /* ... */ .Build();
var engine = Hypercube.CreateEngine(schema);
var dashboard = new LiveRollupDashboard(engine);
dashboard.Run(TimeSpan.FromMilliseconds(500));
```

## Project layout

| Path | Purpose |
|------|---------|
| `src/Hypercube/` | Core rollup engine, insights, Parquet spill, visualization helpers |
| `src/Hypercube.Tui/` | Interactive terminal dashboard and synthetic demo stream |
| `tests/Hypercube.Tests/` | xUnit test suite |

## Insight engines

All insight algorithms operate on immutable `SummarySnapshot` data — no external services required.

| Engine | What it finds |
|--------|----------------|
| `DeterministicInsightEngine` | Ranks interesting cells via within-dimension surprise, historical z-scores, and EWMA shifts |
| `DeterministicInsightEngine.AnalyzeDrivers` | Attributes total primary-metric change to individual cells |
| `DeterministicInsightEngine.DetectSimpsonsParadox` | Flags pooled vs. sibling rate reversals within a dimension |
| `DistributionShapeEngine` | Skewed latency tails; suggests median/p95 for alerting |
| `CoMovementEngine` | EWMA-smoothed pairwise correlations with optional lead/lag |

### Insight kinds (`InsightKind`)

- **DeviationFromExpectation** — Cell primary metric differs from a uniform share within its dimension.
- **ZScoreOutlier** — Cell is an outlier vs. the previous window's distribution.
- **EwmaTrendShift** — Material shift from an exponentially weighted moving average baseline.
- **SimpsonsParadox** — Sibling signal-rates moved opposite to the pooled rate (weight-shift paradox).

## Performance notes

| Component | Complexity | Notes |
|-----------|------------|-------|
| Ingest / per-cell update | O(metrics) per event | Hot cells stay in memory; cold cells rehydrate on access |
| `DeriveSnapshot` | O(cells × metrics) | Metric projection reduces Parquet read cost when configured |
| `CoMovementEngine` | **O(n²)** in active cells | Capped at **200 cells** by default (`CoMovementOptions.MaxActiveCellsForPairwise`). Each surviving pair evaluates up to `2 × MaxLag + 1` lag offsets. Suitable for dashboard-scale cardinality; raise the cap only with intent. |
| Insight refresh (TUI) | Latest-wins | Background worker coalesces stale refreshes; only the newest snapshot pair is analyzed |

## Configuration highlights

```csharp
new EngineConfiguration
{
    MaxKeysPerDimension = 100_000,
    DimensionTimeToLive = TimeSpan.FromHours(24),
    SpillBackend = SpillBackendKind.Parquet,  // or LiteDb
    SpillDirectory = Path.Combine(AppContext.BaseDirectory, "spill"),
    MaxInFlightAdds = 0,                        // 0 = unlimited; set for backpressure
    AllowedLateness = TimeSpan.FromMinutes(5),  // null = no watermark
    EnableDiagnostics = true
}
```

## Local AI API

```csharp
public interface ILocalAiEngine
{
    AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5);
    string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis);
}
```

Implementations: `RuleBasedLocalAiEngine` (deterministic), `CachedLocalAiEngine` (similarity cache), `OnnxLocalAiEngine` (placeholder ONNX path).

## Build and test

```bash
dotnet build Hypercube.sln
dotnet test tests/Hypercube.Tests/Hypercube.Tests.csproj
```

## Debugging tests

- Rider/JetBrains workflow: run tests from the test explorer and attach the debugger to `testhost` when needed.
- If using runsettings for debugging stability, use `test.runsettings` (`MaxCpuCount=1`).
