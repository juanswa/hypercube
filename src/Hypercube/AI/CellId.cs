namespace Hypercube.AI;

/// <summary>
/// Helpers for addressing rollup cells consistently across analysis components.
/// </summary>
public static class CellId
{
    /// <summary>
    /// Builds a canonical cell identifier from a summary row.
    /// </summary>
    /// <param name="row">Row containing dimension and key.</param>
    /// <returns>Identifier in <c>dimension:key</c> form.</returns>
    public static string From(SummaryRow row) => $"{row.Dimension}:{row.Key}";

    /// <summary>Case-insensitive comparer for cell identifiers.</summary>
    public static StringComparer Comparer { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>Compares two cell identifiers using <see cref="Comparer"/>.</summary>
    public static bool Equals(string left, string right) => Comparer.Equals(left, right);
}
