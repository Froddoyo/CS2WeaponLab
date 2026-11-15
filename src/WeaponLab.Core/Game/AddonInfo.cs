namespace WeaponLab.Core.Game;

/// <summary>
/// A Workshop Tools addon: <c>game/csgo_addons/&lt;name&gt;</c> plus its
/// optional sources in <c>content/csgo_addons/&lt;name&gt;</c>.
/// </summary>
public sealed record AddonInfo(string Name, string GamePath, string? ContentPath)
{
    // Folders under csgo_addons that are not addons.
    private static readonly HashSet<string> NonAddonFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "vpks",
        "workshop_items",
    };

    public bool HasWeaponsVData => File.Exists(Path.Combine(GamePath, "scripts", "weapons.vdata_c"));

    public static IReadOnlyList<AddonInfo> List(GameInstall install)
    {
        if (!Directory.Exists(install.GameAddonsPath))
        {
            return [];
        }

        return Directory.EnumerateDirectories(install.GameAddonsPath)
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(name => !NonAddonFolders.Contains(name))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(name =>
            {
                var content = Path.Combine(install.ContentAddonsPath, name);
                return new AddonInfo(name, Path.Combine(install.GameAddonsPath, name), Directory.Exists(content) ? content : null);
            })
            .ToList();
    }
}
