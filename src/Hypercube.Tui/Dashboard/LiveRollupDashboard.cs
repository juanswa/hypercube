using Hypercube.AI;
using Hypercube.Core;
using Hypercube.Core.Diagnostics;
using Hypercube.Models;
using Hypercube.Tui.Demo;
using Hypercube.Visualization;
using Spectre.Console;

namespace Hypercube.Tui.Dashboard;

/// <summary>
/// Spectre.Console live dashboard over a streaming rollup engine.
/// </summary>
public sealed class LiveRollupDashboard
{
    private const int MaxHistorySnapshots = 24;
    private const string DigestMetric = "latency";
    private static readonly string LatencyMeanKey = MetricNameHelper.Mean(DigestMetric);
    private static readonly string LatencyP95Key = MetricNameHelper.Percentile(DigestMetric, 95);
    private static readonly string[] InsightsSpinner = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    private readonly RollupEngine<DemoEvent> _engine;
    private readonly CachedLocalAiEngine _ai;
    private readonly DemoEventGenerator _generator = new();
    private readonly List<SummarySnapshot> _history = [];
    private HistorySeriesIndex? _cachedHistoryIndex;
    private SummarySnapshot? _previousSnapshot;
    private (SummarySnapshot Current, SummarySnapshot? Previous)? _latestPendingInsights;
    private readonly System.Threading.Lock _insightsPendingSync = new();
    private InsightsPanelCache _insightsCache = InsightsPanelCache.Waiting;
    private readonly System.Threading.Lock _insightsSync = new();
    private int _insightsInFlight;
    private long _eventsIngested;
    private long _refreshFrame;
    private RollupDiagnosticsSnapshot? _previousDiagnostics;
    private DateTimeOffset _previousDiagnosticsAt;

    public LiveRollupDashboard(EngineConfiguration? configuration = null)
    {
        var schema = RollupSchema
            .For<DemoEvent>()
            .Dimension(e => e.Channel)
            .Dimension(e => e.Region)
            .Count()
            .CountWhen(e => e.Acknowledged, "signal")
            .Average(e => e.LatencyMs, "latency_avg")
            .PercentileDigest(e => e.LatencyMs, "latency")
            .HyperLogLog(e => e.UserId, "users")
            .PrimaryMetric("count")
            .Build();

        _engine = new RollupEngine<DemoEvent>(schema, configuration ?? new EngineConfiguration
        {
            EnableDiagnostics = true,
            Windowing = new WindowConfiguration
            {
                Strategy = WindowStrategy.Tumbling,
                WindowSize = TimeSpan.FromMinutes(15)
            }
        });

        _ai = new CachedLocalAiEngine(new RuleBasedLocalAiEngine(), diagnostics: _engine.Diagnostics);
    }

    /// <summary>Runs the live dashboard until cancelled or <paramref name="duration"/> elapses.</summary>
    public void Run(TimeSpan refreshInterval, TimeSpan? duration = null, CancellationToken cancellationToken = default)
    {
        var started = _engine.Clock.UtcNow;
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Body").SplitColumns(
                    new Layout("Cells"),
                    new Layout("Insights").Size(48)));

        AnsiConsole.Live(layout)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .Start(ctx =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (duration is not null && _engine.Clock.UtcNow - started >= duration)
                    {
                        break;
                    }

                    IngestBurst();
                    var snapshot = _engine.DeriveSnapshot();
                    PushHistory(snapshot);
                    ScheduleInsightsRefresh(snapshot);

                    _refreshFrame++;
                    layout["Header"].Update(BuildHeader(snapshot, started));
                    layout["Cells"].Update(BuildCellsTable(snapshot));
                    layout["Insights"].Update(GetInsightsPanel());

                    ctx.Refresh();
                    Thread.Sleep(refreshInterval);
                }
            });

        if (!cancellationToken.IsCancellationRequested)
        {
            AnsiConsole.MarkupLine("[grey]Dashboard session ended.[/]");
        }
    }

    private void IngestBurst()
    {
        var burst = Random.Shared.Next(3, 12);
        for (var i = 0; i < burst; i++)
        {
            var evt = _generator.Next();
            _engine.TryAdd(evt, evt.Timestamp);
            _eventsIngested++;
        }
    }

    private void PushHistory(SummarySnapshot snapshot)
    {
        _history.Add(snapshot);
        if (_history.Count > MaxHistorySnapshots)
        {
            _history.RemoveAt(0);
        }

        if (_history.Count >= 2)
        {
            _cachedHistoryIndex = TerminalVisualizer.BuildHistorySeriesIndex(_history);
        }
        else
        {
            _cachedHistoryIndex = null;
        }
    }

    private void ScheduleInsightsRefresh(SummarySnapshot snapshot)
    {
        lock (_insightsPendingSync)
        {
            var previous = _previousSnapshot;
            _previousSnapshot = snapshot;
            // Overwrite stale work — dashboards only need the latest snapshot pair.
            _latestPendingInsights = (snapshot, previous);
        }

        EnsureInsightsWorkerRunning();
    }

    private void EnsureInsightsWorkerRunning()
    {
        if (Interlocked.CompareExchange(ref _insightsInFlight, 1, 0) != 0)
        {
            return;
        }

        Task.Run(ProcessLatestInsights);
    }

    private void ProcessLatestInsights()
    {
        try
        {
            while (TryTakePendingInsights(out var work))
            {
                ProcessInsightsWork(work);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _insightsInFlight, 0);

            // Re-arm the worker if a refresh arrived while we were processing — we cleared
            // _insightsInFlight above so EnsureInsightsWorkerRunning can start a new pass.
            if (HasPendingInsights())
            {
                EnsureInsightsWorkerRunning();
            }
        }
    }

    private bool TryTakePendingInsights(out (SummarySnapshot Current, SummarySnapshot? Previous) work)
    {
        lock (_insightsPendingSync)
        {
            if (_latestPendingInsights is null)
            {
                work = default;
                return false;
            }

            work = _latestPendingInsights.Value;
            _latestPendingInsights = null;
            return true;
        }
    }

    private bool HasPendingInsights()
    {
        lock (_insightsPendingSync)
        {
            return _latestPendingInsights is not null;
        }
    }

    private void ProcessInsightsWork((SummarySnapshot Current, SummarySnapshot? Previous) work)
    {
        try
        {
            var analysis = _ai.AnalyzeSummary(work.Current, work.Previous);
            var cache = BuildInsightsPanelCache(work.Current, analysis);
            lock (_insightsSync)
            {
                _insightsCache = cache;
            }
        }
        catch
        {
            _engine.Diagnostics?.RecordInsightFailure();
        }
    }

    private Panel GetInsightsPanel()
    {
        if (Volatile.Read(ref _insightsInFlight) == 1)
        {
            return InsightsPanelCache.Thinking.Panel;
        }

        lock (_insightsSync)
        {
            return _insightsCache.Panel;
        }
    }

    private Panel BuildHeader(SummarySnapshot snapshot, DateTimeOffset started)
    {
        var elapsed = _engine.Clock.UtcNow - started;
        var diag = _engine.Diagnostics?.CaptureSnapshot();
        var rateText = string.Empty;
        var spillText = string.Empty;
        var aiText = string.Empty;
        var insightsText = string.Empty;

        if (Volatile.Read(ref _insightsInFlight) == 1)
        {
            insightsText = $"  [grey]{InsightsSpinnerFrame()} insights[/]";
        }

        if (diag is { } current)
        {
            if (_previousDiagnostics is { } previous)
            {
                var seconds = (_engine.Clock.UtcNow - _previousDiagnosticsAt).TotalSeconds;
                if (seconds > 0)
                {
                    var rate = (current.EventsProcessed - previous.EventsProcessed) / seconds;
                    rateText = $"  [grey]{rate:0}/s[/]";
                }
            }

            spillText = current.MemoryToDiskSpills > 0
                ? $"  [orange1]spills {current.MemoryToDiskSpills}[/]"
                : string.Empty;
            aiText = current.LastAiInferenceDurationMs > 0
                ? $"  [grey]ai {current.LastAiInferenceDurationMs:0}ms[/]"
                : string.Empty;

            _previousDiagnostics = current;
            _previousDiagnosticsAt = _engine.Clock.UtcNow;
        }

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(
            new Markup($"[bold cyan]HYPERCUBE LIVE[/]  [grey]uptime {elapsed:mm\\:ss}[/]{insightsText}"),
            new Markup($"[yellow]ingested[/] {_eventsIngested}{rateText}{spillText}"),
            new Markup($"[green]cells[/] {snapshot.Rows.Count}{aiText}  [grey]{snapshot.GeneratedAt:HH:mm:ss}[/]"));
        return new Panel(grid) { Border = BoxBorder.Rounded, Header = new PanelHeader(" Stream ") };
    }

    private string InsightsSpinnerFrame() =>
        InsightsSpinner[(int)(Interlocked.Read(ref _refreshFrame) % InsightsSpinner.Length)];

    private Panel BuildCellsTable(SummarySnapshot snapshot)
    {
        var table = new Table().Border(TableBorder.SimpleHeavy);
        table.AddColumn("Dimension");
        table.AddColumn("Key");
        table.AddColumn(new TableColumn("Count").RightAligned());
        table.AddColumn(new TableColumn("Signal").RightAligned());
        table.AddColumn("Trend");
        table.AddColumn(new TableColumn("P95").RightAligned());

        HistorySeriesIndex? historyIndex = _cachedHistoryIndex;

        foreach (var row in snapshot.Rows.OrderByDescending(snapshot.PrimaryValue).Take(14))
        {
            var trend = string.Empty;
            if (historyIndex is not null &&
                historyIndex.TryGetSeries(row.Dimension, row.Key, out var series))
            {
                trend = TerminalVisualizer.RenderSparkline(series);
            }

            var p95 = row.Metrics.ContainsKey(LatencyP95Key)
                ? row[LatencyP95Key].ToString("0")
                : "-";

            table.AddRow(
                Markup.Escape(row.Dimension),
                Markup.Escape(row.Key),
                row.Count.ToString("0"),
                row.SignalCount.ToString("0"),
                string.IsNullOrEmpty(trend) ? "-" : $"[cyan]{trend}[/]",
                p95);
        }

        if (snapshot.Rows.Count == 0)
        {
            table.AddRow("-", "-", "0", "0", "-", "-");
        }

        return new Panel(table)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Dimension Cells ")
        };
    }

    private InsightsPanelCache BuildInsightsPanelCache(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        var builder = new System.Text.StringBuilder();

        builder.AppendLine("[bold]Narrative[/]");
        builder.AppendLine(Markup.Escape(_ai.GenerateNarrative(snapshot, analysis)));
        builder.AppendLine();

        if (analysis.TopInterestingCells.Count > 0)
        {
            builder.AppendLine("[bold]Interesting[/]");
            foreach (var insight in analysis.TopInterestingCells.Take(4))
            {
                builder.AppendLine(
                    $"• [yellow]{Markup.Escape(insight.CellId)}[/] [grey]{insight.Kind}[/] [cyan]{insight.Score:0.##}[/]");
            }

            builder.AppendLine();
        }

        var digestRow = snapshot.Rows.FirstOrDefault(row => row.Metrics.ContainsKey(LatencyMeanKey));
        var shapeRow = digestRow is null ? null : DistributionShapeEngine.AnalyzeRow(digestRow, DigestMetric);

        if (shapeRow is not null && digestRow is not null)
        {
            builder.AppendLine("[bold]Distribution[/]");
            builder.AppendLine(Markup.Escape(shapeRow.Summary));
            var buckets = TerminalVisualizer.ExtractDigestBuckets(digestRow, DigestMetric);
            builder.AppendLine(Markup.Escape(TerminalVisualizer.RenderHistogram(DigestMetric, buckets, maxBarWidth: 20)));
        }

        if (builder.Length == 0)
        {
            builder.Append("[grey]Waiting for data...[/]");
        }

        var panel = new Panel(new Markup(builder.ToString().TrimEnd()))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" AI Insights ")
        };

        return new InsightsPanelCache(panel);
    }

    private sealed class InsightsPanelCache(Panel panel)
    {
        public Panel Panel { get; } = panel;

        public static InsightsPanelCache Waiting { get; } = new(new Panel(new Markup("[grey]Waiting for first analysis...[/]"))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" AI Insights ")
        });

        public static InsightsPanelCache Thinking { get; } = new(new Panel(new Markup("[grey]AI thinking...[/]"))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" AI Insights ")
        });
    }
}
