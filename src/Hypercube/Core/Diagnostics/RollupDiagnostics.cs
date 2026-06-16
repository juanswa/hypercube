using System.Diagnostics.Metrics;

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

/// <summary>
/// OpenTelemetry-compatible diagnostics for the rollup engine.
/// </summary>
public sealed class RollupDiagnostics : IDisposable
{
    public const string MeterName = "Hypercube.Rollup";

    private readonly Meter _meter;
    private readonly Counter<long> _eventsProcessed;
    private readonly Counter<long> _memoryToDiskSpills;
    private readonly Counter<long> _ttlEvictions;
    private readonly Counter<long> _backpressureRejections;
    private readonly Counter<long> _lateEventDrops;
    private readonly Histogram<double> _aiInferenceDurationMs;
    private readonly Counter<long> _insightFailures;
    private long _eventsProcessedTotal;
    private long _memoryToDiskSpillsTotal;
    private long _ttlEvictionsTotal;
    private long _backpressureRejectionsTotal;
    private long _lateEventDropsTotal;
    private long _insightFailuresTotal;
    private double _lastAiInferenceDurationMs;

    public RollupDiagnostics()
    {
        _meter = new Meter(MeterName, "1.1.0");
        _eventsProcessed = _meter.CreateCounter<long>("hypercube.events_processed_total");
        _memoryToDiskSpills = _meter.CreateCounter<long>("hypercube.memory_to_disk_spills_total");
        _ttlEvictions = _meter.CreateCounter<long>("hypercube.ttl_evictions_total");
        _backpressureRejections = _meter.CreateCounter<long>("hypercube.backpressure_rejections_total");
        _lateEventDrops = _meter.CreateCounter<long>("hypercube.late_event_drops_total");
        _aiInferenceDurationMs = _meter.CreateHistogram<double>("hypercube.ai_inference_duration_ms");
        _insightFailures = _meter.CreateCounter<long>("hypercube.insight_failures_total");
    }

    /// <summary>Records a successfully ingested event.</summary>
    public void RecordEventProcessed(int count = 1)
    {
        _eventsProcessed.Add(count);
        Interlocked.Add(ref _eventsProcessedTotal, count);
    }

    /// <summary>Records a dimension store spill from memory to disk.</summary>
    public void RecordSpill(int count = 1)
    {
        _memoryToDiskSpills.Add(count);
        Interlocked.Add(ref _memoryToDiskSpillsTotal, count);
    }

    /// <summary>Records TTL scavenger evictions.</summary>
    public void RecordTtlEviction(int count = 1)
    {
        _ttlEvictions.Add(count);
        Interlocked.Add(ref _ttlEvictionsTotal, count);
    }

    /// <summary>Records backpressure rejections.</summary>
    public void RecordBackpressure(int count = 1)
    {
        _backpressureRejections.Add(count);
        Interlocked.Add(ref _backpressureRejectionsTotal, count);
    }

    /// <summary>Records events dropped by watermark lateness.</summary>
    public void RecordLateDrop(int count = 1)
    {
        _lateEventDrops.Add(count);
        Interlocked.Add(ref _lateEventDropsTotal, count);
    }

    /// <summary>Records local AI inference duration in milliseconds.</summary>
    public void RecordAiInference(double durationMs)
    {
        _aiInferenceDurationMs.Record(durationMs);
        Volatile.Write(ref _lastAiInferenceDurationMs, durationMs);
    }

    /// <summary>Records a failed background insight refresh.</summary>
    public void RecordInsightFailure(int count = 1)
    {
        _insightFailures.Add(count);
        Interlocked.Add(ref _insightFailuresTotal, count);
    }

    /// <summary>Captures current counter totals for UI and health endpoints.</summary>
    public RollupDiagnosticsSnapshot CaptureSnapshot() => new(
        Interlocked.Read(ref _eventsProcessedTotal),
        Interlocked.Read(ref _memoryToDiskSpillsTotal),
        Interlocked.Read(ref _ttlEvictionsTotal),
        Interlocked.Read(ref _backpressureRejectionsTotal),
        Interlocked.Read(ref _lateEventDropsTotal),
        Volatile.Read(ref _lastAiInferenceDurationMs),
        Interlocked.Read(ref _insightFailuresTotal));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
