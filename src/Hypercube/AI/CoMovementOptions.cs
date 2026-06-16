namespace Hypercube.AI;

/// <summary>
/// Tunable parameters for <see cref="CoMovementEngine"/>.
/// </summary>
public sealed class CoMovementOptions
{
    /// <summary>
    /// EWMA smoothing coefficient. Higher values react faster to short-term bursts;
    /// lower values emphasize long-horizon macro shifts. Default is <c>0.35</c>.
    /// </summary>
    public double EwmaAlpha { get; init; } = 0.35;

    /// <summary>Maximum snapshot lag to test in each direction.</summary>
    public int MaxLag { get; init; } = 3;

    /// <summary>Maximum pairs to return.</summary>
    public int TopN { get; init; } = 10;

    /// <summary>Minimum absolute correlation to include.</summary>
    public double MinAbsCorrelation { get; init; } = 0.7;

    /// <summary>
    /// Maximum active cells considered for pairwise correlation.
    /// When exceeded, cells with the highest EWMA variance are retained.
    /// Pairwise work is <c>O(n²)</c> in this cap (default 200 → up to ~19,900 pairs per run).
    /// </summary>
    public int MaxActiveCellsForPairwise { get; init; } = 200;
}
