using System.Collections.Concurrent;
using Parquet;
using Parquet.Data;

namespace Hypercube.State.Parquet;

/// <summary>
/// Columnar Parquet spill backend for <see cref="CellAggregateState"/>.
/// Supports metric-only column projection when materializing snapshots.
/// </summary>
public sealed class ParquetCellSpillBackend<T> : IStateBackend<CellAggregateState>, IMetricProjectableCellBackend<T>, IFlushableSpillBackend, IDisposable
{
    private readonly string _filePath;
    private readonly ParquetCellColumnLayout<T> _layout;
    private readonly int _maxHotKeys;
    private readonly IClock _clock;
    private readonly ConcurrentDictionary<string, HotEntry> _hot = new(StringComparer.Ordinal);
    private readonly Lock _stateLock = new();
    private readonly Lock _flushGate = new();
    private readonly ParquetFlushCoordinator _flushCoordinator;
    private readonly Lock _cacheGate = new();
    private readonly LinkedList<string> _lruOrder = new();
    private readonly Dictionary<string, LinkedListNode<string>> _lruNodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pendingRemovals = new(StringComparer.Ordinal);

    public ParquetCellSpillBackend(string filePath, RollupSchema<T> schema, int maxHotKeys = 0, IClock? clock = null)
    {
        _filePath = filePath;
        _layout = new ParquetCellColumnLayout<T>(schema);
        _maxHotKeys = Math.Max(0, maxHotKeys);
        _clock = clock ?? SystemClock.Instance;
        _flushCoordinator = new ParquetFlushCoordinator(FlushCore);
        LoadExisting();
    }

    /// <inheritdoc />
    public bool TryAdd(string key, CellAggregateState value)
    {
        lock (_stateLock)
        {
            if (_hot.ContainsKey(key) || KeyExistsOnDisk(key))
            {
                return false;
            }

            CacheHotUnderLock(key, value);
        }

        _flushCoordinator.RequestFlush();
        return true;
    }

    /// <inheritdoc />
    public CellAggregateState GetOrAdd(string key, Func<CellAggregateState> factory)
    {
        List<(string Key, HotEntry Entry)>? evicted;
        CellAggregateState result;
        lock (_stateLock)
        {
            if (_hot.TryGetValue(key, out var existing))
            {
                Touch(key);
                return existing.State;
            }

            if (TryHydrateFromDisk(key, out var hydrated))
            {
                evicted = CacheHotUnderLock(key, hydrated!);
                result = hydrated!;
            }
            else
            {
                var created = factory();
                evicted = CacheHotUnderLock(key, created);
                result = created;
            }
        }

        PersistEvicted(evicted);
        return result;
    }

    /// <inheritdoc />
    public bool TryGet(string key, out CellAggregateState? value)
    {
        List<(string Key, HotEntry Entry)>? evicted = null;
        var found = false;
        lock (_stateLock)
        {
            if (_hot.TryGetValue(key, out var entry))
            {
                Touch(key);
                value = entry.State;
                return true;
            }

            if (TryHydrateFromDisk(key, out value))
            {
                evicted = CacheHotUnderLock(key, value!);
                found = true;
            }
            else
            {
                value = null;
            }
        }

        PersistEvicted(evicted);
        return found;
    }

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, CellAggregateState>> Enumerate()
    {
        foreach (var pair in EnumerateProjected(metricProjection: null))
        {
            yield return new KeyValuePair<string, CellAggregateState>(
                pair.Key,
                pair.State ?? throw new InvalidOperationException("Full enumeration requires hydrated cell state."));
        }
    }

    /// <inheritdoc />
    public IEnumerable<ProjectedCellRow> EnumerateProjected(IReadOnlySet<string>? metricProjection)
    {
        KeyValuePair<string, HotEntry>[] hotSnapshot;
        lock (_stateLock)
        {
            hotSnapshot = [.. _hot];
        }

        var metricIndices = _layout.ResolveMetricIndices(metricProjection);
        var hotKeys = new HashSet<string>(hotSnapshot.Select(static pair => pair.Key), StringComparer.Ordinal);

        foreach (var projected in ReadProjectedRowsFromDisk(metricProjection, metricIndices, hotKeys))
        {
            yield return projected;
        }

        foreach (var (key, entry) in hotSnapshot)
        {
            yield return new ProjectedCellRow(
                key,
                CellAggregator<T>.ToValues(_layout.Schema, entry.State, metricProjection),
                entry.State);
        }
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            var diskKeys = ReadKeyColumn();
            diskKeys.UnionWith(_hot.Keys);
            return diskKeys.Count;
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_stateLock)
        {
            _hot.Clear();
            lock (_cacheGate)
            {
                _lruOrder.Clear();
                _lruNodes.Clear();
            }
        }

        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    /// <inheritdoc />
    public void Upsert(string key, CellAggregateState value)
    {
        List<(string Key, HotEntry Entry)>? evicted = null;
        lock (_stateLock)
        {
            evicted = CacheHotUnderLock(key, value);
        }

        PersistEvicted(evicted);
        _flushCoordinator.RequestFlush();
    }

    /// <inheritdoc />
    public bool TryRemove(string key)
    {
        bool existed;
        lock (_stateLock)
        {
            var wasHot = _hot.TryRemove(key, out _);
            lock (_cacheGate)
            {
                if (_lruNodes.TryGetValue(key, out var node))
                {
                    _lruOrder.Remove(node);
                    _lruNodes.Remove(key);
                }
            }

            existed = wasHot || KeyExistsOnDisk(key);
            if (existed)
            {
                _pendingRemovals.Add(key);
            }
        }

        if (!existed)
        {
            return false;
        }

        _flushCoordinator.FlushNow();
        return true;
    }

    /// <inheritdoc />
    public void Dispose() => _flushCoordinator.FlushNow();

    private List<(string Key, HotEntry Entry)>? CacheHotUnderLock(string key, CellAggregateState state)
    {
        _hot[key] = new HotEntry(state, _clock.UtcNow.UtcTicks, _clock);
        Touch(key);
        return EvictUnderLock();
    }

    private void PersistEvicted(List<(string Key, HotEntry Entry)>? evicted)
    {
        if (evicted is not { Count: > 0 })
        {
            return;
        }

        lock (_flushGate)
        {
            PersistRows(evicted.Select(static pair =>
                new PersistedRow(pair.Key, pair.Entry.LastAccessTicks, pair.Entry.State)));
        }
    }

    private void Touch(string key)
    {
        if (_maxHotKeys == 0)
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

    private List<(string Key, HotEntry Entry)>? EvictUnderLock()
    {
        if (_maxHotKeys == 0)
        {
            return null;
        }

        List<(string Key, HotEntry Entry)>? evicted = null;
        lock (_cacheGate)
        {
            while (_lruOrder.Count > _maxHotKeys)
            {
                var key = _lruOrder.Last!.Value;
                _lruOrder.RemoveLast();
                _lruNodes.Remove(key);
                if (_hot.TryRemove(key, out var entry))
                {
                    evicted ??= [];
                    evicted.Add((key, entry));
                }
            }
        }

        return evicted;
    }

    private bool KeyExistsOnDisk(string key) =>
        File.Exists(_filePath) && ReadKeyColumn().Contains(key);

    private void LoadExisting()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        using var stream = ParquetFileIO.OpenReadShared(_filePath);
        using var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();
        if (ParquetCellColumnLayout<T>.IsLegacyPayloadSchema(reader.Schema))
        {
            LoadLegacyPayload(reader);
            return;
        }

        foreach (var row in ReadAllRowsFromDisk())
        {
            lock (_stateLock)
            {
                _hot[row.Key] = new HotEntry(row.State, row.LastAccessTicks, _clock);
            }
        }
    }

    private void LoadLegacyPayload(ParquetReader reader)
    {
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var keys = (string[])rowGroup.ReadColumnAsync(reader.Schema.DataFields[0]).GetAwaiter().GetResult().Data;
            var payloads = (byte[][])rowGroup.ReadColumnAsync(reader.Schema.DataFields[2]).GetAwaiter().GetResult().Data;

            for (var i = 0; i < keys.Length; i++)
            {
                lock (_stateLock)
                {
                    _hot[keys[i]] = new HotEntry(
                        CellAggregateStateSerializer.FromPayload(payloads[i]),
                        lastAccessTicks: 0,
                        _clock);
                }
            }
        }
    }

    private IEnumerable<ProjectedCellRow> ReadProjectedRowsFromDisk(
        IReadOnlySet<string>? metricProjection,
        HashSet<int> metricIndices,
        HashSet<string> excludeKeys)
    {
        if (!File.Exists(_filePath))
        {
            yield break;
        }

        using var stream = ParquetFileIO.OpenReadShared(_filePath);
        using var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();
        if (ParquetCellColumnLayout<T>.IsLegacyPayloadSchema(reader.Schema))
        {
            foreach (var row in ReadLegacyProjectedRows(reader, metricProjection, excludeKeys))
            {
                yield return row;
            }

            yield break;
        }

        var readFields = _layout.ResolveReadFields(metricProjection, includeLastAccess: false);
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var columns = new Dictionary<string, Array>(readFields.Count, StringComparer.Ordinal);
            foreach (var field in readFields)
            {
                columns[field.Name] = rowGroup.ReadColumnAsync(field).GetAwaiter().GetResult().Data;
            }

            var keys = (string[])columns[ParquetCellColumns.KeyColumn];
            for (var rowIndex = 0; rowIndex < keys.Length; rowIndex++)
            {
                var key = keys[rowIndex];
                if (excludeKeys.Contains(key))
                {
                    continue;
                }

                var state = BuildPartialState(columns, rowIndex, metricIndices);
                yield return new ProjectedCellRow(
                    key,
                    CellAggregator<T>.ToValues(_layout.Schema, state, metricProjection),
                    state);
            }
        }
    }

    private IEnumerable<ProjectedCellRow> ReadLegacyProjectedRows(
        ParquetReader reader,
        IReadOnlySet<string>? metricProjection,
        HashSet<string> excludeKeys)
    {
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var keys = (string[])rowGroup.ReadColumnAsync(reader.Schema.DataFields[0]).GetAwaiter().GetResult().Data;
            var payloads = (byte[][])rowGroup.ReadColumnAsync(reader.Schema.DataFields[2]).GetAwaiter().GetResult().Data;

            for (var i = 0; i < keys.Length; i++)
            {
                if (excludeKeys.Contains(keys[i]))
                {
                    continue;
                }

                var state = CellAggregateStateSerializer.FromPayload(payloads[i]);
                yield return new ProjectedCellRow(
                    keys[i],
                    CellAggregator<T>.ToValues(_layout.Schema, state, metricProjection),
                    state);
            }
        }
    }

    private bool TryHydrateFromDisk(string key, out CellAggregateState? state)
    {
        state = null;
        if (!File.Exists(_filePath))
        {
            return false;
        }

        foreach (var row in ReadAllRowsFromDisk())
        {
            if (!string.Equals(row.Key, key, StringComparison.Ordinal))
            {
                continue;
            }

            state = row.State;
            return true;
        }

        return false;
    }

    private HashSet<string> ReadKeyColumn()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(_filePath))
        {
            return keys;
        }

        using var stream = ParquetFileIO.OpenReadShared(_filePath);
        using var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();
        var keyField = reader.Schema.DataFields[0];
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var column = (string[])rowGroup.ReadColumnAsync(keyField).GetAwaiter().GetResult().Data;
            foreach (var key in column)
            {
                keys.Add(key);
            }
        }

        return keys;
    }

    private List<PersistedRow> ReadAllRowsFromDisk(string? excludeKey = null)
    {
        var rows = new List<PersistedRow>();
        if (!File.Exists(_filePath))
        {
            return rows;
        }

        using var stream = ParquetFileIO.OpenReadShared(_filePath);
        using var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();
        if (ParquetCellColumnLayout<T>.IsLegacyPayloadSchema(reader.Schema))
        {
            for (var group = 0; group < reader.RowGroupCount; group++)
            {
                using var rowGroup = reader.OpenRowGroupReader(group);
                var keys = (string[])rowGroup.ReadColumnAsync(reader.Schema.DataFields[0]).GetAwaiter().GetResult().Data;
                var lastAccess = (long[])rowGroup.ReadColumnAsync(reader.Schema.DataFields[1]).GetAwaiter().GetResult().Data;
                var payloads = (byte[][])rowGroup.ReadColumnAsync(reader.Schema.DataFields[2]).GetAwaiter().GetResult().Data;

                for (var i = 0; i < keys.Length; i++)
                {
                    if (excludeKey is not null && string.Equals(keys[i], excludeKey, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    rows.Add(new PersistedRow(keys[i], lastAccess[i], CellAggregateStateSerializer.FromPayload(payloads[i])));
                }
            }

            return rows;
        }

        var readFields = _layout.ResolveReadFields(metricProjection: null, includeLastAccess: true);
        for (var group = 0; group < reader.RowGroupCount; group++)
        {
            using var rowGroup = reader.OpenRowGroupReader(group);
            var columns = new Dictionary<string, Array>(readFields.Count, StringComparer.Ordinal);
            foreach (var field in readFields)
            {
                columns[field.Name] = rowGroup.ReadColumnAsync(field).GetAwaiter().GetResult().Data;
            }

            var keys = (string[])columns[ParquetCellColumns.KeyColumn];
            var lastAccess = (long[])columns[ParquetCellColumns.LastAccessColumn];
            for (var rowIndex = 0; rowIndex < keys.Length; rowIndex++)
            {
                if (excludeKey is not null && string.Equals(keys[rowIndex], excludeKey, StringComparison.Ordinal))
                {
                    continue;
                }

                var metricIndices = _layout.ResolveMetricIndices(metricProjection: null);
                var state = BuildPartialState(columns, rowIndex, metricIndices);
                rows.Add(new PersistedRow(keys[rowIndex], lastAccess[rowIndex], state));
            }
        }

        return rows;
    }

    private CellAggregateState BuildPartialState(
        Dictionary<string, Array> columns,
        int rowIndex,
        HashSet<int> metricIndices)
    {
        var metricValues = new double[_layout.ScalarSlotCount];
        for (var slot = 0; slot < _layout.ScalarSlotCount; slot++)
        {
            var columnName = $"{ParquetCellColumns.ScalarColumnPrefix}{slot}";
            if (!columns.TryGetValue(columnName, out var column))
            {
                continue;
            }

            metricValues[slot] = ((double[])column)[rowIndex];
        }

        var sketchStates = new byte[_layout.Schema.Metrics.Count][];
        foreach (var metricIndex in _layout.SketchMetricIndices)
        {
            if (!metricIndices.Contains(metricIndex))
            {
                sketchStates[metricIndex] = [];
                continue;
            }

            var columnName = $"{ParquetCellColumns.SketchColumnPrefix}{metricIndex}";
            if (!columns.TryGetValue(columnName, out var column))
            {
                sketchStates[metricIndex] = [];
                continue;
            }

            sketchStates[metricIndex] = ((byte[][])column)[rowIndex] ?? [];
        }

        return new CellAggregateState
        {
            MetricValues = metricValues,
            SketchStates = sketchStates,
            ActiveSketches = []
        };
    }

    private void FlushCore()
    {
        List<PersistedRow> hotRows;
        string[] removals;
        lock (_stateLock)
        {
            hotRows = [.. _hot
                .Select(static pair => new PersistedRow(pair.Key, pair.Value.LastAccessTicks, pair.Value.State))];
            removals = [.. _pendingRemovals];
            _pendingRemovals.Clear();
        }

        lock (_flushGate)
        {
            PersistRows(hotRows, removals);
        }
    }

    private void PersistRows(IEnumerable<PersistedRow> hotRows, IReadOnlyList<string>? removals = null)
    {
        var rows = ReadAllRowsFromDisk();
        if (removals is { Count: > 0 })
        {
            rows = [.. rows.Where(row => !removals.Contains(row.Key, StringComparer.Ordinal))];
        }

        var rowByKey = rows.ToDictionary(static row => row.Key, static row => row, StringComparer.Ordinal);

        foreach (var row in hotRows)
        {
            rowByKey[row.Key] = row;
        }

        WriteRows(rowByKey.Values.OrderBy(static row => row.Key, StringComparer.Ordinal));
    }

    /// <summary>Forces a synchronous spill flush. Used during disposal and tests.</summary>
    public void FlushNow() => _flushCoordinator.FlushNow();

    private void WriteRows(IEnumerable<PersistedRow> rows)
    {
        var materialized = rows as IList<PersistedRow> ?? [.. rows];
        if (materialized.Count == 0)
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }

            return;
        }

        var keys = new string[materialized.Count];
        var lastAccess = new long[materialized.Count];
        var scalarColumns = new double[_layout.ScalarSlotCount][];
        for (var slot = 0; slot < _layout.ScalarSlotCount; slot++)
        {
            scalarColumns[slot] = new double[materialized.Count];
        }

        var sketchColumns = _layout.SketchMetricIndices.ToDictionary(
            static index => index,
            _ => new byte[materialized.Count][]);

        for (var rowIndex = 0; rowIndex < materialized.Count; rowIndex++)
        {
            var row = materialized[rowIndex];
            keys[rowIndex] = row.Key;
            lastAccess[rowIndex] = row.LastAccessTicks;
            var snapshot = CellAggregateStateSerializer.Snapshot(row.State);

            for (var slot = 0; slot < _layout.ScalarSlotCount; slot++)
            {
                scalarColumns[slot][rowIndex] = snapshot.MetricValues[slot];
            }

            foreach (var metricIndex in _layout.SketchMetricIndices)
            {
                sketchColumns[metricIndex][rowIndex] = snapshot.SketchStates.Length > metricIndex
                    ? snapshot.SketchStates[metricIndex] ?? []
                    : [];
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_filePath))!);
        using var stream = ParquetFileIO.CreateExclusive(_filePath);
        using var writer = ParquetWriter.CreateAsync(_layout.ParquetSchema, stream).GetAwaiter().GetResult();
        using var groupWriter = writer.CreateRowGroup();

        var keyField = _layout.ParquetSchema.DataFields.First(static field => field.Name == ParquetCellColumns.KeyColumn);
        var lastAccessField = _layout.ParquetSchema.DataFields.First(static field => field.Name == ParquetCellColumns.LastAccessColumn);
        groupWriter.WriteColumnAsync(new DataColumn(keyField, keys)).GetAwaiter().GetResult();
        groupWriter.WriteColumnAsync(new DataColumn(lastAccessField, lastAccess)).GetAwaiter().GetResult();

        for (var slot = 0; slot < _layout.ScalarSlotCount; slot++)
        {
            groupWriter.WriteColumnAsync(new DataColumn(_layout.ScalarFields[slot], scalarColumns[slot])).GetAwaiter().GetResult();
        }

        foreach (var metricIndex in _layout.SketchMetricIndices)
        {
            groupWriter.WriteColumnAsync(new DataColumn(_layout.SketchFields[metricIndex], sketchColumns[metricIndex])).GetAwaiter().GetResult();
        }
    }

    private sealed class HotEntry(CellAggregateState state, long lastAccessTicks, IClock clock)
    {
        public CellAggregateState State { get; set; } = state;

        public long LastAccessTicks { get; private set; } = lastAccessTicks;

        public void Touch() => LastAccessTicks = clock.UtcNow.UtcTicks;
    }

    private readonly record struct PersistedRow(string Key, long LastAccessTicks, CellAggregateState State);
}
