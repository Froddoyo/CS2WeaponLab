using WeaponLab.Core.Game;

namespace WeaponLab.Core.Tests;

public sealed class GameDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void LibraryFoldersParseUnescapesWindowsPaths()
    {
        const string vdf = """
            "libraryfolders"
            {
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "label"		""
                }
                "1"
                {
                    "path"		"F:\\SteamLibrary"
                    "apps" { "730" "123" }
                }
            }
            """;

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"F:\SteamLibrary"], SteamLibraryFolders.Parse(vdf));
    }

    [Fact]
    public void SteamInfParsesVersionFields()
    {
        const string inf = "ClientVersion=2000935\r\nPatchVersion=1.41.9.2\r\nProductName=cs2\r\nSourceRevision=11162119\r\nVersionDate=Oct 22 2026\r\n";

        var version = GameVersion.Parse(inf);

        Assert.Equal(new GameVersion("1.41.9.2", "2000935", "11162119", "Oct 22 2026"), version);
    }

    [Fact]
    public void SteamInfMissingFieldsAreUnknown()
    {
        Assert.Equal("unknown", GameVersion.Parse("ProductName=cs2").PatchVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("game")]
    [InlineData("game/csgo")]
    [InlineData("game/csgo/pak01_dir.vpk")]
    public void InstallIsFoundFromRootOrSubfolders(string relative)
    {
        var install = CreateFakeInstall();

        var found = GameInstall.FromPath(Path.Combine(install, relative));

        Assert.NotNull(found);
        Assert.Equal(Path.GetFullPath(install), found.RootPath);
    }

    [Fact]
    public void InstallIsNotFoundWithoutVpk()
    {
        Directory.CreateDirectory(Path.Combine(_root, "game", "csgo"));

        Assert.Null(GameInstall.FromPath(_root));
    }

    [Fact]
    public void AddonListSkipsNonAddonFoldersAndPairsContent()
    {
        var install = GameInstall.FromPath(CreateFakeInstall())!;
        Directory.CreateDirectory(Path.Combine(install.GameAddonsPath, "weapon_template", "scripts"));
        File.WriteAllBytes(Path.Combine(install.GameAddonsPath, "weapon_template", "scripts", "weapons.vdata_c"), []);
        Directory.CreateDirectory(Path.Combine(install.ContentAddonsPath, "weapon_template"));
        Directory.CreateDirectory(Path.Combine(install.GameAddonsPath, "bare_addon"));
        Directory.CreateDirectory(Path.Combine(install.GameAddonsPath, "vpks"));
        Directory.CreateDirectory(Path.Combine(install.GameAddonsPath, "workshop_items"));

        var addons = AddonInfo.List(install);

        Assert.Equal(["bare_addon", "weapon_template"], addons.Select(addon => addon.Name));
        Assert.Null(addons[0].ContentPath);
        Assert.False(addons[0].HasWeaponsVData);
        Assert.NotNull(addons[1].ContentPath);
        Assert.True(addons[1].HasWeaponsVData);
    }

    private string CreateFakeInstall()
    {
        var install = Path.Combine(_root, GameInstall.FolderName);
        var csgo = Path.Combine(install, "game", "csgo");
        Directory.CreateDirectory(csgo);
        File.WriteAllBytes(Path.Combine(csgo, "pak01_dir.vpk"), []);
        return install;
    }
}
