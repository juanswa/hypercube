namespace Hypercube.AI;

/// <summary>
/// Builds compact numeric profiles from snapshots for similarity comparisons.
/// </summary>
public static class SnapshotProfileVectorizer
{
    /// <summary>
    /// Vectorizes top cells by primary metric into a fixed-length profile.
    /// </summary>
    public static float[] Vectorize(SummarySnapshot snapshot, int dimensions = 32)
    {
        var vector = new float[dimensions];
        var ranked = snapshot.Rows
            .OrderByDescending(snapshot.PrimaryValue)
            .Take(dimensions)
            .Select(snapshot.PrimaryValue)
            .ToArray();

        for (var i = 0; i < ranked.Length && i < dimensions; i++)
        {
            vector[i] = (float)ranked[i];
        }

        return vector;
    }

    /// <summary>
    /// Cosine similarity between two snapshot profiles in [0, 1].
    /// </summary>
    public static double CosineSimilarity(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var length = Math.Min(left.Length, right.Length);
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var i = 0; i < length; i++)
        {
            dot += left[i] * right[i];
            leftNorm += left[i] * left[i];
            rightNorm += right[i] * right[i];
        }

        if (leftNorm <= 1e-12 || rightNorm <= 1e-12)
        {
            return 0;
        }

        return dot / Math.Sqrt(leftNorm * rightNorm);
    }
}
