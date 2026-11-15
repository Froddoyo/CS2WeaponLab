using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Graphs;
using WeaponLab.Core.Profiles;

namespace WeaponLab.App;

/// <summary>
/// The Graph tab: one level of an animation graph at a time on a canvas
/// (open states, state machines and folders to go deeper), an inspector for
/// the selected node, and edits stored in the profile. Yellow marks edits
/// the addon does not have yet, green edits it has.
/// </summary>
public sealed partial class MainWindow
{
    private const string AllGraphsSeparator = "──────── all viewmodel graphs ────────";

    private Dictionary<string, GraphInfo>? _graphInfos;
    private GraphDocument? _graph;
    private GraphDocument? _deliveredGraph;

    /// <summary>CS2's version of the shown graph: green marks where the addon's live copy differs from it.</summary>
    private GraphDocument? _stockGraph;

    /// <summary>The shown graph's CS2 path (the profile's key for its edits).</summary>
    private string? _graphPath;

    /// <summary>The tab shows the addon's own version of the graph.</summary>
    private bool _graphFromAddon;

    /// <summary>The addon's version has a different node layout from CS2's: shown, not editable.</summary>
    private bool _graphReadOnly;
    private string _graphLevel = "";
    private int? _graphSelected;
    private IReadOnlyList<int> _graphSelectedTransitions = [];
    private Point? _graphPanStart;
    private Vector _graphPanOffset;

    private enum ChangeState
    {
        None,
        Pending,
        Applied,
    }

    // ---- Loading ----

    private async Task RefreshGraphAsync()
    {
        BuildGraphLegend();
        if (_connection is not { } connection)
        {
            GraphTitle.Text = "Connect to CS2 first";
            return;
        }

        try
        {
            _graphInfos ??= await Task.Run(() => GraphTree.Load(connection.Archive));
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            Log($"Could not read the graphs: {error.Message}");
            return;
        }

        var target = Target();
        _addonGraphs = target is null ? [] : _graphInfos.Keys
            .Where(path => File.Exists(System.IO.Path.Combine(target.GamePath, (GraphTree.CustomGraphPath(path) + "_c").Replace('/', System.IO.Path.DirectorySeparatorChar))))
            .ToHashSet(StringComparer.Ordinal);
        var clips = _weapon?.ClipPaths.Select(path => path[..^2]).ToHashSet(StringComparer.Ordinal) ?? [];
        var weaponGraphs = _graphInfos.Values
            .Where(graph => graph.Resources.Any(clips.Contains))
            .Select(graph => graph.Path)
            .Order(StringComparer.Ordinal)
            .ToList();
        var items = weaponGraphs.Select(GraphLabel).Concat([AllGraphsSeparator])
            .Concat(_graphInfos.Keys.Where(path => !weaponGraphs.Contains(path)).Order(StringComparer.Ordinal).Select(GraphLabel))
            .ToList();

        _updatingControls = true;
        GraphCombo.ItemsSource = items;
        var keep = _requestedGraph is { } requested && _graphInfos.ContainsKey(requested) ? requested
            : _graphPath is not null && (weaponGraphs.Count == 0 || weaponGraphs.Contains(_graphPath)) ? _graphPath : weaponGraphs.FirstOrDefault();
        _requestedGraph = null;
        GraphCombo.SelectedItem = keep is null ? null : GraphLabel(keep);
        _updatingControls = false;

        if (keep is null)
        {
            _graph = null;
            GraphCanvas.Children.Clear();
            GraphTitle.Text = _weapon is null ? "Select a weapon in the outliner, or pick any graph from the list." : $"No viewmodel graph plays {_weapon.Id}'s clips. Pick any graph from the list.";
            ShowGraphHelp();
            return;
        }

        await LoadGraphAsync(keep);
        if (_requestedGraphNode is { } wanted && wanted.Graph == keep)
        {
            GoToGraphNode(wanted.Node);
        }

        _requestedGraphNode = null;
    }

    /// <summary>CS2 graphs the target addon has a custom copy of.</summary>
    private HashSet<string> _addonGraphs = [];

    /// <summary>Graph list label → CS2 graph path.</summary>
    private readonly Dictionary<string, string> _graphLabelPaths = new(StringComparer.Ordinal);

    /// <summary>
    /// The list label: the addon's <c>_custom</c> copy by its own name when the
    /// addon has one (that is what the tab shows and delivery writes; CS2's file
    /// is never replaced), else CS2's graph.
    /// </summary>
    private string GraphLabel(string path)
    {
        var name = path.Replace(GraphTree.GraphRoot, "", StringComparison.Ordinal);
        var label = _addonGraphs.Contains(path)
            ? $"● {GraphTree.CustomGraphPath(path).Replace(GraphTree.GraphRoot, "", StringComparison.Ordinal)}  (addon copy of {name})"
            : name;
        _graphLabelPaths[label] = path;
        return label;
    }

    private async void OnGraphSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || GraphCombo.SelectedItem is not string label || !_graphLabelPaths.TryGetValue(label, out var path))
        {
            return;
        }

        await LoadGraphAsync(path);
    }

    /// <summary>
    /// Loads a graph by its CS2 path. The tab shows the addon's own version
    /// when it has one (what delivery builds on); edits stay keyed by the CS2
    /// path. An addon graph whose node layout differs from CS2's is shown
    /// read-only, since edits are made by node.
    /// </summary>
    private async Task LoadGraphAsync(string path)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var target = Target();
        GraphDocument stock;
        GraphDocument? own;
        try
        {
            (stock, own) = await Task.Run(() => (
                GraphDocument.Read(connection.Archive.ReadBytes(path + "_c"), path + "_c"),
                GraphStatus.ReadDelivered(target, path)));
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            Log($"Could not read {path}: {error.Message}");
            return;
        }

        if (_graphPath != path)
        {
            _graphLevel = "";
            _graphSelected = null;
            _graphSelectedTransitions = [];
        }

        _graphPath = path;
        _graphFromAddon = own is not null;
        _graphReadOnly = own is not null && own.Nodes.Count != stock.Nodes.Count;
        _graph = own ?? stock;
        _stockGraph = stock;
        _deliveredGraph = GraphStatus.ReadDelivered(target, path);
        DrawGraph();
        RefreshGraphInspector();
        RefreshGraphEditList();
    }

    // ---- Edits and their state ----

    private SortedDictionary<string, string> GraphEdits() =>
        _graphPath is not null && _profile.Graphs.TryGetValue(_graphPath, out var edits) ? edits : new SortedDictionary<string, string>(StringComparer.Ordinal);

    private ChangeState EditState(string key, string value) =>
        _deliveredGraph is not null && GraphStatus.Holds(_deliveredGraph, key, value) ? ChangeState.Applied : ChangeState.Pending;

    /// <summary>
    /// A node's state: yellow while the profile has edits on it the addon lacks,
    /// green when the addon's live copy differs from CS2's here (its own routing,
    /// earlier pushes), untouched otherwise.
    /// </summary>
    private ChangeState NodeState(int index)
    {
        var prefix = index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
        var states = GraphEdits().Where(edit => edit.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(edit => EditState(edit.Key, edit.Value)).ToList();
        if (states.Count > 0)
        {
            return states.Contains(ChangeState.Pending) ? ChangeState.Pending : ChangeState.Applied;
        }

        return LiveChanged(index) ? ChangeState.Applied : ChangeState.None;
    }

    /// <summary>The addon's live copy differs from CS2's at this node: another value, or another clip or child graph played.</summary>
    private bool LiveChanged(int index)
    {
        if (!_graphFromAddon || _graphReadOnly || _graph is not { } live || _stockGraph is not { } stock || index >= stock.Nodes.Count)
        {
            return false;
        }

        var (mine, theirs) = (live[index], stock[index]);
        if (live.PlayedResource(mine) != stock.PlayedResource(theirs))
        {
            return true;
        }

        return mine.Values.Count != theirs.Values.Count
            || mine.Values.Zip(theirs.Values).Any(pair => pair.First.Path != pair.Second.Path || pair.First.Value != pair.Second.Value);
    }

    /// <summary>The state of everything inside a path (for states, machines and folders).</summary>
    private ChangeState InsideState(string path)
    {
        if (_graph is null)
        {
            return ChangeState.None;
        }

        var states = GraphEdits()
            .Select(edit => (Node: Core.Graphs.GraphEdits.Parse(edit.Key).Node, edit.Key, edit.Value))
            .Where(edit => edit.Node < _graph.Nodes.Count && _graph[edit.Node].Path.StartsWith(path + "/", StringComparison.Ordinal))
            .Select(edit => EditState(edit.Key, edit.Value))
            .ToList();
        if (states.Count > 0)
        {
            return states.Contains(ChangeState.Pending) ? ChangeState.Pending : ChangeState.Applied;
        }

        return _graph.Nodes.Any(node => node.Path.StartsWith(path + "/", StringComparison.Ordinal) && LiveChanged(node.Index)) ? ChangeState.Applied : ChangeState.None;
    }

    private IBrush? StateBrush(ChangeState state) => state switch
    {
        ChangeState.Pending => (IBrush)this.FindResource("NeedsApplyBrush")!,
        ChangeState.Applied => (IBrush)this.FindResource("AppliedBrush")!,
        _ => null,
    };

    private HashSet<string> EditedClipPaths()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sandbox in _sandboxes.List())
        {
            foreach (var clip in sandbox.Manifest.Clips)
            {
                if (IsEdited(_connection!.Weapons.First(weapon => weapon.Id == sandbox.Manifest.WeaponId), ExplorerNode.ClipName(clip.ResourcePath)))
                {
                    // The game path, and the custom path an addon's graph plays it at once delivered.
                    result.Add(clip.ResourcePath[..^2]);
                    result.Add(GraphTree.CustomClipPath(sandbox.Manifest.WeaponId, clip.ResourcePath[..^2]));
                }
            }
        }

        return result;
    }

    /// <summary>Whose graph edits start from: the addon's own version, or CS2's.</summary>
    private string GraphBaseName => _graphFromAddon ? "the addon" : "CS2";

    private void CommitGraphEdit(int node, string path, string input, TextBlock? error)
    {
        if (_graph is null)
        {
            return;
        }

        if (_graphReadOnly)
        {
            if (error is not null)
            {
                error.Text = "The addon's version of this graph has a different node layout from CS2's, so it cannot be edited here.";
                error.IsVisible = true;
            }

            return;
        }

        var (value, problem) = Core.Graphs.GraphEdits.Normalize(_graph, node, path, input);
        if (value is null)
        {
            if (error is not null)
            {
                error.Text = problem;
                error.IsVisible = true;
            }

            return;
        }

        var stock = path == GraphDocument.ClipEditKey ? _graph.PlayedResource(_graph[node]) : _graph[node].Value(path);
        var key = Core.Graphs.GraphEdits.Key(node, path);
        if (!_profile.Graphs.TryGetValue(_graphPath!, out var edits))
        {
            _profile.Graphs[_graphPath!] = edits = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        var label = $"{_graph[node].Name} · {GraphGlossary.Field(path).Name}";
        var unchanged = stock is not null && (path == GraphDocument.ClipEditKey ? Core.Graphs.GraphStatus.SameClip(stock, value) : Core.VData.VDataValues.Same(stock, value));
        if (unchanged)
        {
            if (!edits.Remove(key))
            {
                return;
            }

            Log($"Graph: {label} back to {GraphBaseName}'s value ({stock}).");
        }
        else
        {
            if (edits.TryGetValue(key, out var old) && old == value)
            {
                return;
            }

            edits[key] = value;
            Log($"Graph: {label} = {(path == GraphDocument.ClipEditKey ? System.IO.Path.GetFileNameWithoutExtension(value) : value)} ({GraphBaseName}: {stock ?? "none"}). Apply the profile in the Deliver tab.");
        }

        // The first custom graph needs a player model that uses viewmodel_custom; say so while it is fresh.
        if (edits.Count == 1 && _profile.Graphs.Count == 1 && Target() is not null && Core.Addons.AddonChecks.HookedPlayerModels(_inventory).Count == 0)
        {
            Log($"Heads-up: {Target()!.Name} has no player model whose hudmodel graph is {GraphTree.CustomParent}. Graph edits are delivered there, but they only play for player models that use it.");
        }

        if (edits.Count == 0)
        {
            _profile.Graphs.Remove(_graphPath!);
        }

        SaveProfile();
        DrawGraph();
        RefreshGraphInspector();
        RefreshGraphEditList();
        _ = RefreshStatusAsync();
    }

    private void BuildGraphLegend()
    {
        if (GraphLegend.Children.Count > 0)
        {
            return;
        }

        void Chip(IBrush brush, string text, bool outline = false)
        {
            GraphLegend.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 12, 0),
                Spacing = 4,
                Children =
                {
                    new Border
                    {
                        Width = 12, Height = 12, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center,
                        Background = outline ? Brushes.Transparent : brush, BorderBrush = brush, BorderThickness = new Thickness(outline ? 2 : 0),
                    },
                    new TextBlock { Text = text, FontSize = 11, VerticalAlignment = VerticalAlignment.Center },
                },
            });
        }

        Chip((IBrush)this.FindResource("NeedsApplyBrush")!, "Edited in profile, not in the addon yet", outline: true);
        Chip((IBrush)this.FindResource("AppliedBrush")!, "Edited, the addon has it", outline: true);
        Chip(EditedClipBrush, "✎ plays a clip you edited in Blender");
        foreach (var (kind, text) in new[] { (WireKind.Pose, "Pose wire"), (WireKind.Bool, "True/false"), (WireKind.Float, "Number"), (WireKind.Id, "Name/ID") })
        {
            Chip(WireBrush(kind), text);
        }

        foreach (var (category, text) in new[]
                 {
                     (GraphNodeCategory.Parameter, "Game value"), (GraphNodeCategory.StateMachine, "State machine"), (GraphNodeCategory.State, "State"),
                     (GraphNodeCategory.Clip, "Clip"), (GraphNodeCategory.Blend, "Choose/blend"), (GraphNodeCategory.Logic, "Logic"),
                     (GraphNodeCategory.Graph, "Child graph"),
                 })
        {
            Chip(CategoryBrush(category), text);
        }
    }

    // ---- Navigation ----

    private void OnGraphBoxPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: GraphItem item } || _graph is null)
        {
            return;
        }

        e.Handled = true;
        if (item.Kind == GraphItemKind.Input)
        {
            GoToGraphNode(item.Node);
            return;
        }

        if (e.ClickCount >= 2 && item.CanOpen)
        {
            OpenGraphLevel(item.Path);
            return;
        }

        _graphSelected = item.Node >= 0 ? item.Node : null;
        _graphSelectedTransitions = [];
        if (item.Kind == GraphItemKind.Folder)
        {
            OpenGraphLevel(item.Path);
            return;
        }

        DrawGraph();
        RefreshGraphInspector();
    }

    private void OnGraphEdgePressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: GraphEdge edge })
        {
            return;
        }

        e.Handled = true;
        _graphSelectedTransitions = edge.Transitions;
        _graphSelected = edge.Transitions[0];
        DrawGraph();
        RefreshGraphInspector();
    }

    private void OnGraphCanvasPressed(object? sender, PointerPressedEventArgs e)
    {
        // Drag the background to pan.
        _graphPanStart = e.GetPosition(GraphScroll);
        _graphPanOffset = GraphScroll.Offset;
        e.Pointer.Capture(GraphCanvas);
        GraphCanvas.PointerMoved -= OnGraphCanvasMoved;
        GraphCanvas.PointerReleased -= OnGraphCanvasReleased;
        GraphCanvas.PointerMoved += OnGraphCanvasMoved;
        GraphCanvas.PointerReleased += OnGraphCanvasReleased;
    }

    private void OnGraphCanvasMoved(object? sender, PointerEventArgs e)
    {
        if (_graphPanStart is { } start)
        {
            var delta = e.GetPosition(GraphScroll) - start;
            GraphScroll.Offset = new Vector(Math.Max(0, _graphPanOffset.X - delta.X), Math.Max(0, _graphPanOffset.Y - delta.Y));
        }
    }

    private void OnGraphCanvasReleased(object? sender, PointerReleasedEventArgs e)
    {
        _graphPanStart = null;
        e.Pointer.Capture(null);
    }

    private void OnGraphZoomChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        GraphZoomHost.LayoutTransform = new ScaleTransform(GraphZoom.Value, GraphZoom.Value);
    }

    private void OpenGraphLevel(string level)
    {
        _graphLevel = level;
        _graphSelectedTransitions = [];
        GraphScroll.Offset = default;
        DrawGraph();
        RefreshGraphInspector();
    }

    /// <summary>Shows the level that holds a node and selects it.</summary>
    private void GoToGraphNode(int index)
    {
        if (_graph is null || index < 0 || index >= _graph.Nodes.Count)
        {
            return;
        }

        var node = _graph[index];
        _graphLevel = node.Parent;
        _graphSelected = index;
        _graphSelectedTransitions = [];
        DrawGraph();
        RefreshGraphInspector();
    }

    private void OnGraphUpClicked(object? sender, RoutedEventArgs e) => GoUpGraphLevel();

    private void GoUpGraphLevel()
    {
        if (_graphLevel.Length == 0)
        {
            return;
        }

        var parent = _graphLevel.Contains('/') ? _graphLevel[.._graphLevel.LastIndexOf('/')] : "";
        var from = _graphLevel;
        OpenGraphLevel(parent);
        if (_graph?.Nodes.FirstOrDefault(node => node.Path == from) is { } container)
        {
            _graphSelected = container.Index;
            DrawGraph();
            RefreshGraphInspector();
        }
    }

    private void RefreshBreadcrumb()
    {
        GraphBreadcrumb.Children.Clear();
        GraphUpButton.IsEnabled = _graphLevel.Length > 0;
        var label = _graph is null || _graphPath is null ? "" : System.IO.Path.GetFileName(_graphFromAddon ? GraphTree.CustomGraphPath(_graphPath) : _graphPath);
        AddCrumb(label, "");
        var path = "";
        foreach (var part in _graphLevel.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            path = path.Length == 0 ? part : path + "/" + part;
            GraphBreadcrumb.Children.Add(new TextBlock { Text = "›", Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = (IBrush)this.FindResource("MutedTextBrush")! });
            AddCrumb(part, path);
        }

        void AddCrumb(string text, string target)
        {
            var button = new Button { Content = new TextBlock { Text = text }, Padding = new Thickness(6, 1), FontSize = 11 };
            var state = target.Length == 0 ? ChangeState.None : InsideState(target);
            if (StateBrush(state) is { } brush)
            {
                button.BorderBrush = brush;
                button.BorderThickness = new Thickness(1);
            }

            button.Click += (_, _) => OpenGraphLevel(target);
            GraphBreadcrumb.Children.Add(button);
        }
    }

    // ---- Search ----

    private void OnGraphSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var text = GraphSearchBox.Text?.Trim() ?? "";
        if (_graph is null || text.Length < 2)
        {
            GraphSearchResults.IsVisible = false;
            return;
        }

        var edits = GraphEdits();
        var matches = _graph.Nodes
            .Where(node => node.Path.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (node.PlaysData && GraphExplain.PlayedName(_graph, node, edits).Contains(text, StringComparison.OrdinalIgnoreCase)))
            .Take(60)
            .Select(node => new ListBoxItem
            {
                Content = $"{node.Path}   ·  {GraphGlossary.Describe(node.Class).Name}{(node.PlaysData ? "  ·  " + GraphExplain.PlayedName(_graph, node, edits) : "")}",
                Tag = node.Index,
            })
            .ToList();
        GraphSearchResults.ItemsSource = matches;
        GraphSearchResults.IsVisible = matches.Count > 0;
    }

    private void OnGraphSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && GraphSearchResults.ItemsSource is List<ListBoxItem> { Count: > 0 } items && items[0].Tag is int index)
        {
            FinishGraphSearch(index);
        }
        else if (e.Key == Key.Escape)
        {
            GraphSearchBox.Text = "";
        }
    }

    private void OnGraphSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (GraphSearchResults.SelectedItem is ListBoxItem { Tag: int index })
        {
            FinishGraphSearch(index);
        }
    }

    private void FinishGraphSearch(int index)
    {
        GraphSearchResults.IsVisible = false;
        GoToGraphNode(index);
    }

    // ---- Edits list and reset ----

    private void RefreshGraphEditList()
    {
        if (_graph is null)
        {
            GraphEditList.ItemsSource = null;
            GraphResetButton.IsEnabled = false;
            return;
        }

        var items = GraphEdits().Select(edit =>
        {
            var (node, path) = Core.Graphs.GraphEdits.Parse(edit.Key);
            var name = node < _graph.Nodes.Count ? _graph[node].Name : $"node {node}";
            var value = path == GraphDocument.ClipEditKey ? System.IO.Path.GetFileNameWithoutExtension(edit.Value) : edit.Value;
            var state = EditState(edit.Key, edit.Value);
            return new ListBoxItem
            {
                Tag = node,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children =
                    {
                        new Ellipse { Width = 8, Height = 8, Fill = StateBrush(state), VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = $"{name} · {GraphGlossary.Field(path).Name} = {value}", FontSize = 11 },
                    },
                },
            };
        }).ToList();
        GraphEditList.ItemsSource = items;
        GraphEditsHeader.Text = items.Count == 0 ? "Profile edits in this graph (none)" : $"Profile edits in this graph ({items.Count})";
        GraphResetButton.IsEnabled = items.Count > 0;
    }

    private void OnGraphEditSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (GraphEditList.SelectedItem is ListBoxItem { Tag: int node })
        {
            GoToGraphNode(node);
        }
    }

    private async void OnGraphResetClicked(object? sender, RoutedEventArgs e)
    {
        if (_graph is null || GraphEdits().Count is var count && count == 0)
        {
            return;
        }

        if (!await Dialogs.ConfirmAsync(this, "Reset graph",
                $"Remove the {count} edit(s) profile '{_profile.Name}' makes to {System.IO.Path.GetFileName(_graphPath ?? "")}? The addon goes back to its own version of the graph (or CS2's) when you apply the profile.", "Reset"))
        {
            return;
        }

        _profile.Graphs.Remove(_graphPath!);
        SaveProfile();
        Log($"Graph: {_graphPath} reset in '{_profile.Name}'.");
        DrawGraph();
        RefreshGraphInspector();
        RefreshGraphEditList();
        await RefreshStatusAsync();
    }

    // ---- Inspector ----

    private void ShowGraphHelp()
    {
        GraphInspector.Children.Clear();
        AddText(GraphInspector, "How animation graphs work", 14, FontWeight.SemiBold);
        AddText(GraphInspector,
            "The game does not play clips by itself. Every frame it sends the graph a few values: the action (idle, reload, attack…), the weapon, ammo, ironsight amount. " +
            "The graph turns those into the pose you see.\n\n" +
            "• State machines play one state at a time (Idle, Reload, Inspect…) and switch along transitions when a condition becomes true, blending over the transition's blend time.\n\n" +
            "• Inside a state, selectors and blends pick or mix clips, e.g. partial or empty reload depending on ammo.\n\n" +
            "• The shared graphs are the same for every gun; each weapon has a variation (viewmodel_gun.vnmgraph+ak47) that decides which of its clips each clip box plays.\n\n" +
            "What you can change here: which clip a clip box plays, playback speed and looping, transition blend times, the numbers and names conditions compare with, and other tuning values. " +
            "The wiring itself (which box connects to which) stays as Valve built it.\n\n" +
            "Edits go into the profile. Applying the profile delivers the edited graph as a *_custom graph into the addon, like edited clips.",
            11, FontWeight.Normal, muted: true);
    }

    private static TextBlock AddText(Panel panel, string text, double size = 12, FontWeight? weight = null, bool muted = false)
    {
        var block = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeight.Normal, TextWrapping = TextWrapping.Wrap };
        if (muted)
        {
            block.Opacity = 0.7;
        }

        panel.Children.Add(block);
        return block;
    }

    private void RefreshGraphInspector()
    {
        if (_graph is not { } graph || _graphSelected is not { } index || index >= graph.Nodes.Count)
        {
            ShowGraphHelp();
            return;
        }

        var node = graph[index];
        var term = GraphGlossary.Describe(node.Class);
        var edits = GraphEdits();
        GraphInspector.Children.Clear();

        var chip = new Border { Background = CategoryBrush(term.Category), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1), HorizontalAlignment = HorizontalAlignment.Left };
        chip.Child = new TextBlock { Text = term.Name, FontSize = 11, Foreground = Brushes.White };
        GraphInspector.Children.Add(chip);
        AddText(GraphInspector, node.Name, 15, FontWeight.SemiBold);
        AddText(GraphInspector, term.Description, 11, muted: true);
        AddText(GraphInspector, $"{node.Path}   (node {node.Index}, {node.Class})", 10, muted: true);

        var state = NodeState(index);
        if (state != ChangeState.None)
        {
            var line = AddText(GraphInspector, state == ChangeState.Pending ? "Edited in this profile; the addon does not have it yet. Apply the profile in the Deliver tab." : "Edited in this profile; the addon has it.", 11);
            line.Foreground = StateBrush(state);
        }

        if (graph.HasChildren(node.Path))
        {
            var open = new Button { Content = "Open ⤵", HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => OpenGraphLevel(node.Path);
            GraphInspector.Children.Add(open);
        }

        if (_graphSelectedTransitions.Count > 1)
        {
            AddSection("This arrow stands for several transitions");
            foreach (var transition in _graphSelectedTransitions)
            {
                AddLinkButton(transition, transition == index ? "▶ " + graph[transition].Path : graph[transition].Path);
            }
        }

        if (node.PlaysData)
        {
            AddClipPicker(graph, node, edits);
        }

        if (node.Class == "TransitionNode")
        {
            AddTransitionDetails(graph, node, edits);
        }

        if (graph.Transitions.TryGetValue(index, out var transitions))
        {
            AddSection($"Transitions ({transitions.Count})");
            foreach (var transition in transitions)
            {
                AddLinkButton(transition.TransitionNode, GraphExplain.Transition(graph, transition, edits));
            }
        }

        // Transition settings are edited on each transition, where they can be told apart.
        var editable = node.Values.Where(value => value.Editable && !value.Path.Contains("m_transitionDefinitions", StringComparison.Ordinal)).ToList();
        if (editable.Count > 0)
        {
            AddSection("Values");
            foreach (var value in editable)
            {
                AddValueRow(graph, node, value.Path, value.Value, edits);
            }
        }

        if (node.Links.Count > 0)
        {
            AddSection("Uses");
            foreach (var link in node.Links.Where(link => link.Target < graph.Nodes.Count))
            {
                var target = graph[link.Target];
                AddLinkButton(link.Target, $"{GraphGlossary.Field(link.Field).Name}: {target.Name}  ({GraphGlossary.Describe(target.Class).Name})");
            }
        }

        var users = graph.Users(node).Take(20).ToList();
        if (users.Count > 0)
        {
            AddSection("Used by");
            foreach (var (user, link) in users)
            {
                AddLinkButton(user.Index, $"{user.Name}  ({GraphGlossary.Describe(user.Class).Name}, as {GraphGlossary.Field(link.Field).Name.ToLowerInvariant()})");
            }
        }
    }

    private void AddSection(string title)
    {
        GraphInspector.Children.Add(new Border
        {
            BorderBrush = (IBrush)this.FindResource("PanelBorderBrush")!,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(0, 4, 0, 0),
            Child = new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = 12 },
        });
    }

    private void AddLinkButton(int target, string text)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11 },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6, 2),
        };
        if (StateBrush(NodeState(target)) is { } brush)
        {
            button.BorderBrush = brush;
        }

        button.Click += (_, _) => GoToGraphNode(target);
        GraphInspector.Children.Add(button);
    }

    private void AddClipPicker(GraphDocument graph, GraphNode node, IReadOnlyDictionary<string, string> edits)
    {
        AddSection("Plays");
        var stock = graph.PlayedResource(node);
        var current = edits.GetValueOrDefault(Core.Graphs.GraphEdits.Key(node.Index, GraphDocument.ClipEditKey)) ?? stock;
        var editedClips = EditedClipPaths();
        // A weapon clip the addon's graph already plays as its own copy is that same choice: list it once.
        var graphClips = graph.Resources.Where(resource => resource.EndsWith(".vnmclip", StringComparison.Ordinal)).ToList();
        var weaponClips = (_weapon?.ClipPaths.Select(path => path[..^2]) ?? [])
            .Where(clip => !graphClips.Any(played => played != clip && Core.Graphs.GraphStatus.SameClip(played, clip)));
        if (current is not null && graphClips.FirstOrDefault(played => Core.Graphs.GraphStatus.SameClip(played, current)) is { } same)
        {
            current = same; // An older edit naming CS2's path shows as the addon's copy it plays.
        }

        var options = graphClips
            .Concat(weaponClips)
            .Concat(current is null ? [] : [current])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        string Label(string clip) => System.IO.Path.GetFileNameWithoutExtension(clip) + (editedClips.Contains(clip) ? "  ✎ edited" : "") + (clip == stock ? $"  ({GraphBaseName})" : "");
        var combo = new ComboBox
        {
            ItemsSource = options.Select(Label).ToList(),
            SelectedIndex = current is null ? -1 : options.IndexOf(current),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            PlaceholderText = "No clip in this variation",
        };
        ToolTip.SetTip(combo, "Which clip this box plays for this weapon. The list holds the clips this graph already plays and the selected weapon's clips; ✎ marks clips you edited in Blender (they are delivered too).");
        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#E07070")), FontSize = 10, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedIndex >= 0 && options[combo.SelectedIndex] != current)
            {
                CommitGraphEdit(node.Index, GraphDocument.ClipEditKey, options[combo.SelectedIndex], error);
            }
        };
        GraphInspector.Children.Add(combo);
        GraphInspector.Children.Add(error);
        if (current is not null)
        {
            AddText(GraphInspector, current, 10, muted: true);
        }
    }

    private void AddTransitionDetails(GraphDocument graph, GraphNode node, IReadOnlyDictionary<string, string> edits)
    {
        foreach (var (machine, list) in graph.Transitions)
        {
            foreach (var transition in list.Where(transition => transition.TransitionNode == node.Index).Take(6))
            {
                AddSection("When");
                AddText(GraphInspector, GraphExplain.Transition(graph, transition, edits), 11);
                AddLinkButton(transition.ConditionNode, "Condition: " + GraphExplain.Condition(graph, transition.ConditionNode, edits));
                AddValueRow(graph, graph[machine], transition.Definition + ".m_bCanBeForced", transition.CanBeForced ? "true" : "false", GraphEdits());
            }
        }
    }

    /// <summary>
    /// Selector values for a node value: a fixed list for enum fields,
    /// true/false for flags, and for names (IDs, events, bone masks) every
    /// value the graph already uses for that field. Numbers get none.
    /// </summary>
    private static IReadOnlyList<string>? GraphChoices(GraphDocument graph, GraphNode node, string path)
    {
        if (GraphGlossary.ChoicesFor(node.Class, path) is { } fixedList)
        {
            return fixedList;
        }

        var type = node.Values.FirstOrDefault(value => value.Path == path)?.Type;
        if (type == ValveKeyValue.KVValueType.Boolean)
        {
            return ["true", "false"];
        }

        if (type != ValveKeyValue.KVValueType.String)
        {
            return null;
        }

        var key = GraphDocument.LeafKey(path);
        var used = graph.Nodes
            .SelectMany(other => other.Values)
            .Where(value => value.Type == ValveKeyValue.KVValueType.String && value.Value.Length > 0 && GraphDocument.LeafKey(value.Path) == key)
            .Select(value => value.Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return used.Count > 0 ? used : null;
    }

    private void AddValueRow(GraphDocument graph, GraphNode node, string path, string stock, IReadOnlyDictionary<string, string> edits)
    {
        var key = Core.Graphs.GraphEdits.Key(node.Index, path);
        var field = GraphGlossary.Field(path);
        var edited = edits.TryGetValue(key, out var editValue);
        var current = edited ? editValue! : stock;
        var state = edited ? EditState(key, editValue!) : ChangeState.None;

        var label = new TextBlock
        {
            Text = field.Name,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = edited ? FontWeight.SemiBold : FontWeight.Normal,
            Foreground = StateBrush(state) ?? Brushes.White,
        };
        ToolTip.SetTip(label, $"{path} - {field.Description}\nGame value: {stock}");

        var error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#E07070")), FontSize = 10, IsVisible = false, TextWrapping = TextWrapping.Wrap };
        Control editor;
        var choices = GraphChoices(graph, node, path);
        if (choices is not null)
        {
            // A selector you can also type into: picks commit at once, typed text on Enter or leaving the box.
            var list = choices.Contains(current) ? choices.ToList() : [.. choices, current];
            var combo = new ComboBox
            {
                ItemsSource = list,
                SelectedItem = current,
                IsEditable = true,
                Text = current,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = 11,
                MaxDropDownHeight = 400,
            };
            ToolTip.SetTip(combo, "Pick a value or type your own");
            combo.SelectionChanged += (_, _) =>
            {
                if (combo.SelectedItem is string chosen && chosen != current)
                {
                    CommitGraphEdit(node.Index, path, chosen, error);
                }
            };
            combo.LostFocus += (_, _) =>
            {
                if (combo.Text is { } typed && typed != current && combo.SelectedItem as string != typed)
                {
                    CommitGraphEdit(node.Index, path, typed, error);
                }
            };
            combo.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter && combo.Text is { } typed && typed != current)
                {
                    CommitGraphEdit(node.Index, path, typed, error);
                }
            };
            editor = combo;
        }
        else
        {
            var box = new TextBox { Text = current, Classes = { "filter" }, Margin = new Thickness(0), FontSize = 11 };
            box.LostFocus += (_, _) =>
            {
                if (box.Text != current)
                {
                    CommitGraphEdit(node.Index, path, box.Text ?? "", error);
                }
            };
            box.KeyDown += (_, args) =>
            {
                if (args.Key == Key.Enter)
                {
                    CommitGraphEdit(node.Index, path, box.Text ?? "", error);
                }
            };
            editor = box;
        }

        var reset = new Button { Content = "↺", Padding = new Thickness(5, 0), IsVisible = edited, FontSize = 11 };
        ToolTip.SetTip(reset, $"Back to {GraphBaseName}'s value ({stock})");
        reset.Click += (_, _) => CommitGraphEdit(node.Index, path, stock, error);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,Auto") };
        row.Children.Add(label);
        Grid.SetColumn(editor, 1);
        row.Children.Add(editor);
        Grid.SetColumn(reset, 2);
        row.Children.Add(reset);
        GraphInspector.Children.Add(row);
        GraphInspector.Children.Add(error);
    }
}
