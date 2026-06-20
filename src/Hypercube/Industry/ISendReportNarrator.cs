namespace Hypercube.Industry;

/// <summary>
/// Selects and phrases pre-computed observations into prose.
/// NEVER computes, compares, or assigns Kind/materiality/favorability.
/// </summary>
public interface ISendReportNarrator
{
    /// <summary>
    /// Generates a human-readable send report from a fully-classified analysis.
    /// </summary>
    string Generate(SendReportAnalysis analysis);
}