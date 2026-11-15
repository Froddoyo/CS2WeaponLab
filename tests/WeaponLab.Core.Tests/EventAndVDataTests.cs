using ValveKeyValue;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Tests;

public sealed class EventAndVDataTests : IDisposable
{
    private const string Reload = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    [Fact]
    public void SoundEventsCanBeRetimedAndRenamed()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(Reload);
        var before = ClipReader.Read(bytes, Reload);

        // The first two sound events, wherever the game puts them (CS2 1.41.9.3 reordered reload_ak's events).
        var sounds = before.Events.Where(clipEvent => clipEvent.Kind == ClipEventKind.Sound).Take(2).ToArray();
        var (moved, renamed) = (sounds[0].Index, sounds[1].Index);
        var frame = Math.Min(14, Math.Floor(before.LastFrame * (1 - sounds[0].Duration)));
        var edit = new ClipEdit { Events = [new EventChange(moved, null, frame / before.LastFrame), new EventChange(renamed, "WeaponLab.TestSound", null)] };

        var result = ClipWriter.Write(bytes, Reload, edit);
        var after = ClipReader.Read(result.Bytes, Reload);

        Assert.Equal([moved, renamed], result.ChangedEvents);
        Assert.Equal(frame, after.Events[moved].StartFrame(after), precision: 3);
        Assert.Equal("WeaponLab.TestSound", after.Events[renamed].Name);
        foreach (var index in Enumerable.Range(0, before.Events.Count).Except([moved, renamed]))
        {
            Assert.Equal(before.Events[index], after.Events[index]);
        }

        // Poses are untouched: an event-only edit re-encodes nothing.
        Assert.All(result.Streams, stream => Assert.Empty(stream.ChangedTracks));
    }

    [Fact]
    public void GameplayEventsCanBeRetimedButNotGivenUnknownFields()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(Reload);
        var before = ClipReader.Read(bytes, Reload);
        var dropMag = before.Events.First(clipEvent => clipEvent.Name == "WPN_DROP_MAG");

        var result = ClipWriter.Write(bytes, Reload, new ClipEdit { Events = [new EventChange(dropMag.Index, null, 0.5)] });
        Assert.Equal(0.5, ClipReader.Read(result.Bytes, Reload).Events[dropMag.Index].StartTime, precision: 6);

        // m_syncID is not one of the fields a gameplay event lets you change.
        var error = Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, Reload, new ClipEdit
        {
            Events = [new EventChange(dropMag.Index, null, null) { Fields = new Dictionary<string, string> { ["m_syncID"] = "x" } }],
        }));
        Assert.Contains("cannot be changed", error.Message);
    }

    [Fact]
    public void LegacyEventsAreReadOnly()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Revolver = "animation/anims/viewmodel/pistol/pistol_revolver/prepare_shoot_revolver.vnmclip_c";
        var bytes = connection.Archive.ReadBytes(Revolver);
        var legacy = ClipReader.Read(bytes, Revolver).Events.First(clipEvent => clipEvent.Kind == ClipEventKind.Legacy);

        var error = Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, Revolver, new ClipEdit { Events = [new EventChange(legacy.Index, null, 0.5)] }));
        Assert.Contains("read-only", error.Message);
    }

    [Fact]
    public void ParticleEventsCanPlayAnotherSystem()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Draw = "animation/anims/viewmodel/equipment/c4/draw_c4.vnmclip_c";
        const string System = "particles/weaponlab/test_flash.vpcf";
        var bytes = connection.Archive.ReadBytes(Draw);
        var before = ClipReader.Read(bytes, Draw);
        var particle = before.Events.First(clipEvent => clipEvent.Kind == ClipEventKind.Particle);

        var result = ClipWriter.Write(bytes, Draw, new ClipEdit
        {
            Events = [new EventChange(particle.Index, System, null) { Fields = new Dictionary<string, string> { ["m_bDetachFromOwner"] = "true" } }],
        });

        var after = ClipReader.Read(result.Bytes, Draw);
        Assert.Equal(System, after.Events[particle.Index].Name);
        Assert.Equal("true", after.Events[particle.Index].Fields["m_bDetachFromOwner"]);
        Assert.Equal(particle.Fields["m_attachmentPoint0"], after.Events[particle.Index].Fields["m_attachmentPoint0"]);
        foreach (var index in Enumerable.Range(0, before.Events.Count).Except([particle.Index]))
        {
            Assert.Equal(before.Events[index], after.Events[index]);
        }

        // The new system is in the clip's external references, so the game loads it.
        using var resource = new ValveResourceFormat.Resource { FileName = "draw_c4.vnmclip_c" };
        resource.Read(new MemoryStream(result.Bytes));
        Assert.Contains(resource.ExternalReferences!.ResourceRefInfoList, reference => reference.Name == System);

        // A resource path needs a folder and an extension.
        Assert.Throws<ClipWriteException>(() => ClipWriter.Write(bytes, Draw, new ClipEdit { Events = [new EventChange(particle.Index, "not a path", null)] }));
    }

    [Fact]
    public void UnchangedEventValuesKeepTheClipByteIdentical()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(Reload);
        var original = ClipReader.Read(bytes, Reload).Events[2];
        var edit = new ClipEdit { Events = [new EventChange(2, original.Name, original.StartTime)] };

        Assert.True(ClipWriter.Write(bytes, Reload, edit).IdenticalToTemplate);
    }

    [Fact]
    public void GameVDataHasInheritanceAndSkeletonLinks()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var vdata = connection.ReadVData();
        var ak = vdata["weapon_ak47"]!;

        Assert.Equal(["rifle", "primary", "weapon_base"], vdata.BaseChain(ak).Select(block => block.Name));
        Assert.Equal(["weapon_ak47"], vdata.BlocksForSkeleton("animation/skeletons/weapons/ak47.vnmskel").Select(block => block.Name));

        var rows = VDataView.Rows(vdata, ak, addon: null, overrides: null);
        Assert.Equal(VDataSource.SetHere, rows.Single(row => row.Path == "m_nDamage").Source);
        var killAward = rows.Single(row => row.Path == "m_nKillAward");
        Assert.Equal(VDataSource.Inherited, killAward.Source);
        Assert.Equal("rifle", killAward.InheritedFrom);
        Assert.Contains(rows, row => row.Path == "m_flMaxSpeed[0]");
    }

    [Theory]
    [InlineData(KVValueType.Int32, "40", "40", null)]
    [InlineData(KVValueType.Int32, "4.5", null, "Use a whole number.")]
    [InlineData(KVValueType.FloatingPoint64, " 0.25 ", "0.25", null)]
    [InlineData(KVValueType.Boolean, "1", "true", null)]
    [InlineData(KVValueType.Boolean, "maybe", null, "Use true or false.")]
    [InlineData(KVValueType.UInt32, "-1", null, "Use a whole number of 0 or more.")]
    public void EditsAreCheckedAgainstTheOriginalType(KVValueType type, string input, string? value, string? error)
    {
        Assert.Equal((value, error), VDataView.Normalize(type, input));
    }

    [Fact]
    public void ProfileVDataPersistsAndStockValuesRemoveIt()
    {
        var profiles = new Profiles.ProfileStore(_root);
        var profile = profiles.LoadActive();
        var overrides = VDataOverrides.For(profile, () => profiles.Save(profile));
        overrides.Set("weapon_ak47", "m_nDamage", "40", stockValue: "36");
        overrides.Set("weapon_ak47", "m_iMaxClip1", "35", stockValue: "30");

        var reloaded = profiles.LoadActive();
        Assert.Equal("40", reloaded.VData["weapon_ak47"]["m_nDamage"]);

        overrides.Set("weapon_ak47", "m_nDamage", "36", stockValue: "36");
        Assert.Equal(["m_iMaxClip1"], profiles.LoadActive().VData["weapon_ak47"].Keys);
    }

    [Fact]
    public void FirstProfileTakesOverLegacyVDataEdits()
    {
        Directory.CreateDirectory(Path.Combine(_root, "vdata"));
        File.WriteAllText(VDataOverrides.PathFor(_root), """{ "schema": "weaponlab.vdata-overrides/1", "blocks": { "weapon_ak47": { "m_nDamage": "40" } } }""");

        var profile = new Profiles.ProfileStore(_root).LoadActive();

        Assert.Equal(Profiles.ProfileStore.DefaultName, profile.Name);
        Assert.Equal("40", profile.VData["weapon_ak47"]["m_nDamage"]);
    }

    [Fact]
    public void ProfilesRoundTripAndSwitch()
    {
        var profiles = new Profiles.ProfileStore(_root);
        var first = profiles.LoadActive();
        first.TargetAddon = "weapon_template";
        first.EnsureWeapon("rifle_ak").Animations = Profiles.AnimationMode.Default;
        first.EnsureWeapon("rifle_ak").PreviewModel = "addon:cs_hl_template:models/weapons/weapon_1911/weapon_1911.vmdl_c";
        profiles.Save(first);
        profiles.Save(new Profiles.Profile { Name = "Fast AK" });
        profiles.SetActive("Fast AK");

        Assert.Equal(["Default", "Fast AK"], profiles.Names());
        Assert.Equal("Fast AK", profiles.LoadActive().Name);
        var reloaded = profiles.Load("Default");
        Assert.Equal("weapon_template", reloaded.TargetAddon);
        Assert.Equal(Profiles.AnimationMode.Default, reloaded.Weapon("rifle_ak").Animations);
        Assert.Contains("\"default\"", File.ReadAllText(profiles.PathFor("Default")), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Profiles are local workspaces: each has its own folder with its own
    /// sandboxes. The 0.13–0.14 layout (one file per addon, shared sandboxes)
    /// moves over, the shared sandboxes going to the active profile. Rename
    /// and delete move whole folders; delete keeps a backup.
    /// </summary>
    [Fact]
    public void ProfilesAreLocalWorkspacesWithTheirOwnSandboxes()
    {
        // The old layout.
        var folder = Path.Combine(_root, "profiles");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "addon_a.wlprofile.json"), Profiles.ProfileStore.ToJson(new Profiles.Profile { Name = "addon_a", TargetAddon = "addon_a" }));
        File.WriteAllText(Path.Combine(folder, "addon_b.wlprofile.json"), Profiles.ProfileStore.ToJson(new Profiles.Profile { Name = "addon_b", TargetAddon = "addon_b" }));
        File.WriteAllText(Path.Combine(folder, "active.txt"), "addon_b");
        Directory.CreateDirectory(Path.Combine(_root, "sandboxes", "rifle_ak"));
        File.WriteAllText(Path.Combine(_root, "sandboxes", "rifle_ak", "sandbox.json"), "{}");

        var profiles = new Profiles.ProfileStore(_root);
        var moved = profiles.MigrateLegacy();
        Assert.Equal(3, moved.Count);
        Assert.Equal(["addon_a", "addon_b"], profiles.Names());
        Assert.Equal("addon_b", profiles.LoadActive().Name);
        Assert.True(File.Exists(Path.Combine(profiles.SandboxesPathFor("addon_b"), "rifle_ak", "sandbox.json")));
        Assert.False(Directory.Exists(Path.Combine(_root, "sandboxes")));
        Assert.Empty(profiles.MigrateLegacy());

        // A new profile is an empty workspace, separate from the others.
        var second = profiles.Create("Second", "addon_a");
        Assert.Equal("Second", profiles.LoadActive().Name);
        Assert.Empty(profiles.SandboxesFor("Second").List());
        Assert.Throws<ArgumentException>(() => profiles.Create("Second", null));
        second.RecordPushed("addon_a", "animation/anims/custom/rifle_ak/idle_ak.vnmclip_c", "abc");
        profiles.Save(second);

        var renamed = profiles.Rename("Second", "Third");
        Assert.Equal("Third", renamed.Name);
        Assert.True(renamed.WasPushed("addon_a", "animation/anims/custom/rifle_ak/idle_ak.vnmclip_c", "abc"));
        Assert.Equal("Third", profiles.ActiveName);
        Assert.False(profiles.Exists("Second"));

        var backup = profiles.Delete("Third");
        Assert.True(File.Exists(Path.Combine(backup, Profiles.ProfileStore.FileName)));
        Assert.Equal(["addon_a", "addon_b"], profiles.Names());
        Assert.Throws<ArgumentException>(() => profiles.FolderFor("../escape"));
    }

    [Theory]
    [InlineData("m_nDamage", "Damage", "Damage per bullet")]
    [InlineData("m_flMaxSpeed[1]", "Movement", "Run speed (alt/scoped)")]
    [InlineData("taxonomy.rifle", "Identity", "Taxonomy · rifle")]
    [InlineData("m_someNewKey", "Other", "m_someNewKey")]
    public void GlossaryGivesFriendlyNames(string path, string group, string name)
    {
        var term = VDataGlossary.Describe(path);
        Assert.Equal(group, term.Group);
        Assert.Equal(name, term.Name);
    }
}
