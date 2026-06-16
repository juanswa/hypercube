namespace Hypercube.Core;

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
