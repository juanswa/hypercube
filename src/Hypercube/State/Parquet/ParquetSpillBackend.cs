using System.Collections.Concurrent;
using Parquet;
using Parquet.Data;
using Parquet.Schema;

namespace Hypercube.State.Parquet;

/// <summary>
/// Columnar Parquet spill backend storing serialized cell payloads.
/// Optimized for TTL scans on the <c>last_access_ticks</c> column.
/// </summary>
/// <typeparam name="TValue">Reference-type value stored per key.</typeparam>
public sealed class ParquetSpillBackend<TValue> : IStateBackend<TValue>, IFlushableSpillBackend, IDisposable where TValue : class
{
    private readonly string _filePath;
    private readonly Func<TValue, byte[]> _serialize;
    private readonly Func<byte[], TValue> _deserialize;
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Lock _flushGate = new();
    private readonly ParquetFlushCoordinator _flushCoordinator;
    private readonly IClock _clock;

    public ParquetSpillBackend(
        string filePath,
        Func<TValue, byte[]> serialize,
        Func<byte[], TValue> deserialize,
        IClock? clock = null)
    {
        _filePath = filePath;
        _serialize = serialize;
        _deserialize = deserialize;
        _clock = clock ?? SystemClock.Instance;
        _flushCoordinator = new ParquetFlushCoordinator(FlushCore);
        LoadExisting();
    }

    /// <inheritdoc />
    public bool TryAdd(string key, TValue value)
    {
        if (!_entries.TryAdd(key, new Entry(value, lastAccessTicks: 0, _clock)))
        {
            return false;
        }

        _flushCoordinator.RequestFlush();
        return true;
    }

    /// <inheritdoc />
    public TValue GetOrAdd(string key, Func<TValue> factory)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            existing.Touch();
            return existing.Value;
        }

        var created = factory();
        var entry = _entries.GetOrAdd(key, static (_, ctx) => new Entry(ctx.created, lastAccessTicks: 0, ctx.clock), (created, clock: _clock));
        entry.Touch();
        return entry.Value;
    }

    /// <inheritdoc />
    public bool TryGet(string key, out TValue? value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry.Touch();
            value = entry.Value;
            return true;
        }

        value = null;
        return false;
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, TValue>> Enumerate() =>
        _entries.Select(static kvp => new KeyValuePair<string, TValue>(kvp.Key, kvp.Value.Value));

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public void Clear()
    {
        _entries.Clear();
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    /// <inheritdoc />
    public void Upsert(string key, TValue value)
    {
        _entries.AddOrUpdate(
            key,
            static (_, ctx) => new Entry(ctx.value, lastAccessTicks: 0, ctx.clock),
            static (_, entry, ctx) =>
            {
                entry.Value = ctx.value;
                entry.Touch();
                return entry;
            },
            (value, clock: _clock));
        _flushCoordinator.RequestFlush();
    }

    /// <inheritdoc />
    public bool TryRemove(string key)
    {
        var removed = _entries.TryRemove(key, out _);
        if (removed)
        {
            _flushCoordinator.RequestFlush();
        }

        return removed;
    }

    /// <summary>Persists the in-memory map to Parquet on a background thread.</summary>
    public void RequestFlush() => _flushCoordinator.RequestFlush();

    private void FlushCore()
    {
        lock (_flushGate)
        {
            if (_entries.IsEmpty)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }

                return;
            }

            var keys = _entries.Keys.OrderBy(static key => key, StringComparer.Ordinal).ToArray();
            var lastAccess = new long[keys.Length];
            var payloads = new byte[keys.Length][];

            for (var i = 0; i < keys.Length; i++)
            {
                var entry = _entries[keys[i]];
                lastAccess[i] = entry.LastAccessTicks;
                payloads[i] = _serialize(entry.Value);
            }

            var schema = new ParquetSchema(
                new DataField<string>("key"),
                new DataField<long>("last_access_ticks"),
                new DataField<byte[]>("payload"));

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
            using var stream = ParquetFileIO.CreateExclusive(_filePath);
            using var writer = ParquetWriter.CreateAsync(schema, stream).GetAwaiter().GetResult();
            using var groupWriter = writer.CreateRowGroup();
            groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[0], keys)).GetAwaiter().GetResult();
            groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[1], lastAccess)).GetAwaiter().GetResult();
            groupWriter.WriteColumnAsync(new DataColumn((DataField)schema.DataFields[2], payloads)).GetAwaiter().GetResult();
        }
    }

    /// <inheritdoc />
    public void FlushNow() => _flushCoordinator.FlushNow();

    /// <inheritdoc />
    public void Dispose() => _flushCoordinator.FlushNow();

    private void LoadExisting()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        using var stream = ParquetFileIO.OpenReadShared(_filePath);
        using var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var keys = (string[])rowGroup.ReadColumnAsync(reader.Schema.DataFields[0]).GetAwaiter().GetResult().Data;
            var lastAccess = (long[])rowGroup.ReadColumnAsync(reader.Schema.DataFields[1]).GetAwaiter().GetResult().Data;
            var payloads = (byte[][])rowGroup.ReadColumnAsync(reader.Schema.DataFields[2]).GetAwaiter().GetResult().Data;

            for (var i = 0; i < keys.Length; i++)
            {
                _entries[keys[i]] = new Entry(_deserialize(payloads[i]), lastAccess[i], _clock);
            }
        }
    }

    private sealed class Entry(TValue value, long lastAccessTicks, IClock clock)
    {
        public TValue Value { get; set; } = value;

        public long LastAccessTicks { get; private set; } = lastAccessTicks == 0 ? clock.UtcNow.UtcTicks : lastAccessTicks;

        public void Touch() => LastAccessTicks = clock.UtcNow.UtcTicks;
    }
}
