namespace Hypercube.Core;

/// <summary>Abstraction over UTC time for testability and back-testing.</summary>
public interface IClock
{
    /// <summary>Current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}

/// <summary>Production clock backed by <see cref="DateTimeOffset.UtcNow"/>.</summary>
public sealed class SystemClock : IClock
{
    /// <summary>Shared singleton instance.</summary>
    public static SystemClock Instance { get; } = new();

    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
