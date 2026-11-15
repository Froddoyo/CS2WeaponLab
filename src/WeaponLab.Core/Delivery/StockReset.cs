using WeaponLab.Core.Game;

namespace WeaponLab.Core.Delivery;

/// <param name="Files">Manifest keys of the weapon's custom clips and own graph copies in the addon.</param>
/// <param name="Redirects">Custom path → CS2's path (without <c>_c</c>), to point graphs that stay back at CS2's.</param>
/// <param name="Skeletons">Skeleton files in the addon that the weapon's custom clips play on (removed when nothing else uses them).</param>
public sealed record StockResetFiles(IReadOnlyList<string> Files, IReadOnlyDictionary<string, string> Redirects, IReadOnlyList<string> Skeletons)
{
    public int Count => Files.Count + Skeletons.Count;
}

/// <summary>
/// "Set to CS2 default" for a weapon: every file the addon has for it,
/// whoever made it (Weapon Lab, CS2 Animation Lab, a hand edit). Applying
/// removes them and points the graphs that stay (such as the root
/// <c>viewmodel_custom</c>) back at CS2's graphs for that weapon, so the
/// rest of their routing is kept (user decision 2026-10-26).
/// </summary>
public static class StockReset
{
    public static StockResetFiles For(GameConnection connection, AddonTarget target, string weaponId)
    {
        var files = new List<string>();
        var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
        var skeletons = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        // Custom clips: everything in animation/anims/custom/<weapon>/.
        var clipFolder = Path.Combine(target.GamePath, (GraphTree.CustomClipRoot + weaponId).Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(clipFolder))
        {
            foreach (var file in Directory.EnumerateFiles(clipFolder, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
            {
                var key = AddonManifest.KeyFor(target, file);
                files.Add(key);
                if (key.EndsWith(".vnmclip_c", StringComparison.OrdinalIgnoreCase)
                    && WeaponSkeletonOf(File.ReadAllBytes(file), file) is { } skeleton
                    && !connection.Archive.Contains(skeleton + "_c") && File.Exists(AddonManifest.Resolve(target, skeleton + "_c")))
                {
                    skeletons.Add(skeleton + "_c");
                }
            }
        }

        if (connection.FindWeapon(weaponId) is { } weapon)
        {
            foreach (var stock in weapon.ClipPaths.Select(path => path[..^2]))
            {
                redirects[GraphTree.CustomClipPath(weaponId, stock)] = stock;
            }
        }

        // The weapon's own graph copies (viewmodel_gun_custom.vnmgraph+m4a4); shared graphs and the root stay.
        foreach (var graph in connection.Graphs.Values.Where(graph => DeliveryPlanner.GraphOwners(connection, graph).Contains(weaponId)))
        {
            var custom = GraphTree.CustomGraphPath(graph.Path);
            if (File.Exists(AddonManifest.Resolve(target, custom + "_c")))
            {
                files.Add(custom + "_c");
            }

            redirects[custom] = graph.Path;
        }

        return new StockResetFiles(files, redirects, skeletons.ToList());
    }

    /// <summary>
    /// The weapon skeleton a compiled clip plays on (its paired stream's), read
    /// from the clip's data: the reference list does not always name it. Null
    /// when the clip cannot be read or has none.
    /// </summary>
    public static string? WeaponSkeletonOf(byte[] bytes, string file)
    {
        try
        {
            return Clips.ClipReader.Read(bytes, file).SecondarySkeletons.FirstOrDefault();
        }
        catch (Exception error) when (error is InvalidDataException or IOException or NotSupportedException or KeyNotFoundException)
        {
            return Addons.AddonChecks.References(bytes, file).FirstOrDefault(reference => reference.EndsWith(".vnmskel", StringComparison.OrdinalIgnoreCase));
        }
    }
}
