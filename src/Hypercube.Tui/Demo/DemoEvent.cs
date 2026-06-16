namespace Hypercube.Tui.Demo;

/// <summary>Synthetic telemetry event used by the live dashboard demo.</summary>
public sealed record DemoEvent(
    DateTimeOffset Timestamp,
    string Channel,
    string Region,
    string UserId,
    double LatencyMs,
    bool Acknowledged);
