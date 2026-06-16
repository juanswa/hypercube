using System.Collections.Concurrent;
using LiteDB;

namespace Hypercube.State;

/// <summary>
/// LiteDB-backed implementation of <see cref="IStateBackend{TValue}"/> for disk spill storage.
/// Keeps a live in-memory reference per key so concurrent updates mutate the same instance.
/// </summary>
/// <typeparam name="TValue">Reference-type value stored per key.</typeparam>
public sealed class LiteDbBackend<TValue> : IStateBackend<TValue>, IDisposable where TValue : class
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<LiteDbRecord<TValue>> _collection;
    private readonly ConcurrentDictionary<string, TValue> _liveValues = new(StringComparer.Ordinal);
    private readonly int _maxLiveCacheKeys;
    private readonly Lock _cacheGate = new();
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.Ordinal);

    /// <summary>
    /// Opens or creates a LiteDB database and collection for keyed storage.
    /// </summary>
    /// <param name="databasePath">Path to the LiteDB file.</param>
    /// <param name="collectionName">Collection name within the database.</param>
    /// <param name="maxLiveCacheKeys">Maximum live values cached in RAM. <c>0</c> means unlimited.</param>
    public LiteDbBackend(string databasePath, string collectionName, int maxLiveCacheKeys = 0)
    {
        var mapper = new BsonMapper();
        if (typeof(TValue) == typeof(Core.CellAggregateState))
        {
            Core.CellAggregateStateSerializer.Register(mapper);
        }

        _db = new LiteDatabase(databasePath, mapper);
        _collection = _db.GetCollection<LiteDbRecord<TValue>>(collectionName);
        _collection.EnsureIndex(x => x.Key, true);
        _maxLiveCacheKeys = Math.Max(0, maxLiveCacheKeys);
    }

    /// <inheritdoc />
    public bool TryAdd(string key, TValue value)
    {
        if (_collection.Exists(x => x.Key == key))
        {
            return false;
        }

        _collection.Insert(new LiteDbRecord<TValue> { Key = key, Value = value });
        CacheLiveValue(key, value);
        return true;
    }

    /// <inheritdoc />
    public TValue GetOrAdd(string key, Func<TValue> factory)
    {
        if (_liveValues.TryGetValue(key, out var live))
        {
            Touch(key);
            return live;
        }

        var value = _liveValues.GetOrAdd(key, static (lookupKey, ctx) =>
        {
            var existing = ctx.collection.FindOne(x => x.Key == lookupKey);
            if (existing is not null)
            {
                return existing.Value;
            }

            var created = ctx.factory();
            ctx.collection.Insert(new LiteDbRecord<TValue> { Key = lookupKey, Value = created });
            return created;
        }, (collection: _collection, factory));

        Touch(key);
        EvictIfNeeded();
        return value;
    }

    /// <inheritdoc />
    public bool TryGet(string key, out TValue? value)
    {
        if (_liveValues.TryGetValue(key, out value))
        {
            Touch(key);
            return true;
        }

        var row = _collection.FindOne(x => x.Key == key);
        if (row is null)
        {
            value = null;
            return false;
        }

        value = row.Value;
        CacheLiveValue(key, value);
        return true;
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, TValue>> Enumerate()
    {
        foreach (var row in _collection.FindAll())
        {
            yield return new KeyValuePair<string, TValue>(
                row.Key,
                _liveValues.TryGetValue(row.Key, out var live) ? live : row.Value);
        }
    }

    /// <inheritdoc />
    public int Count => _collection.Count();

    /// <inheritdoc />
    public void Clear()
    {
        lock (_cacheGate)
        {
            _liveValues.Clear();
            _lruOrder.Clear();
            _lruNodes.Clear();
        }

        _collection.DeleteAll();
    }

    /// <inheritdoc />
    public void Upsert(string key, TValue value)
    {
        CacheLiveValue(key, value);
        var existing = _collection.FindOne(x => x.Key == key);
        if (existing is not null)
        {
            existing.Value = value;
            _collection.Update(existing);
            return;
        }

        _collection.Insert(new LiteDbRecord<TValue> { Key = key, Value = value });
    }

    /// <inheritdoc />
    public bool TryRemove(string key)
    {
        _liveValues.TryRemove(key, out _);
        lock (_cacheGate)
        {
            if (_lruNodes.TryGetValue(key, out var node))
            {
                _lruOrder.Remove(node);
                _lruNodes.Remove(key);
            }
        }

        return _collection.DeleteMany(x => x.Key == key) > 0;
    }

    /// <summary>Releases the underlying LiteDB database.</summary>
    public void Dispose() => _db.Dispose();

    private void CacheLiveValue(string key, TValue value)
    {
        _liveValues[key] = value;
        Touch(key);
        EvictIfNeeded();
    }

    private void Touch(string key)
    {
        if (_maxLiveCacheKeys == 0)
        {
            return;
        }

        lock (_cacheGate)
        {
            if (_lruNodes.TryGetValue(key, out var node))
            {
                _lruOrder.Remove(node);
            }

            var refreshed = _lruOrder.AddFirst(key);
            _lruNodes[key] = refreshed;
        }
    }

    private void EvictIfNeeded()
    {
        if (_maxLiveCacheKeys == 0)
        {
            return;
        }

        lock (_cacheGate)
        {
            while (_lruOrder.Count > _maxLiveCacheKeys)
            {
                var key = _lruOrder.Last!.Value;
                _lruOrder.RemoveLast();
                _lruNodes.Remove(key);
                _liveValues.TryRemove(key, out _);
            }
        }
    }

    private sealed class LiteDbRecord<T>
    {
        public ObjectId Id { get; set; } = ObjectId.NewObjectId();
        public string Key { get; set; } = string.Empty;
        public T Value { get; set; } = default!;
    }
}
