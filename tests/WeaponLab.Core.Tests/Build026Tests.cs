using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Tests;

/// <summary>0.26 third person: world clips beside each weapon, their ids and addon paths, the worldmodel graph tree, the noise allowance.</summary>
public sealed class Build026Tests
{
    private const string ViewReload = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c";
    private const string WorldReload = "animation/anims/world/rifle/rifle_ak/reload_ak.vnmclip_c";

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    [Fact]
    public void WeaponsGetTheWorldClipsOfTheirFolder()
    {
        var weapons = WeaponCatalog.FromPaths([
            ViewReload,
            "animation/anims/viewmodel/rifle/rifle_ak/idle_ak.vnmclip_c",
            "animation/anims/viewmodel/pistol/pistol_glock18/idle_glock.vnmclip_c",
            WorldReload,
            "animation/anims/world/rifle/rifle_ak/idle_ak.vnmclip+non_additive.vnmclip_c",
            "animation/anims/world/rifle/rifle_ak/sub/deeper.vnmclip_c", // another folder
            "animation/anims/world/loose.vnmclip_c", // straight in the world root
        ]);

        var ak = weapons.Single(weapon => weapon.Id == "rifle_ak");
        Assert.Equal(["animation/anims/world/rifle/rifle_ak/idle_ak.vnmclip+non_additive.vnmclip_c", WorldReload], ak.WorldClipPaths);
        Assert.Empty(weapons.Single(weapon => weapon.Id == "pistol_glock18").WorldClipPaths);
    }

    [Fact]
    public void WorldClipsGetTheirOwnIdsAndAddonFolder()
    {
        Assert.Equal("reload_ak", RigPackage.ClipId(ViewReload));
        Assert.Equal("world_reload_ak", RigPackage.ClipId(WorldReload));
        Assert.True(RigPackage.IsWorldId("world_reload_ak"));
        Assert.False(RigPackage.IsWorldId("reload_ak"));
        Assert.True(WeaponCatalog.IsWorldClip(WorldReload));
        Assert.False(WeaponCatalog.IsWorldClip(ViewReload));

        Assert.Equal("animation/anims/custom/rifle_ak/reload_ak.vnmclip", GraphTree.CustomClipPath("rifle_ak", ViewReload[..^2]));
        Assert.Equal("animation/anims/custom/rifle_ak/world/reload_ak.vnmclip", GraphTree.CustomClipPath("rifle_ak", WorldReload[..^2]));
    }

    /// <summary>A third-person clip clones the worldmodel root and the gun variation that plays it; first-person work never clones it.</summary>
    [Fact]
    public void WorldClipsRouteThroughTheWorldmodelRoot()
    {
        GraphInfo Graph(string name, params string[] resources) => new(name, resources, "");
        var gun = GraphTree.GraphRoot + "viewmodel_gun.vnmgraph+ak47.vnmgraph";
        var worldGun = GraphTree.WorldGraphRoot + "worldmodel_gun.vnmgraph+ak47.vnmgraph";
        var locomotion = GraphTree.WorldGraphRoot + "worldmodel_locomotion.vnmgraph+rifle.vnmgraph";
        var graphs = new[]
        {
            Graph(GraphTree.Parent, gun),
            Graph(gun, ViewReload[..^2]),
            Graph(GraphTree.WorldParent, worldGun, locomotion, "animation/anims/world/rifle/rifle_ak/idle_ak.vnmclip"),
            Graph(worldGun, WorldReload[..^2]),
            Graph(locomotion, "animation/anims/world/rifle/_default_rifle/run.vnmclip"),
        }.ToDictionary(graph => graph.Path);

        var world = GraphTree.Plan(graphs, new Dictionary<string, string> { [WorldReload[..^2]] = GraphTree.CustomClipPath("rifle_ak", WorldReload[..^2]) });
        Assert.Equal([GraphTree.Parent, GraphTree.WorldParent, worldGun], world.Graphs.Select(graph => graph.Path).Order(StringComparer.Ordinal));
        Assert.Equal(GraphTree.WorldCustomParent, world.ResourceMap[GraphTree.WorldParent]);
        Assert.Equal(GraphTree.WorldGraphRoot + "worldmodel_gun_custom.vnmgraph+ak47.vnmgraph", world.ResourceMap[worldGun]);
        Assert.False(world.ResourceMap.ContainsKey(locomotion));
        Assert.Empty(world.UnreferencedClips);

        var first = GraphTree.Plan(graphs, new Dictionary<string, string> { [ViewReload[..^2]] = GraphTree.CustomClipPath("rifle_ak", ViewReload[..^2]) });
        Assert.Equal([GraphTree.Parent, gun], first.Graphs.Select(graph => graph.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OnlyFullWorldClipsWithOneWeaponStreamAreEditable()
    {
        ClipInfo Clip(string path, bool additive, params string[] secondaries) =>
            new(path, "", 74, 2.4, additive, ClipInfo.WorldSkeleton, secondaries, 75, []);
        Assert.True(Clip(WorldReload, false, "animation/skeletons/weapons/ak47.vnmskel").IsEditableWorld);
        Assert.False(Clip("animation/anims/world/rifle/rifle_ak/idle_ak.vnmclip_c", true).IsEditableWorld);
        Assert.False(Clip("animation/anims/world/rifle/rifle_ak/shoot_ak.vnmclip+non_additive.vnmclip_c", false, "animation/skeletons/weapons/ak47.vnmskel").IsEditableWorld);
        Assert.False(Clip(WorldReload, false).IsEditable); // Not the viewmodel rig.
    }

    /// <summary>Sandboxes from before 0.26 have no worldClips in sandbox.json; they read with none, and clips are found by id in either list.</summary>
    [Fact]
    public void OlderSandboxesReadWithoutWorldClips()
    {
        var older = """
            {"schema":"weaponlab.sandbox/1","weaponId":"rifle_ak","category":"rifle","vpkFolder":"animation/anims/viewmodel/rifle/rifle_ak",
             "createdUtc":"2026-11-01T00:00:00Z","game":{"patchVersion":"1","clientVersion":"1","sourceRevision":"1"},
             "clips":[{"resourcePath":"animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c","template":"templates/x","sha256":"a","frameCount":74,"durationSeconds":2.4,"editable":true}]}
            """;
        var manifest = SandboxManifest.FromJson(older);
        Assert.Empty(manifest.WorldClips);
        Assert.NotNull(manifest.FindClip("reload_ak"));
        Assert.Null(manifest.FindClip("world_reload_ak"));

        var withWorld = manifest with { WorldClips = [new SandboxClip(WorldReload, "templates/y", "b", 74, 2.4, true)] };
        var again = SandboxManifest.FromJson(withWorld.ToJson());
        Assert.Equal(WorldReload, again.FindClip("world_reload_ak")?.ResourcePath);
        Assert.Equal(2, again.AllClips().Count());
    }

    [Fact]
    public void ThirdPersonNoiseAllowanceKeepsSmallDriftUnedited()
    {
        var stock = new BonePose(new(10, 0, 60), System.Numerics.Quaternion.Identity, 1);
        var drifted = stock with { Translation = new(10.00005f, 0, 60) }; // 5e-5 units: float noise 60 units out
        Assert.NotNull(EditDeltas.ApplyOne(stock, stock, drifted, additive: false));
        Assert.Null(EditDeltas.ApplyOne(stock, stock, drifted, additive: false, EditDeltas.WorldTolerance));
        var moved = stock with { Translation = new(10.01f, 0, 60) };
        Assert.NotNull(EditDeltas.ApplyOne(stock, stock, moved, additive: false, EditDeltas.WorldTolerance));
    }

    /// <summary>CS2's AK has its third-person clips where the catalog looks (read 2026-11-14): 6 of 12 editable.</summary>
    [Fact]
    public void TheGameAkHasSixEditableWorldClips()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var ak = connection.FindWeapon("rifle_ak") ?? throw new InvalidOperationException("No rifle_ak.");
        Assert.Equal(12, ak.WorldClipPaths.Count);
        var editable = ak.WorldClipPaths.Select(path => ClipReader.Read(connection.Archive.ReadBytes(path), path)).Where(clip => clip.IsEditableWorld).Select(clip => clip.Name).Order(StringComparer.Ordinal);
        Assert.Equal(["draw_ak", "draw_crouch_ak", "reload_ak", "reload_ak_silent", "reload_crouch_ak", "reload_crouch_ak_silent"], editable);

        var graphs = GraphTree.Load(connection.Archive, includeWorld: true);
        var plan = GraphTree.Plan(graphs, new Dictionary<string, string> { [WorldReload[..^2]] = GraphTree.CustomClipPath("rifle_ak", WorldReload[..^2]) });
        Assert.Contains(plan.Graphs, graph => graph.Path == GraphTree.WorldParent);
        Assert.Contains(plan.Graphs, graph => graph.Path == GraphTree.WorldGraphRoot + "worldmodel_gun.vnmgraph+ak47.vnmgraph");
    }
}
