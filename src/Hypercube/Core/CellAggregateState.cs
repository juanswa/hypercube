namespace Hypercube.Core;

/// <summary>
/// Serializable metric scalars for one rollup cell. Persisted by <see cref="State.LiteDbBackend{TValue}"/>
/// on spill; delegates and schema live only in the engine.
/// </summary>
public sealed class CellAggregateState
{
    /// <summary>Running metric values aligned with <see cref="RollupSchema{T}.Metrics"/> indices.</summary>
    public double[] MetricValues { get; set; } = [];

    /// <summary>
    /// Serialized sketch state per metric index. Populated only for disk persistence and hydration.
    /// </summary>
    public byte[][] SketchStates { get; set; } = [];

    /// <summary>
    /// Live sketch objects kept in memory during ingestion. Not serialized to LiteDB directly.
    /// </summary>
    internal object?[] ActiveSketches { get; set; } = [];

    /// <summary>Per-cell lock for concurrent ingestion and consistent reads.</summary>
    internal object Sync { get; } = new();
}
