namespace Hypercube.Industry.Sms;

/// <summary>
/// SMS-specific narrative templates.
/// </summary>
public sealed class SmsNarrativeTemplates : INarrativeTemplates
{
    public string Render(Observation o)
    {
        var metricLabel = o.Metric switch
        {
            "delivery_rate" => "Delivery rate",
            "failure_rate" => "Failure rate",
            _ => o.Metric
        };

        var direction = o.IsFavorable is true ? "improved" : o.IsFavorable is false ? "declined" : "unchanged";
        var baseline = o.SelfExpected ?? o.CohortMedian ?? 0;
        
        return $"{metricLabel} for {o.CellKey} {direction} to {o.Actual:P1} (expected ~{baseline:P1}, deviation {o.Deviation:P1}). [{o.Kind}]";
    }

    public string Summary(SendReportAnalysis analysis)
    {
        var materialCount = analysis.Observations.Count(o => o.IsMaterial);
        var anomalies = analysis.Observations.Where(o => o.Kind == ObservationKind.SelfAnomaly).ToList();
        var peerIssues = analysis.Observations.Where(o => o.Kind is ObservationKind.BelowPeers or ObservationKind.AbovePeers).ToList();
        
        return $"SMS send report for {analysis.Subject.Id}: {analysis.Observations.Count} observations, {materialCount} material. " +
               $"{anomalies.Count} self-anomalies, {peerIssues.Count} peer deviations detected.";
    }
}