using WeaponLab.Core.Addons;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Viewport;

namespace WeaponLab.Core.Tests;

/// <summary>
/// The viewport plays what the addon plays: its own version of a clip when it has one (bug 2026-11-07: the
/// M249 on the AUG slot showed CS2's reload_aug though cs_hl_template has its own, made before Weapon Lab).
/// </summary>
public sealed class AddonClipTests : IDisposable
{
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cache))
        {
            Directory.Delete(_cache, recursive: true);
        }
    }

    /// <summary>Uses PC1's cs_hl_template addon, whose custom AUG clips predate Weapon Lab; skipped where it does not exist.</summary>
    [Fact]
    public void ViewportPlaysTheAddonsOwnClip()
    {
        using var connection = GameLocator.Locate() is null ? null : GameConnection.Open();
        var install = connection?.Install;
        var target = install is null ? null : new AddonTarget("cs_hl_template",
            Path.Combine(install.GamePath, "csgo_addons", "cs_hl_template"), Path.Combine(install.RootPath, "content", "csgo_addons", "cs_hl_template"));
        if (connection is null || target is null || !File.Exists(Path.Combine(target.GamePath, "animation", "anims", "custom", "rifle_aug", "reload_aug.vnmclip_c")))
        {
            Assert.Skip("No cs_hl_template addon with its own AUG clips here.");
        }

        var inventory = AddonInventory.Load(connection, target, AddonManifest.Read(target));
        var weapon = connection.FindWeapon("rifle_aug")!;
        var path = weapon.ClipPaths.First(clip => clip.EndsWith("/reload_aug.vnmclip_c", StringComparison.Ordinal));
        var file = AddonInventory.ClipFile(target, inventory, path);
        Assert.NotNull(file);
        Assert.EndsWith(Path.Combine("custom", "rifle_aug", "reload_aug.vnmclip_c"), file);

        var scene = ViewportScenes.Build(connection, weapon, null, null, _cache, arms: null);
        var addon = ViewportClips.Load(connection, null, path, scene, exported: true, file);
        var stock = ViewportClips.Load(connection, null, path, scene, exported: true);
        Assert.NotNull(addon);
        Assert.NotNull(stock);
        Assert.Equal("addon", addon.Source);
        Assert.Equal("CS2", stock.Source);
        Assert.True(addon.FrameCount != stock.FrameCount || addon.Frames.Zip(stock.Frames).Any(pair => pair.First.Zip(pair.Second).Any(bone => bone.First != bone.Second)),
            "the addon's reload_aug plays the same as CS2's");
    }
}
