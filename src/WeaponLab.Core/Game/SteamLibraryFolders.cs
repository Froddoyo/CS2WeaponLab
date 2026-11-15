using System.Text.RegularExpressions;

namespace WeaponLab.Core.Game;

/// <summary>
/// Reads library paths from Steam's <c>steamapps/libraryfolders.vdf</c>.
/// Only the <c>"path"</c> values are needed, so a full VDF parser is not used.
/// </summary>
public static partial class SteamLibraryFolders
{
    [GeneratedRegex("\"path\"\\s*\"(?<path>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex PathEntry();

    public static IReadOnlyList<string> Parse(string vdfText)
    {
        return PathEntry()
            .Matches(vdfText)
            .Select(match => match.Groups["path"].Value.Replace(@"\\", @"\"))
            .ToList();
    }

    public static IReadOnlyList<string> Read(string steamRoot)
    {
        var file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        return File.Exists(file) ? Parse(File.ReadAllText(file)) : [];
    }
}
