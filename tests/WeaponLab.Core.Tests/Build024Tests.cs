using System.Globalization;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Graphs;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Settings;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Tests;

/// <summary>0.24: the expected class and its timing bands, variations (rare draws/inspects, random reload) and settings. Game tests skip without CS2.</summary>
public sealed class Build024Tests
{
    private const string SsgReload = "animation/anims/viewmodel/rifle/rifle_ssg08/reload_ssg08.vnmclip_c";
    private const string AkGunGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
    private const string AkInspects = "animation/graphs/viewmodel/viewmodel_inspects.vnmgraph+ak47.vnmgraph";

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    /// <summary>A picked gun wins over the vdata's for display, and the Nova's reload gives intro, loop and outro bands on the clip's frames.</summary>
    [Fact]
    public void PickedGunGivesItsReloadTimingAsBands()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var ssg = connection.FindWeapon("rifle_ssg08")!;
        var fromVData = GameTimings.Resolve(connection, ssg, null, null, null);
        Assert.NotNull(fromVData);
        Assert.False(fromVData.Chosen);
        Assert.Equal("SSG08", fromVData.Gun.Name);

        var picked = GameTimings.Resolve(connection, ssg, null, null, "Shotguns/Nova");
        Assert.NotNull(picked);
        Assert.True(picked.Chosen);
        Assert.Equal("Shotguns/Nova", picked.Key);
        Assert.Contains("picked here", picked.Describe());

        var timing = GameTimings.For(connection, picked.Gun, ssg, connection.ReadClip(SsgReload));
        Assert.NotNull(timing);
        Assert.Equal(["intro", "loop", "outro", "done"], timing.Bands.Select(band => band.Kind));
        Assert.Equal(11, timing.Bands[1].Start);
        Assert.Equal(24, timing.Bands[1].End);

        // A clip with no shell loop of its own, timed against a shell-by-shell reload: one plain warning.
        var warnings = GameTimings.Warnings(timing, connection.ReadClip(SsgReload));
        Assert.Contains(warnings, warning => warning.Contains("no shell loop", StringComparison.Ordinal));

        // An unknown pick falls back to the vdata's gun.
        Assert.False(GameTimings.Resolve(connection, ssg, null, null, "Shotguns/NoSuchGun")!.Chosen);
    }

    /// <summary>The AK's weighted choices and PC2's random-reload recipe (reload #62, spare #214 freed by pointing state #213 at #210, reserve #0).</summary>
    [Fact]
    public void AkVariationsMatchTheVerifiedRecipe()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var profile = new Profile { Name = "test" };
        var graphs = Variations.Graphs(connection, profile, null, connection.FindWeapon("rifle_ak")!);
        var groups = Variations.Groups(graphs, profile);
        var draw = Assert.Single(groups, group => group.Kind == VariationKind.Draw);
        Assert.Equal([210, 214], draw.Selectors);
        Assert.Equal("draw_ak", Path.GetFileNameWithoutExtension(draw.Options[0].Clip));
        Assert.Null(draw.Options[1].Clip);

        var inspect = Assert.Single(groups, group => group.Name == "Inspect · Default");
        Assert.Equal([5.0, 1.0, 0.0], inspect.Options.Select(option => option.Weight));
        Assert.Null(inspect.Options[2].Clip); // The empty third option: the place for a rare inspect.

        var rare = Variations.EditsFor(inspect, 2, "animation/anims/viewmodel/rifle/rifle_ak/lookat01_ak.vnmclip", 1);
        Assert.Equal("1", rare["8:m_optionWeights[2]"]);
        Assert.Equal("1", rare["40:m_optionWeights[2]"]);
        Assert.Equal("animation/anims/viewmodel/rifle/rifle_ak/lookat01_ak.vnmclip", rare["11:clip"]);

        var reload = Variations.RandomReloadFor(graphs, profile);
        Assert.NotNull(reload);
        Assert.Equal((62, 214, 213, 210, 0), (reload.ReloadSelector, reload.Spare, reload.SpareState, reload.Keep, reload.ReserveParameter));
        Assert.False(reload.On);
    }

    /// <summary>The random-reload edits patch CS2's AK graph (the patcher verifies everything else stays), and the graph reads back rewired.</summary>
    [Fact]
    public void RandomReloadEditsPatchTheAkGraph()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var profile = new Profile { Name = "test" };
        var graphs = Variations.Graphs(connection, profile, null, connection.FindWeapon("rifle_ak")!);
        var reload = Variations.RandomReloadFor(graphs, profile)!;
        const string Second = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak_lgcy.vnmclip";
        var edits = Variations.RandomReloadEdits(reload, "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip", Second, 1, 1);
        var (bytes, _) = GraphPatcher.Patch(connection.Archive.ReadBytes(AkGunGraph + "_c"), AkGunGraph + "_c", new Dictionary<string, string>(), edits);

        var patched = GraphDocument.Read(bytes, AkGunGraph + "_c");
        Assert.Equal("214", patched[62].Value("m_optionNodeIndices[0]"));
        Assert.Equal("210", patched[213].Value("m_nChildNodeIdx"));
        Assert.Equal("0", patched[214].Value("m_parameterNodeIdx"));
        Assert.Equal(Second, patched.Resources[patched[216].DataSlot]);

        profile.Graphs[AkGunGraph] = new SortedDictionary<string, string>(edits.ToDictionary(), StringComparer.Ordinal);
        var after = Variations.RandomReloadFor(graphs, profile)!;
        Assert.True(after.On);
        Assert.DoesNotContain(Variations.Groups(graphs, profile), group => group.Kind == VariationKind.Draw && group.Selectors.Contains(214)); // Freed: no state plays it.
    }

    /// <summary>A rare inspect patches the AK's inspect graph: the empty option gets the clip (added to the graph's resources) and its weight.</summary>
    [Fact]
    public void RareInspectPatchesTheInspectGraph()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var profile = new Profile { Name = "test" };
        var graphs = Variations.Graphs(connection, profile, null, connection.FindWeapon("rifle_ak")!);
        var inspect = Variations.Groups(graphs, profile).Single(group => group.Name == "Inspect · Default");
        const string Rare = "animation/anims/viewmodel/rifle/rifle_ak/lookat02_ak.vnmclip";
        var (bytes, _) = GraphPatcher.Patch(connection.Archive.ReadBytes(AkInspects + "_c"), AkInspects + "_c", new Dictionary<string, string>(),
            Variations.EditsFor(inspect, 2, Rare, 1));
        var patched = GraphDocument.Read(bytes, AkInspects + "_c");
        Assert.Equal(Rare, patched.Resources[patched[11].DataSlot]);
        Assert.Equal(1, double.Parse(patched[8].Value("m_optionWeights[2]")!, CultureInfo.InvariantCulture));
        Assert.Equal(1, double.Parse(patched[40].Value("m_optionWeights[2]")!, CultureInfo.InvariantCulture));
    }

    /// <summary>Settings: the workspace picked in Settings wins; the environment variable still wins over it.</summary>
    [Fact]
    public void WorkspaceFollowsSettings()
    {
        if (Environment.GetEnvironmentVariable("WEAPONLAB_WORKSPACE") is { Length: > 0 })
        {
            Assert.Skip("WEAPONLAB_WORKSPACE is set on this machine.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "weaponlab-settings-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("WEAPONLAB_SETTINGS");
        try
        {
            Environment.SetEnvironmentVariable("WEAPONLAB_SETTINGS", Path.Combine(folder, "settings.json"));
            Assert.False(AppSettings.Exists);
            new AppSettings { WorkspaceFolder = Path.Combine(folder, "ws"), BlendFolder = Path.Combine(folder, "blends") }.Save();
            Assert.Equal(Path.Combine(folder, "ws"), Sandbox.SandboxStore.DefaultWorkspacePath());
            Assert.Equal(Path.Combine(folder, "blends"), AppSettings.Load().BlendFolder);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WEAPONLAB_SETTINGS", previous);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }
}
