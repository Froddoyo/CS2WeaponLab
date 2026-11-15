using ValveKeyValue;

namespace WeaponLab.Core.Clips.Encoding;

/// <summary>
/// Builds replacement KV values with the same type as the value they
/// replace, so patched clips keep Valve's field types.
/// </summary>
internal static class KVValues
{
    public static KVObject Like(KVObject original, double value)
    {
        return original.ValueType switch
        {
            KVValueType.FloatingPoint => new KVObject((float)value),
            KVValueType.FloatingPoint64 => new KVObject(value),
            KVValueType.Int16 => new KVObject(checked((short)value)),
            KVValueType.UInt16 => new KVObject(checked((ushort)value)),
            KVValueType.Int32 => new KVObject(checked((int)value)),
            KVValueType.UInt32 => new KVObject(checked((uint)value)),
            KVValueType.Int64 => new KVObject(checked((long)value)),
            KVValueType.UInt64 => new KVObject(checked((ulong)value)),
            _ => throw new InvalidDataException($"Cannot write a number over a {original.ValueType} value."),
        };
    }

    public static KVObject Like(KVObject original, bool value)
    {
        if (original.ValueType != KVValueType.Boolean)
        {
            throw new InvalidDataException($"Cannot write a boolean over a {original.ValueType} value.");
        }

        return new KVObject(value);
    }

    /// <summary>Replaces <c>parent[key]</c>, keeping its type.</summary>
    public static void Set(KVObject parent, string key, double value) => parent[key] = Like(parent[key], value);

    public static void Set(KVObject parent, string key, bool value) => parent[key] = Like(parent[key], value);

    /// <summary>A copy of a collection or array tree; leaf values are shared (they are replaced, never changed in place).</summary>
    public static KVObject Clone(KVObject value)
    {
        if (value.IsArray)
        {
            var array = KVObject.Array();
            foreach (var item in value.Values)
            {
                array.Add(Clone(item));
            }

            array.Flag = value.Flag;
            return array;
        }

        if (value.IsCollection)
        {
            var collection = KVObject.Collection();
            foreach (var (key, child) in value.Children)
            {
                collection[key] = Clone(child);
            }

            collection.Flag = value.Flag;
            return collection;
        }

        return value;
    }
}
