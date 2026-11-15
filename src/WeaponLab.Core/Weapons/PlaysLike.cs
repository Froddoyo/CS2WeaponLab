using System.Runtime.CompilerServices;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Graphs;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Weapons;

/// <summary>A gun in CS2's root viewmodel graph: its state and the anim classes that select it.</summary>
/// <param name="Group">The group state it is in, e.g. Shotguns.</param>
/// <param name="Name">The gun state's name, e.g. Nova.</param>
/// <param name="AnimClasses">The <c>weapon_type</c> values that pick it, e.g. weapon_nova.</param>
public sealed record GunState(string Group, string Name, IReadOnlyList<string> AnimClasses);

/// <summary>A group of the root graph's WPN state machine, picked by weapon category.</summary>
/// <param name="Categories">The <c>weapon_category</c> values that pick it, e.g. weapon_category_shotgun.</param>
/// <param name="Default">The gun the group starts on (its state machine's default state).</param>
public sealed record GunGroup(string Name, IReadOnlyList<string> Categories, IReadOnlyList<GunState> Guns, GunState? Default);

/// <summary>
/// How CS2's root viewmodel graph picks the gun whose clips a weapon plays,
/// read from the graph (verified 2026-11-03): the WPN state machine enters a
/// group by <c>weapon_category</c> (which follows the vdata's <c>m_WeaponType</c>),
/// and the group's own state machine picks the gun by <c>weapon_type</c> (the anim
/// class). The game's gameplay (ammo, reload timing) runs this stock graph even
/// when an addon's custom graph changes what the viewmodel shows.
/// </summary>
public sealed class GunSelection
{
    private const string CategoryParameter = "weapon_category";
    private const string ClassParameter = "weapon_type";

    private static readonly ConditionalWeakTable<GameArchive, GunSelection> Cache = new();

    private GunSelection(IReadOnlyList<GunGroup> groups)
    {
        Groups = groups;
    }

    public IReadOnlyList<GunGroup> Groups { get; }

    public GunGroup? GroupFor(string category) =>
        Groups.FirstOrDefault(group => group.Categories.Contains(category, StringComparer.Ordinal));

    /// <summary>CS2's own root graph, read once per game connection.</summary>
    public static GunSelection Of(GameConnection connection) =>
        Cache.GetValue(connection.Archive, archive => Read(GraphDocument.Read(archive.ReadBytes(GraphTree.Parent + "_c"), GraphTree.Parent + "_c")));

    public static GunSelection Read(GraphDocument root)
    {
        var parameters = root.Nodes
            .Where(node => node.Class.Contains("ControlParameterID", StringComparison.Ordinal) && node.Name is CategoryParameter or ClassParameter)
            .ToDictionary(node => node.Index, node => node.Name);
        var comparisons = new Dictionary<int, (string Parameter, IReadOnlyList<string> Ids)>();
        foreach (var node in root.Nodes.Where(node => node.Class == "IDComparisonNode"))
        {
            if (int.TryParse(node.Value("m_nInputValueNodeIdx"), out var input) && parameters.TryGetValue(input, out var parameter))
            {
                comparisons[node.Index] = (parameter, node.Values.Where(value => value.Path.StartsWith("m_comparisionIDs", StringComparison.Ordinal)).Select(value => value.Value).ToList());
            }
        }

        // Each state of a selecting machine has an entry condition that compares one parameter.
        var categoryStates = new List<(GraphNode State, IReadOnlyList<string> Ids)>();
        var gunMachines = new List<(GraphNode Machine, List<(GraphNode State, IReadOnlyList<string> Ids)> States)>();
        foreach (var machine in root.Nodes.Where(node => node.Class == "StateMachineNode"))
        {
            var states = new List<(GraphNode State, string Parameter, IReadOnlyList<string> Ids)>();
            foreach (var (state, entry) in StatesOf(machine))
            {
                var compared = Reachable(root, entry).Where(comparisons.ContainsKey).Select(index => comparisons[index]).ToList();
                if (compared.Count > 0 && compared.All(item => item.Parameter == compared[0].Parameter))
                {
                    states.Add((root[state], compared[0].Parameter, compared.SelectMany(item => item.Ids).Distinct().ToList()));
                }
            }

            if (states.Count > 0 && states.All(state => state.Parameter == CategoryParameter))
            {
                categoryStates.AddRange(states.Select(state => (state.State, state.Ids)));
            }
            else if (states.Count > 0 && states.All(state => state.Parameter == ClassParameter))
            {
                gunMachines.Add((machine, states.Select(state => (state.State, state.Ids)).ToList()));
            }
        }

        var groups = new List<GunGroup>();
        foreach (var (groupState, categories) in categoryStates)
        {
            var (machine, gunStates) = gunMachines.FirstOrDefault(item => item.Machine.Path.StartsWith(groupState.Path + "/", StringComparison.Ordinal));
            if (machine is null)
            {
                groups.Add(new GunGroup(groupState.Name, categories, [], null));
                continue;
            }

            var guns = gunStates.Select(state => new GunState(groupState.Name, state.State.Name, state.Ids)).ToList();
            var fallback = root.DefaultStates.TryGetValue(machine.Index, out var node) ? guns.FirstOrDefault(gun => gun.Name == root[node].Name) : null;
            groups.Add(new GunGroup(groupState.Name, categories, guns, fallback));
        }

        return new GunSelection(groups);
    }

    /// <summary>A machine's (state node, entry condition node) pairs, by state position.</summary>
    private static IEnumerable<(int State, int Entry)> StatesOf(GraphNode machine)
    {
        var states = new SortedDictionary<int, (int State, int Entry)>();
        foreach (var value in machine.Values)
        {
            var match = System.Text.RegularExpressions.Regex.Match(value.Path, @"^m_stateDefinitions\[(\d+)\]\.(m_nStateNodeIdx|m_nEntryConditionNodeIdx)$");
            if (!match.Success || !int.TryParse(value.Value, out var target))
            {
                continue;
            }

            var position = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            var current = states.TryGetValue(position, out var known) ? known : (State: -1, Entry: -1);
            states[position] = match.Groups[2].Value == "m_nStateNodeIdx" ? (target, current.Entry) : (current.State, target);
        }

        return states.Values.Where(pair => pair.State >= 0 && pair.Entry >= 0);
    }

    /// <summary>Nodes a condition reads, a few links deep (conditions can be and/or/not trees).</summary>
    private static IEnumerable<int> Reachable(GraphDocument graph, int start, int depth = 8)
    {
        var seen = new HashSet<int>();
        var pending = new Stack<(int Node, int Depth)>();
        pending.Push((start, 0));
        while (pending.Count > 0)
        {
            var (node, level) = pending.Pop();
            if (node < 0 || node >= graph.Nodes.Count || level > depth || !seen.Add(node))
            {
                continue;
            }

            yield return node;
            foreach (var link in graph[node].Links)
            {
                pending.Push((link.Target, level + 1));
            }
        }
    }
}

/// <summary>Which CS2 gun a weapon's gameplay follows, and what that means for the user.</summary>
/// <param name="Block">The vdata block, e.g. weapon_ssg08.</param>
/// <param name="Gun">The gun whose clips the game runs: the listed one, or the group's default when the class is not listed.</param>
/// <param name="Listed">The anim class is one of the group's guns. When it is not, the group keeps playing the gun of that group the player held last (verified on PC2 2026-11-03), so <see cref="Gun"/> holds only until another one is used.</param>
/// <param name="ScopeHidden">The weapon zooms but its type is not the sniper rifle type, so CS2 draws no scope view (verified on PC2 2026-11-03).</param>
public sealed record PlaysLikeResult(string Block, string WeaponType, string AnimClass, GunGroup? Group, GunState? Gun, bool Listed, bool ScopeHidden)
{
    /// <summary>"the Nova", or "the shotgun the player held last (the Nova until another is used)".</summary>
    public string Describe()
    {
        if (Group is null || Gun is null)
        {
            return $"no gun CS2's graph knows ({WeaponType} has no group)";
        }

        return Listed ? $"the {Gun.Name}" : $"the {PlaysLike.Noun(Group)} the player held last (the {Gun.Name} until another is used)";
    }

    /// <summary>Plain sentences about what this costs the user; empty when nothing needs saying.</summary>
    public IReadOnlyList<string> Warnings()
    {
        var warnings = new List<string>();
        if (Group is null)
        {
            warnings.Add($"CS2's graph has no group for {WeaponType}, so the game may not animate or reload it properly.");
        }
        else if (!Listed && Gun is not null)
        {
            var classes = string.Join(", ", Group.Guns.SelectMany(gun => gun.AnimClasses));
            warnings.Add($"Its anim class {AnimClass} is not one of the {PlaysLike.Noun(Group)} group's ({classes}), so its reload and ammo timing follow whichever {PlaysLike.Noun(Group)} the player held last. Timing here uses the {Gun.Name}, the group's first.");
        }

        if (ScopeHidden)
        {
            warnings.Add("It zooms, but CS2 draws the scope view only for the sniper rifle weapon type: in game the zoom happens without a scope.");
        }

        return warnings;
    }
}

public static class PlaysLike
{
    public const string WeaponTypeKey = "m_WeaponType";
    public const string ZoomLevelsKey = "m_nZoomLevels";
    public const string SniperType = "WEAPONTYPE_SNIPER_RIFLE";

    /// <summary>
    /// m_WeaponType → the root graph's weapon_category. Inferred from the
    /// graph's groups and checked against every stock weapon (each one's anim
    /// class is listed in its type's group; test GunSelection...).
    /// </summary>
    private static readonly Dictionary<string, string> Categories = new(StringComparer.Ordinal)
    {
        ["WEAPONTYPE_PISTOL"] = "weapon_category_pistol",
        ["WEAPONTYPE_TASER"] = "weapon_category_pistol",
        ["WEAPONTYPE_KNIFE"] = "weapon_category_knife",
        ["WEAPONTYPE_RIFLE"] = "weapon_category_rifle",
        ["WEAPONTYPE_SUBMACHINEGUN"] = "weapon_category_smg",
        ["WEAPONTYPE_SHOTGUN"] = "weapon_category_shotgun",
        ["WEAPONTYPE_MACHINEGUN"] = "weapon_category_machinegun",
        ["WEAPONTYPE_GRENADE"] = "weapon_category_grenade",
        [SniperType] = "weapon_category_sniper",
        ["WEAPONTYPE_C4"] = "weapon_category_equipment",
        ["WEAPONTYPE_STACKABLEITEM"] = "weapon_category_equipment",
        ["WEAPONTYPE_EQUIPMENT"] = "weapon_category_equipment",
    };

    private static readonly Dictionary<string, string> Nouns = new(StringComparer.Ordinal)
    {
        ["Pistols"] = "pistol",
        ["Knives"] = "knife",
        ["Rifles"] = "rifle",
        ["SMG"] = "SMG",
        ["Shotguns"] = "shotgun",
        ["MG"] = "machine gun",
        ["Grenades"] = "grenade",
        ["SniperRifles"] = "sniper rifle",
        ["Equipment"] = "equipment",
    };

    private static readonly ConditionalWeakTable<GameArchive, Dictionary<string, WeaponEntry?>> GunWeapons = new();

    public static string? CategoryOf(string weaponType) => Categories.GetValueOrDefault(weaponType);

    public static string Noun(GunGroup group) => Nouns.GetValueOrDefault(group.Name, group.Name);

    public static PlaysLikeResult For(GunSelection selection, VDataBlock block)
    {
        var weaponType = block.Value(WeaponTypeKey) ?? "";
        var animClass = WeaponSlots.SlotOf(block) ?? "";
        var group = CategoryOf(weaponType) is { } category ? selection.GroupFor(category) : null;
        var listed = group?.Guns.FirstOrDefault(gun => gun.AnimClasses.Contains(animClass, StringComparer.Ordinal));
        var zooms = int.TryParse(block.Value(ZoomLevelsKey), out var levels) && levels > 0;
        return new PlaysLikeResult(block.Name, weaponType, animClass, group, listed ?? group?.Default, listed is not null, zooms && weaponType != SniperType);
    }

    /// <summary>
    /// What each weapon playing this weapon's animation slot plays like: the
    /// blocks whose class (or anim class) is the slot's, from the addon's vdata
    /// where it has the block.
    /// </summary>
    public static IReadOnlyList<PlaysLikeResult> ForSlot(GameConnection connection, WeaponEntry weapon, VDataFile? addonVData, string? addonName)
    {
        var slot = WeaponSlots.For(connection, weapon, addonVData, addonName);
        var stock = connection.ReadVData();
        var selection = GunSelection.Of(connection);
        return slot.PlayedBy
            .Select(player => addonVData?[player.Block] ?? stock[player.Block])
            .OfType<VDataBlock>()
            .Select(block => For(selection, block))
            .ToList();
    }

    /// <summary>The stock weapon whose clips a gun state plays (Nova → rifle_nova), or null.</summary>
    public static WeaponEntry? WeaponOf(GameConnection connection, GunState gun)
    {
        var known = GunWeapons.GetValue(connection.Archive, _ => new Dictionary<string, WeaponEntry?>(StringComparer.Ordinal));
        var key = gun.Group + "/" + gun.Name;
        lock (known)
        {
            if (known.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var stock = connection.ReadVData();
        var found = connection.Weapons.FirstOrDefault(weapon => connection.VDataBlocksFor(weapon)
            .Any(name => stock[name] is { } block && gun.AnimClasses.Contains(WeaponSlots.SlotOf(block) ?? "", StringComparer.Ordinal)));
        lock (known)
        {
            known[key] = found;
        }

        return found;
    }

    /// <summary>
    /// The gun's clip in the same role as one of this weapon's clips:
    /// reload_ssg08_lgcy → reload_nova (the _lgcy one when the gun has it).
    /// </summary>
    public static string? MatchingClip(WeaponEntry own, string clipPath, WeaponEntry gun)
    {
        var name = ClipName(clipPath);
        var legacy = name.EndsWith(LegacySuffix, StringComparison.Ordinal);
        var plain = legacy ? name[..^LegacySuffix.Length] : name;
        var ownSuffix = "_" + Suffix(own);
        if (!plain.EndsWith(ownSuffix, StringComparison.Ordinal))
        {
            return null;
        }

        var role = plain[..^ownSuffix.Length];
        var wanted = role + "_" + Suffix(gun);
        var clips = gun.ClipPaths.ToDictionary(ClipName, path => path, StringComparer.Ordinal);
        return (legacy ? clips.GetValueOrDefault(wanted + LegacySuffix) : null) ?? clips.GetValueOrDefault(wanted);
    }

    private const string LegacySuffix = "_lgcy";

    private static string ClipName(string path) => Path.GetFileName(path).Replace(".vnmclip_c", "", StringComparison.Ordinal);

    /// <summary>rifle_ssg08 → ssg08, the name CS2's clips of that weapon end with.</summary>
    private static string Suffix(WeaponEntry weapon) => weapon.Id[(weapon.Id.IndexOf('_') + 1)..];
}
