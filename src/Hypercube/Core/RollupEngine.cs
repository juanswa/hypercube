using System.Threading;
using Hypercube.Models;

namespace Hypercube.Core;

/// <summary>
/// Streaming rollup engine for events of type <typeparamref name="T"/>.
/// Ingests items via <see cref="Add"/> and materializes aggregated results via
/// <see cref="DeriveSnapshot"/>.
/// </summary>
/// <typeparam name="T">The event or fact type to aggregate.</typeparam>
public sealed class RollupEngine<T>
{
    private readonly RollupEngineOptions _options;
    private readonly Dictionary<string, DimensionStore<CellAggregateState>> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _storeGate = new();
    private readonly SemaphoreSlim? _inflightGate;
    private readonly WatermarkTracker? _watermark;

    /// <summary>
    /// Creates a rollup engine using the supplied schema and optional runtime options.
    /// </summary>
    /// <param name="schema">Dimension and metric configuration for <typeparamref name="T"/>.</param>
    /// <param name="options">Spill and cardinality limits. Uses defaults when <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="schema"/> is <c>null</c>.</exception>
    public RollupEngine(RollupSchema<T> schema, RollupEngineOptions? options = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _options = options ?? new RollupEngineOptions();
        Directory.CreateDirectory(_options.SpillDirectory);

        if (_options.MaxInFlightAdds > 0)
        {
            _inflightGate = new SemaphoreSlim(_options.MaxInFlightAdds, _options.MaxInFlightAdds);
        }

        if (_options.AllowedLateness is { } allowedLateness)
        {
            _watermark = new WatermarkTracker(allowedLateness);
        }
    }

    /// <summary>
    /// The schema that defines how items are sliced and aggregated.
    /// </summary>
    public RollupSchema<T> Schema { get; }

    /// <summary>
    /// Current event-time watermark when <see cref="RollupEngineOptions.AllowedLateness"/> is configured.
    /// </summary>
    public WatermarkTracker? Watermark => _watermark;

    /// <summary>
    /// Ingests one item and updates every configured dimension.
    /// </summary>
    /// <param name="item">The event to aggregate.</param>
    public void Add(T item) => AddCore(item);

    /// <summary>
    /// Attempts to ingest one item, applying backpressure and watermark policies when configured.
    /// </summary>
    /// <param name="item">The event to aggregate.</param>
    /// <param name="eventTime">Optional event timestamp for watermark evaluation.</param>
    public RollupAddResult TryAdd(T item, DateTimeOffset? eventTime = null)
    {
        if (_inflightGate is not null && !_inflightGate.Wait(0))
        {
            return RollupAddResult.Backpressure;
        }

        try
        {
            if (eventTime is not null && _watermark is not null)
            {
                if (_watermark.IsLate(eventTime.Value))
                {
                    return RollupAddResult.DroppedLate;
                }

                _watermark.Advance(eventTime.Value);
            }

            AddCore(item);
            return RollupAddResult.Accepted;
        }
        finally
        {
            _inflightGate?.Release();
        }
    }

    /// <summary>
    /// Builds a snapshot of all dimension/key cells and their current metric values.
    /// </summary>
    /// <returns>A new <see cref="SummarySnapshot"/> with UTC generation time.</returns>
    public SummarySnapshot DeriveSnapshot()
    {
        DimensionStore<CellAggregateState>[] stores;
        lock (_storeGate)
        {
            stores = [.. _stores.Values];
        }

        var rows = new List<SummaryRow>();
        foreach (var store in stores)
        {
            foreach (var (key, state) in store.Enumerate())
            {
                rows.Add(new SummaryRow(store.DimensionName, key, CellAggregator<T>.ToValues(Schema, state)));
            }
        }

        return new SummarySnapshot(DateTimeOffset.UtcNow, rows, Schema.PrimaryMetric);
    }

    /// <summary>
    /// Clears all in-memory and tracked dimension stores.
    /// Does not delete spill files already written to disk.
    /// </summary>
    public void Clear()
    {
        lock (_storeGate)
        {
            foreach (var store in _stores.Values)
            {
                store.Clear();
            }

            _stores.Clear();
        }
    }

    private void AddCore(T item)
    {
        foreach (var dimension in Schema.Dimensions)
        {
            var safeKey = Sanitizers.Normalize(dimension.Selector(item));
            var store = GetOrCreateStore(dimension.Name);
            var state = store.GetOrAdd(safeKey, () => CellAggregator<T>.CreateState(Schema));
            CellAggregator<T>.Apply(item, Schema, state);
            store.Upsert(safeKey, state);
        }
    }

    private DimensionStore<CellAggregateState> GetOrCreateStore(string dimension)
    {
        lock (_storeGate)
        {
            if (_stores.TryGetValue(dimension, out var existing))
            {
                return existing;
            }

            var spillPath = Path.Combine(_options.SpillDirectory, $"{dimension}.db");
            var created = new DimensionStore<CellAggregateState>(
                dimension,
                _options.MaxKeysPerDimension,
                spillPath,
                $"dimension_{dimension}",
                maxLiveCacheKeys: _options.MaxLiveCacheKeys);
            _stores[dimension] = created;
            return created;
        }
    }
}
