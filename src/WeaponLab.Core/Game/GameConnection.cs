using System.Collections.Concurrent;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Game;

/// <summary>
/// Everything the app learns when it connects to an install: the build,
/// the addons it can deliver to, and the weapons it can import. Keeps the
/// csgo VPK open until disposed.
/// </summary>
public sealed class GameConnection : IDisposable
{
    private readonly ConcurrentDictionary<string, SkeletonInfo> _skeletons = new(StringComparer.OrdinalIgnoreCase);

    private GameConnection(GameInstall install, GameArchive archive)
    {
        Install = install;
        Archive = archive;
        Version = GameVersion.Read(install);
        Addons = AddonInfo.List(install);
        Weapons = WeaponCatalog.Scan(archive);
    }

    public GameInstall Install { get; }
    public GameArchive Archive { get; }
    public GameVersion? Version { get; }
    public IReadOnlyList<AddonInfo> Addons { get; }
    public IReadOnlyList<WeaponEntry> Weapons { get; }

    /// <summary>
    /// Connects to the given install folder, or locates CS2 through Steam
    /// when no folder is given. Throws with a user-facing message on failure.
    /// </summary>
    public static GameConnection Open(string? installPath = null)
    {
        var install = installPath is null
            ? GameLocator.Locate() ?? throw new GameNotFoundException("CS2 was not found in any Steam library. Choose its install folder manually.")
            : GameInstall.FromPath(installPath) ?? throw new GameNotFoundException($"No CS2 install at '{installPath}'. Choose the '{GameInstall.FolderName}' folder or its game/csgo folder.");

        var archive = new GameArchive(install.CsgoVpkPath);
        try
        {
            return new GameConnection(install, archive);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    public WeaponEntry? FindWeapon(string id)
    {
        return Weapons.FirstOrDefault(weapon => weapon.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public ClipInfo ReadClip(string resourcePath) => ClipReader.Read(Archive, resourcePath);

    /// <summary>The game's weapons.vdata_c, decoded once.</summary>
    public VData.VDataFile ReadVData() => _vdata ??= VData.VDataFile.Read(Archive.ReadBytes(VData.VDataFile.GamePath));

    /// <summary>The viewmodel graphs, read once per connection.</summary>
    public IReadOnlyDictionary<string, Delivery.GraphInfo> Graphs => _graphs ??= Delivery.GraphTree.Load(Archive);

    /// <summary>
    /// The weapon's vdata blocks: those whose m_szAnimSkeleton is the weapon
    /// skeleton its clips use (rifle_ak → weapon_ak47). Empty when none.
    /// </summary>
    public IReadOnlyList<string> VDataBlocksFor(WeaponEntry weapon)
    {
        if (weapon.ClipPaths.Count == 0)
        {
            return [];
        }

        var skeleton = ReadClip(weapon.ClipPaths[0]).SecondarySkeletons.FirstOrDefault();
        return skeleton is null ? [] : ReadVData().BlocksForSkeleton(skeleton).Select(block => block.Name).ToList();
    }

    /// <summary>An addon's weapons.vdata_c override, or null when it has none.</summary>
    public static VData.VDataFile? ReadAddonVData(AddonInfo addon)
    {
        var path = Path.Combine(addon.GamePath, "scripts", "weapons.vdata_c");
        return File.Exists(path) ? VData.VDataFile.Read(File.ReadAllBytes(path), path) : null;
    }

    private VData.VDataFile? _vdata;
    private Dictionary<string, Delivery.GraphInfo>? _graphs;

    public ClipTemplate OpenClipTemplate(string resourcePath) => ClipTemplate.Open(Archive.ReadBytes(resourcePath), resourcePath);

    /// <param name="resourcePath">Skeleton path as clips name it, e.g. <c>animation/skeletons/weapons/ak47.vnmskel</c>.</param>
    public SkeletonInfo ReadSkeleton(string resourcePath)
    {
        return _skeletons.GetOrAdd(resourcePath, path => SkeletonInfo.Read(Archive.ReadBytes(path + "_c"), path));
    }

    public void Dispose() => Archive.Dispose();
}

public sealed class GameNotFoundException(string message) : Exception(message);
