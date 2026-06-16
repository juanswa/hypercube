namespace Hypercube.Core;

/// <summary>Abstraction over UTC time for testability and back-testing.</summary>
public interface IClock
{
    /// <summary>Current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
