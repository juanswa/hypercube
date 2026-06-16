namespace Hypercube.State.Parquet;

/// <summary>Result row from projected spill enumeration.</summary>
public readonly record struct ProjectedCellRow(
    string Key,
    IReadOnlyDictionary<string, double> Metrics,
    CellAggregateState? State);
