using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Tests;

public sealed class WeaponCatalogTests
{
    [Fact]
    public void ClipsAreGroupedByWeaponFolder()
    {
        string[] paths =
        [
            "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c",
            "animation/anims/viewmodel/rifle/rifle_ak/idle_ak.vnmclip_c",
            "animation/anims/viewmodel/pistol/pistol_deagle/fire_deagle.vnmclip_c",
            "animation/anims/viewmodel/chicken/chicken_idle.vnmclip_c",
        ];

        var weapons = WeaponCatalog.FromPaths(paths);

        Assert.Equal(["chicken", "pistol_deagle", "rifle_ak"], weapons.Select(weapon => weapon.Id));
        Assert.Equal(["", "pistol", "rifle"], weapons.Select(weapon => weapon.Category));

        var ak = weapons.Single(weapon => weapon.Id == "rifle_ak");
        Assert.Equal("animation/anims/viewmodel/rifle/rifle_ak", ak.Folder);
        Assert.Equal(
            ["animation/anims/viewmodel/rifle/rifle_ak/idle_ak.vnmclip_c", "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c"],
            ak.ClipPaths);
    }

    [Fact]
    public void UnrelatedFilesAreIgnored()
    {
        string[] paths =
        [
            "animation/anims/viewmodel/loose_clip.vnmclip_c",
            "animation/anims/thirdperson/rifle/run.vnmclip_c",
            "animation/anims/viewmodel/rifle/rifle_ak/notes.txt",
            "animation/graphs/viewmodel/rifle_ak.vnmgraph_c",
        ];

        Assert.Empty(WeaponCatalog.FromPaths(paths));
    }

    [Fact]
    public void NestedCategoriesAreKept()
    {
        var weapons = WeaponCatalog.FromPaths(["animation/anims/viewmodel/knife/special/knife_kukri/draw.vnmclip_c"]);

        var kukri = Assert.Single(weapons);
        Assert.Equal("knife_kukri", kukri.Id);
        Assert.Equal("knife/special", kukri.Category);
    }
}
