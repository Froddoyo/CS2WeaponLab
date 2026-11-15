namespace WeaponLab.Core.Game;

/// <summary>
/// A validated Counter-Strike 2 install. All paths are absolute.
/// </summary>
public sealed record GameInstall
{
    public const string FolderName = "Counter-Strike Global Offensive";

    public required string RootPath { get; init; }

    public string GamePath => Path.Combine(RootPath, "game");
    public string CsgoPath => Path.Combine(GamePath, "csgo");
    public string CorePath => Path.Combine(GamePath, "core");
    public string CsgoVpkPath => Path.Combine(CsgoPath, "pak01_dir.vpk");
    public string CoreVpkPath => Path.Combine(CorePath, "pak01_dir.vpk");
    public string GameAddonsPath => Path.Combine(GamePath, "csgo_addons");
    public string ContentAddonsPath => Path.Combine(RootPath, "content", "csgo_addons");
    public string ResourceCompilerPath => Path.Combine(GamePath, "bin", "win64", "resourcecompiler.exe");

    public bool HasWorkshopTools => File.Exists(ResourceCompilerPath);

    /// <summary>
    /// Accepts the install root, its <c>game</c> or <c>game/csgo</c> folder,
    /// or the csgo <c>pak01_dir.vpk</c> itself. Returns null when the path
    /// does not lead to an install with a csgo VPK.
    /// </summary>
    public static GameInstall? FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var full = Path.GetFullPath(path.Trim().Trim('"'));
        if (File.Exists(full))
        {
            full = Path.GetDirectoryName(full)!;
        }

        foreach (var candidate in RootCandidates(full))
        {
            var install = new GameInstall { RootPath = candidate };
            if (File.Exists(install.CsgoVpkPath))
            {
                return install;
            }
        }

        return null;
    }

    // The given folder may be the root, root/game or root/game/csgo.
    private static IEnumerable<string> RootCandidates(string folder)
    {
        string? current = folder;
        for (var depth = 0; depth < 3 && current is not null; depth++)
        {
            yield return current;
            current = Path.GetDirectoryName(current);
        }
    }
}
