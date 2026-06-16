namespace Hypercube.Core;

/// <summary>Time-window aggregation strategy.</summary>
public enum WindowStrategy
{
    /// <summary>No windowing; aggregates until <see cref="RollupEngine{T}.Clear"/>.</summary>
    Continuous,

    /// <summary>Non-overlapping fixed windows.</summary>
    Tumbling,

    /// <summary>Overlapping windows that slide forward.</summary>
    Sliding,

    /// <summary>Gap-based sessions; a new window opens after inactivity.</summary>
    Session
}
