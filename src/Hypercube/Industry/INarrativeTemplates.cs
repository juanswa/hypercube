namespace Hypercube.Industry;

/// <summary>
/// Deterministic per-industry phrasing templates.
/// The narrator selects and renders; it never computes or decides severity.
/// </summary>
public interface INarrativeTemplates
{
    /// <summary>
    /// Renders a single observation into a human-readable sentence.
    /// </summary>
    string Render(Observation o);

    /// <summary>
    /// Renders the top-line summary sentence for the entire report.
    /// </summary>
    string Summary(SendReportAnalysis analysis);
}