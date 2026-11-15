using System.Globalization;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace WeaponLab.Core.Graphs;

/// <summary>One leaf value of a node, e.g. <c>m_flComparisonValue = 0.33</c> or <c>m_stateDefinitions[0].m_transitionDefinitions[1].m_bCanBeForced</c>.</summary>
public sealed record GraphValue(string Path, string Value, KVValueType Type, bool Editable);

/// <summary>A node field that points at another node, e.g. <c>m_nChildNodeIdx → 23</c>.</summary>
public sealed record GraphLink(string Field, int Target);

/// <summary>A state machine transition: from one state to another when a condition is true.</summary>
/// <param name="Definition">Path of the definition inside the state machine node, for editing <c>m_bCanBeForced</c>.</param>
public sealed record GraphTransition(int From, int To, int TransitionNode, int ConditionNode, bool CanBeForced, string Definition);

/// <summary>One node of a compiled animation graph.</summary>
public sealed class GraphNode
{
    public required int Index { get; init; }

    /// <summary>Short class name, e.g. <c>ClipNode</c> for <c>CNmClipNode::CDefinition</c>.</summary>
    public required string Class { get; init; }

    /// <summary>Folder-like path from the graph editor, e.g. <c>actions/Actions/SM/Reload</c>.</summary>
    public required string Path { get; init; }

    public required IReadOnlyList<GraphValue> Values { get; init; }

    public required IReadOnlyList<GraphLink> Links { get; init; }

    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    public string Parent => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";

    /// <summary>Index into the graph's resources for clip and pose nodes, else -1.</summary>
    public int DataSlot => Value("m_nDataSlotIdx") is { } slot ? int.Parse(slot, CultureInfo.InvariantCulture) : -1;

    public bool PlaysData => Class is "ClipNode" or "AnimationPoseNode";

    public string? Value(string path) => Values.FirstOrDefault(value => value.Path == path)?.Value;
}

/// <summary>
/// A compiled <c>.vnmgraph_c</c> read for display and editing: nodes with
/// their values and links, the clips and graphs it plays, and its state
/// machines. Per-weapon variations (<c>viewmodel_gun.vnmgraph+ak47</c>) share
/// the default graph's nodes and differ in which resource each clip node plays.
/// </summary>
public sealed class GraphDocument
{
    public const string ClipEditKey = "clip";

    private GraphDocument(string resourcePath, string variation, int root, List<GraphNode> nodes, List<string> resources, List<string> parameters,
        Dictionary<int, List<GraphTransition>> machines, Dictionary<int, int> defaultStates)
    {
        ResourcePath = resourcePath;
        Variation = variation;
        RootIndex = root;
        Nodes = nodes;
        Resources = resources;
        ControlParameters = parameters;
        Transitions = machines;
        DefaultStates = defaultStates;
    }

    /// <summary>Resource path without <c>_c</c>.</summary>
    public string ResourcePath { get; }

    public string Variation { get; }

    public int RootIndex { get; }

    public IReadOnlyList<GraphNode> Nodes { get; }

    /// <summary><c>m_resources</c>: the clips and child graphs this graph plays (paths without <c>_c</c>).</summary>
    public IReadOnlyList<string> Resources { get; }

    public IReadOnlyList<string> ControlParameters { get; }

    /// <summary>State machine node → its transitions.</summary>
    public IReadOnlyDictionary<int, List<GraphTransition>> Transitions { get; }

    /// <summary>State machine node → its default state node.</summary>
    public IReadOnlyDictionary<int, int> DefaultStates { get; }

    public GraphNode this[int index] => Nodes[index];

    public static GraphDocument Read(byte[] bytes, string resourcePath)
    {
        using var resource = new Resource { FileName = resourcePath };
        try
        {
            resource.Read(new MemoryStream(bytes));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Cannot decode {resourcePath}: {error.Message}", error);
        }

        var root = (resource.DataBlock as BinaryKV3 ?? throw new InvalidDataException("Not a KV3 graph: " + resourcePath)).Data.Root;
        var paths = root["m_nodePaths"];
        var nodeValues = root["m_nodes"];
        var nodes = new List<GraphNode>(nodeValues.Count);
        var machines = new Dictionary<int, List<GraphTransition>>();
        var defaults = new Dictionary<int, int>();
        for (var index = 0; index < nodeValues.Count; index++)
        {
            var node = nodeValues[index];
            var values = new List<GraphValue>();
            var links = new List<GraphLink>();
            foreach (var (key, child) in node.Children)
            {
                if (key != "_class")
                {
                    Flatten(key, child, values, links);
                }
            }

            var className = node["_class"].ToString() ?? "";
            nodes.Add(new GraphNode
            {
                Index = index,
                Class = ShortClass(className),
                Path = index < paths.Count ? paths[index].ToString() ?? "" : $"node {index}",
                Values = values,
                Links = links,
            });

            if (node.TryGetValue("m_stateDefinitions", out var states))
            {
                machines[index] = ReadTransitions(states);
                if (node.TryGetValue("m_nDefaultStateIndex", out var first) && first.ToInt32(CultureInfo.InvariantCulture) is var position
                    && position >= 0 && position < states.Count)
                {
                    defaults[index] = states[position]["m_nStateNodeIdx"].ToInt32(CultureInfo.InvariantCulture);
                }
            }
        }

        return new GraphDocument(
            resourcePath.EndsWith("_c", StringComparison.Ordinal) ? resourcePath[..^2] : resourcePath,
            root.TryGetValue("m_variationID", out var variation) ? variation.ToString() ?? "" : "",
            root["m_nRootNodeIdx"].ToInt32(CultureInfo.InvariantCulture),
            nodes,
            root["m_resources"].Values.Select(value => value.ToString() ?? "").ToList(),
            root["m_controlParameterIDs"].Values.Select(value => value.ToString() ?? "").ToList(),
            machines,
            defaults)
        {
            ReferencedGraphSlots = root.TryGetValue("m_referencedGraphSlots", out var slots)
                ? slots.Values.Select(slot => slot.TryGetValue("m_dataSlotIdx", out var index) ? index.ToInt32(CultureInfo.InvariantCulture) : -1).ToList()
                : [],
        };
    }

    public static string ShortClass(string className) =>
        className.Replace("::CDefinition", "", StringComparison.Ordinal).Replace("CNm", "", StringComparison.Ordinal);

    private static List<GraphTransition> ReadTransitions(KVObject states)
    {
        var transitions = new List<GraphTransition>();
        for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
        {
            var state = states[stateIndex];
            var from = state["m_nStateNodeIdx"].ToInt32(CultureInfo.InvariantCulture);
            if (!state.TryGetValue("m_transitionDefinitions", out var definitions))
            {
                continue;
            }

            for (var index = 0; index < definitions.Count; index++)
            {
                var definition = definitions[index];
                var target = definition["m_nTargetStateIdx"].ToInt32(CultureInfo.InvariantCulture);
                if (target < 0 || target >= states.Count)
                {
                    continue;
                }

                transitions.Add(new GraphTransition(
                    from,
                    states[target]["m_nStateNodeIdx"].ToInt32(CultureInfo.InvariantCulture),
                    definition["m_nTransitionNodeIdx"].ToInt32(CultureInfo.InvariantCulture),
                    definition["m_nConditionNodeIdx"].ToInt32(CultureInfo.InvariantCulture),
                    definition.TryGetValue("m_bCanBeForced", out var forced) && forced.ToBoolean(CultureInfo.InvariantCulture),
                    $"m_stateDefinitions[{stateIndex}].m_transitionDefinitions[{index}]"));
            }
        }

        return transitions;
    }

    private static void Flatten(string path, KVObject value, List<GraphValue> values, List<GraphLink> links)
    {
        if (value.IsArray)
        {
            for (var index = 0; index < value.Count; index++)
            {
                Flatten($"{path}[{index}]", value[index], values, links);
            }
        }
        else if (value.IsCollection)
        {
            foreach (var (key, child) in value.Children)
            {
                Flatten($"{path}.{key}", child, values, links);
            }
        }
        else
        {
            var text = Format(value);
            var key = LeafKey(path);
            if (IsLink(key))
            {
                var target = (int)value.ToInt64(CultureInfo.InvariantCulture);
                if (target >= 0)
                {
                    links.Add(new GraphLink(path, target));
                }
            }

            values.Add(new GraphValue(path, text, value.ValueType, GraphGlossary.IsEditable(key, value.ValueType)));
        }
    }

    /// <summary>The last key of a path without indices: <c>a[0].m_nChildNodeIdx</c> → <c>m_nChildNodeIdx</c>.</summary>
    public static string LeafKey(string path)
    {
        var start = path.LastIndexOf('.') + 1;
        var end = path.IndexOf('[', start);
        return end < 0 ? path[start..] : path[start..end];
    }

    public static bool IsLink(string key) =>
        key != "m_nNodeIdx" && (key.EndsWith("NodeIdx", StringComparison.Ordinal) || key.EndsWith("NodeIndices", StringComparison.Ordinal));

    public static string Format(KVObject value) => value.ValueType switch
    {
        KVValueType.Boolean => value.ToBoolean(CultureInfo.InvariantCulture) ? "true" : "false",
        KVValueType.FloatingPoint or KVValueType.FloatingPoint64 => FormatFloat(value.ToDouble(CultureInfo.InvariantCulture)),
        KVValueType.Null => "",
        _ => value.ToString(CultureInfo.InvariantCulture) ?? "",
    };

    // Graph floats are stored as doubles of 32-bit floats (0.33000001311302185); show them as the float.
    private static string FormatFloat(double value) =>
        Math.Abs(value) >= 3.4e38 ? value.ToString("R", CultureInfo.InvariantCulture) : ((float)value).ToString("R", CultureInfo.InvariantCulture);

    /// <summary><c>m_referencedGraphSlots</c>: for each child-graph slot, its index into <see cref="Resources"/>.</summary>
    public IReadOnlyList<int> ReferencedGraphSlots { get; private init; } = [];

    /// <summary>
    /// The resource a node plays, or null: a clip or pose node's clip, or a
    /// child-graph node's graph (through <see cref="ReferencedGraphSlots"/>).
    /// </summary>
    public string? PlayedResource(GraphNode node)
    {
        var slot = node.DataSlot;
        if (slot < 0 && node.Value("m_nReferencedGraphIdx") is { } text
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var graphSlot) && graphSlot >= 0 && graphSlot < ReferencedGraphSlots.Count)
        {
            slot = ReferencedGraphSlots[graphSlot];
        }

        return slot >= 0 && slot < Resources.Count ? Resources[slot] : null;
    }

    /// <summary>The node a state, selector or other container shows inside, following child links.</summary>
    public IEnumerable<GraphNode> Linked(GraphNode node) => node.Links.Where(link => link.Target < Nodes.Count).Select(link => Nodes[link.Target]);

    /// <summary>Nodes that link to <paramref name="node"/>.</summary>
    public IEnumerable<(GraphNode Node, GraphLink Link)> Users(GraphNode node) =>
        Nodes.SelectMany(other => other.Links.Where(link => link.Target == node.Index).Select(link => (other, link)));

    // ---- Levels: the folder-like view the graph editor shows ----

    /// <summary>
    /// What one level of the graph contains: nodes whose path sits directly
    /// under <paramref name="level"/>, plus folders for deeper paths that have
    /// no node of their own (e.g. "Global Transitions").
    /// </summary>
    public GraphLevel Level(string level)
    {
        var prefix = level.Length == 0 ? "" : level + "/";
        var nodes = new List<GraphNode>();
        var folders = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (!node.Path.StartsWith(prefix, StringComparison.Ordinal) || node.Path.Length == prefix.Length)
            {
                continue;
            }

            var rest = node.Path[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash < 0)
            {
                nodes.Add(node);
            }
            else
            {
                folders.Add(prefix + rest[..slash]);
            }
        }

        var nodePaths = nodes.Select(node => node.Path).ToHashSet(StringComparer.Ordinal);
        folders.RemoveWhere(nodePaths.Contains);
        return new GraphLevel(level, nodes, folders.ToList());
    }

    /// <summary>True when other nodes sit inside <paramref name="path"/> (the node can be opened).</summary>
    public bool HasChildren(string path) => Nodes.Any(node => node.Path.StartsWith(path + "/", StringComparison.Ordinal));

    /// <summary>The level item (node or folder at <paramref name="level"/>) that contains node <paramref name="index"/>, or null when it is elsewhere.</summary>
    public string? ItemPathAt(string level, int index)
    {
        var path = Nodes[index].Path;
        var prefix = level.Length == 0 ? "" : level + "/";
        if (!path.StartsWith(prefix, StringComparison.Ordinal) || path.Length == prefix.Length)
        {
            return null;
        }

        var slash = path.IndexOf('/', prefix.Length);
        return slash < 0 ? path : path[..slash];
    }
}

/// <param name="Folders">Paths of folders with no node of their own.</param>
public sealed record GraphLevel(string Path, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<string> Folders);
