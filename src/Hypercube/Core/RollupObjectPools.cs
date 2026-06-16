using System.Buffers;
using System.Collections.Concurrent;

namespace Hypercube.Core;

/// <summary>
/// Shared pools that reduce GC pressure on hot ingestion paths.
/// </summary>
public static class RollupObjectPools
{
    private static readonly ConcurrentDictionary<string, string> NormalizedKeyCache = new(StringComparer.Ordinal);

    /// <summary>Shared array pool for metric scratch buffers.</summary>
    public static ArrayPool<double> MetricDoubles => ArrayPool<double>.Shared;

    /// <summary>Shared array pool for byte buffers used during serialization.</summary>
    public static ArrayPool<byte> ByteBuffers => ArrayPool<byte>.Shared;

    /// <summary>
    /// Returns a cached normalized key string to avoid repeated lowercase allocations.
    /// </summary>
    public static string InternNormalizedKey(string? value, string fallback = "unknown")
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var trimmed = value.Trim();
        var lowered = trimmed.ToLowerInvariant();
        return NormalizedKeyCache.GetOrAdd(lowered, static key => key);
    }
}
