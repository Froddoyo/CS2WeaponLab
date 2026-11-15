using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Delivery;

/// <summary>
/// Points a compiled resource at other resources: every string value equal
/// to an old path becomes the new path (e.g. a model's
/// <c>m_vecNmSkeletonRefs</c> entry), and the RERL entries follow. Every
/// other value is checked to be unchanged after decoding.
/// </summary>
public static class ReferencePatcher
{
    /// <param name="map">Old resource path → new resource path, without <c>_c</c>.</param>
    /// <returns>The patched bytes and how many values were replaced.</returns>
    public static (byte[] Bytes, int Replaced) Patch(byte[] original, string resourcePath, IReadOnlyDictionary<string, string> map)
    {
        using var resource = new Resource { FileName = resourcePath };
        resource.Read(new MemoryStream(original));
        var (root, serialize) = Data(resource, resourcePath);
        var before = Leaves(root);

        var replaced = Replace(root, map);
        if (replaced == 0)
        {
            throw new InvalidDataException($"{resourcePath} has none of the references to replace.");
        }

        var blocks = new Dictionary<string, byte[]>();
        using (var data = new MemoryStream())
        {
            serialize(data);
            blocks["DATA"] = data.ToArray();
        }

        if (resource.ExternalReferences is { } references)
        {
            foreach (var reference in references.ResourceRefInfoList.Where(item => map.ContainsKey(item.Name)))
            {
                reference.Id = ResourceId.Of(map[reference.Name]);
                reference.Name = map[reference.Name];
            }

            using var rerl = new MemoryStream();
            references.Serialize(rerl);
            blocks["RERL"] = rerl.ToArray();
        }

        var bytes = ResourceSplicer.ReplaceBlocks(original, blocks);
        Verify(bytes, resourcePath, before, map);
        return (bytes, replaced);
    }

    private static (KVObject Root, Action<Stream> Serialize) Data(Resource resource, string resourcePath) => resource.DataBlock switch
    {
        BinaryKV3 kv3 => (kv3.Data.Root, kv3.Serialize),
        KeyValuesOrNTRO kv => (kv.Data, kv.Serialize),
        _ => throw new InvalidDataException($"{resourcePath} has no KV3 data block."),
    };

    private static int Replace(KVObject value, IReadOnlyDictionary<string, string> map)
    {
        var count = 0;
        if (value.IsArray)
        {
            var items = value.AsArraySpan();
            for (var index = 0; index < items.Length; index++)
            {
                if (Swap(items[index], map) is { } swapped)
                {
                    items[index] = swapped;
                    count++;
                }
                else
                {
                    count += Replace(items[index], map);
                }
            }
        }
        else if (value.IsCollection)
        {
            foreach (var (key, child) in value.Children.ToList())
            {
                if (Swap(child, map) is { } swapped)
                {
                    value[key] = swapped;
                    count++;
                }
                else
                {
                    count += Replace(child, map);
                }
            }
        }

        return count;
    }

    private static KVObject? Swap(KVObject value, IReadOnlyDictionary<string, string> map) =>
        value.ValueType == KVValueType.String && map.TryGetValue(value.ToString() ?? "", out var replacement)
            ? new KVObject(replacement) { Flag = value.Flag }
            : null;

    private static List<(string Path, KVValueType Type, string Value)> Leaves(KVObject root)
    {
        var leaves = new List<(string, KVValueType, string)>();
        void Walk(string path, KVObject value)
        {
            if (value.IsArray)
            {
                for (var index = 0; index < value.Count; index++)
                {
                    Walk($"{path}[{index}]", value[index]);
                }
            }
            else if (value.IsCollection)
            {
                foreach (var (key, child) in value.Children)
                {
                    Walk($"{path}.{key}", child);
                }
            }
            else
            {
                leaves.Add((path, value.ValueType, value.ValueType == KVValueType.BinaryBlob ? Convert.ToHexString(value.AsBlob()) : value.ToString() ?? ""));
            }
        }

        Walk("", root);
        return leaves;
    }

    private static void Verify(byte[] bytes, string resourcePath, List<(string Path, KVValueType Type, string Value)> before, IReadOnlyDictionary<string, string> map)
    {
        using var resource = new Resource { FileName = resourcePath };
        resource.Read(new MemoryStream(bytes));
        var after = Leaves(Data(resource, resourcePath).Root);
        if (after.Count != before.Count)
        {
            throw new InvalidDataException($"{resourcePath} changed shape while patching references.");
        }

        for (var index = 0; index < before.Count; index++)
        {
            var expected = map.TryGetValue(before[index].Value, out var replacement) && before[index].Type == KVValueType.String ? replacement : before[index].Value;
            if (after[index].Path != before[index].Path || after[index].Type != before[index].Type || after[index].Value != expected)
            {
                throw new InvalidDataException($"{resourcePath} value {before[index].Path} changed beyond the reference swap.");
            }
        }
    }
}
