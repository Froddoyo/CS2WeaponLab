using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Path = Avalonia.Controls.Shapes.Path;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Graphs;

namespace WeaponLab.App;

/// <summary>
/// The Graph tab's canvas, laid out like Source 2 Viewer's animgraph view:
/// node cards with a coloured title bar, one input pin per field on the left
/// and the output pin on the right, curved wires coloured by the kind of
/// value they carry, on a dotted grid. Edited spots glow with a slow pulse.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly IBrush CanvasEdgeBrush = new SolidColorBrush(Color.Parse("#6A6A74"));
    private static readonly IBrush SelectedBrush = Brushes.White;
    private static readonly IBrush NodeBackground = new SolidColorBrush(Color.Parse("#2B2B30"));
    private static readonly IBrush FolderBackground = new SolidColorBrush(Color.Parse("#232327"));
    private static readonly IBrush NodeBorder = new SolidColorBrush(Color.Parse("#46464E"));
    private static readonly IBrush PinLabelBrush = new SolidColorBrush(Color.Parse("#B8B8C0"));
    private static readonly IBrush GridDotBrush = new SolidColorBrush(Color.Parse("#34343A"));
    private static readonly IBrush EditedClipBrush = new SolidColorBrush(Color.Parse("#7FB8FF"));

    /// <summary>What a wire carries, guessed from the node it comes out of; sets its colour.</summary>
    private enum WireKind
    {
        Pose,
        Bool,
        Float,
        Id,
        Vector,
    }

    private static IBrush WireBrush(WireKind kind) => new SolidColorBrush(Color.Parse(kind switch
    {
        WireKind.Bool => "#E06C75",
        WireKind.Float => "#98C379",
        WireKind.Id => "#E5C07B",
        WireKind.Vector => "#C678DD",
        _ => "#8FB8DE",
    }));

    private static WireKind WireOf(GraphNode? source)
    {
        if (source is null)
        {
            return WireKind.Pose;
        }

        var name = source.Class;
        if (name.EndsWith("ComparisonNode", StringComparison.Ordinal) || name.Contains("Bool", StringComparison.Ordinal)
            || name is "AndNode" or "OrNode" or "NotNode" || name.Contains("Condition", StringComparison.Ordinal) || name.Contains("IsTagged", StringComparison.Ordinal))
        {
            return WireKind.Bool;
        }

        if (name.Contains("Float", StringComparison.Ordinal))
        {
            return WireKind.Float;
        }

        if (name.Contains("ID", StringComparison.Ordinal))
        {
            return WireKind.Id;
        }

        return name.Contains("Vector", StringComparison.Ordinal) || name.Contains("Target", StringComparison.Ordinal) ? WireKind.Vector : WireKind.Pose;
    }

    private void DrawGraph()
    {
        GraphCanvas.Children.Clear();
        if (_graph is not { } graph)
        {
            return;
        }

        var canvas = GraphLayout.For(graph, _graphLevel);
        GraphCanvas.Width = Math.Max(canvas.Width, 600);
        GraphCanvas.Height = Math.Max(canvas.Height, 400);
        RefreshBreadcrumb();
        DrawGrid(GraphCanvas.Width, GraphCanvas.Height);

        var edits = GraphEdits();
        var editedClips = EditedClipPaths();
        var boxes = canvas.Items.ToDictionary(item => item.Id);
        var wired = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in canvas.Edges)
        {
            if (boxes.TryGetValue(edge.From, out var from) && boxes.TryGetValue(edge.To, out var to))
            {
                if (canvas.IsStateMachine)
                {
                    DrawTransitionArrow(graph, from, to, edge);
                }
                else
                {
                    DrawWire(graph, from, to, edge);
                    wired.Add(edge.From);
                }
            }
        }

        foreach (var item in canvas.Items)
        {
            GraphCanvas.Children.Add(BuildBox(graph, item, edits, editedClips));
        }

        // Pins on top of the cards' edges.
        if (!canvas.IsStateMachine)
        {
            foreach (var item in canvas.Items)
            {
                var kinds = canvas.Edges.Where(edge => edge.To == item.Id).GroupBy(edge => edge.Field)
                    .ToDictionary(group => group.Key, group => WireOf(boxes[group.First().From].Node is var source and >= 0 ? graph[source] : null));
                foreach (var field in item.Inputs)
                {
                    AddPin(item.InputPin(field), WireBrush(kinds.GetValueOrDefault(field)));
                }

                if (wired.Contains(item.Id))
                {
                    AddPin(item.OutputPin, WireBrush(WireOf(item.Node >= 0 ? graph[item.Node] : null)));
                }
            }
        }

        var where = _graphLevel.Length == 0 ? "the top level" : _graphLevel;
        GraphTitle.Text = canvas.IsStateMachine
            ? $"State machine {where}: plays one state at a time. Arrows are transitions; click one to see when it happens and how it blends. Double-click a state to see what it plays."
            : $"Showing {where}. Wires run from a node's output (right) into the input it feeds (left). Double-click a node with ⤵ to open it.";
        // Say which file this is: the addon's _custom copy or CS2's own. CS2's file is never replaced.
        var stockName = System.IO.Path.GetFileName(_graphPath ?? "");
        var customName = _graphPath is null ? "" : System.IO.Path.GetFileName(GraphTree.CustomGraphPath(_graphPath));
        GraphTitle.Text += _graphReadOnly
            ? $" Showing {Target()?.Name}'s {customName}, its copy of CS2's {stockName}; its layout differs from CS2's, so it is view only."
            : _graphFromAddon
                ? $" Showing {Target()?.Name}'s {customName}, its live copy of CS2's {stockName} (green: where it differs from CS2). Edits are pushed into this copy; CS2's file is never replaced."
                : $" Showing CS2's {stockName}. Edits are pushed into the addon as {customName}; CS2's file is never replaced.";
    }

    /// <summary>A dot every 24 units, as one shape.</summary>
    private void DrawGrid(double width, double height)
    {
        const double Step = 24;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var x = Step; x < width; x += Step)
            {
                for (var y = Step; y < height; y += Step)
                {
                    context.BeginFigure(new Point(x, y), true);
                    context.LineTo(new Point(x + 1.4, y));
                    context.LineTo(new Point(x + 1.4, y + 1.4));
                    context.LineTo(new Point(x, y + 1.4));
                    context.EndFigure(true);
                }
            }
        }

        GraphCanvas.Children.Add(new Path { Data = geometry, Fill = GridDotBrush, IsHitTestVisible = false });
    }

    private void AddPin((double X, double Y) at, IBrush brush)
    {
        var pin = new Ellipse { Width = 9, Height = 9, Fill = brush, Stroke = NodeBackground, StrokeThickness = 1.5, IsHitTestVisible = false };
        Canvas.SetLeft(pin, at.X - 4.5);
        Canvas.SetTop(pin, at.Y - 4.5);
        GraphCanvas.Children.Add(pin);
    }

    private Control BuildBox(GraphDocument graph, GraphItem item, IReadOnlyDictionary<string, string> edits, HashSet<string> editedClips)
    {
        var node = item.Node >= 0 ? graph[item.Node] : null;
        var term = node is null ? null : GraphGlossary.Describe(node.Class);
        var category = item.Kind == GraphItemKind.Folder ? null : term?.Category;
        var own = node is null || item.Kind == GraphItemKind.Input ? ChangeState.None : NodeState(node.Index);
        var inside = item.CanOpen ? InsideState(item.Path) : ChangeState.None;
        var selected = node is not null && _graphSelected == node.Index && item.Kind != GraphItemKind.Input;

        // Title bar: the node's name, its kind on the right.
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Height = GraphLayout.HeaderHeight,
            Background = CategoryBrush(category),
        };
        header.Children.Add(new TextBlock
        {
            Text = item.Title + (item.CanOpen ? "  ⤵" : ""),
            FontSize = 11.5,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var kind = new TextBlock
        {
            Text = item.Kind switch
            {
                GraphItemKind.Folder => "Folder",
                GraphItemKind.Input => "from elsewhere",
                _ => term!.Name,
            },
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(kind, 1);
        header.Children.Add(kind);

        // Body: what the node does (plays which clip, compares what).
        var subtitle = node is null ? "" : GraphExplain.Subtitle(graph, node, edits);
        var played = node is { PlaysData: true }
            ? edits.GetValueOrDefault(Core.Graphs.GraphEdits.Key(node.Index, GraphDocument.ClipEditKey)) ?? graph.PlayedResource(node)
            : null;
        var edited = played is not null && editedClips.Contains(played);
        var custom = played is not null && played.StartsWith(GraphTree.CustomClipRoot, StringComparison.Ordinal);
        var body = new TextBlock
        {
            Text = (edited ? "✎ " : "") + subtitle + (custom ? "  (addon clip)" : ""),
            FontSize = 10.5,
            Height = GraphLayout.BodyHeight,
            Padding = new Thickness(7, 4, 7, 0),
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = edited ? EditedClipBrush : (IBrush)this.FindResource("MutedTextBrush")!,
        };

        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(body);

        // One labelled row per input pin.
        foreach (var field in item.Inputs)
        {
            content.Children.Add(new TextBlock
            {
                Text = field.Length == 0 ? "in" : GraphGlossary.Field(field).Name,
                FontSize = 10,
                Height = GraphLayout.PinSpacing,
                Margin = new Thickness(11, 0, 6, 0),
                Foreground = PinLabelBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
        }

        var card = new Border
        {
            Background = item.Kind == GraphItemKind.Folder ? FolderBackground : NodeBackground,
            BorderBrush = selected ? SelectedBrush : StateBrush(own) ?? NodeBorder,
            BorderThickness = new Thickness(selected || own != ChangeState.None ? 2 : 1),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true,
            Child = content,
            Opacity = item.Kind == GraphItemKind.Input ? 0.85 : 1,
        };

        var root = new Panel { Width = GraphLayout.BoxWidth, Height = item.Height, Cursor = new Cursor(StandardCursorType.Hand), Tag = item, Background = Brushes.Transparent };
        if (Glow(own, inside) is { } glow)
        {
            root.Children.Add(glow);
        }
        else if (item.Kind != GraphItemKind.Input && (edited || (item.CanOpen && HoldsEditedClip(graph, item.Path, edits, editedClips))))
        {
            // Plays (or holds a node that plays) a clip exported from Blender with edits: a blue pulse.
            root.Children.Add(GlowOf((ISolidColorBrush)EditedClipBrush, strong: edited));
        }

        root.Children.Add(card);
        if (own == ChangeState.None && inside != ChangeState.None)
        {
            // Edits inside: a dot, so the change can be followed down.
            root.Children.Add(new Ellipse { Width = 9, Height = 9, Fill = StateBrush(inside), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 14, 0) });
        }

        ToolTip.SetTip(root, BoxTip(graph, item, node, term));
        Canvas.SetLeft(root, item.X);
        Canvas.SetTop(root, item.Y);
        root.PointerPressed += OnGraphBoxPressed;
        return root;
    }

    /// <summary>
    /// A soft pulsing glow behind an edited spot: strong around a node with its
    /// own edits, faint around a state, machine or folder holding edits further
    /// down, so edits can be found from the top level. Null when nothing is edited.
    /// </summary>
    private Border? Glow(ChangeState own, ChangeState inside)
    {
        var state = own != ChangeState.None ? own : inside;
        if (StateBrush(state) is not ISolidColorBrush brush)
        {
            return null;
        }

        return GlowOf(brush, strong: own != ChangeState.None);
    }

    private static Border GlowOf(ISolidColorBrush brush, bool strong)
    {
        var glow = new Border
        {
            CornerRadius = new CornerRadius(6),
            Background = NodeBackground,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = strong ? 22 : 14,
                Spread = strong ? 3 : 1,
                Color = Color.FromArgb(strong ? (byte)230 : (byte)140, brush.Color.R, brush.Color.G, brush.Color.B),
            }),
            IsHitTestVisible = false,
        };
        glow.Classes.Add("glowPulse");
        return glow;
    }

    /// <summary>A clip node inside <paramref name="path"/> plays a clip exported from Blender with edits.</summary>
    private static bool HoldsEditedClip(GraphDocument graph, string path, IReadOnlyDictionary<string, string> edits, HashSet<string> editedClips) =>
        editedClips.Count > 0 && graph.Nodes.Any(node => node.PlaysData && node.Path.StartsWith(path + "/", StringComparison.Ordinal)
            && (edits.GetValueOrDefault(Core.Graphs.GraphEdits.Key(node.Index, GraphDocument.ClipEditKey)) ?? graph.PlayedResource(node)) is { } clip
            && editedClips.Contains(clip));

    private static string BoxTip(GraphDocument graph, GraphItem item, GraphNode? node, GraphNodeTerm? term)
    {
        if (node is null)
        {
            return $"Folder {item.Path}\nGroups nodes that belong together. Click to open.";
        }

        var tip = $"{term!.Name}: {term.Description}\n{node.Path}  (node {node.Index}, {node.Class})";
        if (item.Kind == GraphItemKind.Input)
        {
            tip += "\nComes from outside this level. Click to go to it.";
        }
        else if (item.CanOpen)
        {
            tip += "\nDouble-click to open.";
        }

        return tip;
    }

    /// <summary>A data wire from a node's output pin into the input pin it feeds; one that runs backwards loops under the cards.</summary>
    private void DrawWire(GraphDocument graph, GraphItem from, GraphItem to, GraphEdge edge)
    {
        var (sx, sy) = from.OutputPin;
        var (ex, ey) = to.InputPin(edge.Field);
        var start = new Point(sx, sy);
        var end = new Point(ex, ey);
        var brush = WireBrush(WireOf(from.Node >= 0 ? graph[from.Node] : null));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false);
            if (end.X > start.X + 20)
            {
                var bend = Math.Max(50, (end.X - start.X) * 0.5);
                context.CubicBezierTo(new Point(start.X + bend, start.Y), new Point(end.X - bend, end.Y), end);
            }
            else
            {
                var below = Math.Max(from.Y + from.Height, to.Y + to.Height) + 30;
                context.CubicBezierTo(new Point(start.X + 80, start.Y), new Point(start.X + 80, below), new Point((start.X + end.X) / 2, below));
                context.CubicBezierTo(new Point(end.X - 80, below), new Point(end.X - 80, end.Y), end);
            }
        }

        GraphCanvas.Children.Add(new Path { Data = geometry, Stroke = brush, StrokeThickness = 2, Opacity = 0.9, IsHitTestVisible = false });
    }

    /// <summary>A state machine transition: a straight arrow between two states, glowing (pulsing) when edited; click it to inspect.</summary>
    private void DrawTransitionArrow(GraphDocument graph, GraphItem from, GraphItem to, GraphEdge edge)
    {
        var state = edge.Transitions.Select(NodeState).Aggregate(ChangeState.None, (worst, next) => next == ChangeState.Pending || worst == ChangeState.Pending ? ChangeState.Pending : next == ChangeState.Applied ? ChangeState.Applied : worst);
        var selected = edge.Transitions.Count > 0 && edge.Transitions.SequenceEqual(_graphSelectedTransitions);
        var brush = selected ? SelectedBrush : StateBrush(state) ?? CanvasEdgeBrush;

        // Straight arrow between the boxes' edges, shifted sideways so A→B and B→A do not overlap.
        var a = new Point(from.X + GraphLayout.BoxWidth / 2, from.Y + from.Height / 2);
        var b = new Point(to.X + GraphLayout.BoxWidth / 2, to.Y + to.Height / 2);
        var along = b - a;
        var length = Math.Max(1, Math.Sqrt(along.X * along.X + along.Y * along.Y));
        var unit = along / length;
        var side = new Vector(-unit.Y, unit.X) * 6;
        var start = ClipToBox(a, unit, from.Height) + side;
        var end = ClipToBox(b, -unit, to.Height) + side;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false);
            context.LineTo(end);
        }

        if (state != ChangeState.None && StateBrush(state) is { } glow)
        {
            var halo = new Path { Data = geometry, Stroke = glow, StrokeThickness = 10, Opacity = 0.35, IsHitTestVisible = false };
            halo.Classes.Add("glowPulse");
            GraphCanvas.Children.Add(halo);
        }

        GraphCanvas.Children.Add(new Path { Data = geometry, Stroke = brush, StrokeThickness = selected || state != ChangeState.None ? 2.5 : 1.6 });
        GraphCanvas.Children.Add(new Polygon
        {
            Points = [end, end - unit * 9 + new Vector(-unit.Y, unit.X) * 4.5, end - unit * 9 - new Vector(-unit.Y, unit.X) * 4.5],
            Fill = brush,
        });

        if (edge.Transitions.Count > 0)
        {
            // A wide invisible stroke makes the thin arrow easy to click.
            var hit = new Path { Data = geometry, Stroke = Brushes.Transparent, StrokeThickness = 12, Cursor = new Cursor(StandardCursorType.Hand), Tag = edge };
            ToolTip.SetTip(hit, string.Join("\n", graph.Transitions.Values.SelectMany(list => list)
                .Where(transition => edge.Transitions.Contains(transition.TransitionNode) && "n" + transition.From == edge.From)
                .Select(transition => GraphExplain.Transition(graph, transition, GraphEdits()))
                .Distinct()));
            hit.PointerPressed += OnGraphEdgePressed;
            GraphCanvas.Children.Add(hit);
        }
    }

    /// <summary>Where a ray from a box centre leaves the box.</summary>
    private static Point ClipToBox(Point center, Vector direction, double height)
    {
        var halfWidth = GraphLayout.BoxWidth / 2 + 2;
        var halfHeight = height / 2 + 2;
        var scale = Math.Min(
            Math.Abs(direction.X) < 1e-6 ? double.MaxValue : halfWidth / Math.Abs(direction.X),
            Math.Abs(direction.Y) < 1e-6 ? double.MaxValue : halfHeight / Math.Abs(direction.Y));
        return center + direction * scale;
    }

    private static IBrush CategoryBrush(GraphNodeCategory? category) => new SolidColorBrush(Color.Parse(category switch
    {
        GraphNodeCategory.Parameter => "#A8642A",
        GraphNodeCategory.StateMachine => "#6E54A8",
        GraphNodeCategory.State => "#3D68A8",
        GraphNodeCategory.Transition => "#45707E",
        GraphNodeCategory.Clip => "#2E8794",
        GraphNodeCategory.Blend => "#56619C",
        GraphNodeCategory.Logic => "#55555C",
        GraphNodeCategory.Value => "#66584A",
        GraphNodeCategory.Graph => "#8A4677",
        GraphNodeCategory.Pose => "#8A4E36",
        _ => "#3A3A40",
    }));
}
