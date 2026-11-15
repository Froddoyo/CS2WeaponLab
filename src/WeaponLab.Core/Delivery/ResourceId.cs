using System.Buffers.Binary;
using System.Text;

namespace WeaponLab.Core.Delivery;

/// <summary>
/// Source 2 resource IDs as stored in RERL blocks: MurmurHash64B of the
/// lowercase UTF-8 path with seed 0xEDABCDEF. Ported from CS2AL; matches all
/// 1,044 references in the shipped viewmodel graphs.
/// </summary>
public static class ResourceId
{
    private const uint Multiplier = 0x5bd1e995;
    private const uint Seed = 0xEDABCDEF;

    public static ulong Of(string resourcePath)
    {
        var bytes = Encoding.UTF8.GetBytes(resourcePath.ToLowerInvariant());
        var h1 = Seed ^ (uint)bytes.Length;
        uint h2 = 0;
        var index = 0;
        var remaining = bytes.Length;

        unchecked
        {
            while (remaining >= 8)
            {
                h1 = (h1 * Multiplier) ^ Mix(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index)));
                h2 = (h2 * Multiplier) ^ Mix(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index + 4)));
                index += 8;
                remaining -= 8;
            }

            if (remaining >= 4)
            {
                h1 = (h1 * Multiplier) ^ Mix(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index)));
                index += 4;
                remaining -= 4;
            }

            if (remaining >= 3)
            {
                h2 ^= (uint)bytes[index + 2] << 16;
            }

            if (remaining >= 2)
            {
                h2 ^= (uint)bytes[index + 1] << 8;
            }

            if (remaining >= 1)
            {
                h2 ^= bytes[index];
                h2 *= Multiplier;
            }

            h1 ^= h2 >> 18;
            h1 *= Multiplier;
            h2 ^= h1 >> 22;
            h2 *= Multiplier;
            h1 ^= h2 >> 17;
            h1 *= Multiplier;
            h2 ^= h1 >> 19;
            h2 *= Multiplier;
            return ((ulong)h1 << 32) | h2;
        }

        static uint Mix(uint value)
        {
            unchecked
            {
                value *= Multiplier;
                value ^= value >> 24;
                return value * Multiplier;
            }
        }
    }
}
