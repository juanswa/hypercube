using System.Text.Json;

namespace Hypercube.Core;

/// <summary>
/// Normalization helpers used during stream ingestion.
/// </summary>
public static class Sanitizers
{
    private const string Fallback = "unknown";

    /// <summary>
    /// Normalizes a dimension or key value for consistent grouping.
    /// Trims whitespace, lowercases, and maps null/blank values to <c>unknown</c>.
    /// </summary>
    /// <param name="value">Raw input value.</param>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Fallback;
        }

        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Extracts <c>groupName</c> from a JSON payload and normalizes it.
    /// Returns <c>unknown</c> for invalid JSON or missing properties.
    /// </summary>
    /// <param name="jsonPayload">JSON string to parse.</param>
    public static string ExtractGroupName(string? jsonPayload)
    {
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return Fallback;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonPayload);
            if (doc.RootElement.TryGetProperty("groupName", out var groupName))
            {
                return Normalize(groupName.GetString());
            }
        }
        catch (JsonException)
        {
            // Invalid payloads should not interrupt stream ingestion.
        }

        return Fallback;
    }

    /// <summary>
    /// Truncates a payload string to a maximum length for safe storage.
    /// </summary>
    /// <param name="payload">Raw payload text.</param>
    /// <param name="maxLength">Maximum allowed length. Default is 512.</param>
    public static string TrimPayload(string? payload, int maxLength = 512)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return string.Empty;
        }

        return payload.Length <= maxLength ? payload : payload[..maxLength];
    }
}
