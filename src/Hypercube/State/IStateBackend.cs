namespace Hypercube.State;

/// <summary>
/// Abstraction for keyed storage used by <see cref="Core.DimensionStore{TValue}"/>.
/// </summary>
/// <typeparam name="TValue">Reference-type value stored per key.</typeparam>
public interface IStateBackend<TValue> where TValue : class
{
    /// <summary>
    /// Adds a key only when it does not already exist.
    /// </summary>
    /// <returns><c>true</c> when added; <c>false</c> when the key already exists.</returns>
    bool TryAdd(string key, TValue value);

    /// <summary>
    /// Returns an existing value or creates one using <paramref name="factory"/>.
    /// </summary>
    TValue GetOrAdd(string key, Func<TValue> factory);

    /// <summary>
    /// Attempts to read a value by key.
    /// </summary>
    bool TryGet(string key, out TValue? value);

    /// <summary>Enumerates all stored key/value pairs.</summary>
    IEnumerable<KeyValuePair<string, TValue>> Enumerate();

    /// <summary>Number of keys currently stored.</summary>
    int Count { get; }

    /// <summary>Removes all keys.</summary>
    void Clear();

    /// <summary>
    /// Inserts or replaces the value for <paramref name="key"/>.
    /// Required for disk backends where in-memory mutations must be written back.
    /// </summary>
    void Upsert(string key, TValue value);

    /// <summary>Removes a key when present.</summary>
    /// <returns><c>true</c> when the key was removed.</returns>
    bool TryRemove(string key);
}
