namespace Hypercube.Industry;

/// <summary>
/// Identifies a sending account for benchmarking and seasonality lookups.
/// </summary>
public interface ISubject
{
    /// <summary>Sender identifier (account / sub-account).</summary>
    string Id { get; }

    /// <summary>Network carrier, e.g. "Vodacom". Used for peer cohort grouping.</summary>
    string Carrier { get; }

    /// <summary>Account tier, e.g. "Enterprise" or "SMB".</summary>
    string Tier { get; }

    /// <summary>Industry vertical, e.g. "retail", "banking", "logistics".</summary>
    string Vertical { get; }

    /// <summary>Country code, e.g. "ZA".</summary>
    string Country { get; }

    /// <summary>Derived volume bucket, e.g. "under 10k", "10k-100k".</summary>
    string VolumeBand { get; }
}