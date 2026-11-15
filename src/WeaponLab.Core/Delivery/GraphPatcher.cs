using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Clips.Encoding;
using WeaponLab.Core.Graphs;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Delivery;

/// <summary>
/// Clones a compiled graph with its resource references redirected and,
/// optionally, node values edited: the <c>m_resources</c> slots, the edited
/// node values and the matching RERL entries change, every other graph value
/// stays fingerprint-identical. Ported from CS2AL's GraphTools; node edits
/// are Weapon Lab's own.
/// </summary>
public static class GraphPatcher
{
    private const string ResourcesKey = "m_resources";
    private const string NodesKey = "m_nodes";

    /// <param name="map">Stock path → custom path, without <c>_c</c>.</param>
    /// <returns>The patched bytes and the redirects that applied to this graph.</returns>
    public static (byte[] Bytes, IReadOnlyDictionary<string, string> Changes) Patch(byte[] original, string resourcePath, IReadOnlyDictionary<string, string> map) =>
        Patch(original, resourcePath, map, null);

    /// <param name="edits">Graph edits (see <see cref="GraphEdits"/>), applied before redirecting.</param>
    /// <param name="clipAliases">
    /// CS2's clip path → the addon's copy, for clips the addon already has (the
    /// map's own clips count too), so a clip edit naming either one finds the slot
    /// that holds it. See <see cref="GraphEdits.Apply"/>.
    /// </param>
    public static (byte[] Bytes, IReadOnlyDictionary<string, string> Changes) Patch(
        byte[] original,
        string resourcePath,
        IReadOnlyDictionary<string, string> map,
        IReadOnlyDictionary<string, string>? edits,
        IReadOnlyDictionary<string, string>? clipAliases = null)
    {
        using var resource = new Resource { FileName = resourcePath };
        resource.Read(new MemoryStream(original));
        var data = resource.DataBlock as BinaryKV3 ?? throw new InvalidDataException("Not a KV3 graph: " + resourcePath);
        var references = resource.ExternalReferences ?? throw new InvalidDataException("The graph has no RERL: " + resourcePath);

        // Prove the hash before trusting it with new references.
        foreach (var reference in references.ResourceRefInfoList)
        {
            if (ResourceId.Of(reference.Name) != reference.Id)
            {
                throw new InvalidDataException($"Resource ID check failed for {reference.Name} in {resourcePath}.");
            }
        }

        var root = data.Data.Root;
        var editList = edits ?? new Dictionary<string, string>();
        var nodesBefore = root[NodesKey].Values.Select(KVFingerprint.Of).ToList();
        var leavesBefore = editList.Keys.Select(key => GraphEdits.Parse(key).Node).Distinct()
            .Where(index => index >= 0 && index < nodesBefore.Count)
            .ToDictionary(index => index, index => Leaves(root[NodesKey][index]));
        var preserved = root.Children
            .Where(child => child.Key != ResourcesKey && child.Key != NodesKey)
            .ToDictionary(child => child.Key, child => KVFingerprint.Of(child.Value));

        var aliases = new Dictionary<string, string>(clipAliases ?? new Dictionary<string, string>(), StringComparer.Ordinal);
        foreach (var (stock, custom) in map)
        {
            aliases.TryAdd(stock, custom);
        }

        var (addedClips, clipSlots) = GraphEdits.Apply(root, editList, resourcePath, aliases);
        foreach (var clip in addedClips)
        {
            references.ResourceRefInfoList.Add(new ResourceExtRefList.ResourceReferenceInfo { Name = clip, Id = ResourceId.Of(clip) });
        }

        var slots = root[ResourcesKey];
        var slotsBefore = slots.Values.Select(value => (Name: value.ToString() ?? "", value.ValueType, value.Flag)).ToList();
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        var rewritten = KVObject.Array();
        foreach (var slot in slots.Values)
        {
            var name = slot.ToString() ?? "";
            if (map.TryGetValue(name, out var destination))
            {
                changes[name] = destination;
                rewritten.Add(new KVObject(destination) { Flag = slot.Flag });
            }
            else
            {
                rewritten.Add(slot);
            }
        }

        rewritten.Flag = slots.Flag;
        root[ResourcesKey] = rewritten;

        foreach (var reference in references.ResourceRefInfoList)
        {
            if (changes.TryGetValue(reference.Name, out var destination))
            {
                reference.Name = destination;
                reference.Id = ResourceId.Of(destination);
            }
        }

        using var dataStream = new MemoryStream();
        data.Serialize(dataStream);
        using var referenceStream = new MemoryStream();
        references.Serialize(referenceStream);
        var bytes = ResourceSplicer.ReplaceBlocks(original, new Dictionary<string, byte[]>
        {
            ["DATA"] = dataStream.ToArray(),
            ["RERL"] = referenceStream.ToArray(),
        });

        var expected = new Expected(slotsBefore, preserved, changes, references.ResourceRefInfoList.Select(item => (item.Name, item.Id)).ToList(), nodesBefore, leavesBefore, editList, clipSlots);
        Verify(bytes, resourcePath, expected);
        return (bytes, changes);
    }

    private sealed record Expected(
        List<(string Name, KVValueType Type, KVFlag Flag)> Slots,
        Dictionary<string, string> Preserved,
        Dictionary<string, string> Changes,
        List<(string Name, ulong Id)> References,
        List<string> Nodes,
        Dictionary<int, Dictionary<string, (KVValueType Type, string Value)>> EditedNodeLeaves,
        IReadOnlyDictionary<string, string> Edits,
        IReadOnlyDictionary<int, int> ClipSlots);

    /// <summary>Every leaf of a node: path → (type, formatted value).</summary>
    private static Dictionary<string, (KVValueType Type, string Value)> Leaves(KVObject node)
    {
        var leaves = new Dictionary<string, (KVValueType, string)>(StringComparer.Ordinal);
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
                    Walk(path.Length == 0 ? key : $"{path}.{key}", child);
                }
            }
            else
            {
                leaves[path] = (value.ValueType, GraphDocument.Format(value));
            }
        }

        Walk("", node);
        return leaves;
    }

    private static void Verify(byte[] bytes, string resourcePath, Expected expected)
    {
        using var decoded = new Resource { FileName = resourcePath };
        decoded.Read(new MemoryStream(bytes));
        var root = ((BinaryKV3)decoded.DataBlock!).Data.Root;
        foreach (var (key, fingerprint) in expected.Preserved)
        {
            if (KVFingerprint.Of(root[key]) != fingerprint)
            {
                throw new InvalidDataException($"Graph behaviour changed ({key}) in {resourcePath}.");
            }
        }

        var resources = root[ResourcesKey];
        var nodes = root[NodesKey];
        if (nodes.Count != expected.Nodes.Count)
        {
            throw new InvalidDataException("The number of graph nodes changed in " + resourcePath);
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            if (!expected.EditedNodeLeaves.TryGetValue(index, out var before))
            {
                if (KVFingerprint.Of(nodes[index]) != expected.Nodes[index])
                {
                    throw new InvalidDataException($"Graph node {index} changed without an edit in {resourcePath}.");
                }

                continue;
            }

            VerifyEditedNode(index, before, Leaves(nodes[index]), resources, expected, resourcePath);
        }

        if (resources.Count != expected.Slots.Count)
        {
            throw new InvalidDataException("The number of graph resource slots changed in " + resourcePath);
        }

        for (var index = 0; index < expected.Slots.Count; index++)
        {
            var actual = resources[index];
            var slot = expected.Slots[index];
            var name = expected.Changes.GetValueOrDefault(slot.Name, slot.Name);
            if (actual.ToString() != name || actual.Flag != slot.Flag || actual.ValueType != slot.Type)
            {
                throw new InvalidDataException($"Graph resource slot {index} is wrong in {resourcePath}.");
            }
        }

        var decodedReferences = decoded.ExternalReferences!.ResourceRefInfoList;
        foreach (var (name, id) in expected.References)
        {
            if (!decodedReferences.Any(reference => reference.Name == name && reference.Id == id))
            {
                throw new InvalidDataException($"Graph reference {name} did not survive in {resourcePath}.");
            }
        }
    }

    /// <summary>An edited node holds each edit with its original type; every other value is unchanged.</summary>
    private static void VerifyEditedNode(
        int index,
        Dictionary<string, (KVValueType Type, string Value)> before,
        Dictionary<string, (KVValueType Type, string Value)> after,
        KVObject resources,
        Expected expected,
        string resourcePath)
    {
        var wanted = new Dictionary<string, (KVValueType Type, string Value)>(before, StringComparer.Ordinal);
        foreach (var (key, value) in expected.Edits)
        {
            var (node, path) = GraphEdits.Parse(key);
            if (node != index)
            {
                continue;
            }

            // A clip edit plays the slot the edit chose: the clip's own, or the one holding it under its alias.
            var leaf = path == GraphDocument.ClipEditKey ? "m_nDataSlotIdx" : path;
            var target = path == GraphDocument.ClipEditKey
                ? expected.ClipSlots[index].ToString(CultureInfo.InvariantCulture)
                : value;
            // An element added past the end of a list takes the type of the list's first element.
            var type = before.TryGetValue(leaf, out var original) ? original.Type : before.GetValueOrDefault(FirstElement(leaf)).Type;
            if (!after.TryGetValue(leaf, out var actual) || actual.Type != type || !VDataValues.Same(actual.Value, target))
            {
                throw new InvalidDataException($"Graph node {index} value {path} did not take the edit ({value}) in {resourcePath}.");
            }

            // CS2's clip and the addon's copy of it share the file name.
            if (path == GraphDocument.ClipEditKey && Path.GetFileName(SlotName(resources, expected.ClipSlots[index])) != Path.GetFileName(value))
            {
                throw new InvalidDataException($"Graph node {index} plays {SlotName(resources, expected.ClipSlots[index])} instead of {value} in {resourcePath}.");
            }

            wanted[leaf] = actual;
        }

        if (after.Count != wanted.Count || wanted.Any(item => !after.TryGetValue(item.Key, out var actual) || actual != item.Value))
        {
            throw new InvalidDataException($"Graph node {index} changed beyond its edits in {resourcePath}.");
        }
    }

    /// <summary><c>m_comparisionIDs[3]</c> → <c>m_comparisionIDs[0]</c>; any other path stays as it is.</summary>
    private static string FirstElement(string path)
    {
        var open = path.LastIndexOf('[');
        return open > 0 && path.EndsWith(']') ? path[..open] + "[0]" : path;
    }

    private static string SlotName(KVObject resources, int slot) =>
        slot >= 0 && slot < resources.Count ? resources[slot].ToString() ?? "" : "";
}
