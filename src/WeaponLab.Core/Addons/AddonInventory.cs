using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Graphs;

namespace WeaponLab.Core.Addons;

public enum AddonAssetKind
{
    Clip,
    Graph,
    Skeleton,
    Model,
}

/// <summary>An animation-related file in an addon.</summary>
/// <param name="Path">Resource path without <c>_c</c>, e.g. <c>animation/anims/custom/rifle_aug/reload_aug.vnmclip</c>.</param>
/// <param name="FromWeaponLab">Written by a Weapon Lab delivery (listed in its manifest).</param>
/// <param name="Weapons">Weapon folders it belongs to (empty when unknown or shared, like the root graph).</param>
/// <param name="Replaces">The game resource it stands in for, when known.</param>
public sealed record AddonAsset(string Path, AddonAssetKind Kind, bool FromWeaponLab, IReadOnlyList<string> Weapons, string? Replaces)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// What an addon already holds for weapon animation: clips, graphs,
/// skeletons and weapon models, whether Weapon Lab or someone else (CS2
/// Animation Lab, hand work) put them there, and which weapon each belongs
/// to. An addon's own custom graphs also tell which game clips it replaces.
/// </summary>
public sealed class AddonInventory
{
    private AddonInventory(IReadOnlyList<AddonAsset> assets, IReadOnlyDictionary<string, string> clipOverrides, IReadOnlySet<string>? vdataBlocks = null, IReadOnlyList<PlayerModel>? playerModels = null)
    {
        PlayerModels = playerModels ?? [];
        Assets = assets;
        ClipOverrides = clipOverrides;
        OwnVDataBlocks = vdataBlocks ?? new HashSet<string>();
    }

    /// <summary>vdata blocks where the addon's own values (not ones Weapon Lab set) differ from CS2's.</summary>
    public IReadOnlySet<string> OwnVDataBlocks { get; }

    public IReadOnlyList<AddonAsset> Assets { get; }

    /// <summary>The addon's player models and the first-person (hudmodel) graph each one uses.</summary>
    public IReadOnlyList<PlayerModel> PlayerModels { get; }

    /// <summary>Game clip → the addon clip its graphs play instead (paths without <c>_c</c>).</summary>
    public IReadOnlyDictionary<string, string> ClipOverrides { get; }

    public static AddonInventory Empty { get; } = new([], new Dictionary<string, string>());

    /// <summary>
    /// The addon's own version of a game clip (path without <c>_c</c>): the clip its graphs play instead,
    /// else a clip that stands in for it by name; null when the addon plays CS2's.
    /// </summary>
    public string? ClipFor(string stockClip) =>
        ClipOverrides.GetValueOrDefault(stockClip)
        ?? Assets.FirstOrDefault(asset => asset.Kind == AddonAssetKind.Clip && asset.Replaces == stockClip)?.Path;

    /// <summary>The file of the addon's own version of a game clip (<c>…vnmclip_c</c> path), or null when it has none on disk.</summary>
    public static string? ClipFile(AddonTarget? target, AddonInventory inventory, string clipPath)
    {
        var stock = clipPath.EndsWith("_c", StringComparison.Ordinal) ? clipPath[..^2] : clipPath;
        if (target is null || inventory.ClipFor(stock) is not { } own)
        {
            return null;
        }

        var file = IO.AtomicFile.SafeJoin(target.GamePath, own + "_c");
        return File.Exists(file) ? file : null;
    }

    /// <summary>Assets of a weapon that Weapon Lab did not deliver.</summary>
    /// <summary>Everything the addon has for a weapon, whoever made it: the live state.</summary>
    public IReadOnlyList<AddonAsset> For(string weaponId) =>
        Assets.Where(asset => asset.Weapons.Contains(weaponId)).ToList();

    public IReadOnlyList<AddonAsset> OwnFor(string weaponId) =>
        Assets.Where(asset => !asset.FromWeaponLab && asset.Weapons.Contains(weaponId)).ToList();

    public static AddonInventory Load(GameConnection connection, AddonTarget target, AddonManifest? manifest)
    {
        if (!Directory.Exists(target.GamePath))
        {
            return Empty;
        }

        var weapons = connection.Weapons.ToDictionary(weapon => weapon.Id, StringComparer.OrdinalIgnoreCase);
        var stockClipsByFile = connection.Weapons
            .SelectMany(weapon => weapon.ClipPaths.Select(path => (File: System.IO.Path.GetFileName(path[..^2]), weapon.Id, Stock: path[..^2])))
            .GroupBy(item => item.File, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);

        var assets = new List<AddonAsset>();
        var players = new List<PlayerModel>();
        var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(target.GamePath, "*_c", SearchOption.AllDirectories))
        {
            var key = System.IO.Path.GetRelativePath(target.GamePath, file).Replace('\\', '/');
            var path = key[..^2];
            var ours = manifest?.Files.ContainsKey(key) == true;
            if (path.EndsWith(".vnmclip", StringComparison.Ordinal))
            {
                assets.Add(Clip(path, ours, weapons.Keys, stockClipsByFile));
            }
            else if (path.EndsWith(".vnmgraph", StringComparison.Ordinal))
            {
                assets.Add(Graph(connection, file, path, ours, overrides));
            }
            else if (path.EndsWith(".vnmskel", StringComparison.Ordinal))
            {
                assets.Add(new AddonAsset(path, AddonAssetKind.Skeleton, ours, [], null));
            }
            else if (path.EndsWith(".vmdl", StringComparison.Ordinal))
            {
                if (path.Contains("weapon", StringComparison.OrdinalIgnoreCase))
                {
                    assets.Add(new AddonAsset(path, AddonAssetKind.Model, ours, [], null));
                }

                if (PlayerGraphs(file) is { } refs)
                {
                    players.Add(new PlayerModel(path, refs.Hud) { WorldGraphs = refs.World });
                }
            }
        }

        return new AddonInventory(assets.OrderBy(asset => asset.Path, StringComparer.Ordinal).ToList(), overrides, OwnVData(connection, target, manifest), players);
    }

    // Graph references sit in the uncompressed RERL block, unlike the "hudmodel" name inside the data.
    private static readonly byte[] GraphMarker = "animation/graphs/"u8.ToArray();

    /// <summary>
    /// The graph a model names as its "hudmodel" (first-person) graph, and the graphs of its "" and "worldmodel"
    /// (third-person) entries; null for models without a hudmodel graph.
    /// </summary>
    private static (string Hud, IReadOnlyList<string> World)? PlayerGraphs(string file)
    {
        try
        {
            // Only models that reference a graph can be player models; skip parsing everything else.
            var bytes = File.ReadAllBytes(file);
            if (bytes.AsSpan().IndexOf(GraphMarker) < 0)
            {
                return null;
            }

            using var resource = new ValveResourceFormat.Resource { FileName = System.IO.Path.GetFileName(file) };
            resource.Read(new MemoryStream(bytes));
            if (resource.DataBlock is not ValveResourceFormat.ResourceTypes.KeyValuesOrNTRO model
                || !model.Data.TryGetValue("m_animGraph2Refs", out var graphs))
            {
                return null;
            }

            string? GraphOf(string identifier) => graphs.Values
                .FirstOrDefault(graph => graph.TryGetValue("m_sIdentifier", out var name) && name.ToString() == identifier)?["m_hGraph"].ToString();
            var hud = GraphOf("hudmodel");
            if (hud is null)
            {
                return null;
            }

            // Agent models give both "" and "worldmodel" the third-person graph (read from files 2026-11-14).
            var world = new[] { GraphOf(""), GraphOf("worldmodel") }.OfType<string>().Where(graph => graph.Length > 0).ToList();
            return (hud, world);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HashSet<string> OwnVData(GameConnection connection, AddonTarget target, AddonManifest? manifest)
    {
        var blocks = new HashSet<string>(StringComparer.Ordinal);
        if (!File.Exists(target.VDataCompiledPath))
        {
            return blocks;
        }

        VData.VDataFile addon;
        try
        {
            addon = VData.VDataFile.Read(File.ReadAllBytes(target.VDataCompiledPath), target.VDataCompiledPath);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            return blocks;
        }

        var stock = connection.ReadVData();
        foreach (var block in addon.Blocks)
        {
            if (stock[block.Name] is not { } stockBlock)
            {
                continue;
            }

            // Values Weapon Lab set, and keys its class changes replaced, are the profile's, not the addon's own.
            var ours = manifest?.VData.GetValueOrDefault(block.Name)?.Keys.ToHashSet(StringComparer.Ordinal) ?? [];
            var swapped = manifest?.VDataClasses.GetValueOrDefault(block.Name)?.ToHashSet(StringComparer.Ordinal) ?? [];
            bool Theirs(VData.VDataProperty property) => !ours.Contains(property.Path) && !swapped.Contains(VData.VDataGlossary.RootKey(property.Path));
            var differs = block.Properties.Where(Theirs).Any(property => stockBlock.Value(property.Path) is not { } value || !VData.VDataValues.Same(value, property.Value));
            if (differs)
            {
                blocks.Add(block.Name);
            }
        }

        return blocks;
    }

    /// <summary>A clip under custom/&lt;weapon&gt;/ belongs to that weapon; otherwise it is matched by file name.</summary>
    private static AddonAsset Clip(string path, bool ours, IEnumerable<string> weaponIds, Dictionary<string, List<(string File, string Id, string Stock)>> stockByFile)
    {
        var file = System.IO.Path.GetFileName(path);
        var folder = path.StartsWith(GraphTree.CustomClipRoot, StringComparison.Ordinal) ? path[GraphTree.CustomClipRoot.Length..].Split('/')[0] : null;
        var candidates = stockByFile.GetValueOrDefault(file) ?? [];
        var weapon = folder is not null && weaponIds.Contains(folder, StringComparer.OrdinalIgnoreCase) ? folder : candidates.FirstOrDefault().Id;
        var replaces = candidates.FirstOrDefault(candidate => string.Equals(candidate.Id, weapon, StringComparison.OrdinalIgnoreCase)).Stock;
        return new AddonAsset(path, AddonAssetKind.Clip, ours, weapon is null ? [] : [weapon], replaces);
    }

    /// <summary>
    /// A custom graph stands in for the game graph without "_custom". Its
    /// resource slots line up with the game graph's, so a slot that points
    /// elsewhere shows which game clip the addon replaced.
    /// </summary>
    private static AddonAsset Graph(GameConnection connection, string file, string path, bool ours, Dictionary<string, string> overrides)
    {
        var stock = path.Replace("_custom.vnmgraph", ".vnmgraph", StringComparison.Ordinal);
        if (stock == path || !connection.Graphs.TryGetValue(stock, out var info))
        {
            return new AddonAsset(path, AddonAssetKind.Graph, ours, [], null);
        }

        try
        {
            var addon = GraphDocument.Read(File.ReadAllBytes(file), path + "_c");
            for (var slot = 0; slot < Math.Min(addon.Resources.Count, info.Resources.Count); slot++)
            {
                var (from, to) = (info.Resources[slot], addon.Resources[slot]);
                if (from != to && from.EndsWith(".vnmclip", StringComparison.Ordinal) && to.EndsWith(".vnmclip", StringComparison.Ordinal))
                {
                    overrides[from] = to;
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            // An unreadable graph is still listed; it just tells nothing about clips.
        }

        // The root graph routes every weapon; it belongs to none.
        var weapons = stock == GraphTree.Parent ? [] : DeliveryPlanner.GraphOwners(connection, info);
        return new AddonAsset(path, AddonAssetKind.Graph, ours, weapons, stock);
    }
}
