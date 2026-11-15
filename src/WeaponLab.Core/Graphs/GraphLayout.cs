namespace WeaponLab.Core.Graphs;

public enum GraphItemKind
{
    Node,

    /// <summary>A path folder with no node of its own, e.g. "Global Transitions".</summary>
    Folder,

    /// <summary>A node outside this level that feeds into it, e.g. the game value "action".</summary>
    Input,
}

/// <summary>A node box on the canvas, Source 2 Viewer style: a title bar with the output pin, a body, then one input pin per field.</summary>
/// <param name="Node">The node index for Node and Input items, else -1.</param>
/// <param name="Path">Node or folder path; opening it shows this path's level.</param>
/// <param name="Inputs">The fields that take a wire in (link field paths such as <c>m_nChildNodeIdx</c>), top to bottom; "" for a folder's single pin.</param>
public sealed record GraphItem(string Id, GraphItemKind Kind, int Node, string Path, string Title, double X, double Y, bool CanOpen, IReadOnlyList<string> Inputs, double Height)
{
    /// <summary>Where the output pin sits (right edge, in the title bar).</summary>
    public (double X, double Y) OutputPin => (X + GraphLayout.BoxWidth, Y + GraphLayout.HeaderHeight / 2);

    /// <summary>Where a field's input pin sits (left edge); the title bar's middle when the field has no pin.</summary>
    public (double X, double Y) InputPin(string field)
    {
        var row = Inputs.ToList().IndexOf(field);
        return row < 0 ? (X, Y + GraphLayout.HeaderHeight / 2) : (X, Y + GraphLayout.PinTop + (row + 0.5) * GraphLayout.PinSpacing);
    }
}

/// <summary>A wire: data flows from <paramref name="From"/>'s output into <paramref name="To"/>'s <paramref name="Field"/> pin.</summary>
/// <param name="Field">The input field on the target ("" for state machine arrows and folders).</param>
/// <param name="Transitions">For state machine levels: the transition nodes this arrow stands for.</param>
public sealed record GraphEdge(string From, string To, string Label, IReadOnlyList<int> Transitions, string Field = "");

public sealed record GraphCanvas(string Level, bool IsStateMachine, IReadOnlyList<GraphItem> Items, IReadOnlyList<GraphEdge> Edges, double Width, double Height);

/// <summary>
/// Lays out one level of a graph left to right like Source 2 Viewer: inputs
/// on the left, the result on the right, each wire going into the pin of the
/// field it feeds. A state machine's level shows its states, with an arrow per
/// pair of states that have transitions between them.
/// </summary>
public static class GraphLayout
{
    public const double BoxWidth = 220;
    public const double HeaderHeight = 22;
    public const double BodyHeight = 34;
    public const double PinSpacing = 17;
    public const double PinTop = HeaderHeight + BodyHeight;
    public const double ColumnGap = 110;
    public const double RowGap = 26;
    private const double Margin = 30;

    /// <summary>The height of a box with no input pins (states, inputs from elsewhere).</summary>
    public const double BoxHeight = PinTop + 6;

    private sealed record Box(GraphItemKind Kind, int Node, string Path, string Title, bool CanOpen)
    {
        public List<string> Inputs { get; } = [];

        public double Height => Inputs.Count == 0 ? BoxHeight : PinTop + Inputs.Count * PinSpacing + 6;
    }

    public static GraphCanvas For(GraphDocument graph, string level)
    {
        var machine = graph.Nodes.FirstOrDefault(node => node.Path == level && node.Class == "StateMachineNode");
        return machine is not null && graph.Transitions.ContainsKey(machine.Index)
            ? StateMachine(graph, level, machine)
            : Flow(graph, level);
    }

    // ---- Blend trees, states and folders: data flow ----

    private static GraphCanvas Flow(GraphDocument graph, string level)
    {
        var contents = graph.Level(level);
        var items = new Dictionary<string, Box>(StringComparer.Ordinal);
        var byPath = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in contents.Nodes)
        {
            var id = "n" + node.Index;
            items[id] = new Box(GraphItemKind.Node, node.Index, node.Path, node.Name, graph.HasChildren(node.Path));
            byPath.TryAdd(node.Path, id);
        }

        foreach (var folder in contents.Folders)
        {
            var id = "f" + folder;
            items[id] = new Box(GraphItemKind.Folder, -1, folder, folder[(folder.LastIndexOf('/') + 1)..], true);
            byPath[folder] = id;
        }

        string? ItemOf(int index)
        {
            if (items.ContainsKey("n" + index))
            {
                return "n" + index;
            }

            return graph.ItemPathAt(level, index) is { } path && byPath.TryGetValue(path, out var id) ? id : null;
        }

        var edges = new List<(string From, string To, string Field)>();
        foreach (var node in graph.Nodes)
        {
            if (ItemOf(node.Index) is not { } user)
            {
                continue;
            }

            // A node's own links feed its named pins; links from deeper inside a folder feed the folder's single pin.
            var ownPins = user == "n" + node.Index;
            foreach (var link in node.Links.Where(link => link.Target < graph.Nodes.Count))
            {
                var source = ItemOf(link.Target);
                if (source is null)
                {
                    // Comes from outside this level: show it as an input.
                    var target = graph[link.Target];
                    source = "x" + target.Index;
                    items.TryAdd(source, new Box(GraphItemKind.Input, target.Index, target.Path, target.Name, false));
                }

                var edge = (From: source, To: user, Field: ownPins ? link.Field : "");
                if (source != user && !edges.Contains(edge))
                {
                    edges.Add(edge);
                    if (!items[user].Inputs.Contains(edge.Field))
                    {
                        items[user].Inputs.Add(edge.Field);
                    }
                }
            }
        }

        // Longest-path layers: an item sits one column right of its rightmost input.
        var inputs = items.Keys.ToDictionary(id => id, id => edges.Where(edge => edge.To == id).Select(edge => edge.From).Distinct().ToList());
        var layer = new Dictionary<string, int>(StringComparer.Ordinal);
        int Layer(string id, HashSet<string> visiting)
        {
            if (layer.TryGetValue(id, out var known))
            {
                return known;
            }

            if (!visiting.Add(id))
            {
                return 0; // A cycle; break it here.
            }

            var value = items[id].Kind == GraphItemKind.Input ? 0 : inputs[id].Select(source => Layer(source, visiting) + 1).DefaultIfEmpty(items.Values.Any(item => item.Kind == GraphItemKind.Input) ? 1 : 0).Max();
            visiting.Remove(id);
            layer[id] = value;
            return value;
        }

        foreach (var id in items.Keys)
        {
            Layer(id, []);
        }

        var positions = Stack(items, layer, edges);
        return Build(level, false, items, positions, edges.Select(edge => new GraphEdge(edge.From, edge.To, "", [], edge.Field)).ToList());
    }

    /// <summary>
    /// Columns by layer, boxes stacked by their real heights. Within a column a
    /// box is ordered by where its wires go out (the pins it feeds in the next
    /// column), so wires cross as little as they can.
    /// </summary>
    private static Dictionary<string, (double X, double Y)> Stack(Dictionary<string, Box> items, Dictionary<string, int> layer, List<(string From, string To, string Field)> edges)
    {
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var columns = items.Keys.GroupBy(id => layer[id]).OrderByDescending(group => group.Key).ToList();

        // Right to left: the result column first, then each column ordered by the pins it feeds.
        var order = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            var ranked = column
                .Select(id =>
                {
                    var targets = edges.Where(edge => edge.From == id && order.ContainsKey(edge.To))
                        .Select(edge => order[edge.To] + items[edge.To].Inputs.IndexOf(edge.Field) * 0.01)
                        .ToList();
                    return (Id: id, Weight: targets.Count == 0 ? double.MaxValue : targets.Average());
                })
                .OrderBy(item => item.Weight)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToList();
            var y = Margin;
            for (var row = 0; row < ranked.Count; row++)
            {
                order[ranked[row].Id] = row;
                positions[ranked[row].Id] = (Margin + column.Key * (BoxWidth + ColumnGap), y);
                y += items[ranked[row].Id].Height + RowGap;
            }
        }

        return positions;
    }

    // ---- State machines: states and transitions ----

    private static GraphCanvas StateMachine(GraphDocument graph, string level, GraphNode machine)
    {
        var transitions = graph.Transitions[machine.Index];
        var states = machine.Values
            .Where(value => value.Path.EndsWith(".m_nStateNodeIdx", StringComparison.Ordinal) && !value.Path.Contains("m_transitionDefinitions", StringComparison.Ordinal))
            .Select(value => int.Parse(value.Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        var items = states.ToDictionary(
            state => "n" + state,
            state => new Box(GraphItemKind.Node, state, graph[state].Path, graph[state].Name, graph.HasChildren(graph[state].Path)));

        // Folders such as "Global Transitions" and "Entry Overrides" sit in their own column.
        var contents = graph.Level(level);
        foreach (var folder in contents.Folders)
        {
            items["f" + folder] = new Box(GraphItemKind.Folder, -1, folder, folder[(folder.LastIndexOf('/') + 1)..], true);
        }

        foreach (var node in contents.Nodes.Where(node => !states.Contains(node.Index)))
        {
            items["n" + node.Index] = new Box(GraphItemKind.Node, node.Index, node.Path, node.Name, graph.HasChildren(node.Path));
        }

        var edges = transitions
            .GroupBy(transition => (transition.From, transition.To))
            .Select(group => new GraphEdge(
                "n" + group.Key.From,
                "n" + group.Key.To,
                string.Join(", ", group.Select(transition => graph[transition.TransitionNode].Name).Distinct()),
                group.Select(transition => transition.TransitionNode).Distinct().ToList()))
            .ToList();

        // States on a circle, the default state at the top, so every pair has a clear straight arrow.
        var start = graph.DefaultStates.TryGetValue(machine.Index, out var first) ? first : states.FirstOrDefault();
        var ordered = states.Skip(states.IndexOf(start)).Concat(states.Take(Math.Max(0, states.IndexOf(start)))).ToList();
        var radius = ordered.Count <= 1 ? 0 : Math.Max(170, ordered.Count * (BoxWidth + 50) / (2 * Math.PI));
        var center = (X: Margin + radius + BoxWidth / 2, Y: Margin + radius + BoxHeight / 2);
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        for (var index = 0; index < ordered.Count; index++)
        {
            var angle = -Math.PI / 2 + 2 * Math.PI * index / ordered.Count;
            positions["n" + ordered[index]] = (center.X + radius * Math.Cos(angle) - BoxWidth / 2, center.Y + radius * Math.Sin(angle) - BoxHeight / 2);
        }

        // Folders and other nodes in a column to the right.
        var column = center.X + radius + BoxWidth / 2 + ColumnGap;
        var y = Margin;
        foreach (var id in items.Keys.Where(id => !positions.ContainsKey(id)).Order(StringComparer.Ordinal))
        {
            positions[id] = (column, y);
            y += items[id].Height + RowGap;
        }

        return Build(level, true, items, positions, edges);
    }

    // ---- Shared ----

    private static GraphCanvas Build(
        string level,
        bool isStateMachine,
        Dictionary<string, Box> items,
        Dictionary<string, (double X, double Y)> positions,
        List<GraphEdge> edges)
    {
        var result = items
            .Select(item => new GraphItem(
                item.Key,
                item.Value.Kind,
                item.Value.Node,
                item.Value.Path,
                item.Value.Title,
                positions[item.Key].X,
                positions[item.Key].Y,
                item.Value.CanOpen,
                item.Value.Inputs,
                item.Value.Height))
            .OrderBy(item => item.X)
            .ThenBy(item => item.Y)
            .ToList();
        var width = result.Select(item => item.X + BoxWidth).DefaultIfEmpty(0).Max() + Margin;
        var height = result.Select(item => item.Y + item.Height).DefaultIfEmpty(0).Max() + Margin;
        return new GraphCanvas(level, isStateMachine, result, edges, width, height);
    }
}
