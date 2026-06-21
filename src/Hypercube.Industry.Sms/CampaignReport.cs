namespace Hypercube.Industry.Sms;

/// <summary>
/// Deterministic SMS campaign report metadata and analysis output.
/// </summary>
public sealed record CampaignReport(
    ISubject Subject,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    SummarySnapshot Snapshot,
    SendReportAnalysis Analysis,
    string? Grade = null)
{
    /// <summary>
    /// Exposes the fully classified observations that power the narrative.
    /// </summary>
    public IReadOnlyList<Observation> Observations => Analysis.Observations;
}
