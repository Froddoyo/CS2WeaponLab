using System.Text.RegularExpressions;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Checks;

/// <summary>
/// What an error, warning or check is about, for its Show button: a graph
/// node, a weapon's clip, its vdata, or just the weapon.
/// </summary>
/// <param name="WeaponId">The weapon, when the message names one or one of its clips.</param>
public sealed record CheckTarget(string? WeaponId)
{
    /// <summary>The clip's resource path, as in <see cref="WeaponEntry.ClipPaths"/>.</summary>
    public string? ClipPath { get; init; }

    /// <summary>CS2's graph path (without <c>_c</c>), also when the message names the addon's custom copy.</summary>
    public string? Graph { get; init; }

    public int? Node { get; init; }

    /// <summary>The message is about a vdata block of the weapon.</summary>
    public bool VData { get; init; }

    /// <summary>Button tooltip: where Show goes.</summary>
    public string Describe() =>
        Graph is not null ? $"Open {Path.GetFileName(Graph)}{(Node is { } node ? $" at node {node}" : "")} in the Graphs tab"
        : ClipPath is not null ? $"Open {Blender.RigPackage.ClipId(ClipPath)} in Anim Events"
        : VData ? $"Open {WeaponId} in the VData tab"
        : $"Select {WeaponId}";
}

/// <summary>Finds what a message from delivery or a check points at, by the names in it.</summary>
public static partial class CheckTargets
{
    [GeneratedRegex(@"Graph node (\d+)\b.*?\bin (\S+?\.vnmgraph\S*)", RegexOptions.Singleline)]
    private static partial Regex GraphNodePattern();

    [GeneratedRegex(@"[A-Za-z0-9_]+")]
    private static partial Regex WordPattern();

    /// <param name="graphs">CS2's graph paths (without <c>_c</c>).</param>
    /// <param name="blocksOf">A weapon's vdata block names (e.g. weapon_ssg08), to point vdata messages at the weapon.</param>
    /// <param name="subject">A warning's subject (a weapon id or vdata block), tried first.</param>
    public static CheckTarget? Find(string message, IReadOnlyList<WeaponEntry> weapons, IEnumerable<string> graphs,
        Func<WeaponEntry, IReadOnlyList<string>>? blocksOf = null, string? subject = null)
    {
        if (GraphNodePattern().Match(message) is { Success: true } graphMatch)
        {
            var named = graphMatch.Groups[2].Value.TrimEnd('.', ',', ';', ')', '"', '\'');
            named = named.EndsWith("_c", StringComparison.Ordinal) ? named[..^2] : named;
            var graph = graphs.FirstOrDefault(path => path == named || TryCustom(path) == named);
            if (graph is not null)
            {
                return new CheckTarget(null) { Graph = graph, Node = int.Parse(graphMatch.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) };
            }
        }

        // The longest clip name in the message wins, so "shoot1_ssg08_lgcy" is not read as "shoot1_ssg08".
        var words = WordPattern().Matches(subject + " " + message).Select(match => match.Value).ToHashSet(StringComparer.Ordinal);
        var clip = weapons
            .SelectMany(weapon => weapon.ClipPaths.Select(path => (Weapon: weapon, Path: path, Id: Blender.RigPackage.ClipId(path))))
            .Where(item => words.Contains(item.Id))
            .OrderByDescending(item => item.Id.Length)
            .FirstOrDefault();
        if (clip.Weapon is not null)
        {
            return new CheckTarget(clip.Weapon.Id) { ClipPath = clip.Path };
        }

        var weaponNamed = weapons.Where(weapon => (subject is not null && weapon.Id == subject) || words.Contains(weapon.Id)).OrderByDescending(weapon => weapon.Id == subject).FirstOrDefault();
        if (weaponNamed is not null)
        {
            return new CheckTarget(weaponNamed.Id);
        }

        if (blocksOf is not null)
        {
            var byBlock = weapons.FirstOrDefault(weapon => blocksOf(weapon).Any(block => block == subject || words.Contains(block)));
            if (byBlock is not null)
            {
                return new CheckTarget(byBlock.Id) { VData = true };
            }
        }

        return null;
    }

    private static string? TryCustom(string graph)
    {
        try
        {
            return GraphTree.CustomGraphPath(graph);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
