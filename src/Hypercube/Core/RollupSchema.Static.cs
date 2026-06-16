namespace Hypercube.Core;

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
