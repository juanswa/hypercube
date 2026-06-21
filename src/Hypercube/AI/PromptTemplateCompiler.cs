using System.Globalization;
using System.Text;
using Hypercube.Industry;

namespace Hypercube.AI;

/// <summary>
/// Deterministic prompt serialization for small local LLM backends.
/// </summary>
public sealed class PromptTemplateCompiler
{
    private const string DefaultSystemPrompt =
        "You are Hypercube, a concise analytics assistant. Summarize anomalies and drivers using only supplied metrics.";

    private readonly string _systemPrompt;

    /// <summary>
    /// Creates a compiler with an optional system prompt override.
    /// </summary>
    public PromptTemplateCompiler(string? systemPrompt = null) =>
        _systemPrompt = systemPrompt ?? DefaultSystemPrompt;

    /// <summary>
    /// Builds a hyper-compressed prompt payload from a snapshot and optional analysis.
    /// </summary>
    public string Compile(SummarySnapshot snapshot, AiAnalysisResult? analysis = null) =>
        RenderSnapshot(snapshot, analysis);

    /// <summary>
    /// Builds a grounded fact list for a fully classified send-report analysis.
    /// </summary>
    public string CompileReport(SendReportAnalysis analysis)
    {
        var sb = new StringBuilder();
        sb.Append("[subject] ").Append(analysis.Subject.Id)
          .Append(" carrier=").Append(analysis.Subject.Carrier)
          .Append(" tier=").Append(analysis.Subject.Tier).AppendLine();
        sb.AppendLine("[facts] use ONLY these:");
        foreach (var o in analysis.Observations.Where(o => o.IsMaterial))
        {
            var baseline = o.SelfExpected ?? o.CohortMedian ?? 0d;
            sb.Append("- ").Append(o.Dimension).Append(':').Append(o.CellKey)
              .Append(" metric=").Append(o.Metric)
              .Append(" actual=").Append(o.Actual.ToString("0.####", CultureInfo.InvariantCulture))
              .Append(" baseline=").Append(baseline.ToString("0.####", CultureInfo.InvariantCulture))
              .Append(" deviation=").Append(o.Deviation.ToString("0.####", CultureInfo.InvariantCulture))
              .Append(" kind=").Append(o.Kind)
              .Append(" favorable=").Append(o.IsFavorable?.ToString() ?? "n/a")
              .AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>Every numeric value the narrator is permitted to mention, in raw and percentage forms.</summary>
    public static IReadOnlyList<double> AllowedNumbers(SendReportAnalysis analysis)
    {
        var allowed = new List<double>();
        foreach (var o in analysis.Observations)
        {
            void Add(double v)
            {
                allowed.Add(v);
                allowed.Add(v * 100);
            }

            Add(o.Actual);
            Add(o.SelfExpected ?? o.CohortMedian ?? 0d);
            Add(Math.Abs(o.Deviation));
        }

        return allowed;
    }

    public static IReadOnlyList<double> AllowedNumbers(SummarySnapshot snapshot)
    {
        var allowed = new List<double>();
        foreach (var row in snapshot.Rows)
        {
            foreach (var value in row.Metrics.Values)
            {
                allowed.Add(value);
                allowed.Add(value * 100);
            }
        }

        return allowed;
    }

    private string RenderSnapshot(SummarySnapshot snapshot, AiAnalysisResult? analysis = null)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[system]{_systemPrompt}");
        builder.AppendLine($"[snapshot] generated={snapshot.GeneratedAt:O}; primary={snapshot.PrimaryMetric}; cells={snapshot.Rows.Count}");

        foreach (var row in snapshot.Rows.OrderByDescending(snapshot.PrimaryValue).Take(12))
        {
            builder.Append("cell ");
            builder.Append(row.Dimension);
            builder.Append(':');
            builder.Append(row.Key);
            builder.Append(" primary=");
            builder.Append(snapshot.PrimaryValue(row).ToString("0.##"));
            builder.Append(" metrics=");
            builder.AppendJoin(',', row.Metrics.Select(static kv => $"{kv.Key}={kv.Value:0.##}"));
            builder.AppendLine();
        }

        if (analysis is not null)
        {
            builder.AppendLine("[insights]");
            foreach (var insight in analysis.RecommendedInsights.Take(5))
            {
                builder.Append('-');
                builder.Append(' ');
                builder.AppendLine(insight);
            }
        }

        return builder.ToString().TrimEnd();
    }
}
