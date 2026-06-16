namespace Hypercube.State.Parquet;

/// <summary>
/// Optional capability for spill backends that can enumerate metric projections without full hydration.
/// </summary>
public interface IMetricProjectableCellBackend<T>
{
    /// <summary>Enumerates rows with metric-only column projection from disk plus hot-cache overlay.</summary>
    IEnumerable<ProjectedCellRow> EnumerateProjected(IReadOnlySet<string>? metricProjection);
}
