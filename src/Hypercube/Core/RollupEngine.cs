namespace Hypercube.Core;

/// <summary>
/// Streaming rollup engine for events of type <typeparamref name="T"/>.
/// <para>
/// <b>Hot/cold tiering:</b> each dimension keeps a bounded in-memory LRU of active cells.
/// When capacity is exceeded, cold cells spill to a configured backend (LiteDB or columnar Parquet)
/// and are rehydrated on access. Parquet backends can flush to disk on a background thread so ingest
/// stays responsive.
/// </para>
/// <para>
/// <b>Snapshots and insights:</b> <see cref="DeriveSnapshot"/> materializes an immutable
/// <see cref="SummarySnapshot"/> for deterministic analysis (driver decomposition,
/// co-movement, distribution shape) and optional local-AI narrative layers. Snapshots support
/// metric projection for columnar spill reads.
/// </para>
/// </summary>
/// <typeparam name="T">The event or fact type to aggregate.</typeparam>
public sealed class RollupEngine<T> : IDisposable
{
    private readonly EngineConfiguration _configuration;
    private readonly IClock _clock;
    private readonly Dictionary<string, DimensionStore<CellAggregateState>> _stores = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Lock _storeGate = new();
    private readonly SemaphoreSlim? _inflightGate;
    private readonly WatermarkTracker? _watermark;
    private readonly RollupDiagnostics? _diagnostics;

    /// <summary>
    /// Creates a rollup engine using the supplied schema and lifecycle configuration.
    /// </summary>
    public RollupEngine(RollupSchema<T> schema, EngineConfiguration? configuration = null)
    {
        Schema = schema ?? throw new ArgumentNullException(nameof(schema));
        _configuration = configuration ?? new EngineConfiguration();
        _clock = _configuration.Clock;
        Directory.CreateDirectory(_configuration.SpillDirectory);

        if (_configuration.MaxInFlightAdds > 0)
        {
            _inflightGate = new SemaphoreSlim(_configuration.MaxInFlightAdds, _configuration.MaxInFlightAdds);
        }

        if (_configuration.AllowedLateness is { } allowedLateness)
        {
            _watermark = new WatermarkTracker(allowedLateness);
        }

        if (_configuration.EnableDiagnostics)
        {
            _diagnostics = new RollupDiagnostics();
        }
    }

    /// <summary>
    /// Creates a rollup engine using legacy <see cref="RollupEngineOptions"/>.
    /// </summary>
    public RollupEngine(RollupSchema<T> schema, RollupEngineOptions? options)
        : this(schema, options is null ? null : EngineConfiguration.FromLegacy(options))
    {
    }

    /// <summary>The schema that defines how items are sliced and aggregated.</summary>
    public RollupSchema<T> Schema { get; }

    /// <summary>Active engine lifecycle configuration.</summary>
    public EngineConfiguration Configuration => _configuration;

    /// <summary>Current event-time watermark when configured.</summary>
    public WatermarkTracker? Watermark => _watermark;

    /// <summary>Diagnostics instruments when <see cref="EngineConfiguration.EnableDiagnostics"/> is enabled.</summary>
    public RollupDiagnostics? Diagnostics => _diagnostics;

    /// <summary>Clock used for snapshots and lifecycle timestamps.</summary>
    public IClock Clock => _clock;

    /// <summary>Ingests one item and updates every configured dimension.</summary>
    public void Add(T item) => AddCore(item, eventTime: null);

    /// <summary>
    /// Attempts to ingest one item, applying backpressure and watermark policies when configured.
    /// </summary>
    public RollupAddResult TryAdd(T item, DateTimeOffset? eventTime = null)
    {
        if (_inflightGate is SemaphoreSlim gate && !gate.Wait(0))
        {
            _diagnostics?.RecordBackpressure();
            return RollupAddResult.Backpressure;
        }

        try
        {
            if (eventTime is not null && _watermark is not null)
            {
                if (_watermark.IsLate(eventTime.Value))
                {
                    _diagnostics?.RecordLateDrop();
                    return RollupAddResult.DroppedLate;
                }

                _watermark.Advance(eventTime.Value);
            }

            AddCore(item, eventTime);
            return RollupAddResult.Accepted;
        }
        finally
        {
            _inflightGate?.Release();
        }
    }

    /// <summary>
    /// Builds a snapshot of all dimension/key cells and their current metric values.
    /// Each call returns a new <see cref="SummarySnapshot"/> with lazily built row indexes.
    /// </summary>
    public SummarySnapshot DeriveSnapshot(IReadOnlySet<string>? metricProjection = null)
    {
        DimensionStore<CellAggregateState>[] stores;
        lock (_storeGate)
        {
            stores = [.. _stores.Values];
        }

        var rows = new List<SummaryRow>();
        foreach (var store in stores)
        {
            if (metricProjection is not null &&
                store.Backend is IMetricProjectableCellBackend<T> projectable)
            {
                foreach (var projected in projectable.EnumerateProjected(metricProjection))
                {
                    rows.Add(new SummaryRow(store.DimensionName, projected.Key, projected.Metrics));
                }

                continue;
            }

            foreach (var (key, state) in store.Enumerate())
            {
                rows.Add(new SummaryRow(
                    store.DimensionName,
                    key,
                    CellAggregator<T>.ToValues(Schema, state, metricProjection)));
            }
        }

        return new SummarySnapshot(_configuration.Clock.UtcNow, rows, Schema.PrimaryMetric);
    }

    /// <summary>
    /// Evicts stale dimension keys based on <see cref="EngineConfiguration.DimensionTimeToLive"/>.
    /// </summary>
    /// <returns>Total keys removed across dimensions.</returns>
    public int ScavengeStaleDimensions()
    {
        if (_configuration.DimensionTimeToLive is not { } ttl)
        {
            return 0;
        }

        var removed = 0;
        lock (_storeGate)
        {
            foreach (var store in _stores.Values)
            {
                removed += store.ScavengeStale(ttl);
            }
        }

        if (removed > 0)
        {
            _diagnostics?.RecordTtlEviction(removed);
        }

        return removed;
    }

    /// <summary>Clears all in-memory and tracked dimension stores.</summary>
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

    /// <summary>Flushes all spilled dimension backends to disk synchronously.</summary>
    public void FlushSpill()
    {
        lock (_storeGate)
        {
            foreach (var store in _stores.Values)
            {
                store.FlushBackend();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_storeGate)
        {
            foreach (var store in _stores.Values)
            {
                store.Dispose();
            }

            _stores.Clear();
        }

        _diagnostics?.Dispose();
        _inflightGate?.Dispose();
    }

    private void AddCore(T item, DateTimeOffset? eventTime)
    {
        foreach (var dimension in Schema.Dimensions)
        {
            var baseKey = Sanitizers.Normalize(dimension.Selector(item));
            var safeKey = eventTime is not null
                ? WindowManager.QualifyKey(baseKey, eventTime.Value, _configuration.Windowing)
                : baseKey;

            var store = GetOrCreateStore(dimension.Name);
            var spilledBefore = store.HasSpilledToDisk;
            store.GetOrAddAndMutate(safeKey, () => CellAggregator<T>.CreateState(Schema), state =>
                CellAggregator<T>.Apply(item, Schema, state));

            if (!spilledBefore && store.HasSpilledToDisk)
            {
                _diagnostics?.RecordSpill();
            }
        }

        _diagnostics?.RecordEventProcessed();
    }

    private DimensionStore<CellAggregateState> GetOrCreateStore(string dimension)
    {
        lock (_storeGate)
        {
            if (_stores.TryGetValue(dimension, out var existing))
            {
                return existing;
            }

            var spillPath = Path.Combine(_configuration.SpillDirectory, $"{dimension}.db");
            var created = new DimensionStore<CellAggregateState>(
                dimension,
                _configuration.MaxKeysPerDimension,
                spillPath,
                $"dimension_{dimension}",
                maxLiveCacheKeys: _configuration.MaxLiveCacheKeys,
                spillBackend: _configuration.SpillBackend,
                parquetSpillFactory: _configuration.SpillBackend == SpillBackendKind.Parquet
                    ? spillPath => new ParquetCellSpillBackend<T>(
                        Path.ChangeExtension(spillPath, ".parquet"),
                        Schema,
                        _configuration.MaxLiveCacheKeys,
                        _configuration.Clock)
                    : null,
                clock: _configuration.Clock);
            _stores[dimension] = created;
            return created;
        }
    }
}
