namespace WeaponLab.Core.Game;

/// <summary>
/// The game build as recorded in <c>game/csgo/steam.inf</c>. Used to tag
/// sandboxes so a game update can be detected later.
/// </summary>
public sealed record GameVersion(string PatchVersion, string ClientVersion, string SourceRevision, string VersionDate)
{
    public override string ToString() => $"{PatchVersion} (client {ClientVersion}, {VersionDate})";

    public static GameVersion Parse(string steamInfText)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in steamInfText.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator > 0)
            {
                values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        string Value(string key) => values.GetValueOrDefault(key, "unknown");
        return new GameVersion(Value("PatchVersion"), Value("ClientVersion"), Value("SourceRevision"), Value("VersionDate"));
    }

    public static GameVersion? Read(GameInstall install)
    {
        var file = Path.Combine(install.CsgoPath, "steam.inf");
        return File.Exists(file) ? Parse(File.ReadAllText(file)) : null;
    }
}
