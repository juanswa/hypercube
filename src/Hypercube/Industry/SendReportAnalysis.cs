namespace Hypercube.Industry;

/// <summary>
/// Complete analysis result for a send report, combining intrinsic insights with classified observations.
/// </summary>
/// <param name="Subject">The sending account being analyzed.</param>
/// <param name="Intrinsic">Self-referential AI analysis.</param>
/// <param name="Observations">Classified observations ready for narration.</param>
public sealed record SendReportAnalysis(
    ISubject Subject,
    AiAnalysisResult Intrinsic,
    IReadOnlyList<Observation> Observations);
