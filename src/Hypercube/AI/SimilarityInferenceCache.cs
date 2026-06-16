using System.Collections.Concurrent;
using Hypercube.Models;

namespace Hypercube.AI;

/// <summary>
/// Caches AI analysis results when snapshot profiles are highly similar.
/// </summary>
public sealed class SimilarityInferenceCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly double _similarityThreshold;

    public SimilarityInferenceCache(double similarityThreshold = 0.99)
    {
        if (similarityThreshold is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(similarityThreshold));
        }

        _similarityThreshold = similarityThreshold;
    }

    /// <summary>
    /// Attempts to return a cached analysis for a similar snapshot profile.
    /// </summary>
    public bool TryGet(SummarySnapshot snapshot, out AiAnalysisResult? result)
    {
        var profile = SnapshotProfileVectorizer.Vectorize(snapshot);
        foreach (var entry in _entries.Values)
        {
            if (SnapshotProfileVectorizer.CosineSimilarity(profile, entry.Profile) >= _similarityThreshold)
            {
                result = entry.Result;
                return true;
            }
        }

        result = null;
        return false;
    }

    /// <summary>Stores an analysis result keyed by profile fingerprint.</summary>
    public void Store(SummarySnapshot snapshot, AiAnalysisResult result)
    {
        var profile = SnapshotProfileVectorizer.Vectorize(snapshot);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(string.Join('|', profile.Select(static v => v.ToString("R"))))));
        _entries[key] = new CacheEntry(profile, result);
    }

    private sealed record CacheEntry(float[] Profile, AiAnalysisResult Result);
}
