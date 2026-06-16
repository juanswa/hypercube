namespace Hypercube.Core.Sketches;

/// <summary>
/// Compact streaming quantile sketch (t-digest style) with serializable centroid state.
/// </summary>
public sealed class TDigestState
{
    private const int Compression = 100;
    private readonly List<Centroid> _centroids = [];

    public long Count { get; private set; }
    public double Sum { get; private set; }

    public double Mean => Count == 0 ? 0 : Sum / Count;

    public void Add(double value, double weight = 1)
    {
        if (weight <= 0)
        {
            return;
        }

        Count++;
        Sum += value;
        _centroids.Add(new Centroid(value, weight));
        if (_centroids.Count > Compression * 3)
        {
            Compress();
        }
    }

    public double Quantile(double q)
    {
        if (_centroids.Count == 0)
        {
            return 0;
        }

        q = Math.Clamp(q, 0, 1);
        var ordered = _centroids.OrderBy(c => c.Mean).ToList();
        var totalWeight = ordered.Sum(c => c.Weight);
        if (totalWeight <= 0)
        {
            return ordered[0].Mean;
        }

        var target = q * totalWeight;
        double cumulative = 0;
        foreach (var centroid in ordered)
        {
            cumulative += centroid.Weight;
            if (cumulative >= target)
            {
                return centroid.Mean;
            }
        }

        return ordered[^1].Mean;
    }

    public byte[] Serialize()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(Count);
        writer.Write(Sum);
        writer.Write(_centroids.Count);
        foreach (var centroid in _centroids)
        {
            writer.Write(centroid.Mean);
            writer.Write(centroid.Weight);
        }

        return stream.ToArray();
    }

    public static TDigestState Deserialize(byte[] data)
    {
        var digest = new TDigestState();
        if (data.Length == 0)
        {
            return digest;
        }

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        digest.Count = reader.ReadInt64();
        digest.Sum = reader.ReadDouble();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            digest._centroids.Add(new Centroid(reader.ReadDouble(), reader.ReadDouble()));
        }

        return digest;
    }

    private void Compress()
    {
        if (_centroids.Count <= Compression)
        {
            return;
        }

        var ordered = _centroids.OrderBy(c => c.Mean).ToList();
        var totalWeight = ordered.Sum(c => c.Weight);
        var binWeight = totalWeight / Compression;
        var compressed = new List<Centroid>(Compression);
        double binSum = 0;
        double binWeightSum = 0;

        foreach (var centroid in ordered)
        {
            binSum += centroid.Mean * centroid.Weight;
            binWeightSum += centroid.Weight;
            if (binWeightSum >= binWeight)
            {
                compressed.Add(new Centroid(binSum / binWeightSum, binWeightSum));
                binSum = 0;
                binWeightSum = 0;
            }
        }

        if (binWeightSum > 0)
        {
            compressed.Add(new Centroid(binSum / binWeightSum, binWeightSum));
        }

        _centroids.Clear();
        _centroids.AddRange(compressed);
    }

    private readonly record struct Centroid(double Mean, double Weight);
}
