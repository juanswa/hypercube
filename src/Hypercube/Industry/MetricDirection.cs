namespace Hypercube.Industry;

/// <summary>
/// Whether a higher or lower metric value is preferable.
/// </summary>
public enum MetricDirection
{
    /// <summary>Higher values are better (e.g. delivery_rate, reply_rate).</summary>
    HigherIsBetter,

    /// <summary>Lower values are better (e.g. failure_rate, optout_rate).</summary>
    LowerIsBetter,

    /// <summary>No directional preference (e.g. volume).</summary>
    Neutral
}