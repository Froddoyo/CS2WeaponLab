using System.Globalization;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Graphs;

public enum VariationKind
{
    /// <summary>Draws: deploy_variation is rolled again on every draw.</summary>
    Draw,

    /// <summary>Inspects: inspect_variation is rolled again on every inspect.</summary>
    Inspect,
}

/// <summary>One option of a weighted clip selector: the clip it plays (null = none, skipped in game) and its weight.</summary>
/// <param name="Clip">A resource path without <c>_c</c>, or null for an empty option.</param>
public sealed record VariationOption(int Position, string? Clip, double Weight);

/// <summary>
/// A set of weighted clip selectors (ParameterizedClipSelectorNode) of one weapon graph that pick between the same
/// options, e.g. the draw (Deploy0 and Deploy2) or the inspect's Default state (inspect0 and inspect1). An edit
/// goes to every selector of the group.
/// </summary>
/// <param name="Name">Draw, or Inspect · Default / Draw / Transition Fix….</param>
/// <param name="Selectors">The selector nodes.</param>
/// <param name="OptionNodes">Per selector, the clip node of each option.</param>
public sealed record VariationGroup(VariationKind Kind, string Graph, string Name, IReadOnlyList<int> Selectors, IReadOnlyList<IReadOnlyList<int>> OptionNodes, IReadOnlyList<VariationOption> Options);

/// <summary>
/// The random-reload recipe PC2 verified in game on the AK (2026-10-29): a second draw selector that duplicates the
/// first is freed (its state plays the first one), its two options get two reload clips, its pick follows the
/// reserve ammo (weapon_ammo_reserve, which changes on every reload, so it looks random), and the reload selector's
/// first option plays it.
/// </summary>
/// <param name="ReloadSelector">The Reload0 ClipSelectorNode.</param>
/// <param name="Spare">The duplicate draw selector that becomes the reload picker.</param>
/// <param name="SpareState">The state that played <paramref name="Spare"/>; it plays <paramref name="Keep"/> instead.</param>
/// <param name="Keep">The draw selector both draw states play afterwards.</param>
/// <param name="ReserveParameter">The weapon_ammo_reserve control parameter node.</param>
/// <param name="On">The profile (or the addon) already has it.</param>
public sealed record RandomReload(string Graph, int ReloadSelector, int Spare, int SpareState, int Keep, IReadOnlyList<int> SpareOptions, int ReserveParameter, bool On,
    IReadOnlyList<string?> Clips, IReadOnlyList<double> Weights);

/// <summary>
/// Variations (0.24): rare draws and inspects, and random reloads, as graph edits on a weapon's own graphs
/// (delivered like every graph edit). Weighted selectors pick an option as a fixed function of their parameter,
/// which the game rolls again per draw (deploy_variation) and per inspect (inspect_variation); see pc2/MEMORY
/// "WEIGHTED SELECTORS" (verified in game).
/// </summary>
public static class Variations
{
    private const string Selector = "ParameterizedClipSelectorNode";
    private const string Reserve = "weapon_ammo_reserve";
    private static readonly IReadOnlyDictionary<string, string> NoEdits = new Dictionary<string, string>();

    /// <summary>The weapon's own graphs (the ones that mostly play its clips): CS2's path and the addon's version when it has one (same layout), else CS2's.</summary>
    public static IReadOnlyList<(string Path, GraphDocument Document)> Graphs(GameConnection connection, Profile profile, AddonTarget? target, WeaponEntry weapon)
    {
        var result = new List<(string Path, GraphDocument Document)>();
        foreach (var info in GraphTree.Load(connection.Archive).Values.Where(info => DeliveryPlanner.GraphOwners(connection, info).Contains(weapon.Id)))
        {
            var document = GraphStatus.ReadDelivered(target, info.Path) is { } delivered && delivered.Nodes.Count == CountNodes(connection, info.Path)
                ? delivered
                : GraphDocument.Read(connection.Archive.ReadBytes(info.Path + "_c"), info.Path);
            result.Add((info.Path, document));
        }

        return result;
    }

    private static int CountNodes(GameConnection connection, string graph) => GraphDocument.Read(connection.Archive.ReadBytes(graph + "_c"), graph).Nodes.Count;

    /// <summary>The weapon's draw and inspect selector groups, with the profile's pending edits applied to what they show.</summary>
    public static IReadOnlyList<VariationGroup> Groups(IReadOnlyList<(string Path, GraphDocument Document)> graphs, Profile profile)
    {
        var groups = new List<VariationGroup>();
        var gunGraphs = graphs.Count(item => !item.Path.Contains("viewmodel_inspects", StringComparison.Ordinal));
        foreach (var (path, graph) in graphs)
        {
            // A weapon with more than one graph of a kind (the SSG08's legacy one) names each by its variation.
            var variation = Path.GetFileNameWithoutExtension(path)[(Path.GetFileNameWithoutExtension(path).LastIndexOf('+') + 1)..];
            var several = path.Contains("viewmodel_inspects", StringComparison.Ordinal) ? graphs.Count - gunGraphs > 1 : gunGraphs > 1;
            var suffix = several ? " (" + variation + ")" : "";
            var edits = profile.Graphs.GetValueOrDefault(path) ?? new SortedDictionary<string, string>(StringComparer.Ordinal);
            var inspects = path.Contains("viewmodel_inspects", StringComparison.Ordinal);
            var selectors = graph.Nodes.Where(node => node.Class == Selector && (inspects || node.Path.Contains("/Deploying/", StringComparison.Ordinal)))
                .Where(node => Parent(graph, node, edits) is not null) // A selector no state plays (freed for a random reload) is not a draw any more.
                .ToList();
            foreach (var family in selectors.GroupBy(node => inspects ? StateName(graph, node, edits) : "Draw"))
            {
                var members = family.Where(node => OptionNodes(node).Count == OptionNodes(family.First()).Count).ToList();
                var first = members[0];
                var options = OptionNodes(first)
                    .Select((option, position) => new VariationOption(position, ClipOf(graph, option, edits), WeightOf(first, position, edits)))
                    .ToList();
                groups.Add(new VariationGroup(inspects ? VariationKind.Inspect : VariationKind.Draw, path, (inspects ? "Inspect · " + family.Key : "Draw") + suffix,
                    members.Select(node => node.Index).ToList(), members.Select(OptionNodes).ToList(), options));
            }
        }

        return groups;
    }

    /// <summary>The graph edits that give a group's option a clip and a weight (every selector of the group). CS2 stores weights as whole numbers.</summary>
    /// <param name="clip">A clip resource path without <c>_c</c>; null keeps the option's clip.</param>
    public static IReadOnlyDictionary<string, string> EditsFor(VariationGroup group, int position, string? clip, double weight)
    {
        var edits = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var member = 0; member < group.Selectors.Count; member++)
        {
            edits[GraphEdits.Key(group.Selectors[member], $"m_optionWeights[{position}]")] = Math.Round(weight).ToString(CultureInfo.InvariantCulture);
            if (clip is not null)
            {
                edits[GraphEdits.Key(group.OptionNodes[member][position], GraphDocument.ClipEditKey)] = clip;
            }
        }

        return edits;
    }

    /// <summary>The random-reload recipe for the weapon's gun graph, or null when its graph does not have the pieces.</summary>
    public static RandomReload? RandomReloadFor(IReadOnlyList<(string Path, GraphDocument Document)> graphs, Profile profile)
    {
        foreach (var (path, graph) in graphs.Where(item => !item.Path.Contains("viewmodel_inspects", StringComparison.Ordinal)))
        {
            var edits = profile.Graphs.GetValueOrDefault(path) ?? new SortedDictionary<string, string>(StringComparer.Ordinal);
            var reload = graph.Nodes.FirstOrDefault(node => node.Class == "ClipSelectorNode" && node.Path.EndsWith("/Reload/SM/Reload0/ReloadSelector", StringComparison.Ordinal));
            var reserve = graph.Nodes.FirstOrDefault(node => node.Name == Reserve && node.Class.StartsWith("ControlParameter", StringComparison.Ordinal));
            var draws = graph.Nodes.Where(node => node.Class == Selector && node.Path.Contains("/Deploying/", StringComparison.Ordinal)).ToList();
            if (reload is null || reserve is null || draws.Count < 2)
            {
                continue;
            }

            // The spare: a draw selector that plays the same as the first one (a duplicate state), so its state can play the first.
            var keep = draws[0];
            var spare = draws.Skip(1).FirstOrDefault(node => OptionNodes(node).Count >= 2
                && OptionNodes(node).Select(option => ClipOf(graph, option, NoEdits)).SequenceEqual(OptionNodes(keep).Select(option => ClipOf(graph, option, NoEdits))));
            var spareState = spare is null ? null : graph.Nodes.Where(node => node.Links.Any(link => link.Field == "m_nChildNodeIdx" && link.Target == spare.Index)).ToList();
            if (spare is null || spareState is not { Count: 1 } || OptionNodes(spare).Any(option => !graph[option].PlaysData))
            {
                continue;
            }

            var options = OptionNodes(spare).Take(2).ToList();
            var on = Current(graph, reload.Index, "m_optionNodeIndices[0]", edits) == spare.Index.ToString(CultureInfo.InvariantCulture);
            return new RandomReload(path, reload.Index, spare.Index, spareState[0].Index, keep.Index, options, reserve.Index, on,
                options.Select(option => on ? ClipOf(graph, option, edits) : null).ToList(),
                [WeightOf(spare, 0, on ? edits : NoEdits), WeightOf(spare, 1, on ? edits : NoEdits)]);
        }

        return null;
    }

    /// <summary>The graph edits for a random reload between two clips (or, with <paramref name="on"/> false, the keys to drop to undo it).</summary>
    public static IReadOnlyDictionary<string, string> RandomReloadEdits(RandomReload recipe, string clipA, string clipB, double weightA, double weightB) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [GraphEdits.Key(recipe.SpareState, "m_nChildNodeIdx")] = recipe.Keep.ToString(CultureInfo.InvariantCulture),
        [GraphEdits.Key(recipe.Spare, "m_parameterNodeIdx")] = recipe.ReserveParameter.ToString(CultureInfo.InvariantCulture),
        [GraphEdits.Key(recipe.Spare, "m_optionWeights[0]")] = Math.Round(weightA).ToString(CultureInfo.InvariantCulture),
        [GraphEdits.Key(recipe.Spare, "m_optionWeights[1]")] = Math.Round(weightB).ToString(CultureInfo.InvariantCulture),
        [GraphEdits.Key(recipe.SpareOptions[0], GraphDocument.ClipEditKey)] = clipA,
        [GraphEdits.Key(recipe.SpareOptions[1], GraphDocument.ClipEditKey)] = clipB,
        [GraphEdits.Key(recipe.ReloadSelector, "m_optionNodeIndices[0]")] = recipe.Spare.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>The keys <see cref="RandomReloadEdits"/> writes, to take a pending random reload out of the profile again.</summary>
    public static IReadOnlyList<string> RandomReloadKeys(RandomReload recipe) => [.. RandomReloadEdits(recipe, "", "", 1, 1).Keys];

    private static IReadOnlyList<int> OptionNodes(GraphNode selector) =>
        selector.Values.Where(value => value.Path.StartsWith("m_optionNodeIndices[", StringComparison.Ordinal))
            .Select(value => int.Parse(value.Value, CultureInfo.InvariantCulture)).ToList();

    private static double WeightOf(GraphNode selector, int position, IReadOnlyDictionary<string, string> edits)
    {
        var key = $"m_optionWeights[{position}]";
        var text = edits.GetValueOrDefault(GraphEdits.Key(selector.Index, key)) ?? selector.Value(key);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight) ? weight : 0;
    }

    private static string? ClipOf(GraphDocument graph, int option, IReadOnlyDictionary<string, string> edits)
    {
        if (edits.GetValueOrDefault(GraphEdits.Key(option, GraphDocument.ClipEditKey)) is { } edited)
        {
            return edited;
        }

        var slot = graph[option].DataSlot;
        return slot >= 0 && slot < graph.Resources.Count ? graph.Resources[slot] : null;
    }

    private static string? Current(GraphDocument graph, int node, string path, IReadOnlyDictionary<string, string> edits) =>
        edits.GetValueOrDefault(GraphEdits.Key(node, path)) ?? graph[node].Value(path);

    /// <summary>The state node that plays a selector (its m_nChildNodeIdx, with pending edits), or null.</summary>
    private static GraphNode? Parent(GraphDocument graph, GraphNode selector, IReadOnlyDictionary<string, string> edits) =>
        graph.Nodes.FirstOrDefault(node => node.Links.Any(link => link.Field == "m_nChildNodeIdx")
            && (Current(graph, node.Index, "m_nChildNodeIdx", edits) ?? "") == selector.Index.ToString(CultureInfo.InvariantCulture));

    private static string StateName(GraphDocument graph, GraphNode selector, IReadOnlyDictionary<string, string> edits) =>
        Parent(graph, selector, edits)?.Name ?? selector.Parent;
}
