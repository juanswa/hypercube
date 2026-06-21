namespace Hypercube.State;

/// <summary>
/// LiteDB-backed implementation of <see cref="IStateBackend{TValue}"/> for disk spill storage.
/// Keeps a live in-memory reference per key so concurrent updates mutate the same instance.
/// </summary>
/// <typeparam name="TValue">Reference-type value stored per key.</typeparam>
public sealed class LiteDbBackend<TValue> : IStateBackend<TValue>, IFlushableSpillBackend, IDisposable where TValue : class
{
    private readonly LiteDatabase _db;
    private readonly ILiteCollection<LiteDbRecord<TValue>> _collection;
    private readonly ConcurrentDictionary<string, TValue> _liveValues = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _dirtyKeys = new(StringComparer.Ordinal);
    private readonly int _maxLiveCacheKeys;
    private readonly object _dbGate = new();
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
        if (typeof(TValue) == typeof(CellAggregateState))
        {
            CellAggregateStateSerializer.Register(mapper);
        }

        _db = new LiteDatabase(databasePath, mapper);
        _collection = _db.GetCollection<LiteDbRecord<TValue>>(collectionName);
        _collection.EnsureIndex(x => x.Key, true);
        _maxLiveCacheKeys = Math.Max(0, maxLiveCacheKeys);
    }

    /// <inheritdoc />
    public bool TryAdd(string key, TValue value)
    {
        lock (_dbGate)
        {
            if (_collection.Exists(x => x.Key == key))
            {
                return false;
            }

            _collection.Insert(new LiteDbRecord<TValue> { Key = key, Value = value });
        }

        CacheLiveValue(key, value, dirty: false);
        return true;
    }

    /// <inheritdoc />
    public TValue GetOrAdd(string key, Func<TValue> factory)
    {
        lock (_dbGate)
        {
            if (_liveValues.TryGetValue(key, out var live))
            {
                Touch(key);
                return live;
            }

            var existing = _collection.FindOne(x => x.Key == key);
            if (existing is not null)
            {
                var value = existing.Value;
                CacheLiveValue(key, value, dirty: false);
                EvictIfNeeded();
                return value;
            }

            var created = factory();
            try
            {
                _collection.Insert(new LiteDbRecord<TValue> { Key = key, Value = created });
            }
            catch (LiteException ex) when (IsDuplicateKey(ex))
            {
                existing = _collection.FindOne(x => x.Key == key);
                if (existing is not null)
                {
                    var value = existing.Value;
                    CacheLiveValue(key, value, dirty: false);
                    EvictIfNeeded();
                    return value;
                }

                throw;
            }

            CacheLiveValue(key, created, dirty: false);
            EvictIfNeeded();
            return created;
        }
    }

    /// <inheritdoc />
    public bool TryGet(string key, out TValue? value)
    {
        if (_liveValues.TryGetValue(key, out value))
        {
            Touch(key);
            return true;
        }

        lock (_dbGate)
        {
            var row = _collection.FindOne(x => x.Key == key);
            if (row is null)
            {
                value = null;
                return false;
            }

            value = row.Value;
        }

        CacheLiveValue(key, value, dirty: false);
        return true;
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, TValue>> Enumerate()
    {
        IList<LiteDbRecord<TValue>> rows;
        lock (_dbGate)
        {
            rows = _collection.FindAll().ToList();
        }

        foreach (var row in rows)
        {
            yield return new KeyValuePair<string, TValue>(
                row.Key,
                _liveValues.TryGetValue(row.Key, out var live) ? live : row.Value);
        }
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            lock (_dbGate)
            {
                return _collection.Count();
            }
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_cacheGate)
        {
            _liveValues.Clear();
            _lruOrder.Clear();
            _lruNodes.Clear();
        }

        lock (_dbGate)
        {
            _collection.DeleteAll();
        }
    }

    /// <inheritdoc />
    public void Upsert(string key, TValue value)
    {
        CacheLiveValue(key, value, dirty: true);
    }

    /// <inheritdoc />
    public void FlushNow()
    {
        var dirtyKeys = _dirtyKeys.Keys.ToArray();
        if (dirtyKeys.Length == 0)
        {
            return;
        }

        lock (_dbGate)
        {
            foreach (var key in dirtyKeys)
            {
                if (!_liveValues.TryGetValue(key, out var value))
                {
                    _dirtyKeys.TryRemove(key, out _);
                    continue;
                }

                var existing = _collection.FindOne(x => x.Key == key);
                if (existing is not null)
                {
                    existing.Value = value;
                    _collection.Update(existing);
                }
                else
                {
                    _collection.Insert(new LiteDbRecord<TValue> { Key = key, Value = value });
                }

                _dirtyKeys.TryRemove(key, out _);
            }
        }
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

    /// <summary>Flushes any dirty cells to disk, then releases the underlying LiteDB database.</summary>
    public void Dispose()
    {
        FlushNow();
        _db.Dispose();
    }

    private void CacheLiveValue(string key, TValue value, bool dirty)
    {
        _liveValues[key] = value;
        if (dirty)
        {
            _dirtyKeys[key] = 0;
        }

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

    /// <summary>
    /// Returns <c>true</c> when the LiteDB exception signals a unique-index duplicate-key violation.
    /// Matches on the exception message text as the primary discriminator; the <c>ErrorCode</c>
    /// property is present on <see cref="LiteException"/> but its enum values are not publicly
    /// accessible in the pinned LiteDB v5 version, so message matching is the reliable path.
    /// </summary>
    /// <remarks>
    /// TODO(juan): confirm LiteDB dup-key discriminator — message matching is used as a fallback
    /// because <c>LiteException.ErrorCodes</c> is not publicly accessible in the pinned version.
    /// </remarks>
    private static bool IsDuplicateKey(LiteException ex) =>
        ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);

    private void EvictIfNeeded()
    {
        if (_maxLiveCacheKeys == 0)
        {
            return;
        }

        var toPersist = new List<(string Key, TValue Value)>();
        lock (_cacheGate)
        {
            while (_lruOrder.Count > _maxLiveCacheKeys)
            {
                var key = _lruOrder.Last!.Value;
                _lruOrder.RemoveLast();
                _lruNodes.Remove(key);
                if (_liveValues.TryRemove(key, out var value) && _dirtyKeys.TryRemove(key, out _))
                {
                    toPersist.Add((key, value));
                }
            }
        }

        if (toPersist.Count > 0)
        {
            lock (_dbGate)
            {
                foreach (var (key, value) in toPersist)
                {
                    var existing = _collection.FindOne(x => x.Key == key);
                    if (existing is not null)
                    {
                        existing.Value = value;
                        _collection.Update(existing);
                    }
                    else
                    {
                        _collection.Insert(new LiteDbRecord<TValue> { Key = key, Value = value });
                    }
                }
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
