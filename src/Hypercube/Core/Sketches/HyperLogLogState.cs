using System.Numerics;
using System.Text;

namespace Hypercube.Core.Sketches;

/// <summary>
/// HyperLogLog sketch for approximate distinct counting.
/// </summary>
public sealed class HyperLogLogState
{
    private const int Precision = 14;
    private const int RegisterCount = 1 << Precision;
    private const double Alpha = (0.7213 / (1.0 + (1.079 / RegisterCount)));

    private readonly byte[] _registers = new byte[RegisterCount];

    public void Add(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var hash = Hash64(value);
        var index = (int)(hash >> (64 - Precision));
        var rank = ComputeRank(hash);
        if (_registers[index] < rank)
        {
            _registers[index] = (byte)Math.Min(rank, byte.MaxValue);
        }
    }

    public long Estimate()
    {
        double sum = 0;
        var zeroRegisters = 0;
        for (var i = 0; i < RegisterCount; i++)
        {
            if (_registers[i] == 0)
            {
                zeroRegisters++;
            }

            sum += Math.Pow(2, -_registers[i]);
        }

        var estimate = Alpha * RegisterCount * RegisterCount / sum;
        if (estimate <= 2.5 * RegisterCount && zeroRegisters > 0)
        {
            estimate = RegisterCount * Math.Log(RegisterCount / (double)zeroRegisters);
        }

        return (long)Math.Round(estimate);
    }

    public byte[] Serialize() => (byte[])_registers.Clone();

    public static HyperLogLogState Deserialize(byte[] data)
    {
        var sketch = new HyperLogLogState();
        if (data.Length == RegisterCount)
        {
            data.CopyTo(sketch._registers, 0);
        }

        return sketch;
    }

    private static int ComputeRank(ulong hash)
    {
        var shifted = hash << Precision;
        var rank = BitOperations.LeadingZeroCount(shifted | 1UL << (Precision - 1)) + 1;
        return Math.Min(rank, 63);
    }

    private static ulong Hash64(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return XxHash64(bytes);
    }

    private static ulong XxHash64(byte[] bytes)
    {
        const ulong Prime1 = 11400714785074694791UL;
        const ulong Prime2 = 14029467366897019727UL;
        const ulong Prime3 = 1609587929392839161UL;
        const ulong Prime4 = 9650029242287828579UL;
        const ulong Prime5 = 2870177450012600261UL;

        var length = bytes.Length;
        var index = 0;
        ulong hash;

        if (length >= 32)
        {
            var v1 = unchecked(Prime1 + Prime2);
            var v2 = Prime2;
            var v3 = 0UL;
            var v4 = ~Prime1 + 1;

            while (index <= length - 32)
            {
                v1 = Round(v1, BitConverter.ToUInt64(bytes, index));
                v2 = Round(v2, BitConverter.ToUInt64(bytes, index + 8));
                v3 = Round(v3, BitConverter.ToUInt64(bytes, index + 16));
                v4 = Round(v4, BitConverter.ToUInt64(bytes, index + 24));
                index += 32;
            }

            hash = BitOperations.RotateLeft(v1, 1)
                 + BitOperations.RotateLeft(v2, 7)
                 + BitOperations.RotateLeft(v3, 12)
                 + BitOperations.RotateLeft(v4, 18);
            hash = MergeRound(hash, v1);
            hash = MergeRound(hash, v2);
            hash = MergeRound(hash, v3);
            hash = MergeRound(hash, v4);
        }
        else
        {
            hash = Prime5;
        }

        hash += (ulong)length;

        while (index + 8 <= length)
        {
            hash ^= Round(0, BitConverter.ToUInt64(bytes, index));
            hash = BitOperations.RotateLeft(hash, 27) * Prime1 + Prime4;
            index += 8;
        }

        if (index + 4 <= length)
        {
            hash ^= (ulong)BitConverter.ToUInt32(bytes, index) * Prime1;
            hash = BitOperations.RotateLeft(hash, 23) * Prime2 + Prime3;
            index += 4;
        }

        while (index < length)
        {
            hash ^= bytes[index] * Prime5;
            hash = BitOperations.RotateLeft(hash, 11) * Prime1;
            index++;
        }

        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;

        return hash;
    }

    private static ulong Round(ulong acc, ulong input)
    {
        const ulong Prime1 = 11400714785074694791UL;
        const ulong Prime2 = 14029467366897019727UL;

        acc += input * Prime2;
        acc = BitOperations.RotateLeft(acc, 31);
        acc *= Prime1;
        return acc;
    }

    private static ulong MergeRound(ulong acc, ulong val)
    {
        const ulong Prime1 = 11400714785074694791UL;
        const ulong Prime4 = 9650029242287828579UL;

        val = Round(0, val);
        acc ^= val;
        acc = acc * Prime1 + Prime4;
        return acc;
    }
}
