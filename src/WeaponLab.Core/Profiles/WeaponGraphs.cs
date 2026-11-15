using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Profiles;

/// <summary>How a graph relates to a weapon.</summary>
public enum GraphLink
{
    /// <summary>The weapon's own graph: it plays mostly this weapon's clips (viewmodel_gun+ak47).</summary>
    Own,

    /// <summary>Plays some of the weapon's clips but belongs to other weapons.</summary>
    Shared,

    /// <summary>Routes to the weapon's graphs without playing its clips (the root viewmodel graph).</summary>
    Route,
}

/// <param name="Graph">The game graph's resource path (without <c>_c</c>).</param>
/// <param name="State">Yellow: this weapon's change is not in the addon yet. Green: it is. Blue: the addon's
/// version was changed for another weapon or by the addon itself. Untouched: CS2's graph.</param>
public sealed record WeaponGraph(string Graph, GraphLink Link, WeaponState State, string Tip)
{
    public string Name => Path.GetFileNameWithoutExtension(Graph);
}

/// <summary>
/// The graphs a weapon's animations go through, for the outliner: the graphs
/// that play its clips and the ones that route to them, each coloured by
/// whether the addon's custom version carries this weapon's change.
/// </summary>
public static class WeaponGraphs
{
    /// <param name="weaponPending">The weapon has clip changes the addon does not have yet (its graphs will change on apply).</param>
    /// <param name="pendingWeapons">Every weapon with work not pushed yet (0.24.1): a shared or routing graph they will change shows teal on the others.</param>
    public static IReadOnlyList<WeaponGraph> For(GameConnection connection, Profile profile, AddonTarget? target, AddonManifest? manifest, string weaponId, bool weaponPending,
        IReadOnlySet<string>? pendingWeapons = null)
    {
        var weapon = connection.FindWeapon(weaponId);
        if (weapon is null)
        {
            return [];
        }

        var clips = weapon.ClipPaths.Select(path => path[..^2]).ToHashSet(StringComparer.Ordinal);
        var playing = connection.Graphs.Values.Where(graph => graph.Resources.Any(clips.Contains)).Select(graph => graph.Path).ToHashSet(StringComparer.Ordinal);

        // Graphs that reference the ones playing the weapon, up to the root.
        var routing = new HashSet<string>(StringComparer.Ordinal);
        var frontier = new Queue<string>(playing);
        while (frontier.Count > 0)
        {
            var child = frontier.Dequeue();
            foreach (var parent in connection.Graphs.Values.Where(graph => graph.Resources.Contains(child) && !playing.Contains(graph.Path)))
            {
                if (routing.Add(parent.Path))
                {
                    frontier.Enqueue(parent.Path);
                }
            }
        }

        // The live addon: the weapon's own custom paths (its clips and own graph copies) that a routing graph may point at.
        var mine = weapon.ClipPaths.Select(path => GraphTree.CustomClipPath(weaponId, path[..^2]))
            .Concat(playing.Where(graph => DeliveryPlanner.GraphOwners(connection, connection.Graphs[graph]).Contains(weaponId)).Select(GraphTree.CustomGraphPath))
            .ToHashSet(StringComparer.Ordinal);

        var deliveredFor = manifest?.Clips.Where(item => item.Value.Count > 0).Select(item => item.Key).ToList() ?? [];
        var result = new List<WeaponGraph>();
        foreach (var graph in playing.Concat(routing).Distinct().OrderBy(path => routing.Contains(path) ? 1 : 0).ThenBy(path => path, StringComparer.Ordinal))
        {
            var owners = DeliveryPlanner.GraphOwners(connection, connection.Graphs[graph]);
            var link = routing.Contains(graph) ? GraphLink.Route : owners.Contains(weaponId) ? GraphLink.Own : GraphLink.Shared;
            var (state, tip) = State(graph, link, owners);
            result.Add(new WeaponGraph(graph, link, state, tip));
        }

        return result;

        (WeaponState, string) State(string graph, GraphLink link, IReadOnlyList<string> owners)
        {
            var custom = GraphTree.CustomGraphPath(graph);
            var file = target is null ? null : Path.Combine(target.GamePath, custom.Replace('/', Path.DirectorySeparatorChar) + "_c");
            var edits = profile.Graphs.GetValueOrDefault(graph);
            var editsPending = edits is { Count: > 0 } && link == GraphLink.Own;
            if (weaponPending || editsPending)
            {
                return (WeaponState.NeedsApply, $"{Path.GetFileName(custom)} will change for {weaponId} on the next apply.");
            }

            // Teal (0.24.1): the next push changes this shared graph for other weapons, while this one has nothing pending.
            if (link != GraphLink.Own)
            {
                var pendingOthers = (pendingWeapons ?? new HashSet<string>())
                    .Where(id => id != weaponId && (link == GraphLink.Route || owners.Contains(id)))
                    .OrderBy(id => id, StringComparer.Ordinal).ToList();
                if (pendingOthers.Count > 0 || edits is { Count: > 0 } || profile.StockGraphs.Contains(graph))
                {
                    return (WeaponState.PendingElsewhere, $"{Path.GetFileName(custom)} changes on the next push for "
                        + (pendingOthers.Count > 0 ? string.Join(", ", pendingOthers.Take(6)) : "another weapon's graph edits") + $", not {weaponId}.");
                }
            }

            if (file is null || !File.Exists(file))
            {
                return (WeaponState.Untouched, "CS2's graph; the addon has no custom version.");
            }

            if (link == GraphLink.Own)
            {
                return (WeaponState.Applied, $"The addon has its own {Path.GetFileName(custom)} for {weaponId}.");
            }

            // A shared or routing graph is green when the addon's copy points at this weapon's custom work, blue when its changes are for others.
            IReadOnlyList<string> resources;
            try
            {
                resources = Graphs.GraphDocument.Read(File.ReadAllBytes(file), custom + "_c").Resources;
            }
            catch (InvalidDataException)
            {
                resources = [];
            }

            if (resources.Any(mine.Contains))
            {
                return (WeaponState.Applied, $"{Path.GetFileName(custom)} in the addon plays {weaponId}'s custom work.");
            }

            var others = link == GraphLink.Route ? deliveredFor.Where(id => id != weaponId).ToList() : owners.Where(id => id != weaponId).ToList();
            return (WeaponState.ChangedElsewhere, $"{Path.GetFileName(custom)} is changed in the addon for {(others.Count == 0 ? "other weapons" : string.Join(", ", others))}, not {weaponId}.");
        }
    }
}
