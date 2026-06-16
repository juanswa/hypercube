using System.Linq.Expressions;

namespace Hypercube.Core;

/// <summary>
/// Supported aggregation operations for rollup metrics.
/// </summary>
public enum AggregationKind
{
    /// <summary>Increment a counter once per ingested item.</summary>
    Count,

    /// <summary>Sum a numeric field across ingested items.</summary>
    Sum,

    /// <summary>Track the minimum value of a numeric field.</summary>
    Min,

    /// <summary>Track the maximum value of a numeric field.</summary>
    Max,

    /// <summary>Increment a counter when a predicate evaluates to <c>true</c>.</summary>
    CountWhen,

    /// <summary>Streaming percentile digest over a numeric field.</summary>
    TDigest,

    /// <summary>Running arithmetic mean of a numeric field (sum and count tracked internally).</summary>
    Average,

    /// <summary>Approximate distinct count via HyperLogLog over a string entity key.</summary>
    HyperLogLog
}

/// <summary>
/// Describes one dimension used to slice rollup data.
/// </summary>
/// <typeparam name="T">The event type being aggregated.</typeparam>
public sealed class DimensionDefinition<T>(string name, Func<T, string> selector)
{
    /// <summary>Dimension name (for example <c>channel</c>, <c>status</c>).</summary>
    public string Name { get; } = name;

    /// <summary>Maps an item to the key used within this dimension.</summary>
    public Func<T, string> Selector { get; } = selector;
}

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

/// <summary>
/// Immutable rollup configuration: which dimensions to slice by, which metrics to compute,
/// and which metric is considered primary for downstream analysis.
/// </summary>
/// <typeparam name="T">The event type being aggregated.</typeparam>
public sealed class RollupSchema<T>
{
    internal RollupSchema(
        IReadOnlyList<DimensionDefinition<T>> dimensions,
        IReadOnlyList<MetricDefinition<T>> metrics,
        string primaryMetric)
    {
        if (dimensions.Count == 0)
        {
            throw new InvalidOperationException("At least one dimension must be configured.");
        }

        if (metrics.Count == 0)
        {
            throw new InvalidOperationException("At least one metric must be configured.");
        }

        Dimensions = dimensions;
        Metrics = metrics;
        PrimaryMetric = primaryMetric;
        MetricValueOffsets = BuildMetricOffsets(metrics);
        MetricValueSlotCount = MetricValueOffsets.Length == 0
            ? 0
            : MetricValueOffsets[^1] + metrics[^1].SlotCount;
    }

    /// <summary>All dimensions that each ingested item fans out across.</summary>
    public IReadOnlyList<DimensionDefinition<T>> Dimensions { get; }

    /// <summary>All metrics computed per dimension key.</summary>
    public IReadOnlyList<MetricDefinition<T>> Metrics { get; }

    /// <summary>
    /// Metric name used as the primary measure in insight and driver analysis.
    /// </summary>
    public string PrimaryMetric { get; }

    /// <summary>Offset into <see cref="CellAggregateState.MetricValues"/> for each metric index.</summary>
    internal int[] MetricValueOffsets { get; }

    /// <summary>Total number of scalar slots required for all configured metrics.</summary>
    internal int MetricValueSlotCount { get; }

    private static int[] BuildMetricOffsets(IReadOnlyList<MetricDefinition<T>> metrics)
    {
        var offsets = new int[metrics.Count];
        var cursor = 0;
        for (var i = 0; i < metrics.Count; i++)
        {
            offsets[i] = cursor;
            cursor += metrics[i].SlotCount;
        }

        return offsets;
    }
}

/// <summary>
/// Fluent builder for <see cref="RollupSchema{T}"/>.
/// </summary>
/// <typeparam name="T">The event type being aggregated.</typeparam>
public sealed class RollupSchemaBuilder<T>
{
    private readonly List<DimensionDefinition<T>> _dimensions = [];
    private readonly List<MetricDefinition<T>> _metrics = [];
    private string _primaryMetric = "count";

    /// <summary>
    /// Adds a named dimension using a custom key selector.
    /// </summary>
    /// <param name="name">Dimension name stored in snapshots.</param>
    /// <param name="selector">Maps each item to a key within this dimension.</param>
    public RollupSchemaBuilder<T> Dimension(string name, Func<T, string> selector)
    {
        _dimensions.Add(new DimensionDefinition<T>(Sanitizers.Normalize(name), selector));
        return this;
    }

    /// <summary>
    /// Adds a dimension using a property selector. The dimension name defaults to the property name.
    /// </summary>
    /// <param name="selector">Property expression returning the dimension key.</param>
    public RollupSchemaBuilder<T> Dimension(Expression<Func<T, string>> selector)
    {
        var name = Sanitizers.Normalize(GetMemberName(selector));
        return Dimension(name, selector.Compile());
    }

    /// <summary>
    /// Counts every ingested item under the given metric name.
    /// </summary>
    /// <param name="name">Metric name. Defaults to <c>count</c>.</param>
    public RollupSchemaBuilder<T> Count(string name = "count")
    {
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.Count));
        return this;
    }

    /// <summary>
    /// Sums a numeric field across ingested items.
    /// </summary>
    /// <param name="selector">Numeric property to sum.</param>
    /// <param name="name">Metric name. Defaults to the property name.</param>
    public RollupSchemaBuilder<T> Sum(Expression<Func<T, double>> selector, string? name = null)
    {
        name ??= GetMemberName(selector);
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.Sum, valueSelector: selector.Compile()));
        return this;
    }

    /// <summary>
    /// Tracks the minimum value of a numeric field.
    /// </summary>
    /// <param name="selector">Numeric property to minimize.</param>
    /// <param name="name">Metric name. Defaults to the property name.</param>
    public RollupSchemaBuilder<T> Min(Expression<Func<T, double>> selector, string? name = null)
    {
        name ??= GetMemberName(selector);
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.Min, valueSelector: selector.Compile()));
        return this;
    }

    /// <summary>
    /// Tracks the maximum value of a numeric field.
    /// </summary>
    /// <param name="selector">Numeric property to maximize.</param>
    /// <param name="name">Metric name. Defaults to the property name.</param>
    public RollupSchemaBuilder<T> Max(Expression<Func<T, double>> selector, string? name = null)
    {
        name ??= GetMemberName(selector);
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.Max, valueSelector: selector.Compile()));
        return this;
    }

    /// <summary>
    /// Counts items that satisfy a predicate.
    /// </summary>
    /// <param name="predicate">Condition evaluated per item.</param>
    /// <param name="name">Metric name for the conditional counter.</param>
    public RollupSchemaBuilder<T> CountWhen(Expression<Func<T, bool>> predicate, string name)
    {
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.CountWhen, predicate: predicate.Compile()));
        return this;
    }

    /// <summary>
    /// Tracks a streaming percentile digest (mean, p50, p95, p99) for a numeric field.
    /// </summary>
    /// <param name="selector">Numeric property to observe.</param>
    /// <param name="name">Base metric name. Defaults to the property name.</param>
    public RollupSchemaBuilder<T> PercentileDigest(Expression<Func<T, double>> selector, string? name = null)
    {
        name ??= GetMemberName(selector);
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.TDigest, valueSelector: selector.Compile()));
        return this;
    }

    /// <summary>
    /// Tracks the arithmetic mean of a numeric field.
    /// </summary>
    /// <param name="selector">Numeric property to average.</param>
    /// <param name="name">Metric name. Defaults to the property name.</param>
    public RollupSchemaBuilder<T> Average(Expression<Func<T, double>> selector, string? name = null)
    {
        name ??= GetMemberName(selector);
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.Average, valueSelector: selector.Compile()));
        return this;
    }

    /// <summary>
    /// Tracks approximate distinct counts for a string entity (for example user IDs or source IPs).
    /// </summary>
    /// <param name="selector">Entity key to observe.</param>
    /// <param name="name">Metric name. Defaults to the property name.</param>
    public RollupSchemaBuilder<T> HyperLogLog(Expression<Func<T, string>> selector, string? name = null)
    {
        name ??= GetMemberName(selector);
        _metrics.Add(new MetricDefinition<T>(name, AggregationKind.HyperLogLog, stringSelector: selector.Compile()));
        return this;
    }

    /// <summary>
    /// Sets the metric used as the primary measure in analysis. Defaults to <c>count</c>.
    /// </summary>
    /// <param name="name">An existing metric name from this schema.</param>
    public RollupSchemaBuilder<T> PrimaryMetric(string name)
    {
        _primaryMetric = name;
        return this;
    }

    /// <summary>
    /// Builds an immutable <see cref="RollupSchema{T}"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no dimensions or no metrics were configured.
    /// </exception>
    public RollupSchema<T> Build() => new(_dimensions, _metrics, _primaryMetric);

    private static string GetMemberName<TMember>(Expression<Func<T, TMember>> selector)
    {
        return selector.Body switch
        {
            MemberExpression member => member.Member.Name,
            UnaryExpression { Operand: MemberExpression unaryMember } => unaryMember.Member.Name,
            _ => throw new ArgumentException("Expression must be a member access.", nameof(selector))
        };
    }
}

/// <summary>
/// Entry point for creating <see cref="RollupSchema{T}"/> instances.
/// </summary>
public static class RollupSchema
{
    /// <summary>
    /// Starts building a rollup schema for <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The event type to configure.</typeparam>
    public static RollupSchemaBuilder<T> For<T>() => new();
}
