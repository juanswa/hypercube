using System.Linq.Expressions;

namespace Hypercube.Core;

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
    /// Tracks a volume-weighted ratio: sum(<paramref name="numerator"/>) / sum(<paramref name="denominator"/>).
    /// Large and small contributors are weighted by denominator, unlike an unweighted mean of per-event ratios.
    /// </summary>
    public RollupSchemaBuilder<T> Ratio(
        Expression<Func<T, double>> numerator,
        Expression<Func<T, double>> denominator,
        string name)
    {
        _metrics.Add(new MetricDefinition<T>(
            name,
            AggregationKind.Ratio,
            valueSelector: numerator.Compile(),
            denominatorSelector: denominator.Compile()));
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
