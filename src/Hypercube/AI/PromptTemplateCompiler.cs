using System.Text;

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
    public string Compile(SummarySnapshot snapshot, AiAnalysisResult? analysis = null)
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
