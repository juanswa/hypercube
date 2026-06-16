namespace Hypercube.Core.Diagnostics;

/// <summary>Point-in-time rollup diagnostics for dashboards and health checks.</summary>
public readonly record struct RollupDiagnosticsSnapshot(
    long EventsProcessed,
    long MemoryToDiskSpills,
    long TtlEvictions,
    long BackpressureRejections,
    long LateEventDrops,
    double LastAiInferenceDurationMs,
    long InsightFailures);
