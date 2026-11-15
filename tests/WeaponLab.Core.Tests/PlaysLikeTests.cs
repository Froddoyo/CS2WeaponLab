using ValveKeyValue;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.VData;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Tests;

/// <summary>Which gun a weapon plays like, the game's timing that follows, and fitting events to it. Skipped without CS2.</summary>
public sealed class PlaysLikeTests
{
    private const string SsgReload = "animation/anims/viewmodel/rifle/rifle_ssg08/reload_ssg08.vnmclip_c";
    private const string AkReload = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c";

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    private static VDataBlock With(VDataBlock block, string key, string value) =>
        block with { Properties = [.. block.Properties.Where(property => property.Path != key), new VDataProperty(key, value, KVValueType.String)] };

    /// <summary>
    /// The groups and defaults read from CS2's root graph, and the weapon type →
    /// category map checked on every stock weapon: each one whose anim class the
    /// graph lists plays like itself, in the group its type maps to.
    /// </summary>
    [Fact]
    public void EveryStockWeaponPlaysLikeItselfInItsTypesGroup()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var selection = GunSelection.Of(connection);
        Assert.Equal("Nova", selection.GroupFor("weapon_category_shotgun")?.Default?.Name);
        Assert.Equal("USP", selection.GroupFor("weapon_category_pistol")?.Default?.Name);
        Assert.Equal("AK", selection.GroupFor("weapon_category_rifle")?.Default?.Name);
        Assert.Equal(["weapon_nova", "weapon_xm1014", "weapon_sawedoff", "weapon_mag7"],
            selection.GroupFor("weapon_category_shotgun")!.Guns.SelectMany(gun => gun.AnimClasses));

        var listed = selection.Groups.SelectMany(group => group.Guns).SelectMany(gun => gun.AnimClasses).ToHashSet();
        var wrong = new List<string>();
        var checkedCount = 0;
        foreach (var block in connection.ReadVData().Blocks.Where(block => block.IsWeapon && listed.Contains(WeaponSlots.SlotOf(block) ?? "")))
        {
            checkedCount++;
            var result = PlaysLike.For(selection, block);
            if (!result.Listed || result.Gun?.AnimClasses.Contains(result.AnimClass) != true)
            {
                wrong.Add($"{block.Name} ({result.WeaponType}, {result.AnimClass}) -> {result.Group?.Name ?? "no group"}");
            }
        }

        Assert.True(checkedCount > 40, $"only {checkedCount} weapons checked");
        Assert.True(wrong.Count == 0, "Not in their type's group: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// The Annabelle's setup (SSG08 block, shotgun type, anim class weapon_ssg08):
    /// not listed in the shotgun group, so it plays like the Nova until another
    /// shotgun is held, loses its scope view, and its reload's game timing is
    /// reload_nova's (intro ends 11, shell counted 20, loop ends 24, outro ends 49).
    /// </summary>
    [Fact]
    public void AnnabelleSetupPlaysLikeTheNovaWithItsTiming()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var block = With(connection.ReadVData()["weapon_ssg08"]!, PlaysLike.WeaponTypeKey, "WEAPONTYPE_SHOTGUN");
        var result = PlaysLike.For(GunSelection.Of(connection), block);
        Assert.Equal("Shotguns", result.Group?.Name);
        Assert.False(result.Listed);
        Assert.Equal("Nova", result.Gun?.Name);
        Assert.True(result.ScopeHidden);
        Assert.Contains("held last", result.Describe());
        Assert.Equal(2, result.Warnings().Count);

        var weapon = connection.Weapons.First(item => item.Id == "rifle_ssg08");
        var timing = GameTimings.For(connection, result, weapon, connection.ReadClip(SsgReload));
        Assert.NotNull(timing);
        Assert.Equal("reload_nova", timing.Reference.Name);
        Assert.Equal(new ShellLoop(11, 24, 20), timing.Loop);
        Assert.Equal(49, timing.OutroEnd, precision: 2); // The two clips' frame rates differ in the fourth decimal.
        Assert.Equal(["intro ends", "shell counted", "loop ends", "outro ends"], timing.Marks.Select(mark => mark.Label));
    }

    /// <summary>A listed class from another gun: an AK with the FAMAS's anim class refills at the FAMAS's time (1.67 s, verified on PC2).</summary>
    [Fact]
    public void ListedClassPlaysLikeThatGunsMagazineRefill()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var block = With(connection.ReadVData()["weapon_ak47"]!, WeaponSlots.AnimClassKey, "weapon_famas");
        var result = PlaysLike.For(GunSelection.Of(connection), block);
        Assert.True(result.Listed);
        Assert.Equal("FAMAS", result.Gun?.Name);
        Assert.Empty(result.Warnings());

        var clip = connection.ReadClip(AkReload);
        var timing = GameTimings.For(connection, result, connection.Weapons.First(item => item.Id == "rifle_ak"), clip);
        Assert.NotNull(timing?.Refill);
        Assert.Null(timing.Loop);
        Assert.Equal(1.67, timing.Refill!.Value / clip.FramesPerSecond, precision: 1);
    }

    /// <summary>
    /// Fitting a shell reload's events to the gun's timing keeps each event's
    /// place within its part: every template event moves through the same
    /// piecewise map (0→0, 42→11, 50→20, 61→24, last→49), and the loop marks
    /// land on the new frames.
    /// </summary>
    [Fact]
    public void FittingMovesEveryEventWithinItsPart()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var template = connection.ReadClip(SsgReload);
        var from = new ShellLoop(42, 61, 50);
        var current = ShellReload.Apply(template, StoredEventEdits.Empty, from, silentReload: false);
        var target = new ShellLoop(11, 24, 20);
        var fitted = ShellReload.Fit(template, current, from, target, 49, silentReload: false);

        var last = template.LastFrame;
        var map = new FrameMap([(0, 0), (42, 11), (50, 20), (61, 24), (last, 49)]);
        var loopMarks = new[] { ShellReload.Intro, ShellReload.Loop, ShellReload.Outro, ShellReload.AddAmmo, ShellReload.Silent, ShellReload.DropMag };
        var changes = fitted.Changes.ToDictionary(change => change.Index);
        foreach (var clipEvent in template.Events.Where(clipEvent => !loopMarks.Contains(clipEvent.Name) && !fitted.List.Removed.Contains(clipEvent.Index)))
        {
            var before = current.Changes.FirstOrDefault(change => change.Index == clipEvent.Index)?.StartTime ?? clipEvent.StartTime;
            var after = changes.GetValueOrDefault(clipEvent.Index)?.StartTime ?? clipEvent.StartTime;
            Assert.Equal(map.Map(before * last), after * last, precision: 3);
        }

        var loop = ShellReload.Read(fitted.List.Sync!, [], last);
        Assert.Equal(11, loop!.LoopStart);
        Assert.Equal(24, loop.LoopEnd);
        var addAmmo = fitted.List.Added.FirstOrDefault(item => item.Name == ShellReload.AddAmmo)?.StartTime
            ?? fitted.Changes.First(change => template.Events[change.Index].Name == ShellReload.AddAmmo).StartTime;
        Assert.Equal(20, addAmmo!.Value * last, precision: 3);

        // The fitted edits still write.
        ClipWriter.Write(connection.Archive.ReadBytes(SsgReload), SsgReload, new ClipEdit { Events = fitted.Changes, EventList = fitted.List });
    }
}
