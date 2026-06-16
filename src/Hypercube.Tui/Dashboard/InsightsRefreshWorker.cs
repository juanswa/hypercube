using Hypercube.AI;
using Hypercube.Core.Diagnostics;
using Hypercube.Models;

namespace Hypercube.Tui.Dashboard;

/// <summary>
/// Latest-wins background worker for snapshot insight analysis.
/// Pending refreshes are coalesced; the worker re-arms if new work arrives during processing.
/// </summary>
internal sealed class InsightsRefreshWorker
{
    private readonly ILocalAiEngine _ai;
    private readonly Action<SummarySnapshot, AiAnalysisResult> _onSuccess;
    private readonly RollupDiagnostics? _diagnostics;

    private (SummarySnapshot Current, SummarySnapshot? Previous)? _latestPending;
    private readonly System.Threading.Lock _pendingSync = new();
    private int _inFlight;

    public InsightsRefreshWorker(
        ILocalAiEngine ai,
        Action<SummarySnapshot, AiAnalysisResult> onSuccess,
        RollupDiagnostics? diagnostics = null)
    {
        _ai = ai;
        _onSuccess = onSuccess;
        _diagnostics = diagnostics;
    }

    /// <summary><c>true</c> while a background analysis pass is running.</summary>
    public bool IsInFlight => Volatile.Read(ref _inFlight) == 1;

    /// <summary>Queues the latest snapshot pair, overwriting any stale pending work.</summary>
    public void Schedule(SummarySnapshot current, SummarySnapshot? previous)
    {
        lock (_pendingSync)
        {
            _latestPending = (current, previous);
        }

        EnsureRunning();
    }

    private void EnsureRunning()
    {
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
        {
            return;
        }

        Task.Run(ProcessLatest);
    }

    private void ProcessLatest()
    {
        try
        {
            while (TryTakePending(out var work))
            {
                ProcessWork(work);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _inFlight, 0);

            // Re-arm if a refresh arrived while we were processing.
            if (HasPending())
            {
                EnsureRunning();
            }
        }
    }

    private bool TryTakePending(out (SummarySnapshot Current, SummarySnapshot? Previous) work)
    {
        lock (_pendingSync)
        {
            if (_latestPending is null)
            {
                work = default;
                return false;
            }

            work = _latestPending.Value;
            _latestPending = null;
            return true;
        }
    }

    private bool HasPending()
    {
        lock (_pendingSync)
        {
            return _latestPending is not null;
        }
    }

    private void ProcessWork((SummarySnapshot Current, SummarySnapshot? Previous) work)
    {
        try
        {
            var analysis = _ai.AnalyzeSummary(work.Current, work.Previous);
            _onSuccess(work.Current, analysis);
        }
        catch
        {
            _diagnostics?.RecordInsightFailure();
        }
    }
}
