using WeaponLab.Core.Checks;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Tests;

/// <summary>Revert to addon default, kept copies of pushed clips, and Show targets of delivery messages (0.21).</summary>
public sealed class RevertTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    private static void MoveFirstEvent(SandboxInfo sandbox, double start)
    {
        EventEdits.Save(sandbox, "reload_ak", [new EventChange(0, null, start)]);
        EventEdits.Apply(sandbox, "reload_ak");
    }

    /// <summary>
    /// An unpushed clip is set aside; once pushed, a copy is kept, and a later
    /// unpushed change reverts to exactly the addon's version. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task ClipRevertsToTheVersionTheAddonHas()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profile { Name = "test" };
        string[] blocks = ["weapon_ak47"];

        // Not pushed yet: the addon has no version, so the work is set aside.
        MoveFirstEvent(sandbox, 0.05);
        var before = AddonRevert.Plan(profile, store, connection, target, "rifle_ak", blocks, RevertParts.Clips);
        var setAside = Assert.Single(before);
        Assert.True(setAside.CanRevert);
        Assert.False(setAside.RestoresKeptCopy);
        AddonRevert.Apply(profile, store, "rifle_ak", [setAside], Path.Combine(_root, "backup1"));
        Assert.Null(ClipExporter.LastExport(sandbox, "reload_ak"));
        Assert.False(File.Exists(EventEdits.PathFor(sandbox, "reload_ak")));
        Assert.True(File.Exists(Path.Combine(_root, "backup1", "events", "reload_ak.json")));

        // Pushed: the addon holds it, and a copy is kept.
        MoveFirstEvent(sandbox, 0.05);
        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        var result = await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: TestContext.Current.CancellationToken);
        plan.RecordIn(profile);
        Assert.Equal(1, PendingWork.KeepLive(profile, store, target));
        Assert.Equal(0, PendingWork.KeepLive(profile, store, target)); // The same copy is not made twice.
        var pushedSha = ClipExporter.LastExport(sandbox, "reload_ak")!.OutputSha256;
        Assert.Equal(pushedSha, PushedClips.KeptSha(sandbox, "reload_ak"));

        // Changed again and not pushed: revert puts the pushed version back.
        MoveFirstEvent(sandbox, 0.1);
        Assert.Equal(PushState.Pending, Assert.Single(PendingWork.Clips(profile, sandbox, target)).State);
        var restore = Assert.Single(AddonRevert.Plan(profile, store, connection, target, "rifle_ak", blocks, RevertParts.All, onlyClip: "reload_ak"));
        Assert.True(restore.RestoresKeptCopy);
        AddonRevert.Apply(profile, store, "rifle_ak", [restore], Path.Combine(_root, "backup2"));

        Assert.Equal(PushState.Live, Assert.Single(PendingWork.Clips(profile, sandbox, target)).State);
        Assert.Equal(0.05, EventEdits.Read(sandbox, "reload_ak").Changes.Single().StartTime);
        Assert.Equal(0.1, EventEdits.Read(new SandboxInfo(Path.Combine(_root, "backup2"), sandbox.Manifest), "reload_ak").Changes.Single().StartTime);

        AddonDelivery.Rollback(result.BackupFolder);
    }

    /// <summary>
    /// One clip set to CS2 default: the next push removes the addon's copy and its graph plays CS2's
    /// clip again; the profile's list is cleared once pushed. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task OneClipGoesBackToCs2s()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profile { Name = "test" };

        MoveFirstEvent(sandbox, 0.05);
        var first = DeliveryPlanner.Plan(store, connection, target, profile);
        var pushed = await AddonDelivery.DeliverAsync(first, connection.Install, workspace, cancellation: TestContext.Current.CancellationToken);
        first.RecordIn(profile);
        const string custom = "animation/anims/custom/rifle_ak/reload_ak.vnmclip";
        var clipFile = AddonManifest.Resolve(target, custom + "_c");
        Assert.True(File.Exists(clipFile));

        PushedClips.SetAside(sandbox, "reload_ak", Path.Combine(_root, "backup"));
        profile.EnsureWeapon("rifle_ak").StockClips.Add("reload_ak");
        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Contains(clipFile, plan.Removals, StringComparer.OrdinalIgnoreCase);
        Assert.Equal([("rifle_ak", "reload_ak")], plan.StockClips);
        var graph = Assert.Single(plan.Files, file => file.ManifestKey.Contains("+ak47.vnmgraph_c", StringComparison.Ordinal));
        Assert.True(graph.Bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(custom)) < 0, "the graph still plays the removed clip");
        Assert.True(graph.Bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip")) >= 0);

        var second = await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: TestContext.Current.CancellationToken);
        plan.RecordIn(profile);
        Assert.False(File.Exists(clipFile));
        Assert.Empty(profile.Weapon("rifle_ak").StockClips);

        AddonDelivery.Rollback(second.BackupFolder);
        AddonDelivery.Rollback(pushed.BackupFolder);
    }

    /// <summary>Graph edits and vdata values the addon does not have are dropped. Skipped without CS2.</summary>
    [Fact]
    public void GraphEditsAndVDataValuesRevertInTheProfile()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var store = new SandboxStore(Path.Combine(_root, "workspace"));
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        const string graph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
        var profile = new Profile { Name = "test" };
        profile.Graphs[graph] = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["5:m_flSpeedMultiplier"] = "2" };
        profile.VData["weapon_ak47"] = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["m_nDamage"] = "40" };

        var items = AddonRevert.Plan(profile, store, connection, target, "rifle_ak", ["weapon_ak47"], RevertParts.All);
        var graphItem = Assert.Single(items, item => item.Kind == RevertKind.Graph);
        Assert.Equal(["5:m_flSpeedMultiplier"], graphItem.GraphKeys);
        var value = Assert.Single(items, item => item.Kind == RevertKind.VData);
        Assert.Null(value.Value); // The addon does not set it, so the edit goes.

        AddonRevert.Apply(profile, store, "rifle_ak", items, Path.Combine(_root, "backup"));
        Assert.Empty(profile.Graphs);
        Assert.Empty(profile.VData);
    }

    /// <summary>
    /// A custom skeleton the addon does not have is one revert item that takes the clips only re-targeted to it
    /// along. Before 0.22.5 each of those clips showed greyed out and nothing could be reverted (user's galil,
    /// 2026-11-07). Skipped without CS2.
    /// </summary>
    [Fact]
    public void CustomSkeletonTheAddonLacksRevertsWithItsClips()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Model = "game:weapons/models/ak47/weapon_rif_ak47.vmdl_c";
        var store = new SandboxStore(Path.Combine(_root, "workspace"));
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        var setup = Skeletons.CustomSkeletons.Create(sandbox, connection, Model, targetAddon: null);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profile { Name = "test" };
        profile.EnsureWeapon("rifle_ak").CustomSkeleton = new CustomSkeletonChoice(Model, setup.Path);
        profile.EnsureWeapon("rifle_ak").PreviewModel = Model;
        string[] blocks = ["weapon_ak47"];
        var retargeted = PendingWork.Clips(profile, sandbox, target).Where(clip => clip.State == PushState.Pending).ToList();
        Assert.NotEmpty(retargeted);
        Assert.All(retargeted, clip => Assert.StartsWith("re-targeted", clip.Changes));

        // The weapon's row: the skeleton alone, standing for every clip re-targeted to it.
        var items = AddonRevert.Plan(profile, store, connection, target, "rifle_ak", blocks, RevertParts.Clips | RevertParts.Skeleton);
        var skeleton = Assert.Single(items);
        Assert.Equal(RevertKind.Skeleton, skeleton.Kind);
        Assert.True(skeleton.CanRevert);
        Assert.Null(skeleton.AdoptsAddonSkeleton); // The fake addon gives the AK no skeleton: back to CS2's.
        Assert.Contains($"Its {retargeted.Count} clip(s)", skeleton.Detail);

        // One clip's row cannot drop the skeleton, and says which row can.
        var clip = Assert.Single(AddonRevert.Plan(profile, store, connection, target, "rifle_ak", blocks, RevertParts.All, onlyClip: retargeted[0].ClipId));
        Assert.False(clip.CanRevert);
        Assert.Contains("Skeleton row", clip.Detail);

        var backup = Path.Combine(_root, "backup");
        Assert.Throws<ArgumentNullException>(() => AddonRevert.Apply(profile, store, "rifle_ak", items, backup));
        var done = AddonRevert.Apply(profile, store, "rifle_ak", items, backup, connection);
        Assert.Contains("back on CS2's default skeleton", Assert.Single(done));
        Assert.Null(Skeletons.CustomSkeletons.Load(sandbox));
        Assert.Null(profile.Weapon("rifle_ak").CustomSkeleton);
        Assert.Null(profile.Weapon("rifle_ak").PreviewModel);
        Assert.True(File.Exists(Path.Combine(backup, Skeletons.CustomSkeletons.Folder, "skeleton.vnmskel_c")));
        Assert.DoesNotContain(PendingWork.Clips(profile, sandbox, target), item => item.State == PushState.Pending);
        Assert.Empty(AddonRevert.Plan(profile, store, connection, target, "rifle_ak", blocks, RevertParts.All));
    }

    private static readonly WeaponEntry Ssg08 = new("rifle_ssg08", "rifle", "animation/anims/viewmodel/rifle/rifle_ssg08",
    [
        "animation/anims/viewmodel/rifle/rifle_ssg08/shoot1_ssg08.vnmclip_c",
        "animation/anims/viewmodel/rifle/rifle_ssg08/shoot1_ssg08_lgcy.vnmclip_c",
    ]);

    private static readonly string[] Graphs = ["animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ssg08_legacy.vnmgraph"];

    [Fact]
    public void ShowFindsTheGraphNodeOfADeliveryError()
    {
        // The 2026-11-02 Deliver error, as GraphPatcher words it.
        const string error = "Delivery failed and was rolled back: Graph node 29 value clip did not take the edit "
            + "(animation/anims/viewmodel/rifle/rifle_ssg08/shoot1_ssg08_lgcy.vnmclip) in animation/graphs/viewmodel/viewmodel_gun_custom.vnmgraph+ssg08_legacy.vnmgraph_c.";
        var target = CheckTargets.Find(error, [Ssg08], Graphs);
        Assert.NotNull(target);
        Assert.Equal(Graphs[0], target.Graph);
        Assert.Equal(29, target.Node);
    }

    [Fact]
    public void ShowFindsTheLongestClipThenTheWeaponThenItsVData()
    {
        var clip = CheckTargets.Find("shoot1_ssg08_lgcy is not in the addon in its latest version.", [Ssg08], Graphs);
        Assert.Equal(Ssg08.ClipPaths[1], clip?.ClipPath);
        Assert.Equal("rifle_ssg08", clip?.WeaponId);

        var weapon = CheckTargets.Find("rifle_ssg08's sandbox has a custom skeleton the profile does not use.", [Ssg08], Graphs);
        Assert.Equal("rifle_ssg08", weapon?.WeaponId);
        Assert.Null(weapon?.ClipPath);

        var vdata = CheckTargets.Find("the model does not target its skeleton", [Ssg08], Graphs, _ => ["weapon_ssg08"], subject: "weapon_ssg08");
        Assert.True(vdata?.VData);
        Assert.Equal("rifle_ssg08", vdata?.WeaponId);

        Assert.Null(CheckTargets.Find("resourcecompiler finished but wrote no weapons.vdata_c.", [Ssg08], Graphs));
    }
}
