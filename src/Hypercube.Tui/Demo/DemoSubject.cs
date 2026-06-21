namespace Hypercube.Tui.Demo;

using global::Hypercube.Industry;

internal sealed record DemoSubject(
    string Id,
    string Carrier,
    string Tier,
    string Vertical,
    string Country,
    string VolumeBand) : ISubject;
