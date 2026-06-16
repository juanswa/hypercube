using System.Collections.Concurrent;

namespace Hypercube.State;

/// <summary>
/// In-memory implementation of <see cref="IStateBackend{TValue}"/> backed by a concurrent dictionary.
/// </summary>
/// <typeparam name="TValue">Reference-type value stored per key.</typeparam>
public sealed class InMemoryBackend<TValue> : IStateBackend<TValue> where TValue : class
{
    private readonly ConcurrentDictionary<string, TValue> _store = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public bool TryAdd(string key, TValue value) => _store.TryAdd(key, value);

    /// <inheritdoc />
    public TValue GetOrAdd(string key, Func<TValue> factory) => _store.GetOrAdd(key, _ => factory());

    /// <inheritdoc />
    public bool TryGet(string key, out TValue? value) => _store.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, TValue>> Enumerate() => _store;

    /// <inheritdoc />
    public int Count => _store.Count;

    /// <inheritdoc />
    public void Clear() => _store.Clear();

    /// <inheritdoc />
    public void Upsert(string key, TValue value) => _store[key] = value;
}
