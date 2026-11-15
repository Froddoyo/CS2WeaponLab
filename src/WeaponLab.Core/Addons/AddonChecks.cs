using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Addons;

/// <summary>A player model of the addon and the graph its first-person view uses.</summary>
public sealed record PlayerModel(string Path, string HudGraph)
{
    /// <summary>The graphs of its "" and "worldmodel" entries (third person, 0.26).</summary>
    public IReadOnlyList<string> WorldGraphs { get; init; } = [];
}

/// <summary>A problem that stops delivered work from showing in game.</summary>
public sealed record AddonWarning(string Subject, string Message);

/// <summary>
/// Checks that delivered work can actually reach the game. Workshop addons
/// cannot override CS2's own files, so custom graphs only play through the
/// addon's own player models (their hudmodel graph set to
/// viewmodel_custom), and a weapon only animates when its model targets the
/// skeleton its vdata names.
/// </summary>
public static class AddonChecks
{
    /// <summary>Warns when custom graphs would have no player model pointing at them.</summary>
    /// <param name="deliversWorldGraphs">Third-person graphs go out too (0.26): player models need worldmodel_custom as well.</param>
    public static IReadOnlyList<AddonWarning> Hookup(AddonInventory inventory, bool deliversCustomGraphs, bool deliversWorldGraphs = false)
    {
        var warnings = new List<AddonWarning>();
        if (deliversCustomGraphs && HookedPlayerModels(inventory).Count == 0)
        {
            warnings.Add(new AddonWarning(GraphTree.CustomParent,
                $"No player model in this addon uses {GraphTree.CustomParent} as its hudmodel graph, so the custom animations will not play. " +
                "Give the addon's player models (their .vmdl AnimGraph2List) a hudmodel graph of that file; Workshop addons cannot replace CS2's own viewmodel graph."));
        }

        if (deliversWorldGraphs && WorldHookedPlayerModels(inventory).Count == 0)
        {
            warnings.Add(new AddonWarning(GraphTree.WorldCustomParent,
                $"No player model in this addon uses {GraphTree.WorldCustomParent} for third person, so the third-person animations will not play. " +
                "In the player models' .vmdl AnimGraph2List, set the \"\" and \"worldmodel\" entries to that file (untested in game which one CS2 uses)."));
        }

        return warnings;
    }

    public static IReadOnlyList<PlayerModel> HookedPlayerModels(AddonInventory inventory) =>
        inventory.PlayerModels.Where(model => string.Equals(model.HudGraph, GraphTree.CustomParent, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Player models whose third-person entries ("" or "worldmodel") name worldmodel_custom (0.26).</summary>
    public static IReadOnlyList<PlayerModel> WorldHookedPlayerModels(AddonInventory inventory) =>
        inventory.PlayerModels.Where(model => model.WorldGraphs.Any(graph => string.Equals(graph, GraphTree.WorldCustomParent, StringComparison.OrdinalIgnoreCase))).ToList();

    /// <summary>
    /// For the VData tab only (delivery never checks vdata): for every weapon whose model differs from CS2's (in the profile's
    /// unsaved VData edits or the addon's vdata): the model must be set and
    /// exist. The vdata skeleton (m_szAnimSkeleton) is not checked: a custom
    /// skeleton works with CS2's value there, since the model's own skeleton
    /// reference and the clips decide (verified in game 2026-10-25).
    /// </summary>
    public static IReadOnlyList<AddonWarning> Models(GameConnection connection, AddonTarget target, Profile profile)
    {
        var stock = connection.ReadVData();
        VDataFile? addon = null;
        try
        {
            addon = File.Exists(target.VDataCompiledPath) ? VDataFile.Read(File.ReadAllBytes(target.VDataCompiledPath), target.VDataCompiledPath) : null;
        }
        catch (InvalidDataException)
        {
            // An unreadable addon vdata is reported elsewhere; check against CS2's values.
        }

        var warnings = new List<AddonWarning>();
        foreach (var block in stock.Blocks.Where(block => block.IsWeapon))
        {
            // The profile's VData edits (not saved yet) win over the addon's saved vdata, which wins over CS2's.
            var edit = profile.VData.GetValueOrDefault(block.Name)?.GetValueOrDefault(VDataBlock.WorldModelKey);
            var model = edit ?? addon?[block.Name]?.Value(VDataBlock.WorldModelKey) ?? block.Value(VDataBlock.WorldModelKey);
            var where = edit is not null ? "an unsaved edit in the VData tab" : "the addon's vdata";
            if (model is null || model == block.Value(VDataBlock.WorldModelKey))
            {
                continue;
            }

            if (model.Length == 0)
            {
                warnings.Add(new AddonWarning(block.Name, $"has an empty m_szWorldModel ({where}), so the weapon has no model. Set it in the VData tab."));
            }
            else if (SkeletonRefs(connection, target, model) is null)
            {
                warnings.Add(new AddonWarning(block.Name,
                    $"m_szWorldModel is {model} ({where}), but that model is in neither the addon nor the game. Fix the path in the VData tab or add the model."));
            }
        }

        return warnings;
    }

    /// <summary>
    /// Files left in the addon that point at something this delivery removes:
    /// graphs and player models outside Weapon Lab's manifest (a hand-made
    /// hookup copy, the addon's own work). The game fails to load such a
    /// graph and the viewmodel shows without arms.
    /// </summary>
    /// <param name="removedKeys">Manifest keys of the files the delivery removes (and does not write again).</param>
    /// <param name="writtenKeys">Manifest keys of the files the delivery writes.</param>
    public static IReadOnlyList<AddonWarning> BrokenReferences(AddonTarget target, IReadOnlyCollection<string> removedKeys, IReadOnlyCollection<string> writtenKeys)
    {
        var removed = removedKeys.Where(key => key.EndsWith("_c", StringComparison.Ordinal)).Select(key => key[..^2]).ToList();
        if (removed.Count == 0 || !Directory.Exists(target.GamePath))
        {
            return [];
        }

        var skip = removedKeys.Concat(writtenKeys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<AddonWarning>();
        var candidates = Directory.EnumerateFiles(target.GamePath, "*.vnmgraph_c", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(target.GamePath, "*.vmdl_c", SearchOption.AllDirectories));
        foreach (var file in candidates)
        {
            var key = Path.GetRelativePath(target.GamePath, file).Replace('\\', '/');
            if (skip.Contains(key))
            {
                continue;
            }

            // References sit in the uncompressed RERL block: only parse files that name a removed path.
            var bytes = File.ReadAllBytes(file);
            var named = removed.Where(path => bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(path)) >= 0).ToList();
            if (named.Count == 0)
            {
                continue;
            }

            var broken = References(bytes, file).Where(reference => named.Contains(reference, StringComparer.OrdinalIgnoreCase)).ToList();
            if (broken.Count > 0)
            {
                warnings.Add(new AddonWarning(key,
                    $"points at {string.Join(", ", broken)}, which this delivery removes. The game cannot load it afterwards " +
                    "(a broken viewmodel graph shows the gun without arms). Remove or update this file, or keep delivering what it uses."));
            }
        }

        return warnings;
    }

    /// <summary>Keys of the addon's graphs, models and clips (outside <paramref name="skip"/>) that reference a resource.</summary>
    internal static IReadOnlyList<string> FilesUsing(AddonTarget target, string resource, IReadOnlySet<string> skip)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(resource);
        var users = new List<string>();
        foreach (var pattern in new[] { "*.vnmgraph_c", "*.vmdl_c", "*.vnmclip_c" })
        {
            foreach (var file in Directory.EnumerateFiles(target.GamePath, pattern, SearchOption.AllDirectories))
            {
                var key = Path.GetRelativePath(target.GamePath, file).Replace('\\', '/');
                var bytes = skip.Contains(key) ? null : File.ReadAllBytes(file);
                if (bytes is not null && bytes.AsSpan().IndexOf(name) >= 0
                    && References(bytes, file).Contains(resource, StringComparer.OrdinalIgnoreCase))
                {
                    users.Add(key);
                }
            }
        }

        return users;
    }

    internal static IEnumerable<string> References(byte[] bytes, string file)
    {
        try
        {
            using var resource = new ValveResourceFormat.Resource { FileName = Path.GetFileName(file) };
            resource.Read(new MemoryStream(bytes));
            return resource.ExternalReferences?.ResourceRefInfoList.Select(reference => reference.Name).ToList() ?? [];
        }
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException)
        {
            return [];
        }
    }

    /// <summary>A model's m_vecNmSkeletonRefs, read from the addon or the game; null when the model is missing.</summary>
    private static IReadOnlyList<string>? SkeletonRefs(GameConnection connection, AddonTarget target, string model)
    {
        var file = Path.Combine(target.GamePath, model.Replace('/', Path.DirectorySeparatorChar) + "_c");
        byte[] bytes;
        if (File.Exists(file))
        {
            bytes = File.ReadAllBytes(file);
        }
        else if (connection.Archive.Contains(model + "_c"))
        {
            bytes = connection.Archive.ReadBytes(model + "_c");
        }
        else
        {
            return null;
        }

        try
        {
            using var resource = new ValveResourceFormat.Resource { FileName = Path.GetFileName(file) };
            resource.Read(new MemoryStream(bytes));
            return resource.DataBlock is ValveResourceFormat.ResourceTypes.KeyValuesOrNTRO data && data.Data.TryGetValue("m_vecNmSkeletonRefs", out var refs)
                ? refs.Values.Select(value => value.ToString() ?? "").ToList()
                : [];
        }
        catch (InvalidDataException)
        {
            return [];
        }
    }
}
