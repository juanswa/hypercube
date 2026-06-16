using Hypercube;
using Hypercube.AI;
using Hypercube.Core;
using Hypercube.Core.Diagnostics;
using Hypercube.Models;
using Hypercube.Tui.Demo;
using Hypercube.Visualization;
using Spectre.Console;
using Spectre.Console.Rendering;

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
    private static readonly string[] InsightsSpinner = ["|", "/", "-", "\\"];
    private static readonly TimeSpan InsightsRefreshInterval = TimeSpan.FromSeconds(2);

    private readonly RollupEngine<DemoEvent> _engine;
    private readonly CachedLocalAiEngine _ai;
    private readonly DemoEventGenerator _generator = new();
    private readonly List<SummarySnapshot> _history = [];
    private HistorySeriesIndex? _cachedHistoryIndex;
    private SummarySnapshot? _previousSnapshot;
    private readonly InsightsRefreshWorker _insightsWorker;
    private readonly System.Threading.Lock _insightsSync = new();
    private InsightsPanelCache? _insightsCache;
    private bool _hasInsights;
    private List<string> _alertMarkupLines = [];
    private int _alertsScrollOffset;
    private bool _alertsPinnedToBottom = true;
    private long _eventsIngested;
    private long _refreshFrame;
    private DateTimeOffset _lastInsightsScheduledAt;
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
                    try
                    {
                        var built = BuildInsightsPanelCache(snapshot, analysis);
                        _insightsCache = built;
                        _alertMarkupLines = built.AlertLines;
                        if (_alertsPinnedToBottom || !_hasInsights)
                        {
                            _alertsScrollOffset = int.MaxValue;
                        }

                        _hasInsights = true;
                    }
                    catch
                    {
                        _insightsCache = InsightsPanelCache.Error;
                        _hasInsights = true;
                    }
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
        var insightsLayout = new Layout("Insights")
            .SplitRows(
                new Layout("InsightsSnapshot").Size(5),
                new Layout("InsightsAlerts"),
                new Layout("InsightsLatency").Size(8));

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Body").SplitColumns(
                    new Layout("Cells"),
                    insightsLayout.Size(48)));

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
                    var alertsViewport = AlertsViewportLines();
                    ProcessInsightsScrollInput(alertsViewport);

                    layout["Header"].Update(BuildHeader(snapshot, started));
                    layout["Cells"].Update(BuildCellsTable(snapshot));
                    layout["InsightsSnapshot"].Update(GetSnapshotPanel());
                    layout["InsightsAlerts"].Update(GetAlertsPanel(alertsViewport));
                    layout["InsightsLatency"].Update(GetLatencyPanel());

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

        var now = _engine.Clock.UtcNow;
        if (now - _lastInsightsScheduledAt < InsightsRefreshInterval)
        {
            return;
        }

        _lastInsightsScheduledAt = now;
        _insightsWorker.Schedule(snapshot, previous);
    }

    private int AlertsViewportLines()
    {
        var height = Math.Max(AnsiConsole.Profile.Height, 24);
        return Math.Clamp(height - 20, 6, 18);
    }

    private void ProcessInsightsScrollInput(int viewportHeight)
    {
        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true);
            var maxOffset = Math.Max(0, _alertMarkupLines.Count - viewportHeight);
            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.PageUp:
                    _alertsScrollOffset = Math.Max(0, _alertsScrollOffset - (key.Key == ConsoleKey.PageUp ? 3 : 1));
                    _alertsPinnedToBottom = false;
                    break;
                case ConsoleKey.DownArrow:
                case ConsoleKey.PageDown:
                    _alertsScrollOffset = Math.Min(
                        maxOffset,
                        _alertsScrollOffset + (key.Key == ConsoleKey.PageDown ? 3 : 1));
                    _alertsPinnedToBottom = _alertsScrollOffset >= maxOffset;
                    break;
                case ConsoleKey.Home:
                    _alertsScrollOffset = 0;
                    _alertsPinnedToBottom = false;
                    break;
                case ConsoleKey.End:
                    _alertsScrollOffset = maxOffset;
                    _alertsPinnedToBottom = true;
                    break;
            }
        }
    }

    private Panel GetSnapshotPanel()
    {
        lock (_insightsSync)
        {
            if (_hasInsights && _insightsCache?.Snapshot is not null)
            {
                return WrapInsightsSection(_insightsCache.Snapshot, " Snapshot ");
            }
        }

        if (_insightsWorker.IsInFlight)
        {
            return WrapInsightsSection(InsightsPanelCache.ThinkingContent, " Snapshot ");
        }

        return WrapInsightsSection(InsightsPanelCache.WaitingContent, " Snapshot ");
    }

    private Panel GetAlertsPanel(int viewportHeight)
    {
        lock (_insightsSync)
        {
            if (_hasInsights && _alertMarkupLines.Count > 0)
            {
                return BuildScrollableAlertsPanel(viewportHeight);
            }
        }

        if (_insightsWorker.IsInFlight)
        {
            return WrapInsightsSection(new Markup("[grey]Refreshing alerts...[/]"), " Alerts ");
        }

        return WrapInsightsSection(new Markup("[grey]Waiting for alerts...[/]"), " Alerts ");
    }

    private Panel GetLatencyPanel()
    {
        lock (_insightsSync)
        {
            if (_hasInsights && _insightsCache?.Latency is not null)
            {
                return WrapInsightsSection(_insightsCache.Latency, " Latency ");
            }
        }

        return WrapInsightsSection(new Markup("[grey]—[/]"), " Latency ");
    }

    private Panel BuildScrollableAlertsPanel(int viewportHeight)
    {
        var maxOffset = Math.Max(0, _alertMarkupLines.Count - viewportHeight);
        if (_alertsPinnedToBottom)
        {
            _alertsScrollOffset = maxOffset;
        }
        else
        {
            _alertsScrollOffset = Math.Clamp(_alertsScrollOffset, 0, maxOffset);
        }

        var visible = _alertMarkupLines
            .Skip(_alertsScrollOffset)
            .Take(viewportHeight)
            .ToList();

        var header = " Alerts ";
        if (_alertMarkupLines.Count > viewportHeight)
        {
            var from = _alertsScrollOffset + 1;
            var to = _alertsScrollOffset + visible.Count;
            header = $" Alerts {from}-{to}/{_alertMarkupLines.Count} ↑↓ ";
        }

        var body = visible.Count == 0
            ? "[grey]No alerts.[/]"
            : string.Join(Environment.NewLine, visible);

        return new Panel(new Markup(body))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(header)
        };
    }

    private static Panel WrapInsightsSection(IRenderable content, string header) =>
        new(content)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(header)
        };

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
                Markup.Escape(TerminalVisualizer.FormatDimensionKey(row.Key)),
                row.Count.ToString("0"),
                row.SignalCount.ToString("0"),
                string.IsNullOrEmpty(trend) ? "-" : $"[cyan]{Markup.Escape(trend)}[/]",
                p95);
        }

        if (snapshot.Rows.Count == 0)
        {
            table.AddRow("-", "-", "0", "0", "-", "-");
        }

        return new Panel(table)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader($" Dimension Cells  {PlainLanguageInsights.CellsTableLegend} ")
        };
    }

    private InsightsPanelCache BuildInsightsPanelCache(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        const int summaryWrapWidth = 40;
        const int alertWrapWidth = 38;
        var snapshotLines = WrapText(_ai.GenerateNarrative(snapshot, analysis), summaryWrapWidth);
        IRenderable snapshotContent = new Rows(
            new Markup("[bold]What's happening[/]"),
            new Markup(Markup.Escape(snapshotLines)));

        var highlights = SelectTopInsights(analysis);
        var alertLines = BuildAlertMarkupLines(highlights, alertWrapWidth);

        IRenderable? latencyContent = null;
        var digestRow = snapshot.Rows.FirstOrDefault(row => row.Metrics.ContainsKey(LatencyMeanKey));
        var shapeRow = digestRow is null ? null : DistributionShapeEngine.AnalyzeRow(digestRow, DigestMetric);

        if (shapeRow is not null && digestRow is not null)
        {
            var buckets = TerminalVisualizer.ExtractDigestBuckets(digestRow, DigestMetric);
            latencyContent = new Rows(
                new Markup(Markup.Escape(WrapText(
                    PlainLanguageInsights.WriteLatencySummary(shapeRow),
                    summaryWrapWidth))),
                new Markup(Markup.Escape(
                    TerminalVisualizer.RenderHistogram(DigestMetric, buckets, maxBarWidth: 12, compact: true))));
        }

        if (snapshot.Rows.Count == 0)
        {
            snapshotContent = new Markup("[grey]Waiting for data...[/]");
            alertLines = [];
            latencyContent = null;
        }

        return new InsightsPanelCache(snapshotContent, alertLines, latencyContent);
    }

    private static List<string> BuildAlertMarkupLines(
        IReadOnlyList<InterestingCellInsight> highlights,
        int wrapWidth)
    {
        var lines = new List<string>();
        foreach (var insight in highlights)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            var headline = PlainLanguageInsights.WriteAlertHeadline(insight);
            var body = PlainLanguageInsights.WriteAlertBody(insight);
            var combined = $"{headline} — {body}";
            if (combined.Length <= wrapWidth + 10)
            {
                lines.Add($"[yellow]⚠ {Markup.Escape(combined)}[/]");
                continue;
            }

            lines.Add($"[yellow]⚠ {Markup.Escape(headline)}[/]");
            foreach (var line in WrapText(body, wrapWidth).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                lines.Add(Markup.Escape(line));
            }
        }

        return lines;
    }

    private static IReadOnlyList<InterestingCellInsight> SelectTopInsights(AiAnalysisResult analysis)
    {
        var ranked = analysis.TopInterestingCells
            .GroupBy(static insight => insight.CellId, CellId.Comparer)
            .Select(static group => group.OrderByDescending(static i => i.Score).First())
            .OrderByDescending(static insight => insight.Score);

        var selected = new List<InterestingCellInsight>();
        var kindCounts = new Dictionary<InsightKind, int>();
        foreach (var insight in ranked)
        {
            if (selected.Count >= 3)
            {
                break;
            }

            kindCounts.TryGetValue(insight.Kind, out var count);
            if (count >= 2)
            {
                continue;
            }

            kindCounts[insight.Kind] = count + 1;
            selected.Add(insight);
        }

        return selected;
    }

    private static string WrapText(string text, int width)
    {
        if (width <= 0 || text.Length <= width)
        {
            return text;
        }

        var lines = new List<string>();
        var current = new System.Text.StringBuilder();
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current.Append(word);
                continue;
            }

            if (current.Length + 1 + word.Length <= width)
            {
                current.Append(' ').Append(word);
            }
            else
            {
                lines.Add(current.ToString());
                current.Clear().Append(word);
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current.ToString());
        }

        return string.Join(Environment.NewLine, lines);
    }

    private sealed class InsightsPanelCache(IRenderable snapshot, List<string> alertLines, IRenderable? latency)
    {
        public IRenderable Snapshot { get; } = snapshot;

        public List<string> AlertLines { get; } = alertLines;

        public IRenderable? Latency { get; } = latency;

        public static IRenderable WaitingContent { get; } = new Markup("[grey]Waiting for first analysis...[/]");

        public static IRenderable ThinkingContent { get; } = new Markup("[grey]AI thinking...[/]");

        public static InsightsPanelCache Error { get; } = new(
            new Markup("[red]Insight panel failed to render.[/]"),
            [],
            null);
    }
}
