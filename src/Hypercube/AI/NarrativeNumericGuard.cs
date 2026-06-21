using System.Globalization;
using System.Text.RegularExpressions;

namespace Hypercube.AI;

public static class NarrativeNumericGuard
{
    private static readonly Regex NumberPattern = new(@"\d+(?:\.\d+)?", RegexOptions.Compiled);

    /// <summary>True when every number in <paramref name="narrative"/> matches some allowed value within tolerance.</summary>
    public static bool IsGrounded(string narrative, IReadOnlyList<double> allowed, double relTolerance = 0.01)
    {
        foreach (Match m in NumberPattern.Matches(narrative))
        {
            if (!double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n))
            {
                continue;
            }

            var ok = allowed.Any(a => IsClose(n, a, relTolerance) || IsClose(n, a * 100d, relTolerance));
            if (!ok)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsClose(double actual, double expected, double relTolerance) =>
        Math.Abs(actual - expected) <= relTolerance * Math.Max(1.0, Math.Abs(expected));
}
