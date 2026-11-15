using System.Buffers.Binary;
using ValveResourceFormat.ResourceTypes;

namespace WeaponLab.Core.Clips.Encoding;

/// <summary>
/// Replaces the DATA block of a compiled resource while keeping the header,
/// RERL, RED2 and padding byte-for-byte.
/// </summary>
/// <remarks>
/// Resource layout: u32 file size, u16 header version, u16 version,
/// u32 block-table offset (relative to byte 8), u32 block count, then
/// 12-byte entries of FourCC, u32 offset (relative to the field) and u32 size.
/// Only resources whose DATA is the final block are supported.
/// </remarks>
public static class ResourceSplicer
{
    private const int BlockTableOffsetField = 8;
    private const int BlockCountField = 12;
    private const int BlockEntrySize = 12;

    public static byte[] ReplaceData(byte[] original, BinaryKV3 data)
    {
        using var stream = new MemoryStream();
        data.Serialize(stream);
        return ReplaceData(original, stream.ToArray());
    }

    public static byte[] ReplaceData(byte[] original, byte[] newBlock)
    {
        var (entry, start, size) = FindDataBlock(original);
        if (start + size != original.Length)
        {
            throw new InvalidDataException("DATA must be the final block with no trailing data.");
        }

        var result = new byte[start + newBlock.Length];
        original.AsSpan(0, start).CopyTo(result);
        newBlock.CopyTo(result, start);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(0), checked((uint)result.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(entry + 8), checked((uint)newBlock.Length));
        return result;
    }

    /// <summary>
    /// Replaces any named blocks (e.g. DATA and RERL), copying the others
    /// verbatim and keeping header and block order. Blocks start on 16-byte
    /// boundaries, as in Valve's files.
    /// </summary>
    public static byte[] ReplaceBlocks(byte[] original, IReadOnlyDictionary<string, byte[]> replacements)
    {
        var table = checked(BlockTableOffsetField + (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(BlockTableOffsetField)));
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(BlockCountField)));

        using var stream = new MemoryStream();
        stream.Write(original.AsSpan(0, table + count * BlockEntrySize));
        var entries = new List<(int Entry, int Start, int Size)>();
        var lastEnd = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = table + index * BlockEntrySize;
            var name = System.Text.Encoding.ASCII.GetString(original, entry, 4);
            var start = checked(entry + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entry + 4)));
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(original.AsSpan(entry + 8)));
            lastEnd = Math.Max(lastEnd, start + size);

            var block = replacements.TryGetValue(name, out var replacement) ? replacement : original.AsSpan(start, size).ToArray();
            while (stream.Position % 16 != 0)
            {
                stream.WriteByte(0);
            }

            entries.Add((entry, (int)stream.Position, block.Length));
            stream.Write(block);
        }

        if (lastEnd != original.Length)
        {
            throw new InvalidDataException("The resource has unexpected trailing data.");
        }

        var result = stream.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result, checked((uint)result.Length));
        foreach (var (entry, start, size) in entries)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(entry + 4), checked((uint)(start - entry - 4)));
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(entry + 8), checked((uint)size));
        }

        return result;
    }

    /// <returns>The block-table entry position, the block start and its size.</returns>
    internal static (int Entry, int Start, int Size) FindDataBlock(byte[] bytes)
    {
        var table = checked(BlockTableOffsetField + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(BlockTableOffsetField)));
        var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(BlockCountField)));

        var found = (Entry: -1, Start: 0, Size: 0);
        for (var index = 0; index < count; index++)
        {
            var entry = table + index * BlockEntrySize;
            if (!bytes.AsSpan(entry, 4).SequenceEqual("DATA"u8))
            {
                continue;
            }

            if (found.Entry != -1)
            {
                throw new InvalidDataException("Multiple DATA blocks are not supported.");
            }

            var start = checked(entry + 4 + (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 4)));
            var size = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8)));
            found = (entry, start, size);
        }

        if (found.Entry == -1)
        {
            throw new InvalidDataException("The resource has no DATA block.");
        }

        return found;
    }
}
