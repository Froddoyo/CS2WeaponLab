using WeaponLab.Core.Game;

namespace WeaponLab.Core.Weapons;

/// <summary>
/// One weapon folder of viewmodel animation clips in the game VPK.
/// </summary>
/// <param name="Id">Folder name, e.g. <c>ak47</c>.</param>
/// <param name="Category">Folders between the viewmodel root and the weapon folder, e.g. <c>rifles</c>; empty when none.</param>
/// <param name="Folder">VPK folder holding the clips.</param>
/// <param name="ClipPaths">Full VPK paths of the compiled clips, sorted.</param>
public sealed record WeaponEntry(string Id, string Category, string Folder, IReadOnlyList<string> ClipPaths)
{
    /// <summary>
    /// The weapon's third-person clips (0.26): the compiled clips directly in <c>animation/anims/world/</c> under the
    /// same category and folder name (e.g. <c>world/rifle/rifle_ak/</c>), sorted. Empty when the game has none there
    /// (2026-11-14: pistol_glock18, rifle_scar20 and a few others keep theirs elsewhere).
    /// </summary>
    public IReadOnlyList<string> WorldClipPaths { get; init; } = [];
}

/// <summary>
/// Lists viewmodel weapons by scanning the VPK directory tree. Clip contents
/// are not decoded here; that happens on import.
/// </summary>
public static class WeaponCatalog
{
    public const string ViewmodelClipRoot = "animation/anims/viewmodel/";
    public const string ViewmodelGraphRoot = "animation/graphs/viewmodel/";
    public const string ClipExtension = ".vnmclip_c";

    /// <summary>Third-person (worldmodel) clips, one folder per weapon like the viewmodel's (verified from files 2026-11-14).</summary>
    public const string WorldClipRoot = "animation/anims/world/";

    public static IReadOnlyList<WeaponEntry> Scan(GameArchive archive)
    {
        return FromPaths(archive.AllPaths());
    }

    /// <summary>A third-person clip: one under <see cref="WorldClipRoot"/>.</summary>
    public static bool IsWorldClip(string resourcePath) =>
        resourcePath.Replace('\\', '/').StartsWith(WorldClipRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Groups clip paths by their folder. Paths outside the viewmodel root
    /// and files directly in it are ignored. Each weapon also gets the world
    /// clips of the folder with its name (<see cref="WeaponEntry.WorldClipPaths"/>).
    /// </summary>
    internal static IReadOnlyList<WeaponEntry> FromPaths(IEnumerable<string> paths)
    {
        var all = paths.Select(path => path.Replace('\\', '/')).ToList();
        bool IsClip(string path, string root) =>
            path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && path.EndsWith(ClipExtension, StringComparison.OrdinalIgnoreCase);

        // Relative folder (e.g. rifle/rifle_ak) -> its world clips; files straight in the world root have none.
        var worldByFolder = all
            .Where(path => IsClip(path, WorldClipRoot) && path.LastIndexOf('/') > WorldClipRoot.Length)
            .GroupBy(path => path[WorldClipRoot.Length..path.LastIndexOf('/')], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Order(StringComparer.Ordinal).ToList(), StringComparer.OrdinalIgnoreCase);

        var clipsByFolder = all
            .Where(path => IsClip(path, ViewmodelClipRoot))
            .GroupBy(path => path[..path.LastIndexOf('/')], StringComparer.OrdinalIgnoreCase);

        var weapons = new List<WeaponEntry>();
        foreach (var group in clipsByFolder)
        {
            var folder = group.Key;
            var relative = folder.Length >= ViewmodelClipRoot.Length ? folder[ViewmodelClipRoot.Length..] : "";
            if (relative.Length == 0)
            {
                continue;
            }

            var lastSlash = relative.LastIndexOf('/');
            var id = lastSlash < 0 ? relative : relative[(lastSlash + 1)..];
            var category = lastSlash < 0 ? "" : relative[..lastSlash];
            var clips = group.Order(StringComparer.Ordinal).ToList();
            weapons.Add(new WeaponEntry(id, category, folder, clips) { WorldClipPaths = worldByFolder.GetValueOrDefault(relative) ?? [] });
        }

        return weapons
            .OrderBy(weapon => weapon.Category, StringComparer.Ordinal)
            .ThenBy(weapon => weapon.Id, StringComparer.Ordinal)
            .ToList();
    }
}
