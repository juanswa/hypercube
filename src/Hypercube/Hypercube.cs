namespace Hypercube;

/// <summary>
/// Public entry points for constructing and configuring rollup engines.
/// </summary>
public static class Hypercube
{
    /// <summary>
    /// Creates a streaming rollup engine for the supplied schema and optional lifecycle configuration.
    /// </summary>
    public static RollupEngine<T> CreateEngine<T>(
        RollupSchema<T> schema,
        EngineConfiguration? configuration = null) =>
        new(schema, configuration);

    /// <summary>
    /// Creates a rollup engine using legacy <see cref="RollupEngineOptions"/>.
    /// </summary>
    public static RollupEngine<T> CreateEngine<T>(
        RollupSchema<T> schema,
        RollupEngineOptions options) =>
        new(schema, options);
}
