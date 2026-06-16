namespace Hypercube.Tui.Demo;

/// <summary>Generates a realistic bursty stream for the TUI demo.</summary>
public sealed class DemoEventGenerator
{
    private static readonly string[] Channels = ["sms", "email", "push", "webhook"];
    private static readonly string[] Regions = ["east", "west", "eu", "apac"];
    private readonly Random _random = new(42);
    private long _sequence;

    public DemoEvent Next()
    {
        _sequence++;
        var channel = Channels[_random.Next(Channels.Length)];
        var region = Regions[_random.Next(Regions.Length)];

        // Inject periodic latency spikes on sms/east.
        var spike = channel == "sms" && region == "east" && _sequence % 17 == 0;
        var latency = spike
            ? 120 + (_random.NextDouble() * 180)
            : 8 + (_random.NextDouble() * 40);

        return new DemoEvent(
            DateTimeOffset.UtcNow,
            channel,
            region,
            $"user-{_random.Next(1, 250)}",
            latency,
            Acknowledged: _random.NextDouble() > 0.35);
    }
}
