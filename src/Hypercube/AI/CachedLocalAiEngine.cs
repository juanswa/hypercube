using System.Diagnostics;
using Hypercube.Core.Diagnostics;
using Hypercube.Models;

namespace Hypercube.AI;

/// <summary>
/// Decorates an <see cref="ILocalAiEngine"/> with similarity caching and diagnostics timing.
/// </summary>
public sealed class CachedLocalAiEngine : ILocalAiEngine
{
    private readonly ILocalAiEngine _inner;
    private readonly SimilarityInferenceCache _cache;
    private readonly RollupDiagnostics? _diagnostics;

    public CachedLocalAiEngine(
        ILocalAiEngine inner,
        SimilarityInferenceCache? cache = null,
        RollupDiagnostics? diagnostics = null)
    {
        _inner = inner;
        _cache = cache ?? new SimilarityInferenceCache();
        _diagnostics = diagnostics;
    }

    /// <inheritdoc />
    public AiAnalysisResult AnalyzeSummary(SummarySnapshot snapshot, SummarySnapshot? previousSnapshot = null, int topN = 5)
    {
        if (_cache.TryGet(snapshot, out var cached) && cached is not null)
        {
            _diagnostics?.RecordAiInference(0);
            return cached;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = _inner.AnalyzeSummary(snapshot, previousSnapshot, topN);
        stopwatch.Stop();
        _diagnostics?.RecordAiInference(stopwatch.Elapsed.TotalMilliseconds);
        _cache.Store(snapshot, result);
        return result;
    }

    /// <inheritdoc />
    public string GenerateNarrative(SummarySnapshot snapshot, AiAnalysisResult analysis) =>
        _inner.GenerateNarrative(snapshot, analysis);
}
