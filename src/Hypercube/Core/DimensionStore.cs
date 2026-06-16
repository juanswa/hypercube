namespace Hypercube.Core;

/// <summary>
/// Key/value store for one dimension with automatic spillover from memory to disk
/// when cardinality exceeds a configured threshold.
/// </summary>
/// <typeparam name="TValue">Reference-type payload stored per key.</typeparam>
public sealed class DimensionStore<TValue> : IDisposable where TValue : class
{
    private readonly Lock _gate = new();
    private readonly IClock _clock;
    private readonly int _maxKeys;
    private readonly string _spillPath;
    private readonly string _collectionName;
    private readonly int _maxLiveCacheKeys;
    private readonly SpillBackendKind _spillBackend;
    private readonly Func<TValue, byte[]>? _parquetSerialize;
    private readonly Func<byte[], TValue>? _parquetDeserialize;
    private readonly Func<string, IStateBackend<TValue>>? _parquetSpillFactory;
    private readonly Dictionary<string, long> _lastAccessTicks = new(StringComparer.Ordinal);
    private IStateBackend<TValue> _backend;
    private bool _hasSpilled;

    /// <summary>
    /// Creates a dimension store backed by memory, with optional spill to disk.
    /// </summary>
    public DimensionStore(
        string dimensionName,
        int maxKeys,
        string spillPath,
        string collectionName,
        IStateBackend<TValue>? initialBackend = null,
        int maxLiveCacheKeys = 0,
        SpillBackendKind spillBackend = SpillBackendKind.LiteDb,
        Func<TValue, byte[]>? parquetSerialize = null,
        Func<byte[], TValue>? parquetDeserialize = null,
        Func<string, IStateBackend<TValue>>? parquetSpillFactory = null,
        IClock? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);

        DimensionName = dimensionName;
        _maxKeys = maxKeys;
        _spillPath = spillPath;
        _collectionName = collectionName;
        _maxLiveCacheKeys = Math.Max(0, maxLiveCacheKeys);
        _spillBackend = spillBackend;
        _parquetSerialize = parquetSerialize;
        _parquetDeserialize = parquetDeserialize;
        _parquetSpillFactory = parquetSpillFactory;
        _clock = clock ?? SystemClock.Instance;
        _backend = initialBackend ?? new InMemoryBackend<TValue>();
    }

    /// <summary>Dimension name used when materializing <see cref="Models.SummaryRow"/> instances.</summary>
    public string DimensionName { get; }

    /// <summary><c>true</c> after the store has migrated its data to disk.</summary>
    public bool HasSpilledToDisk
    {
        get
        {
            lock (_gate)
            {
                return _hasSpilled;
            }
        }
    }

    /// <inheritdoc cref="IStateBackend{TValue}.Count" />
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _backend.Count;
            }
        }
    }

    /// <summary>
    /// Adds a key only if it does not already exist.
    /// </summary>
    public bool TryAddKey(string key, TValue value)
    {
        lock (_gate)
        {
            Touch(key);
            EnsureSpillIfNeeded();
            return _backend.TryAdd(key, value);
        }
    }

    /// <summary>
    /// Returns the value for <paramref name="key"/>, creating it with <paramref name="factory"/> when missing.
    /// </summary>
    public TValue GetOrAdd(string key, Func<TValue> factory)
    {
        lock (_gate)
        {
            Touch(key);
            EnsureSpillIfNeeded();
            return _backend.GetOrAdd(key, factory);
        }
    }

    /// <summary>Enumerates all key/value pairs in the active backend.</summary>
    public IEnumerable<KeyValuePair<string, TValue>> Enumerate()
    {
        lock (_gate)
        {
            return [.. _backend.Enumerate()];
        }
    }

    /// <summary>Active storage backend (for projection-aware snapshot materialization).</summary>
    internal IStateBackend<TValue> Backend
    {
        get
        {
            lock (_gate)
            {
                return _backend;
            }
        }
    }

    /// <summary>Removes all keys from the active backend.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _backend.Clear();
            _lastAccessTicks.Clear();
        }
    }

    /// <summary>
    /// Persists the latest value for a key. Call after mutating a value returned from <see cref="GetOrAdd"/>.
    /// </summary>
    public void Upsert(string key, TValue value)
    {
        lock (_gate)
        {
            Touch(key);
            EnsureSpillIfNeeded();
            _backend.Upsert(key, value);
        }
    }

    /// <summary>
    /// Evicts keys that have not been accessed since <paramref name="ttl"/> elapsed.
    /// </summary>
    /// <returns>Number of keys removed.</returns>
    public int ScavengeStale(TimeSpan ttl)
    {
        var cutoffTicks = (_clock.UtcNow - ttl).UtcTicks;
        var removed = 0;

        lock (_gate)
        {
            foreach (var key in _lastAccessTicks.Keys.ToArray())
            {
                if (_lastAccessTicks.TryGetValue(key, out var ticks) && ticks >= cutoffTicks)
                {
                    continue;
                }

                if (_backend.TryRemove(key))
                {
                    _lastAccessTicks.Remove(key);
                    removed++;
                }
            }
        }

        return removed;
    }

    private void Touch(string key) => _lastAccessTicks[key] = _clock.UtcNow.UtcTicks;

    private void EnsureSpillIfNeeded()
    {
        if (_hasSpilled || _backend.Count < _maxKeys)
        {
            return;
        }

        IStateBackend<TValue> diskBackend = _spillBackend switch
        {
            SpillBackendKind.Parquet when _parquetSpillFactory is not null =>
                _parquetSpillFactory(_spillPath),
            SpillBackendKind.Parquet when _parquetSerialize is not null && _parquetDeserialize is not null =>
                new ParquetSpillBackend<TValue>(
                    Path.ChangeExtension(_spillPath, ".parquet"),
                    _parquetSerialize,
                    _parquetDeserialize),
            _ => new LiteDbBackend<TValue>(_spillPath, _collectionName, _maxLiveCacheKeys)
        };

        foreach (var kvp in _backend.Enumerate())
        {
            diskBackend.TryAdd(kvp.Key, kvp.Value);
        }

        _backend = diskBackend;
        _hasSpilled = true;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_backend is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    /// <summary>Flushes the active backend when it supports synchronous persistence.</summary>
    public void FlushBackend()
    {
        lock (_gate)
        {
            if (_backend is IFlushableSpillBackend flushable)
            {
                flushable.FlushNow();
            }
        }
    }
}
