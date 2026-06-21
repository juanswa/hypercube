using System.Globalization;
using Hypercube.Industry;
using Hypercube.Industry.Sms;
using Hypercube.Models;
using Spectre.Console;

namespace Hypercube.Tui.Dashboard;

internal static class ExecutiveCampaignReportRenderer
{
    private const string FailureRateMetric = "failure_rate";
    private const string DeliveryRateMetric = "delivery_rate";
    private const string RejectRateMetric = "rejectd_rate";
    private const string SpamRateMetric = "spam_rate";

    public static void Render(CampaignReport report, long totalMessages, string? polishedNarrative, bool aiFallbackMode)
    {
        var model = BuildModel(report);
        var grade = CampaignGrade.Compute(model);
        var root = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .Title("[bold white on blue] EXECUTIVE CAMPAIGN PERFORMANCE AUDIT [/]")
            .AddColumn(new TableColumn("Content").PadLeft(2).PadRight(2));

        root.AddRow(new Markup(Markup.Escape($"Grade {grade}; Sent {FormatCount(model.Sent)}; Attempted {FormatCount(model.Attempted)}; Delivered {FormatCount(model.Delivered)}; Failed {FormatCount(model.Failed)}; Cancelled {FormatCount(model.Cancelled)}; Delivery {FormatRate(model.DeliveryRate)} of attempted; Failure {FormatRate(model.FailureRate)} of attempted; Segments {model.SegmentCount}.")));
        if (!string.IsNullOrWhiteSpace(polishedNarrative))
        {
            root.AddEmptyRow();
            root.AddRow(new Markup(Markup.Escape(polishedNarrative.ReplaceLineEndings(" "))));
        }

        root.AddEmptyRow();
        root.AddRow(new Panel(new Markup(Markup.Escape(BuildSankeyText(model)))) { Header = new PanelHeader(" Delivery Flow (Sankey) "), Border = BoxBorder.Rounded });
        root.AddEmptyRow();
        root.AddRow(new Panel(BuildWorstBestTable(model)) { Header = new PanelHeader(" Segment Leaderboard "), Border = BoxBorder.Rounded });
        root.AddEmptyRow();
        root.AddRow(new Markup(Markup.Escape(BuildFailureReasonLine(model))));
        root.AddEmptyRow();
        root.AddRow(new Markup(Markup.Escape(BuildParetoLine(model))));
        root.AddEmptyRow();
        root.AddRow(new Markup(Markup.Escape(BuildTimingLine(model))));
        root.AddEmptyRow();
        root.AddRow(new Markup(Markup.Escape(BuildAnomalyLine(model))));
        root.AddEmptyRow();
        root.AddRow(new Markup(Markup.Escape($"Mode: {(aiFallbackMode ? "Fallback Mode (Weights Missing)" : "Local AI (ONNX Runtime - Fully Offline)")}; Subject: {report.Subject.Id}; Window: {report.WindowStart:u} -> {report.WindowEnd:u}; Total messages: {totalMessages.ToString("N0", CultureInfo.InvariantCulture)}.")));

        AnsiConsole.Write(root);
    }

    public static string RenderMarkdown(CampaignReport report, long totalMessages, string? polishedNarrative, bool aiFallbackMode)
    {
        var model = BuildModel(report);
        var grade = CampaignGrade.Compute(model);
        var sb = new System.Text.StringBuilder();

        sb.AppendLine("# 📊 Executive Campaign Performance Audit");
        sb.AppendLine();
        sb.AppendLine("## 🗣️ Executive narrative");
        sb.AppendLine($"- {BuildExecutiveNarrative(model, polishedNarrative)}");
        sb.AppendLine();
        sb.AppendLine("## 👑 Campaign totals");
        sb.AppendLine($"- **Campaign Score / Grade:** **{grade}**");
        sb.AppendLine($"- **Sent:** {FormatCount(model.Sent)}");
        sb.AppendLine($"- **Attempted:** {FormatCount(model.Attempted)}");
        sb.AppendLine($"- **Delivered:** {FormatCount(model.Delivered)}");
        sb.AppendLine($"- **Failed:** {FormatCount(model.Failed)}");
        sb.AppendLine($"- **Cancelled:** {FormatCount(model.Cancelled)}");
        sb.AppendLine($"- **Delivery rate:** {FormatRate(model.DeliveryRate)}");
        sb.AppendLine($"- **Failure rate:** {FormatRate(model.FailureRate)}");
        sb.AppendLine("- **Rate meaning:** Delivery and failure rates use **Attempted = Sent − Cancelled** as the denominator.");
        sb.AppendLine($"- **Window:** {report.WindowStart:u} → {report.WindowEnd:u}; **#segments:** {model.SegmentCount.ToString("N0", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- **Mode:** {(aiFallbackMode ? "Fallback Mode (Weights Missing)" : "Local AI (ONNX Runtime - Fully Offline)")}");
        sb.AppendLine($"- **Subject:** {report.Subject.Id}; **Total messages:** {totalMessages.ToString("N0", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("## 🧭 Segment leaderboard");
        foreach (var segment in model.WorstSegments)
        {
            sb.AppendLine($"- **Worst:** {segment.Key} · failure {FormatRate(segment.FailureRate)} · sent {FormatCount(segment.Sent)} · {segment.MultipleVsCampaign.ToString("0.0", CultureInfo.InvariantCulture)}× campaign average");
        }

        foreach (var segment in model.BestSegments)
        {
            sb.AppendLine($"- **Best:** {segment.Key} · delivery {FormatRate(segment.DeliveryRate)} · sent {FormatCount(segment.Sent)} · {(segment.FailureRate / Math.Max(model.FailureRate, 1e-9)).ToString("0.0", CultureInfo.InvariantCulture)}× campaign avg failure");
        }

        sb.AppendLine();
        sb.AppendLine("## 🧪 Status mix");
        sb.AppendLine($"- DELIVRD: {FormatCount(model.Delivered)} ({FormatRate(model.Attempted <= 0 ? 0d : model.Delivered / model.Attempted)} of attempted)");
        sb.AppendLine($"- EXPIRED: {FormatCount(model.Expired)} ({FormatRate(model.Attempted <= 0 ? 0d : model.Expired / model.Attempted)} of attempted)");
        sb.AppendLine($"- UNDELIV: {FormatCount(model.Undeliv)} ({FormatRate(model.Attempted <= 0 ? 0d : model.Undeliv / model.Attempted)} of attempted)");
        sb.AppendLine($"- REJECTD: {FormatCount(model.Rejectd)} ({FormatRate(model.Attempted <= 0 ? 0d : model.Rejectd / model.Attempted)} of attempted)");
        sb.AppendLine($"- SPAM: {FormatCount(model.Spam)} ({FormatRate(model.Attempted <= 0 ? 0d : model.Spam / model.Attempted)} of attempted)");
        sb.AppendLine($"- CANCELLED: {FormatCount(model.Cancelled)} ({FormatRate(model.Sent <= 0 ? 0d : model.Cancelled / model.Sent)} of sent)");

        sb.AppendLine();
        sb.AppendLine("## 🔀 Delivery flow (Sankey)");
        sb.AppendLine("```text");
        sb.AppendLine(BuildSankeyText(model));
        sb.AppendLine("```");

        sb.AppendLine();
        sb.AppendLine("## 🧩 Failure-reason ranking");
        sb.AppendLine($"- {BuildFailureReasonLine(model)}");

        sb.AppendLine();
        sb.AppendLine("## 🎯 Worst segment per reason");
        foreach (var reason in model.WorstReasonSegments)
        {
            sb.AppendLine($"- {reason.Reason}: {reason.Key} · rate {FormatRate(reason.Rate)} · sent {FormatCount(reason.Sent)}");
        }

        sb.AppendLine();
        sb.AppendLine("## 📉 Failure concentration (Pareto)");
        sb.AppendLine($"- {BuildParetoLine(model)}");

        sb.AppendLine();
        sb.AppendLine("## ⏰ Timing patterns");
        sb.AppendLine($"- {BuildTimingLine(model)}");

        sb.AppendLine();
        sb.AppendLine("## ⚠️ Ranked anomaly red flags");
        foreach (var ranked in model.RankedAnomalies)
        {
            var observation = ranked.Observation;
            var sent = ranked.Sent;
            var baseline = observation.SelfExpected ?? observation.CohortMedian ?? 0d;
            sb.AppendLine($"- **{observation.Kind}:** {DescribeDimension(observation.Dimension)} ({observation.CellKey}) {observation.Metric}; actual {FormatRate(observation.Actual)}, baseline {FormatRate(baseline)}, deviation {FormatSigned(observation.Deviation)}, sent {FormatCount(sent)}.");
        }

        sb.AppendLine();
        sb.AppendLine("## 💥 Quantified impact");
        sb.AppendLine($"- **{FormatCount(model.Failed)} messages were not delivered.**");
        sb.AppendLine($"- {BuildParetoLine(model)}");

        sb.AppendLine();
        sb.AppendLine("## 🚀 Actionable recommendations");
        if (model.PrimaryRecommendation is not null)
        {
            sb.AppendLine($"1. **{model.PrimaryRecommendation}**");
            if (!string.IsNullOrWhiteSpace(model.SecondaryRecommendation))
            {
                sb.AppendLine($"2. **Secondary focus:** {model.SecondaryRecommendation}");
            }
        }
        else
        {
            sb.AppendLine("1. **Continue monitoring; no material unfavorable high-volume segment was detected.**");
        }

        sb.AppendLine();
        sb.AppendLine("## Honest gaps");
        sb.AppendLine("- Delivery latency: insufficient data; the v1 SMS schema does not include latency metrics.");
        sb.AppendLine("- Sub-account/route-level attribution: insufficient data; the current schema does not include route identifiers.");

        return sb.ToString();
    }

    private static ReportModel BuildModel(CampaignReport report)
    {
        var carrierRows = report.Snapshot.Rows.Where(static r => r.Dimension.Equals("carrier", StringComparison.OrdinalIgnoreCase)).ToList();
        var sent = carrierRows.Sum(static r => r["sent"]);
        var delivered = carrierRows.Sum(static r => r["delivered"]);
        var expired = carrierRows.Sum(static r => r["expired"]);
        var undeliv = carrierRows.Sum(static r => r["undeliv"]);
        var rejectd = carrierRows.Sum(static r => r["rejectd"]);
        var spam = carrierRows.Sum(static r => r["spam"]);
        var cancelled = carrierRows.Sum(static r => r["cancelled"]);
        var attempted = Math.Max(0d, sent - cancelled);
        var failed = expired + undeliv + rejectd + spam;
        var deliveryRate = attempted <= 0 ? 0d : delivered / attempted;
        var failureRate = attempted <= 0 ? 0d : failed / attempted;

        var segmentRows = report.Snapshot.Rows
            .Where(static r => r.Dimension.Equals("carrier_message_type", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var volumeFloor = Math.Max(1000d, sent * 0.001d);
        var eligibleSegments = segmentRows
            .Where(r => r["sent"] >= volumeFloor)
            .Select(r => new SegmentRow(
                r.Key,
                r["sent"],
                r["delivery_rate"],
                r["failure_rate"],
                failureRate <= 0 ? 0d : r["failure_rate"] / failureRate,
                r["expired"] + r["undeliv"] + r["rejectd"] + r["spam"],
                r["rejectd"],
                r["spam"],
                r["expired"],
                r["undeliv"]))
            .ToList();

        var worst = eligibleSegments.OrderByDescending(static s => s.FailureRate).Take(3).ToList();
        var best = eligibleSegments.OrderByDescending(static s => s.DeliveryRate).Take(3).ToList();
        var topFailed = eligibleSegments.OrderByDescending(static s => s.Failed).FirstOrDefault();
        var worstRejectd = eligibleSegments.OrderByDescending(static s => s.RejectdRate).FirstOrDefault();
        var worstSpam = eligibleSegments.OrderByDescending(static s => s.SpamRate).FirstOrDefault();
        var worstReachability = eligibleSegments.OrderByDescending(static s => s.ExpiredUndelivRate).FirstOrDefault();
        var worstReasonSegments = new List<ReasonSegmentRow>();
        if (worstRejectd is not null) worstReasonSegments.Add(new ReasonSegmentRow("REJECTD", worstRejectd.Key, worstRejectd.RejectdRate, worstRejectd.Sent));
        if (worstSpam is not null) worstReasonSegments.Add(new ReasonSegmentRow("SPAM", worstSpam.Key, worstSpam.SpamRate, worstSpam.Sent));
        if (worstReachability is not null) worstReasonSegments.Add(new ReasonSegmentRow("UNDELIV+EXPIRED", worstReachability.Key, worstReachability.ExpiredUndelivRate, worstReachability.Sent));

        var dominantReason = DetermineDominantReason(expired, undeliv, rejectd, spam);
        var recommendation = BuildRecommendation(topFailed, dominantReason);
        var secondary = BuildSecondaryRecommendation(topFailed, dominantReason);
        var rankedAnomalies = report.Observations
            .Where(static o => o.IsMaterial && o.IsFavorable is false)
            .Select(o =>
            {
                var row = report.Snapshot.Rows.FirstOrDefault(r => r.Dimension.Equals(o.Dimension, StringComparison.OrdinalIgnoreCase) && r.Key.Equals(o.CellKey, StringComparison.OrdinalIgnoreCase));
                var rowSent = row is null ? 0d : row["sent"];
                return new RankedObservation(o, rowSent, Math.Abs(o.Deviation) * Math.Log10(1d + Math.Max(rowSent, 0d)));
            })
            .OrderByDescending(static o => o.Rank)
            .Take(8)
            .ToList();

        var hodRows = report.Snapshot.Rows.Where(static r => r.Dimension.Equals("hod", StringComparison.OrdinalIgnoreCase)).ToList();
        var bestHour = hodRows.OrderByDescending(static r => r[DeliveryRateMetric]).FirstOrDefault();
        var worstHour = hodRows.OrderBy(static r => r[DeliveryRateMetric]).FirstOrDefault();
        var dowRows = report.Snapshot.Rows.Where(static r => r.Dimension.Equals("dow", StringComparison.OrdinalIgnoreCase)).ToList();

        return new ReportModel(sent, attempted, delivered, failed, cancelled, expired, undeliv, rejectd, spam, deliveryRate, failureRate, eligibleSegments.Count, worst, best, topFailed, worstReasonSegments, dominantReason, rankedAnomalies, bestHour, worstHour, dowRows, recommendation, secondary);
    }

    private static string BuildFailureReasonLine(ReportModel model)
    {
        if (model.Failed <= 0)
        {
            return "No non-delivered attempted messages were recorded.";
        }

        return $"Of {FormatCount(model.Failed)} failures: REJECTD {FormatRate(model.Rejectd / model.Failed)}, SPAM {FormatRate(model.Spam / model.Failed)}, UNDELIV {FormatRate(model.Undeliv / model.Failed)}, EXPIRED {FormatRate(model.Expired / model.Failed)}. Dominant reason: {model.DominantReason}.";
    }

    private static string BuildParetoLine(ReportModel model)
    {
        if (model.TopFailedSegment is null || model.Failed <= 0)
        {
            return "No material failure concentration was detected in carrier × message type segments.";
        }

        var share = model.TopFailedSegment.Failed / model.Failed;
        var reason = model.TopFailedSegment.DominantReason;
        var reasonShare = model.TopFailedSegment.DominantReasonShare;
        return $"{FormatRate(share)} of all failures came from {model.TopFailedSegment.Key}; {FormatRate(reasonShare)} of those were {reason} — a likely carrier-filter/throttle signature.";
    }

    private static string BuildTimingLine(ReportModel model)
    {
        if (model.BestHour is null || model.WorstHour is null)
        {
            return "Hour-of-day and weekday/weekend timing analysis is unavailable for this snapshot.";
        }

        var spread = model.BestHour[DeliveryRateMetric] - model.WorstHour[DeliveryRateMetric];
        var dowText = model.DowRows.Count == 0
            ? "weekday/weekend split unavailable"
            : string.Join(", ",
                model.DowRows
                    .OrderBy(static r => r.Key)
                    .Take(2)
                    .Select(r => $"{r.Key} {FormatRate(r[DeliveryRateMetric])}"));
        return $"Delivery dipped to {FormatRate(model.WorstHour[DeliveryRateMetric])} at hour-of-day {model.WorstHour.Key}, vs {FormatRate(model.BestHour[DeliveryRateMetric])} at peak (spread {FormatRate(spread)}); {dowText}.";
    }

    private static string BuildAnomalyLine(ReportModel model)
    {
        if (model.RankedAnomalies.Count == 0)
        {
            return "No material unfavorable observations were ranked by severity × volume.";
        }

        var top = model.RankedAnomalies[0];
        var baseline = top.Observation.SelfExpected ?? top.Observation.CohortMedian ?? 0d;
        return $"Top ranked anomaly: {top.Observation.Kind} on {DescribeDimension(top.Observation.Dimension)} ({top.Observation.CellKey}, {top.Observation.Metric}) actual {FormatRate(top.Observation.Actual)} vs baseline {FormatRate(baseline)}, deviation {FormatSigned(top.Observation.Deviation)}, sent {FormatCount(top.Sent)}.";
    }

    private static string BuildSankeyText(ReportModel model)
    {
        var cancelledShare = model.Sent <= 0 ? 0d : model.Cancelled / model.Sent;
        var attemptedShare = model.Sent <= 0 ? 0d : model.Attempted / model.Sent;
        var deliveredShare = model.Attempted <= 0 ? 0d : model.Delivered / model.Attempted;
        var failedShare = model.Attempted <= 0 ? 0d : model.Failed / model.Attempted;
        var expiredShare = model.Failed <= 0 ? 0d : model.Expired / model.Failed;
        var undelivShare = model.Failed <= 0 ? 0d : model.Undeliv / model.Failed;
        var rejectdShare = model.Failed <= 0 ? 0d : model.Rejectd / model.Failed;
        var spamShare = model.Failed <= 0 ? 0d : model.Spam / model.Failed;

        return string.Join(Environment.NewLine,
            $"SENT {FormatCount(model.Sent)}",
            $"  ├─ ATTEMPTED {FormatCount(model.Attempted)} ({FormatRate(attemptedShare)} of sent)",
            $"  └─ CANCELLED {FormatCount(model.Cancelled)} ({FormatRate(cancelledShare)} of sent)",
            $"      = Balance check over sent denominator: ATTEMPTED + CANCELLED = SENT ({FormatCount(model.Attempted + model.Cancelled)} = {FormatCount(model.Sent)}, 100.0% of sent)",
            $"  │   ├─ DELIVRD {FormatCount(model.Delivered)} ({FormatRate(deliveredShare)} of attempted)",
            $"  │   └─ FAILED {FormatCount(model.Failed)} ({FormatRate(failedShare)} of attempted)",
            $"  │       = Balance check over attempted denominator: DELIVRD + FAILED = ATTEMPTED ({FormatCount(model.Delivered + model.Failed)} = {FormatCount(model.Attempted)}, 100.0% of attempted)",
            $"  │       ├─ EXPIRED {FormatCount(model.Expired)} ({FormatRate(expiredShare)} of failed)",
            $"  │       ├─ UNDELIV {FormatCount(model.Undeliv)} ({FormatRate(undelivShare)} of failed)",
            $"  │       ├─ REJECTD {FormatCount(model.Rejectd)} ({FormatRate(rejectdShare)} of failed)",
            $"  │       └─ SPAM {FormatCount(model.Spam)} ({FormatRate(spamShare)} of failed)",
            $"  │           = Balance check over failed denominator: EXPIRED + UNDELIV + REJECTD + SPAM = FAILED ({FormatCount(model.Expired + model.Undeliv + model.Rejectd + model.Spam)} = {FormatCount(model.Failed)}, 100.0% of failed)");
    }

    private static string BuildExecutiveNarrative(ReportModel model, string? polishedNarrative)
    {
        var narrative = string.IsNullOrWhiteSpace(polishedNarrative)
            ? "Campaign performance is summarized deterministically from the observed delivery outcomes."
            : polishedNarrative.ReplaceLineEndings(" ").Trim();
        var concentration = model.TopFailedSegment is null || model.Failed <= 0
            ? "No single carrier × message type segment materially concentrates failures."
            : $"Failure concentration is meaningful: {model.TopFailedSegment.Key} contributes {FormatRate(model.TopFailedSegment.Failed / model.Failed)} of failed attempted messages.";
        var timing = model.BestHour is null || model.WorstHour is null
            ? "Hour-of-day timing data is not available in this snapshot."
            : $"Hour-of-day spread is {FormatRate(model.BestHour[DeliveryRateMetric] - model.WorstHour[DeliveryRateMetric])} between the best and worst hours.";

        return $"{narrative} Overall, {FormatCount(model.Sent)} messages were sent, {FormatCount(model.Attempted)} were attempted at carriers, and {FormatCount(model.Failed)} were not delivered ({FormatRate(model.FailureRate)} failure over attempted traffic). {BuildFailureReasonLine(model)} {concentration} {timing}";
    }

    private static string DescribeDimension(string dimension) =>
        dimension.ToLowerInvariant() switch
        {
            "hod" => "Hour of day",
            "dow" => "Day group (weekday/weekend)",
            "carrier_message_type" => "Carrier × message type",
            "message_type" => "Message type",
            "carrier" => "Carrier",
            _ => dimension
        };

    private static Table BuildWorstBestTable(ReportModel model)
    {
        var table = new Table().AddColumn("Group").AddColumn("Segment").AddColumn("Rate").AddColumn("Sent").AddColumn("Vs campaign");
        foreach (var segment in model.WorstSegments)
        {
            table.AddRow("Worst", segment.Key, FormatRate(segment.FailureRate), FormatCount(segment.Sent), $"{segment.MultipleVsCampaign.ToString("0.0", CultureInfo.InvariantCulture)}x");
        }

        foreach (var segment in model.BestSegments)
        {
            table.AddRow("Best", segment.Key, FormatRate(segment.DeliveryRate), FormatCount(segment.Sent), "-");
        }

        if (model.WorstSegments.Count == 0 && model.BestSegments.Count == 0)
        {
            table.AddRow("-", "No segment above volume floor", "-", "-", "-");
        }

        return table;
    }

    private static string FormatRate(double value) => value.ToString("P1", CultureInfo.InvariantCulture);

    private static string FormatSigned(double value) => value.ToString("+0.0%;-0.0%;0.0%", CultureInfo.InvariantCulture);

    private static string FormatCount(double value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static class CampaignGrade
    {
        public static string Compute(ReportModel model)
        {
            var concentration = model.TopFailedSegment is null || model.Failed <= 0
                ? 0d
                : model.TopFailedSegment.Failed / model.Failed;
            if (model.DeliveryRate >= 0.98 && concentration <= 0.50)
            {
                return "A";
            }

            if (model.DeliveryRate >= 0.95)
            {
                return "B";
            }

            if (model.DeliveryRate >= 0.90 && concentration <= 0.70)
            {
                return "C";
            }

            return "D";
        }
    }

    private static string DetermineDominantReason(double expired, double undeliv, double rejectd, double spam)
    {
        var reasons = new Dictionary<string, double>
        {
            ["REJECTD"] = rejectd,
            ["SPAM"] = spam,
            ["UNDELIV"] = undeliv,
            ["EXPIRED"] = expired
        };

        return reasons.OrderByDescending(static p => p.Value).First().Key;
    }

    private static string? BuildRecommendation(SegmentRow? worst, string dominantReason)
    {
        if (worst is null)
        {
            return null;
        }

        return dominantReason switch
        {
            "REJECTD" => $"Carrier/route rejection. Move {worst.Key} to a backup route and raise the throttle/blocklist issue with the provider for the affected window.",
            "SPAM" => $"Carrier spam filtering. Review {worst.Key} message content and sender-ID registration, and verify opt-in provenance.",
            "UNDELIV" => $"Permanent undeliverables (invalid numbers / no route). Clean the recipient list for {worst.Key}.",
            "EXPIRED" => $"Validity-window lapses (handset off / out of coverage). Extend validity or retry, and prefer higher-reachability send hours for {worst.Key}.",
            _ => $"Messages cancelled before attempt. Investigate the cancellation source/config for {worst.Key}."
        };
    }

    private static string? BuildSecondaryRecommendation(SegmentRow? worst, string dominantReason)
    {
        if (worst is null)
        {
            return null;
        }

        return $"Prioritize {worst.Key} where failure is {worst.MultipleVsCampaign.ToString("0.0", CultureInfo.InvariantCulture)}× campaign average, then work through ranked anomalies by severity × volume.";
    }

    private sealed record SegmentRow(string Key, double Sent, double DeliveryRate, double FailureRate, double MultipleVsCampaign, double Failed, double Rejectd, double Spam, double Expired, double Undeliv)
    {
        public double RejectdRate => Sent <= 0 ? 0d : Rejectd / Sent;

        public double SpamRate => Sent <= 0 ? 0d : Spam / Sent;

        public double ExpiredUndelivRate => Sent <= 0 ? 0d : (Expired + Undeliv) / Sent;

        public string DominantReason => DetermineDominantReason(Expired, Undeliv, Rejectd, Spam);

        public double DominantReasonShare
        {
            get
            {
                if (Failed <= 0)
                {
                    return 0d;
                }

                return DominantReason switch
                {
                    "REJECTD" => Rejectd / Failed,
                    "SPAM" => Spam / Failed,
                    "UNDELIV" => Undeliv / Failed,
                    _ => Expired / Failed
                };
            }
        }
    }

    private sealed record ReasonSegmentRow(string Reason, string Key, double Rate, double Sent);

    private sealed record RankedObservation(Observation Observation, double Sent, double Rank);

    private sealed record ReportModel(
        double Sent,
        double Attempted,
        double Delivered,
        double Failed,
        double Cancelled,
        double Expired,
        double Undeliv,
        double Rejectd,
        double Spam,
        double DeliveryRate,
        double FailureRate,
        int SegmentCount,
        List<SegmentRow> WorstSegments,
        List<SegmentRow> BestSegments,
        SegmentRow? TopFailedSegment,
        List<ReasonSegmentRow> WorstReasonSegments,
        string DominantReason,
        List<RankedObservation> RankedAnomalies,
        SummaryRow? BestHour,
        SummaryRow? WorstHour,
        List<SummaryRow> DowRows,
        string? PrimaryRecommendation,
        string? SecondaryRecommendation);
}
