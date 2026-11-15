using System.Globalization;
using System.Security.Cryptography;
using ValveKeyValue;

namespace WeaponLab.Core.Clips.Encoding;

/// <summary>
/// Hashes a KV tree including value types and flags, so a value written
/// back with a different type counts as a change.
/// </summary>
public static class KVFingerprint
{
    public static string Of(KVObject value)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            Walk(writer, value);
        }

        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    /// <summary>Fingerprint of a collection without the named children.</summary>
    public static string OfExcept(KVObject collection, params string[] excludedKeys)
    {
        var kept = collection.Children.Where(child => !excludedKeys.Contains(child.Key));
        return Of(KVObject.Collection(kept));
    }

    private static void Walk(BinaryWriter writer, KVObject value)
    {
        writer.Write((int)value.ValueType);
        writer.Write((int)value.Flag);
        if (value.IsArray)
        {
            writer.Write(value.Count);
            foreach (var item in value.Values)
            {
                Walk(writer, item);
            }
        }
        else if (value.IsCollection)
        {
            writer.Write(value.Count);
            foreach (var (key, child) in value.Children)
            {
                writer.Write(key);
                Walk(writer, child);
            }
        }
        else if (value.ValueType == KVValueType.BinaryBlob)
        {
            var bytes = value.AsBlob();
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }
        else
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
