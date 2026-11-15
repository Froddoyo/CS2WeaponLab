using Microsoft.Win32;

namespace WeaponLab.Core.Game;

/// <summary>
/// Finds the CS2 install through Steam: registry keys, default install
/// folders, then every library listed in <c>libraryfolders.vdf</c>.
/// </summary>
public static class GameLocator
{
    public static GameInstall? Locate()
    {
        foreach (var library in SteamLibraries())
        {
            var install = GameInstall.FromPath(Path.Combine(library, "steamapps", "common", GameInstall.FolderName));
            if (install is not null)
            {
                return install;
            }
        }

        return null;
    }

    public static IReadOnlyList<string> SteamLibraries()
    {
        var steamRoots = SteamRoots().ToList();
        var libraries = new List<string>(steamRoots);
        foreach (var root in steamRoots)
        {
            libraries.AddRange(SteamLibraryFolders.Read(root));
        }

        return libraries
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> SteamRoots()
    {
        if (OperatingSystem.IsWindows())
        {
            var fromUser = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (!string.IsNullOrEmpty(fromUser))
            {
                yield return fromUser;
            }

            var fromMachine = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
            if (!string.IsNullOrEmpty(fromMachine))
            {
                yield return fromMachine;
            }
        }

        foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            var programFiles = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrEmpty(programFiles))
            {
                yield return Path.Combine(programFiles, "Steam");
            }
        }
    }
}
