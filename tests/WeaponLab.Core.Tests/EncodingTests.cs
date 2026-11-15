using System.Buffers.Binary;
using System.Numerics;
using ValveKeyValue;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Tests;

public sealed class EncodingTests
{
    [Fact]
    public void IdentityQuaternionPacksToKnownWords()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            TrackEncoder.WriteQuaternion(writer, Quaternion.Identity);
        }

        // W is dropped (index 3 = both flag bits); X, Y, Z = 0 map to the 15-bit midpoint.
        var bytes = stream.ToArray();
        ushort[] words = [.. Enumerable.Range(0, 3).Select(index => BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2)))];
        Assert.Equal([0xC000, 0xC000, 0x4000], words);
    }

    [Fact]
    public void QuaternionSignIsFlippedSoTheDroppedComponentIsPositive()
    {
        static byte[] Pack(Quaternion q)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            TrackEncoder.WriteQuaternion(writer, q);
            writer.Flush();
            return stream.ToArray();
        }

        var q = Quaternion.Normalize(new Quaternion(0.1f, 0.2f, 0.3f, 0.9f));
        Assert.Equal(Pack(q), Pack(new Quaternion(-q.X, -q.Y, -q.Z, -q.W)));
    }

    [Fact]
    public void FingerprintSeesValueTypes()
    {
        Assert.NotEqual(KVFingerprint.Of(new KVObject(33)), KVFingerprint.Of(new KVObject(33L)));
        Assert.Equal(KVFingerprint.Of(new KVObject(33)), KVFingerprint.Of(new KVObject(33)));
    }

    [Fact]
    public void FingerprintExceptIgnoresNamedKeys()
    {
        var a = KVObject.Collection();
        a.Add("m_nTrackReadOffset", 1);
        a.Add("m_bIsRotationStatic", true);
        var b = KVObject.Collection();
        b.Add("m_nTrackReadOffset", 99);
        b.Add("m_bIsRotationStatic", true);

        Assert.Equal(KVFingerprint.OfExcept(a, "m_nTrackReadOffset"), KVFingerprint.OfExcept(b, "m_nTrackReadOffset"));
        Assert.NotEqual(KVFingerprint.Of(a), KVFingerprint.Of(b));
    }

    [Theory]
    [InlineData(KVValueType.Int32)]
    [InlineData(KVValueType.UInt32)]
    [InlineData(KVValueType.FloatingPoint64)]
    public void ReplacementValuesKeepTheirType(KVValueType type)
    {
        KVObject original = type switch
        {
            KVValueType.Int32 => new KVObject(5),
            KVValueType.UInt32 => new KVObject(5u),
            _ => new KVObject(5.0),
        };

        Assert.Equal(type, KVValues.Like(original, 7).ValueType);
    }

    [Fact]
    public void SplicerReplacesFinalDataBlockAndFixesSizes()
    {
        // Header (16) + two block entries (24) + RERL payload (4) + DATA payload (6).
        var original = new byte[50];
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(0), 50);
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(8), 8);   // table at 16
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(12), 2);
        "RERL"u8.CopyTo(original.AsSpan(16));
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(20), 20);  // 20 + 20 = 40
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(24), 4);
        "DATA"u8.CopyTo(original.AsSpan(28));
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(32), 12);  // 32 + 12 = 44
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(36), 6);
        original.AsSpan(40, 4).Fill(0xAA);
        original.AsSpan(44, 6).Fill(0xBB);

        var result = ResourceSplicer.ReplaceData(original, [1, 2, 3]);

        Assert.Equal(47, result.Length);
        Assert.Equal(47u, BinaryPrimitives.ReadUInt32LittleEndian(result));
        Assert.Equal(3u, BinaryPrimitives.ReadUInt32LittleEndian(result.AsSpan(36)));
        // Everything before DATA except the two size fields is untouched.
        Assert.True(result.AsSpan(4, 32).SequenceEqual(original.AsSpan(4, 32)));
        Assert.True(result.AsSpan(40, 4).SequenceEqual(original.AsSpan(40, 4)));
        Assert.Equal([1, 2, 3], result[44..]);
    }

    [Fact]
    public void SplicerRejectsDataThatIsNotLast()
    {
        var original = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(8), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(12), 1);
        "DATA"u8.CopyTo(original.AsSpan(16));
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(20), 8);  // starts at 28
        BinaryPrimitives.WriteUInt32LittleEndian(original.AsSpan(24), 4);  // ends at 32, file is 40

        Assert.Throws<InvalidDataException>(() => ResourceSplicer.ReplaceData(original, [1]));
    }

    [Fact]
    public void RotationDistanceTreatsOppositeSignsAsEqual()
    {
        var q = Quaternion.Normalize(new Quaternion(0.3f, -0.2f, 0.1f, 0.9f));
        Assert.Equal(0, BonePose.RotationDistance(q, new Quaternion(-q.X, -q.Y, -q.Z, -q.W)), precision: 6);
        Assert.Equal(10, BonePose.RotationDegrees(Quaternion.Identity, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 10 * MathF.PI / 180)), precision: 3);
    }
}
