using System.Diagnostics;
using Hypercube.AI;
using Hypercube.Core;
using global::Hypercube.Industry;
using global::Hypercube.Industry.Sms;
using Hypercube.Models;
using Hypercube.Tui.Demo;
using Hypercube.Visualization;
using Hypercube.AI.Onnx;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Hypercube.Tui.Dashboard;

internal sealed class CampaignBuildDashboard : IDisposable
{
    private const string FailureRateMetric = "failure_rate";
    private const string DeliveryRateMetric = "delivery_rate";
    private const string RejectdRateMetric = "rejectd_rate";
    private const string SpamRateMetric = "spam_rate";
    private const string ReplyRateMetric = "reply_rate";
    private const string OptOutRateMetric = "opt_out_rate";
    private static readonly TimeSpan InsightsRefreshInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan UiFrameInterval = TimeSpan.FromMilliseconds(100);

    private readonly SmsIndustryPlugin _plugin = new();
    private readonly RollupEngine<SmsEvent> _engine;
    private readonly CachedLocalAiEngine _ai;
    private readonly OnnxTextGenerator? _onnxGenerator;
    private readonly SmsCampaignGenerator _generator;
    private readonly ISubject _subject;
    private readonly InMemoryAccountHistory _history;
    private readonly ISendReportNarrator? _reportNarrator;
    private readonly InsightsRefreshWorker _insightsWorker;
    private readonly System.Threading.Lock _insightsSync = new();
    private readonly List<string> _activityLog = [];
    private InsightsPanelCache? _insightsCache;
    private bool _hasInsights;
    private List<string> _alertMarkupLines = [];
    private int _alertsScrollOffset;
    private bool _alertsPinnedToBottom = true;
    private DateTimeOffset _lastInsightsScheduledAt = DateTimeOffset.MinValue;

    public CampaignBuildDashboard(ISubject subject, OnnxTextGenerator? onnxGenerator = null, SmsCampaignGenerator? generator = null)
    {
        _subject = subject ?? throw new ArgumentNullException(nameof(subject));
        _onnxGenerator = onnxGenerator;
        _generator = generator ?? new SmsCampaignGenerator();

        _engine = new RollupEngine<SmsEvent>(
            _plugin.BuildSubjectSchema(),
            new EngineConfiguration
            {
                EnableDiagnostics = true
            });

        _history = new InMemoryAccountHistory();
        SeedHistory();

        _ai = new CachedLocalAiEngine(new RuleBasedLocalAiEngine(), diagnostics: _engine.Diagnostics);
        _reportNarrator = onnxGenerator is null
            ? null
            : new OnnxSendReportNarrator(onnxGenerator, _plugin.Narrative.Summary);
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

    public async Task RunAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Campaign count must be positive.");
        }

        var windowStart = _engine.Clock.UtcNow;
        var windowDuration = TimeSpan.FromDays(7);
        long ingested = 0;
        long totalMessages = 0;

        var ingestTask = Task.Run(() =>
        {
            foreach (var evt in _generator.Generate(_subject, count, windowStart, windowDuration))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _engine.Add(evt);
                Interlocked.Add(ref totalMessages, evt.Total);
                Interlocked.Increment(ref ingested);
            }
        }, cancellationToken);

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(5),
                new Layout("Activity").Size(8),
                new Layout("Body").SplitColumns(
                    new Layout("Cells"),
                    new Layout("Insights").Size(52).SplitRows(
                        new Layout("Narrative"),
                        new Layout("Alerts").Size(22))));

        CampaignReport? finalReport = null;
        var finalMessages = 0L;

        AnsiConsole.Live(layout)
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Top)
            .Start(ctx =>
            {
                var previousCount = 0L;
                var previousTimestamp = Stopwatch.GetTimestamp();
                SummarySnapshot? previousSnapshot = null;

                while (!ingestTask.IsCompleted && !cancellationToken.IsCancellationRequested)
                {
                    var currentCount = Volatile.Read(ref ingested);
                    var currentTimestamp = Stopwatch.GetTimestamp();
                    var elapsedSeconds = (currentTimestamp - previousTimestamp) / (double)Stopwatch.Frequency;
                    var eventsPerSecond = elapsedSeconds > 0 ? (currentCount - previousCount) / elapsedSeconds : 0;

                    previousCount = currentCount;
                    previousTimestamp = currentTimestamp;

                    var snapshot = _engine.DeriveSnapshot();
                    AppendActivityLog(currentCount, snapshot);
                    ScheduleInsightsRefresh(snapshot, previousSnapshot);
                    previousSnapshot = snapshot;

                    var alertsViewport = AlertsViewportLines();
                    ProcessInsightsScrollInput(alertsViewport);

                    layout["Header"].Update(BuildHeader(
                        snapshot,
                        windowStart,
                        windowDuration,
                        currentCount,
                        count,
                        eventsPerSecond,
                        Volatile.Read(ref totalMessages)));
                    layout["Activity"].Update(BuildActivityPanel());
                    layout["Cells"].Update(BuildCellsTable(snapshot));
                    layout["Narrative"].Update(BuildNarrativePanel("Local AI narrative panel ready for final audit..."));
                    layout["Alerts"].Update(BuildAlertsPanel(snapshot, alertsViewport));

                    ctx.Refresh();
                    Thread.Sleep(UiFrameInterval);
                }

                try
                {
                    ingestTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    AnsiConsole.MarkupLine("[grey]Campaign ingest cancelled.[/]");
                    return;
                }

                var finalSnapshot = _engine.DeriveSnapshot();
                finalMessages = Volatile.Read(ref totalMessages);
                finalReport = _plugin.BuildCampaignReport(
                    _subject,
                    finalSnapshot,
                    _history,
                    windowStart,
                    _engine.Clock.UtcNow);

                RenderReport(finalReport, finalMessages, layout["Narrative"], ctx);
            });

        if (finalReport is not null)
        {
            RenderFinalReport(finalReport, finalMessages);
        }
    }

    public void Dispose()
    {
        _insightsWorker.WaitForIdle();
        _engine.Dispose();
    }

    private void SeedHistory()
    {
        var carriers = new[] { "Vodacom", "MTN", "CellC", "Telkom" };
        var messageTypes = new[] { "OTP", "Transactional", "Promotional" };

        for (var i = 1; i <= 2; i++)
        {
            var rows = new List<SummaryRow>();

            foreach (var carrier in carriers)
            {
                rows.Add(new SummaryRow("carrier", carrier, new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.97,
                    ["failure_rate"] = 0.03,
                    ["rejectd_rate"] = 0.01,
                    ["spam_rate"] = 0.005
                }));
            }

            foreach (var messageType in messageTypes)
            {
                rows.Add(new SummaryRow("message_type", messageType, new Dictionary<string, double>
                {
                    ["delivery_rate"] = 0.97,
                    ["failure_rate"] = 0.03,
                    ["rejectd_rate"] = 0.01,
                    ["spam_rate"] = 0.005
                }));
            }

            foreach (var carrier in carriers)
            {
                foreach (var messageType in messageTypes)
                {
                    rows.Add(new SummaryRow("carrier_message_type", $"{carrier}|{messageType}", new Dictionary<string, double>
                    {
                        ["delivery_rate"] = 0.97,
                        ["failure_rate"] = 0.03,
                        ["rejectd_rate"] = 0.01,
                        ["spam_rate"] = 0.005
                    }));
                }
            }

            _history.Append(_subject.Id, new SummarySnapshot(
                _engine.Clock.UtcNow.AddHours(-i * 6),
                rows,
                FailureRateMetric));
        }
    }

    private void ScheduleInsightsRefresh(SummarySnapshot snapshot, SummarySnapshot? previous)
    {
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
        return Math.Clamp(height - 22, 6, 24);
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

    private Panel BuildHeader(
        SummarySnapshot snapshot,
        DateTimeOffset windowStart,
        TimeSpan windowDuration,
        long ingested,
        int count,
        double eventsPerSecond,
        long totalMessages)
    {
        var progressPercent = count == 0 ? 0d : ingested * 100.0 / count;
        var progressText = $"[yellow]progress[/] {BuildProgressText(ingested, count)} {ingested.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}/{count.ToString("0", System.Globalization.CultureInfo.InvariantCulture)} ({progressPercent.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}%)";
        var rateText = $"[grey]{eventsPerSecond.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}/s[/]";
        var messagesText = $"[green]messages {totalMessages.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}[/]";
        var cellsText = $"[green]cells {snapshot.Rows.Count}[/]";
        var modeText = BuildModeTelemetry();
        var windowText = $"[grey]window {windowStart:HH:mm:ss}+{windowDuration.Days}d[/]";

        var grid = new Grid();
        grid.AddColumn();
        grid.AddColumn();
        grid.AddRow(
            new Rows(
                new Markup($"[bold cyan]SMS CAMPAIGN[/]  {windowText}"),
                new Markup(Markup.Escape(progressText))),
            new Markup($"{rateText}  {messagesText}  {cellsText}  {modeText}  [grey]{snapshot.GeneratedAt:HH:mm:ss}[/]"));

        return new Panel(grid)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Campaign Build ")
        };
    }

    private Panel BuildActivityPanel()
    {
        var lines = _activityLog.TakeLast(7).Prepend("[bold cyan]Real-Time Telemetry[/]").ToList();
        return new Panel(new Markup(string.Join(Environment.NewLine, lines.Select(Markup.Escape))))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Raw SMS Activity Log ")
        };
    }

    private Panel BuildNarrativePanel(string text)
    {
        return new Panel(new Markup(Markup.Escape(text)))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Local AI Executive Narrative ")
        };
    }

    private void AppendActivityLog(long currentCount, SummarySnapshot snapshot)
    {
        var top = snapshot.Rows
            .OrderByDescending(static row => row["failure_rate"])
            .FirstOrDefault();
        var topText = top is null
            ? "cells=0"
            : $"{top.Dimension}:{top.Key} failure={top["failure_rate"].ToString("P1", System.Globalization.CultureInfo.InvariantCulture)}";
        var mode = _onnxGenerator is null ? "fallback" : "onnx";
        _activityLog.Add($"[{_engine.Clock.UtcNow:HH:mm:ss}] ingested={currentCount} cells={snapshot.Rows.Count} {topText} mode={mode}");
        if (_activityLog.Count > 80)
        {
            _activityLog.RemoveAt(0);
        }
    }

    private Panel BuildCellsTable(SummarySnapshot snapshot)
    {
        var table = new Table().Border(TableBorder.SimpleHeavy);
        table.AddColumn("Dimension");
        table.AddColumn("Key");
        table.AddColumn(new TableColumn("DELIVRD").RightAligned());
        table.AddColumn(new TableColumn("UNDELIV").RightAligned());
        table.AddColumn(new TableColumn("REJECTD").RightAligned());
        table.AddColumn(new TableColumn("SPAM").RightAligned());
        table.AddColumn(new TableColumn("EXPIRED").RightAligned());
        table.AddColumn(new TableColumn("CANCELLED").RightAligned());
        table.AddColumn(new TableColumn("REPLY").RightAligned());
        table.AddColumn(new TableColumn("OPT-OUT").RightAligned());

        foreach (var row in snapshot.Rows.OrderByDescending(row => row[FailureRateMetric]).Take(18))
        {
            var sent = row["sent"];
            var cancelled = row["cancelled"];
            var attempted = Math.Max(0d, sent - cancelled);
            var undelivRate = attempted <= 0 ? 0d : row["undeliv"] / attempted;
            var expiredRate = attempted <= 0 ? 0d : row["expired"] / attempted;
            var cancelledRate = sent <= 0 ? 0d : cancelled / sent;
            table.AddRow(
                Markup.Escape(FriendlyDimensionName(row.Dimension)),
                Markup.Escape(TerminalVisualizer.FormatDimensionKey(row.Key)),
                row[DeliveryRateMetric].ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                undelivRate.ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                row[RejectdRateMetric].ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                row[SpamRateMetric].ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                expiredRate.ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                cancelledRate.ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                row[ReplyRateMetric].ToString("P1", System.Globalization.CultureInfo.InvariantCulture),
                row[OptOutRateMetric].ToString("P1", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (snapshot.Rows.Count == 0)
        {
            table.AddRow("-", "-", "-", "-", "-", "-", "-", "-", "-", "-");
        }

        return new Panel(table)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Dimension Cells ")
        };
    }

    private static string FriendlyDimensionName(string dimension) =>
        dimension.ToLowerInvariant() switch
        {
            "hod" => "Hour of day",
            "dow" => "Weekday/Weekend",
            "carrier_message_type" => "Carrier x message type",
            "message_type" => "Message type",
            "carrier" => "Carrier",
            _ => dimension
        };

    private Panel BuildInsightsPanel()
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

    private Panel BuildAlertsPanel(SummarySnapshot snapshot, int viewportHeight)
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

        return BuildHardRuleAuditPanel(snapshot);
    }

    private Panel BuildHardRuleAuditPanel(SummarySnapshot snapshot)
    {
        var top = snapshot.Rows
            .Where(static row => row.Metrics.TryGetValue("failure_rate", out _))
            .OrderByDescending(static row => row["failure_rate"])
            .FirstOrDefault();
        if (top is null || top["failure_rate"] < 0.05)
        {
            return new Panel(new Markup("[grey]No hard-rule audit alerts. Failure-rate thresholds are within normal range.[/]"))
            {
                Border = BoxBorder.Rounded,
                Header = new PanelHeader(" Alerts ")
            };
        }

        var severity = top["failure_rate"] >= 0.10 ? "CRITICAL" : "WARNING";
        var color = top["failure_rate"] >= 0.10 ? "red" : "yellow";
        var text = $"{severity}: {top.Dimension} {top.Key} failure rate is {top["failure_rate"]:P1}; review this cell before increasing campaign volume.";
        return new Panel(new Markup($"[{color}]{Markup.Escape(text)}[/]"))
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(" Live System Audits ")
        };
    }

    private InsightsPanelCache BuildInsightsPanelCache(SummarySnapshot snapshot, AiAnalysisResult analysis)
    {
        const int summaryWrapWidth = 44;
        const int alertWrapWidth = 44;
        var snapshotLines = WrapText(_ai.GenerateNarrative(snapshot, analysis), summaryWrapWidth);
        IRenderable snapshotContent = new Rows(
            new Markup("[bold]What's happening[/]"),
            new Markup(Markup.Escape(snapshotLines)));

        var highlights = SelectTopInsights(analysis);
        var alertLines = BuildAlertMarkupLines(highlights, alertWrapWidth);

        if (snapshot.Rows.Count == 0)
        {
            snapshotContent = new Markup("[grey]Waiting for data...[/]");
            alertLines = [];
        }

        return new InsightsPanelCache(snapshotContent, alertLines);
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

    private void RenderReport(CampaignReport report, long totalMessages, Layout narrativeLayout, LiveDisplayContext ctx)
    {
        var aiFallbackMode = _reportNarrator is null;
        var polishedNarrative = _reportNarrator?.Generate(report.Analysis) ?? _plugin.Narrative.Summary(report.Analysis);
        var markdown = ExecutiveCampaignReportRenderer.RenderMarkdown(report, totalMessages, polishedNarrative, aiFallbackMode);
        var streamed = new System.Text.StringBuilder();

        foreach (var chunk in ChunkText(markdown, 120))
        {
            streamed.Append(chunk);
            narrativeLayout.Update(BuildNarrativePanel(streamed.ToString()));
            ctx.Refresh();
            Thread.Sleep(5);
        }

        File.WriteAllText("campaign-audit.md", markdown);
        AnsiConsole.MarkupLine("[green]Wrote campaign-audit.md[/]");
    }

    private void RenderFinalReport(CampaignReport report, long totalMessages)
    {
        var aiFallbackMode = _reportNarrator is null;
        var polishedNarrative = _reportNarrator?.Generate(report.Analysis) ?? _plugin.Narrative.Summary(report.Analysis);
        ExecutiveCampaignReportRenderer.Render(report, totalMessages, polishedNarrative, aiFallbackMode);
    }

    private static IEnumerable<string> ChunkText(string text, int chunkSize)
    {
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            yield return text.Substring(i, Math.Min(chunkSize, text.Length - i));
        }
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
                lines.Add($"[yellow]! {Markup.Escape(combined)}[/]");
                continue;
            }

            lines.Add($"[yellow]! {Markup.Escape(headline)}[/]");
            foreach (var line in WrapText(body, wrapWidth).Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                lines.Add(Markup.Escape(line));
            }
        }

        return lines;
    }

    private static Panel WrapInsightsSection(IRenderable content, string header) =>
        new(content)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader(header)
        };

    private string BuildModeTelemetry()
    {
        var ramGb = Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d / 1024d;
        if (_onnxGenerator is null)
        {
            return "[yellow]AI: Fallback Mode (Weights Missing)[/]";
        }

        return $"[green]AI: Local AI (ONNX Runtime - Fully Offline)[/] [grey]{_onnxGenerator.LastInferenceTokensPerSecond.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} tok/s | {ramGb.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} GB[/]";
    }

    private static string BuildProgressText(long ingested, int count)
    {
        const int width = 24;
        if (count <= 0)
        {
            return "[" + new string('-', width) + "]";
        }

        var filled = (int)Math.Round(width * Math.Clamp(ingested / (double)count, 0, 1));
        return "[" + new string('#', filled) + new string('-', width - filled) + "]";
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

    private sealed class InsightsPanelCache(IRenderable snapshot, List<string> alertLines)
    {
        public IRenderable Snapshot { get; } = snapshot;

        public List<string> AlertLines { get; } = alertLines;

        public static IRenderable WaitingContent { get; } = new Markup("[grey]Waiting for first analysis...[/]");

        public static IRenderable ThinkingContent { get; } = new Markup("[grey]AI thinking...[/]");

        public static InsightsPanelCache Error { get; } = new(
            new Markup("[red]Insight panel failed to render.[/]"),
            []);
    }
}
