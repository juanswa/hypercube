using System.Numerics;
using System.Security.Cryptography;
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
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return BitConverter.ToUInt64(bytes, 0);
    }
}
