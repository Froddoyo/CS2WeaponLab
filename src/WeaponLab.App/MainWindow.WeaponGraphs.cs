using WeaponLab.Core.Delivery;
using WeaponLab.Core.Profiles;

namespace WeaponLab.App;

/// <summary>
/// The outliner's Graphs node: the graphs a weapon's animations go through,
/// each coloured yellow (this weapon's change is not in the addon yet), green
/// (it is), or blue (the addon's version was changed for another weapon, or
/// is the addon's own). Clicking a graph opens it in the Graph tab.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>A graph the Graph tab opens the next time it refreshes.</summary>
    private string? _requestedGraph;

    private void FillGraphsNode(ExplorerNode node, WeaponLab.Core.Weapons.WeaponEntry weapon, bool colour, AddonManifest? manifest, IReadOnlySet<string>? pendingWeapons = null)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var pending = AspectStatus(weapon, StatusAspect.Animations).State == WeaponState.NeedsApply;
        var graphs = WeaponGraphs.For(connection, _profile, Target(), manifest, weapon.Id, pending, pendingWeapons);
        node.Children.Clear();
        foreach (var graph in graphs)
        {
            var link = graph.Link switch
            {
                GraphLink.Own => "own",
                GraphLink.Shared => "shared",
                _ => "routes to it",
            };
            node.Children.Add(new ExplorerNode
            {
                Kind = ExplorerNodeKind.Graph,
                Label = graph.Name,
                Detail = link,
                Weapon = weapon,
                GraphPath = graph.Graph,
                State = _profile.StockGraphs.Contains(graph.Graph) ? WeaponState.NeedsApply : colour ? graph.State : WeaponState.Untouched,
                StatusTip = _profile.StockGraphs.Contains(graph.Graph) ? "Set to CS2 default: rebuilt from CS2's graph on the next push (this profile's clips stay routed)." : graph.Tip,
            });
        }

        // The node takes its most important child colour: yellow, then teal, then green, then blue.
        var states = node.Children.Select(child => child.State).ToList();
        node.State = states.Contains(WeaponState.NeedsApply) ? WeaponState.NeedsApply
            : states.Contains(WeaponState.PendingElsewhere) ? WeaponState.PendingElsewhere
            : states.Contains(WeaponState.Applied) ? WeaponState.Applied
            : states.Contains(WeaponState.ChangedElsewhere) ? WeaponState.ChangedElsewhere
            : WeaponState.Untouched;
        node.Detail = graphs.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        node.StatusTip = node.State switch
        {
            WeaponState.NeedsApply => "A graph will change for this weapon on the next apply.",
            WeaponState.Applied => "The addon's graphs carry this weapon's change.",
            WeaponState.PendingElsewhere => "A graph this weapon plays through changes on the next push for another weapon.",
            WeaponState.ChangedElsewhere => "Some of these graphs are changed in the addon for another weapon, or are the addon's own.",
            _ => "",
        };
    }

    private static AddonManifest? TryReadManifest(AddonTarget target)
    {
        try
        {
            return AddonManifest.Read(target);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Opens a graph in the Graph tab.</summary>
    private void OpenGraphInEditor(string graph)
    {
        _requestedGraph = graph;
        if (ToolTabs.SelectedIndex == GraphTool)
        {
            RefreshCurrentTool();
        }
        else
        {
            ToolTabs.SelectedIndex = GraphTool;
        }
    }
}
