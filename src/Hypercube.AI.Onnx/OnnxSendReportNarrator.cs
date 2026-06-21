using Hypercube.AI;
using Hypercube.Industry;

namespace Hypercube.AI.Onnx;

public sealed class OnnxSendReportNarrator : ISendReportNarrator
{
    private readonly OnnxTextGenerator _generator;
    private readonly PromptTemplateCompiler _compiler = new();
    private readonly Func<SendReportAnalysis, string> _fallback;

    public OnnxSendReportNarrator(OnnxTextGenerator generator, Func<SendReportAnalysis, string> fallback)
    {
        _generator = generator;
        _fallback = fallback;
    }

    public string Generate(SendReportAnalysis analysis)
    {
        var sb = new System.Text.StringBuilder();
        GenerateStreaming(analysis, token => sb.Append(token));
        return sb.ToString();
    }

    public void GenerateStreaming(SendReportAnalysis analysis, Action<string> onToken)
    {
        try
        {
            var grounded = _compiler.CompileReport(analysis);
            var sb = new System.Text.StringBuilder();
            _generator.GenerateStreaming(
                GroundingPrompts.ReportNarrator,
                grounded,
                token =>
                {
                    sb.Append(token);
                    onToken(token);
                });

            if (!NarrativeNumericGuard.IsGrounded(sb.ToString(), PromptTemplateCompiler.AllowedNumbers(analysis)))
            {
                StreamFallback(analysis, onToken);
            }
        }
        catch
        {
            StreamFallback(analysis, onToken);
        }
    }

    private void StreamFallback(SendReportAnalysis analysis, Action<string> onToken)
    {
        var fallback = _fallback(analysis);
        foreach (var chunk in ChunkText(fallback, 80))
        {
            onToken(chunk);
        }
    }

    private static IEnumerable<string> ChunkText(string text, int chunkSize)
    {
        for (var i = 0; i < text.Length; i += chunkSize)
        {
            yield return text.Substring(i, Math.Min(chunkSize, text.Length - i));
        }
    }
}
