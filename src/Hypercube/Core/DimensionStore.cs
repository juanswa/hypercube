using System.Threading;
using Hypercube.State;

namespace Hypercube.Core;

/// <summary>
/// Key/value store for one dimension with automatic spillover from memory to LiteDB
/// when cardinality exceeds a configured threshold.
/// </summary>
/// <typeparam name="TValue">Reference-type payload stored per key.</typeparam>
public sealed class DimensionStore<TValue> where TValue : class
{
    private readonly Lock _gate = new();
    private readonly int _maxKeys;
    private readonly string _spillPath;
    private readonly string _collectionName;
    private readonly int _maxLiveCacheKeys;
    private IStateBackend<TValue> _backend;
    private bool _hasSpilled;

    /// <summary>
    /// Creates a dimension store backed by memory, with optional spill to LiteDB.
    /// </summary>
    /// <param name="dimensionName">Dimension name surfaced in snapshots.</param>
    /// <param name="maxKeys">Maximum in-memory keys before spilling to disk.</param>
    /// <param name="spillPath">LiteDB file path used when spilling.</param>
    /// <param name="collectionName">LiteDB collection name for this dimension.</param>
    /// <param name="initialBackend">Optional custom backend. Defaults to <see cref="InMemoryBackend{TValue}"/>.</param>
    /// <param name="maxLiveCacheKeys">LRU cap for live values after spill. <c>0</c> means unlimited.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxKeys"/> is not positive.</exception>
    public DimensionStore(
        string dimensionName,
        int maxKeys,
        string spillPath,
        string collectionName,
        IStateBackend<TValue>? initialBackend = null,
        int maxLiveCacheKeys = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxKeys);

        DimensionName = dimensionName;
        _maxKeys = maxKeys;
        _spillPath = spillPath;
        _collectionName = collectionName;
        _maxLiveCacheKeys = Math.Max(0, maxLiveCacheKeys);
        _backend = initialBackend ?? new InMemoryBackend<TValue>();
    }

    /// <summary>Dimension name used when materializing <see cref="Models.SummaryRow"/> instances.</summary>
    public string DimensionName { get; }

    /// <summary>
    /// <c>true</c> after the store has migrated its data to disk.
    /// </summary>
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

    /// <summary>Current number of keys in the active backend.</summary>
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
    /// <returns><c>true</c> when the key was added; <c>false</c> when it already existed.</returns>
    public bool TryAddKey(string key, TValue value)
    {
        lock (_gate)
        {
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

    /// <summary>Removes all keys from the active backend.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _backend.Clear();
        }
    }

    /// <summary>
    /// Persists the latest value for a key. Call after mutating a value returned from <see cref="GetOrAdd"/>.
    /// </summary>
    public void Upsert(string key, TValue value)
    {
        lock (_gate)
        {
            EnsureSpillIfNeeded();
            _backend.Upsert(key, value);
        }
    }

    private void EnsureSpillIfNeeded()
    {
        if (_hasSpilled || _backend.Count < _maxKeys)
        {
            return;
        }

        var diskBackend = new LiteDbBackend<TValue>(_spillPath, _collectionName, _maxLiveCacheKeys);
        foreach (var kvp in _backend.Enumerate())
        {
            diskBackend.TryAdd(kvp.Key, kvp.Value);
        }

        _backend = diskBackend;
        _hasSpilled = true;
    }
}
