using Hypercube;
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
    private readonly InsightsRefreshWorker _insightsWorker;
    private readonly System.Threading.Lock _insightsSync = new();
    private InsightsPanelCache _insightsCache = InsightsPanelCache.Waiting;
    private long _eventsIngested;
    private long _refreshFrame;
    private RollupDiagnosticsSnapshot? _previousDiagnostics;
    private DateTimeOffset _previousDiagnosticsAt;

    /// <summary>
    /// Runs the demo dashboard with a built-in synthetic event schema.
    /// </summary>
    public LiveRollupDashboard(EngineConfiguration? configuration = null)
        : this(CreateDemoEngine(configuration))
    {
    }

    /// <summary>
    /// Runs the dashboard over a caller-supplied rollup engine (for example from <see cref="Hypercube.CreateEngine"/>).
    /// </summary>
    public LiveRollupDashboard(RollupEngine<DemoEvent> engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _ai = new CachedLocalAiEngine(new RuleBasedLocalAiEngine(), diagnostics: _engine.Diagnostics);
        _insightsWorker = new InsightsRefreshWorker(
            _ai,
            (snapshot, analysis) =>
            {
                lock (_insightsSync)
                {
                    _insightsCache = BuildInsightsPanelCache(snapshot, analysis);
                }
            },
            _engine.Diagnostics);
    }

    private static RollupEngine<DemoEvent> CreateDemoEngine(EngineConfiguration? configuration)
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

        return Hypercube.CreateEngine(schema, configuration ?? new EngineConfiguration
        {
            EnableDiagnostics = true,
            Windowing = new WindowConfiguration
            {
                Strategy = WindowStrategy.Tumbling,
                WindowSize = TimeSpan.FromMinutes(15)
            }
        });
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
        var previous = _previousSnapshot;
        _previousSnapshot = snapshot;
        _insightsWorker.Schedule(snapshot, previous);
    }

    private Panel GetInsightsPanel()
    {
        if (_insightsWorker.IsInFlight)
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

        if (_insightsWorker.IsInFlight)
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
