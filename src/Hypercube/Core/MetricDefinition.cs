namespace Hypercube.Core;

/// <summary>
/// Describes one metric and how it is computed from items of type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">The event type being aggregated.</typeparam>
public sealed class MetricDefinition<T>
{
    internal MetricDefinition(
        string name,
        AggregationKind kind,
        Func<T, double>? valueSelector = null,
        Func<T, bool>? predicate = null,
        Func<T, string>? stringSelector = null)
    {
        Name = name;
        Kind = kind;
        ValueSelector = valueSelector;
        Predicate = predicate;
        StringSelector = stringSelector;
    }

    /// <summary>Metric name exposed in <see cref="Models.SummaryRow.Metrics"/>.</summary>
    public string Name { get; }

    /// <summary>The aggregation operation applied to each ingested item.</summary>
    public AggregationKind Kind { get; }

    /// <summary>Number of <see cref="CellAggregateState.MetricValues"/> slots consumed by this metric.</summary>
    internal int SlotCount => Kind switch
    {
        AggregationKind.Average => 2,
        _ => 1
    };

    /// <summary>
    /// Selector for <see cref="AggregationKind.Sum"/>, <see cref="AggregationKind.Min"/>,
    /// <see cref="AggregationKind.Max"/>, and <see cref="AggregationKind.Average"/>.
    /// </summary>
    public Func<T, double>? ValueSelector { get; }

    /// <summary>
    /// Predicate for <see cref="AggregationKind.CountWhen"/>. <c>null</c> for other kinds.
    /// </summary>
    public Func<T, bool>? Predicate { get; }

    /// <summary>
    /// Selector for <see cref="AggregationKind.HyperLogLog"/> entity keys.
    /// </summary>
    public Func<T, string>? StringSelector { get; }
}
