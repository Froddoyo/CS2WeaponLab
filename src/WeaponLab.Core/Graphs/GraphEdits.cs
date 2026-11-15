using System.Globalization;
using System.Text.RegularExpressions;
using ValveKeyValue;
using WeaponLab.Core.Clips.Encoding;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Graphs;

/// <summary>
/// Edits to one graph, keyed <c>&lt;node index&gt;:&lt;value path&gt;</c>, e.g.
/// <c>116:m_flDuration</c> = <c>0.2</c>. The key <c>&lt;node&gt;:clip</c> makes a clip
/// or pose node play another clip (a resource path without <c>_c</c>); a clip
/// the graph does not list yet is added to its resources.
/// </summary>
public static partial class GraphEdits
{
    [GeneratedRegex(@"^(?<key>[^.\[\]]+)|\.(?<key>[^.\[\]]+)|\[(?<index>\d+)\]")]
    private static partial Regex PathSegment();

    public static string Key(int node, string path) => node.ToString(CultureInfo.InvariantCulture) + ":" + path;

    public static (int Node, string Path) Parse(string key)
    {
        var colon = key.IndexOf(':');
        return colon > 0 && int.TryParse(key[..colon], NumberStyles.None, CultureInfo.InvariantCulture, out var node)
            ? (node, key[(colon + 1)..])
            : throw new InvalidDataException($"Bad graph edit key '{key}'.");
    }

    /// <summary>Checks an edit against the graph and returns it in canonical form, or an error.</summary>
    public static (string? Value, string? Error) Normalize(GraphDocument graph, int node, string path, string input)
    {
        if (node < 0 || node >= graph.Nodes.Count)
        {
            return (null, $"The graph has no node {node}.");
        }

        var target = graph[node];
        var text = input.Trim();
        if (path == GraphDocument.ClipEditKey)
        {
            if (!target.PlaysData)
            {
                return (null, "Only clip and pose nodes play a clip.");
            }

            var clip = text.EndsWith("_c", StringComparison.Ordinal) ? text[..^2] : text;
            return clip.EndsWith(".vnmclip", StringComparison.Ordinal) ? (clip, null) : (null, "Pick a .vnmclip.");
        }

        var value = target.Values.FirstOrDefault(item => item.Path == path);
        if (value is null)
        {
            return (null, $"Node {node} has no value {path}.");
        }

        if (!value.Editable)
        {
            return (null, "This value wires nodes together and cannot be edited.");
        }

        if (GraphGlossary.ChoicesFor(target.Class, path) is { } choices && !choices.Contains(text))
        {
            return (null, "Use one of: " + string.Join(", ", choices) + ".");
        }

        if (GraphDocument.LeafKey(path) == "m_nDefaultStateIndex")
        {
            var states = graph.Transitions.ContainsKey(node) ? CountStates(graph, node) : 0;
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position) || position < 0 || position >= states)
            {
                return (null, $"Use a state position from 0 to {states - 1}.");
            }
        }

        return VDataView.Normalize(value.Type, text);
    }

    private static int CountStates(GraphDocument graph, int machine) =>
        graph[machine].Values.Count(value => value.Path.EndsWith(".m_nStateNodeIdx", StringComparison.Ordinal) && !value.Path.Contains("m_transitionDefinitions", StringComparison.Ordinal));

    /// <summary>
    /// Applies edits to a decoded graph root. Returns clips that had to be
    /// added to <c>m_resources</c> (the caller adds their RERL entries) and
    /// the resource slot each clip edit's node now plays.
    /// </summary>
    /// <param name="clipAliases">
    /// CS2's clip path → the addon's copy of it (<c>animation/anims/custom/&lt;weapon&gt;/</c>).
    /// A clip edit naming either one plays the slot that already holds the clip
    /// under the other name, instead of adding the clip a second time.
    /// </param>
    public static (IReadOnlyList<string> Added, IReadOnlyDictionary<int, int> ClipSlots) Apply(
        KVObject root,
        IReadOnlyDictionary<string, string> edits,
        string resourcePath,
        IReadOnlyDictionary<string, string>? clipAliases = null)
    {
        var nodes = root["m_nodes"];
        var resources = root["m_resources"];
        var added = new List<string>();
        var clipSlots = new Dictionary<int, int>();
        foreach (var (key, value) in edits.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var (index, path) = Parse(key);
            if (index < 0 || index >= nodes.Count)
            {
                throw new InvalidDataException($"{resourcePath}: no node {index} for edit {key}. The game's graph may have changed.");
            }

            var node = nodes[index];
            if (path == GraphDocument.ClipEditKey)
            {
                var slot = SlotHolding(resources, value, clipAliases);
                if (slot < 0)
                {
                    var template = resources.Count > 0 ? resources[0] : null;
                    resources.Add(new KVObject(value) { Flag = template?.Flag ?? KVFlag.Resource });
                    slot = resources.Count - 1;
                    added.Add(value);
                }

                node["m_nDataSlotIdx"] = KVValues.Like(node["m_nDataSlotIdx"], slot);
                clipSlots[index] = slot;
                continue;
            }

            SetLeaf(node, path, value, $"{resourcePath} node {index}");
        }

        return (added, clipSlots);
    }

    /// <summary>The slot holding a clip: under its own path, else under its alias (the addon's copy, or CS2's original); -1 if none.</summary>
    private static int SlotHolding(KVObject resources, string clip, IReadOnlyDictionary<string, string>? clipAliases)
    {
        var slot = IndexOf(resources, clip);
        if (slot >= 0 || clipAliases is null)
        {
            return slot;
        }

        if (clipAliases.TryGetValue(clip, out var copy) && IndexOf(resources, copy) is var copySlot and >= 0)
        {
            return copySlot;
        }

        var original = clipAliases.FirstOrDefault(alias => alias.Value == clip).Key;
        return original is null ? -1 : IndexOf(resources, original);
    }

    private static int IndexOf(KVObject resources, string value)
    {
        for (var index = 0; index < resources.Count; index++)
        {
            if (resources[index].ToString() == value)
            {
                return index;
            }
        }

        return -1;
    }

    private static void SetLeaf(KVObject node, string path, string value, string label)
    {
        var segments = PathSegment().Matches(path).ToList();
        if (segments.Count == 0 || string.Concat(segments.Select(segment => segment.Value)) != path)
        {
            throw new InvalidDataException($"{label}: unsupported value path '{path}'.");
        }

        var parent = node;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var last = index == segments.Count - 1;
            if (segment.Groups["index"].Success)
            {
                var position = int.Parse(segment.Groups["index"].Value, CultureInfo.InvariantCulture);

                // One past the end of a list adds an element like the first one: another ID for an ID comparison.
                if (last && parent.IsArray && parent.Count > 0 && position == parent.Count)
                {
                    parent.Add(Typed(parent[0], value, label));
                    return;
                }

                if (!parent.IsArray || position >= parent.Count)
                {
                    throw new InvalidDataException($"{label}: {path} does not exist in the game's graph.");
                }

                if (last)
                {
                    parent.AsArraySpan()[position] = Typed(parent[position], value, label);
                    return;
                }

                parent = parent[position];
            }
            else
            {
                var key = segment.Groups["key"].Value;
                if (!parent.TryGetValue(key, out var child))
                {
                    throw new InvalidDataException($"{label}: {path} does not exist in the game's graph.");
                }

                if (last)
                {
                    parent[key] = Typed(child, value, label);
                    return;
                }

                parent = child;
            }
        }
    }

    private static KVObject Typed(KVObject original, string value, string label)
    {
        return original.ValueType switch
        {
            KVValueType.Boolean => new KVObject(value == "true") { Flag = original.Flag },
            KVValueType.String => new KVObject(value) { Flag = original.Flag },
            KVValueType.FloatingPoint or KVValueType.FloatingPoint64 or KVValueType.Int16 or KVValueType.Int32 or KVValueType.Int64
                or KVValueType.UInt16 or KVValueType.UInt32 or KVValueType.UInt64 =>
                KVValues.Like(original, double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            _ => throw new InvalidDataException($"{label}: {original.ValueType} values cannot be written."),
        };
    }
}
