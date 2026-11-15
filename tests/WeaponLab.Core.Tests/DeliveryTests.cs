using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Tests;

public sealed class DeliveryTests : IDisposable
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

    [Theory]
    [InlineData("animation/graphs/viewmodel/viewmodel.vnmgraph", "animation/graphs/viewmodel/viewmodel_custom.vnmgraph")]
    [InlineData("animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph", "animation/graphs/viewmodel/viewmodel_gun_custom.vnmgraph+ak47.vnmgraph")]
    public void CustomGraphPathsFollowCs2Al(string stock, string custom)
    {
        Assert.Equal(custom, GraphTree.CustomGraphPath(stock));
    }

    [Fact]
    public void PlanClonesBranchesParentsAndOnlyWhatTheRootReaches()
    {
        GraphInfo Graph(string name, params string[] resources) => new(name, resources, "");
        var root = GraphTree.Parent;
        var gun = GraphTree.GraphRoot + "gun.vnmgraph";
        var inspect = GraphTree.GraphRoot + "inspect.vnmgraph";
        var orphan = GraphTree.GraphRoot + "orphan.vnmgraph";
        var other = GraphTree.GraphRoot + "other.vnmgraph";
        var graphs = new[]
        {
            Graph(root, gun, other),
            Graph(gun, "anims/reload.vnmclip", inspect),
            Graph(inspect, "anims/inspect.vnmclip"),
            Graph(orphan, "anims/reload.vnmclip"), // plays the clip, but nothing routes to it
            Graph(other, "anims/other.vnmclip"),
        }.ToDictionary(graph => graph.Path);

        var plan = GraphTree.Plan(graphs, new Dictionary<string, string> { ["anims/reload.vnmclip"] = "anims/custom/reload.vnmclip", ["anims/unused.vnmclip"] = "anims/custom/unused.vnmclip" });

        Assert.Equal([gun, inspect, root], plan.Graphs.Select(graph => graph.Path).Order(StringComparer.Ordinal));
        Assert.Equal(GraphTree.CustomParent, plan.ResourceMap[root]);
        Assert.False(plan.ResourceMap.ContainsKey(other));
        Assert.Equal(["anims/unused.vnmclip"], plan.UnreferencedClips);
    }

    [Fact]
    public void ResourceIdsMatchEveryShippedViewmodelGraphReference()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var checkedReferences = 0;
        foreach (var path in connection.Archive.AllPaths().Where(path => path.StartsWith(GraphTree.GraphRoot, StringComparison.Ordinal) && path.EndsWith(".vnmgraph_c", StringComparison.Ordinal)))
        {
            using var resource = new Resource { FileName = path };
            resource.Read(new MemoryStream(connection.Archive.ReadBytes(path)));
            foreach (var reference in resource.ExternalReferences!.ResourceRefInfoList)
            {
                Assert.Equal(reference.Id, ResourceId.Of(reference.Name));
                checkedReferences++;
            }
        }

        Assert.True(checkedReferences > 1000);
    }

    /// <summary>The user's CZ75-as-SMG case: base the CZ75 on the Bizon, keep its name, set one value on top.</summary>
    [Fact]
    public void ClassSwapCopiesTheDonorsPartsAndKeepsTheName()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var stock = connection.ReadVData();
        var swap = new VDataClassSwap { From = "weapon_bizon", Parts = ["type", "animations", "handling"] };
        var keys = swap.RootKeys(stock["weapon_cz75a"]!, stock["weapon_bizon"]!);
        Assert.Contains("_base", keys);
        Assert.Contains("m_szAnimSkeleton", keys);
        Assert.DoesNotContain("_class", keys);
        Assert.DoesNotContain("m_szName", keys);
        Assert.DoesNotContain(VDataBlock.WorldModelKey, keys);

        var bytes = connection.Archive.ReadBytes(VDataFile.GamePath);
        var (text, _) = VDataCompiler.BuildText(bytes, [("weapon_cz75a", "m_nDamage", "50")], [new VDataKeySwap("weapon_cz75a", "weapon_bizon", keys)], bytes);

        var document = ValveKeyValue.KVSerializer.Create(ValveKeyValue.KVSerializationFormat.KeyValues3Text).Deserialize(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text)));
        var cz = document.Root["weapon_cz75a"];
        var bizon = stock["weapon_bizon"]!;
        Assert.Equal("smg", cz["_base"].ToString());
        Assert.Equal("weapon_cz75a", cz["_class"].ToString());
        Assert.Equal("weapon_cz75a", cz["m_szName"].ToString());
        Assert.Equal(bizon.Value("m_szAnimSkeleton"), cz["m_szAnimSkeleton"].ToString());
        Assert.Equal(bizon.Value("m_iMaxClip1"), cz["m_iMaxClip1"].ToString());
        Assert.Equal("50", cz["m_nDamage"].ToString());
        Assert.False(cz["taxonomy"].ContainsKey("pistol"));
        Assert.Equal(stock["weapon_cz75a"]!.Value(VDataBlock.WorldModelKey), cz[VDataBlock.WorldModelKey].ToString());

        var applied = swap.Apply(stock["weapon_cz75a"]!, bizon);
        Assert.Equal("smg", applied.Base);
        Assert.True(VDataClassSwap.Matches(applied, bizon, keys));
    }

    /// <summary>The content source is edited in place: only the edited values' text changes. Skipped without CS2.</summary>
    [Fact]
    public void VDataSourceIsPatchedInPlace()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var stock = connection.Archive.ReadBytes(VDataFile.GamePath);
        var (source, _) = VDataCompiler.BuildText(stock, []);
        source = source.Replace("\n", "\r\n", StringComparison.Ordinal);
        var vdata = connection.ReadVData();
        var swap = new VDataClassSwap { From = "weapon_ak47", Parts = ["sounds"] };
        var keys = swap.RootKeys(vdata["weapon_famas"]!, vdata["weapon_ak47"]!);
        (string Block, string Path, string Value)[] values =
        [
            ("weapon_ak47", "m_flArmorRatio", "2"),
            ("weapon_ak47", "m_flSpread[1]", "0.004"),
            ("weapon_ak47", VDataBlock.WorldModelKey, "models/weapons/test/test.vmdl"),
        ];

        var patched = VDataSourcePatcher.Patch(source, values, [new VDataKeySwap("weapon_famas", "weapon_ak47", keys)], stock, stock, null);

        var before = source.Split("\r\n");
        var after = patched.Split("\r\n");
        Assert.Equal(before.Length, after.Length);
        var changed = before.Zip(after).Where(pair => pair.First != pair.Second).Select(pair => pair.Second.Trim()).ToList();
        Assert.Contains("m_flArmorRatio = 2.0", changed);
        Assert.Contains(changed, line => line.StartsWith("m_flSpread = [", StringComparison.Ordinal) && line.Contains("0.004", StringComparison.Ordinal));
        Assert.Contains(changed, line => line.Contains("resource_name:\"models/weapons/test/test.vmdl\"", StringComparison.Ordinal));
        Assert.Contains(changed, line => line.Contains(vdata["weapon_ak47"]!.Value("m_aShootSounds.WEAPON_SOUND_SINGLE")!, StringComparison.Ordinal));
        Assert.True(changed.Count <= 6, "only the edited lines change: " + string.Join(" | ", changed));
        Assert.DoesNotContain("\n", patched.Replace("\r\n", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void VDataTextCarriesOverridesWithTheirTypes()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var overrides = VDataOverrides.Load(_root);
        overrides.Set("weapon_ak47", "m_nDamage", "40", "36");
        overrides.Set("weapon_ak47", "m_flMaxSpeed[1]", "200", "215");

        var (text, applied) = VDataCompiler.BuildText(connection.Archive.ReadBytes(VDataFile.GamePath), overrides);

        Assert.Equal(2, applied);
        var ak = text[text.IndexOf("weapon_ak47 =", StringComparison.Ordinal)..];
        Assert.Contains("m_nDamage = 40", ak[..ak.IndexOf("_base", StringComparison.Ordinal)]);
        Assert.StartsWith("<!-- kv3 ", text);
    }

    /// <summary>
    /// Delivers the AK's reload (one bone turned) into a fake addon folder,
    /// checks the clip and graphs, then rolls back. No vdata, so the compiler
    /// is not needed. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task DeliveryWritesClipAndGraphsAndRollsBack()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(sandbox, connection);

        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var existing = Path.Combine(target.GamePath, "readme.txt");
        File.WriteAllText(existing, "mine");

        var profile = new Profiles.Profile { Name = "test" };
        profile.VData["weapon_ak47"] = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["m_nDamage"] = "40" };
        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Equal(["reload_ak"], plan.Clips.Select(clip => clip.ClipId));
        Assert.DoesNotContain(plan.Files, file => AddonManifest.IsVData(file.ManifestKey)); // vdata is the VData tab's alone
        Assert.Contains(GraphTree.CustomParent, plan.Graphs);

        var result = await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: TestContext.Current.CancellationToken);

        var clipFile = Path.Combine(target.GamePath, "animation", "anims", "custom", "rifle_ak", "reload_ak.vnmclip_c");
        Assert.True(File.Exists(clipFile));
        using (var root = new Resource { FileName = "viewmodel_custom.vnmgraph_c" })
        {
            root.Read(Path.Combine(target.GamePath, "animation", "graphs", "viewmodel", "viewmodel_custom.vnmgraph_c"));
            var resources = ((BinaryKV3)root.DataBlock!).Data.Root["m_resources"].Values.Select(value => value.ToString()).ToList();
            Assert.Contains("animation/graphs/viewmodel/viewmodel_gun_custom.vnmgraph+ak47.vnmgraph", resources);
        }

        Assert.Equal("mine", File.ReadAllText(existing));
        AddonDelivery.Rollback(result.BackupFolder);

        Assert.False(File.Exists(clipFile));
        Assert.False(File.Exists(target.ManifestPath));
        Assert.Equal("mine", File.ReadAllText(existing));
        Assert.Null(AddonDelivery.LastDelivery(workspace, "fake_addon"));
    }

    /// <summary>
    /// An exported clip shows only in the Animations part of the status, and
    /// deleting the export makes the clip unedited again. Skipped without CS2.
    /// </summary>
    [Fact]
    public void StatusIsSplitPerAspectAndExportsCanBeDeleted()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(sandbox, connection);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profiles.Profile { Name = "test" };

        var status = Profiles.ProfileStatus.Load(profile, store, connection, target).For("rifle_ak", ["weapon_ak47"]);
        Assert.Equal(Profiles.WeaponState.NeedsApply, status.Part(Profiles.StatusAspect.Animations).State);
        Assert.Equal(Profiles.WeaponState.Untouched, status.Part(Profiles.StatusAspect.VData).State);
        Assert.Equal(Profiles.WeaponState.Untouched, status.Part(Profiles.StatusAspect.Graph).State);
        Assert.Equal(Profiles.WeaponState.NeedsApply, status.Part(Profiles.StatusAspect.All).State);

        var output = ClipExporter.LastExport(sandbox, "reload_ak")!.SandboxOutput!;
        Assert.True(ClipExporter.DeleteExport(sandbox, "reload_ak"));
        Assert.Null(ClipExporter.LastExport(sandbox, "reload_ak"));
        Assert.False(File.Exists(output));
        Assert.False(ClipExporter.DeleteExport(sandbox, "reload_ak"));
        var after = Profiles.ProfileStatus.Load(profile, store, connection, target).For("rifle_ak", ["weapon_ak47"]);
        Assert.Equal(Profiles.WeaponState.Untouched, after.State);
    }

    /// <summary>
    /// The cs_hl_template case: the addon already routes the AUG through its
    /// own viewmodel_custom graph (made by CS2 Animation Lab, no Weapon Lab
    /// manifest). Delivering an AK edit must keep that routing, and setting
    /// the AK back to Default must restore the addon's own graph, not delete
    /// it. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task DeliveryBuildsOnTheAddonsOwnGraphsAndRestoresThem()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string AugGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+aug.vnmgraph";
        const string AkGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
        var cancel = TestContext.Current.CancellationToken;
        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));

        // The addon's own root graph, routing the AUG to its custom variation.
        var rootKey = GraphTree.CustomParent + "_c";
        var (own, _) = GraphPatcher.Patch(connection.Archive.ReadBytes(GraphTree.Parent + "_c"), rootKey,
            new Dictionary<string, string> { [AugGraph] = GraphTree.CustomGraphPath(AugGraph) });
        var rootPath = Path.Combine(target.GamePath, rootKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(rootPath)!);
        File.WriteAllBytes(rootPath, own);

        var inventory = Addons.AddonInventory.Load(connection, target, null);
        Assert.Contains(inventory.Assets, asset => asset.Path == GraphTree.CustomParent && !asset.FromWeaponLab && asset.Weapons.Count == 0);

        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(sandbox, connection);
        var profile = new Profiles.Profile { Name = "test" };
        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Contains(plan.Notes, note => note.Contains("Builds on the addon's current " + rootKey, StringComparison.Ordinal));
        await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: cancel);

        var root = Graphs.GraphDocument.Read(File.ReadAllBytes(rootPath), rootKey);
        Assert.Contains(GraphTree.CustomGraphPath(AugGraph), root.Resources);
        Assert.Contains(GraphTree.CustomGraphPath(AkGraph), root.Resources);
        Assert.True(Directory.Exists(Path.Combine(AddonOriginals.AddonFolder(workspace, "fake_addon"), "first-snapshot")));

        // Leaving the AK out of later pushes changes nothing in the addon: it is the live state.
        plan.RecordIn(profile);
        profile.EnsureWeapon("rifle_ak").Animations = Profiles.AnimationMode.Default;
        var later = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.True(later.IsEmpty);
        Assert.Empty(later.Removals);
        Assert.True(File.Exists(Path.Combine(target.GamePath, "animation", "anims", "custom", "rifle_ak", "reload_ak.vnmclip_c")));
    }

    /// <summary>
    /// Set to CS2 default deletes every file the addon has for the weapon,
    /// Weapon Lab's and the addon's own, keeps the root's other routing and
    /// points it back at CS2's graph for the weapon. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task SetToCs2DefaultDeletesTheAddonsOwnFilesForTheWeapon()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string AugGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+aug.vnmgraph";
        const string AkGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
        var cancel = TestContext.Current.CancellationToken;
        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        void Put(string key, byte[] bytes)
        {
            var path = AddonManifest.Resolve(target, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        // The addon's own work, made outside Weapon Lab: a root routing the AUG and the AK, the AK's graph and an idle.
        var rootKey = GraphTree.CustomParent + "_c";
        var (root, _) = GraphPatcher.Patch(connection.Archive.ReadBytes(GraphTree.Parent + "_c"), rootKey,
            new Dictionary<string, string> { [AugGraph] = GraphTree.CustomGraphPath(AugGraph), [AkGraph] = GraphTree.CustomGraphPath(AkGraph) });
        Put(rootKey, root);
        var akGraphKey = GraphTree.CustomGraphPath(AkGraph) + "_c";
        Put(akGraphKey, connection.Archive.ReadBytes(AkGraph + "_c"));
        var weapon = connection.FindWeapon("rifle_ak")!;
        var idle = weapon.ClipPaths.First(path => Path.GetFileName(path).StartsWith("idle_ak", StringComparison.Ordinal));
        var idleKey = GraphTree.CustomClipPath("rifle_ak", idle[..^2]) + "_c";
        Put(idleKey, connection.Archive.ReadBytes(idle));

        // Weapon Lab delivers an AK edit on top.
        var sandbox = store.Create(connection, weapon, targetAddon: null);
        ExportOneEdit(sandbox, connection);
        var profile = new Profiles.Profile { Name = "test" };
        await AddonDelivery.DeliverAsync(DeliveryPlanner.Plan(store, connection, target, profile), connection.Install, workspace, cancellation: cancel);
        Assert.True(File.Exists(AddonManifest.Resolve(target, "animation/anims/custom/rifle_ak/reload_ak.vnmclip_c")));

        profile.EnsureWeapon("rifle_ak").ResetToStock = true;
        var status = Profiles.ProfileStatus.Load(profile, store, connection, target).For("rifle_ak", []);
        Assert.Equal(Profiles.WeaponState.NeedsApply, status.State);

        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Equal(["rifle_ak"], plan.StockResets);
        Assert.DoesNotContain(plan.Files, file => file.ManifestKey == idleKey || file.ManifestKey == akGraphKey);
        await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: cancel);

        Assert.False(Directory.Exists(AddonManifest.Resolve(target, "animation/anims/custom/rifle_ak")) && Directory.EnumerateFiles(AddonManifest.Resolve(target, "animation/anims/custom/rifle_ak")).Any());
        Assert.False(File.Exists(AddonManifest.Resolve(target, akGraphKey)));
        var after = Graphs.GraphDocument.Read(File.ReadAllBytes(AddonManifest.Resolve(target, rootKey)), rootKey);
        Assert.Contains(GraphTree.CustomGraphPath(AugGraph), after.Resources);
        Assert.Contains(AkGraph, after.Resources);
        Assert.DoesNotContain(GraphTree.CustomGraphPath(AkGraph), after.Resources);
        Assert.Empty(AddonManifest.Read(target)!.Clips);

        // Nothing comes back: with the flag cleared the addon already matches.
        profile.EnsureWeapon("rifle_ak").ResetToStock = false;
        profile.EnsureWeapon("rifle_ak").Animations = Profiles.AnimationMode.Default;
        var again = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.True(again.IsEmpty);

        // The re-pointed root is recorded with its new bytes: no "changed outside Weapon Lab" on the next push (bug 2026-10-31).
        Assert.DoesNotContain(again.Notes, note => note.Contains("changed outside Weapon Lab", StringComparison.Ordinal));
    }

    /// <summary>An export report whose clip file is gone counts as not exported instead of crashing the plan (bug 2026-10-31). Skipped without CS2.</summary>
    [Fact]
    public void AnExportWithoutItsClipFileIsNotPushed()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var store = new SandboxStore(Path.Combine(_root, "workspace"));
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(sandbox, connection);
        var report = ClipExporter.LastExport(sandbox, "reload_ak")!;
        File.Delete(report.SandboxOutput!);

        var profile = new Profiles.Profile { Name = "test" };
        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.DoesNotContain(plan.Clips, clip => clip.ClipId == "reload_ak");
    }

    /// <summary>
    /// The CS2 root copy: asked for once, it is viewmodel_custom's bytes at
    /// CS2's root graph path, stays in sync on later pushes (Set to CS2
    /// default included) and is removed when switched off. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task Cs2RootCopyFollowsViewmodelCustomUntilSwitchedOff()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var cancel = TestContext.Current.CancellationToken;
        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var rootPath = AddonManifest.Resolve(target, GraphTree.CustomParent + "_c");
        var copyPath = AddonManifest.Resolve(target, DeliveryPlanner.Cs2RootCopyKey);

        var weapon = connection.FindWeapon("rifle_ak")!;
        ExportOneEdit(store.Create(connection, weapon, targetAddon: null), connection);
        var profile = new Profiles.Profile { Name = "test" };
        await AddonDelivery.DeliverAsync(DeliveryPlanner.Plan(store, connection, target, profile, cs2RootCopy: true), connection.Install, workspace, cancellation: cancel);
        Assert.Equal(File.ReadAllBytes(rootPath), File.ReadAllBytes(copyPath));
        Assert.True(AddonManifest.Read(target)!.Files.ContainsKey(DeliveryPlanner.Cs2RootCopyKey));

        // Nothing pending: the copy matches the live root.
        Assert.True(DeliveryPlanner.Plan(store, connection, target, profile).IsEmpty);

        // Set to CS2 default patches the root back; the copy follows without being asked.
        profile.EnsureWeapon("rifle_ak").ResetToStock = true;
        await AddonDelivery.DeliverAsync(DeliveryPlanner.Plan(store, connection, target, profile), connection.Install, workspace, cancellation: cancel);
        Assert.Equal(File.ReadAllBytes(rootPath), File.ReadAllBytes(copyPath));
        Assert.DoesNotContain(GraphTree.CustomGraphPath("animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph"),
            Graphs.GraphDocument.Read(File.ReadAllBytes(copyPath), DeliveryPlanner.Cs2RootCopyKey).Resources);

        // Switched off: CS2's own root graph is used again.
        var off = DeliveryPlanner.Plan(store, connection, target, profile, cs2RootCopy: false);
        Assert.Contains(copyPath, off.Removals);
        Assert.DoesNotContain(off.Warnings, warning => warning.Message.Contains(GraphTree.Parent, StringComparison.Ordinal));
        await AddonDelivery.DeliverAsync(off, connection.Install, workspace, cancellation: cancel);
        Assert.False(File.Exists(copyPath));
        Assert.False(AddonManifest.Read(target)!.Files.ContainsKey(DeliveryPlanner.Cs2RootCopyKey));
        Assert.True(DeliveryPlanner.Plan(store, connection, target, profile).IsEmpty);
    }

    /// <summary>
    /// A graph edit alone (no edited clips) delivers the edited graph and its
    /// parents, shows as applied, and Default skips it. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task GraphEditsAreDeliveredAndFollowTheProfile()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Graph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);

        var stock = Graphs.GraphDocument.Read(connection.Archive.ReadBytes(Graph + "_c"), Graph + "_c");
        var transition = stock.Nodes.First(node => node.Class == "TransitionNode");
        var key = Graphs.GraphEdits.Key(transition.Index, "m_flDuration");
        var profile = new Profiles.Profile { Name = "test" };
        profile.Graphs[Graph] = new SortedDictionary<string, string> { [key] = "0.35" };
        Assert.False(Graphs.GraphStatus.Load(profile, connection, target)[Graph].IsApplied);

        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Empty(plan.Clips);
        Assert.Contains(GraphTree.CustomGraphPath(Graph), plan.Graphs);
        Assert.Contains(GraphTree.CustomParent, plan.Graphs);
        var result = await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: TestContext.Current.CancellationToken);

        var delivered = Graphs.GraphStatus.ReadDelivered(target, Graph)!;
        Assert.Equal("0.35", delivered[transition.Index].Value("m_flDuration"));
        Assert.True(Graphs.GraphStatus.Load(profile, connection, target)[Graph].IsApplied);
        var status = Profiles.ProfileStatus.Load(profile, store, connection, target).For("rifle_ak", []);
        Assert.Equal(Profiles.WeaponState.Applied, status.State);

        // Pushed edits leave the profile: they are live in the addon now.
        plan.RecordIn(profile);
        Assert.Empty(profile.Graphs);

        // Left out of the push, a new edit is skipped and nothing is removed.
        profile.Graphs[Graph] = new SortedDictionary<string, string> { [key] = "0.5" };
        profile.EnsureWeapon("rifle_ak").Animations = Profiles.AnimationMode.Default;
        var skipped = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Empty(skipped.Graphs);
        Assert.Empty(skipped.Removals);

        AddonDelivery.Rollback(result.BackupFolder);
        Assert.Null(Graphs.GraphStatus.ReadDelivered(target, Graph));
    }

    /// <summary>
    /// The push model: pending work turns green once pushed; a pushed clip
    /// changed in the addon by hand is the live state and is not pushed over
    /// (only a new export does), one deleted by hand is pushed again, and
    /// leaving the weapon out removes nothing. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task PushedWorkIsLiveAndHandEditsAreNotStomped()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var cancel = TestContext.Current.CancellationToken;
        var workspace = Path.Combine(_root, "workspace");
        var store = new SandboxStore(workspace);
        var weapon = connection.FindWeapon("rifle_ak")!;
        ExportOneEdit(store.Create(connection, weapon, targetAddon: null), connection);
        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profiles.Profile { Name = "test" };
        var clipFile = Path.Combine(target.GamePath, "animation", "anims", "custom", "rifle_ak", "reload_ak.vnmclip_c");

        Profiles.WeaponStatus Status() => Profiles.ProfileStatus.Load(profile, store, connection, target).For("rifle_ak", ["weapon_ak47"]);
        Assert.Equal(Profiles.WeaponState.NeedsApply, Status().State);
        var first = DeliveryPlanner.Plan(store, connection, target, profile);
        await AddonDelivery.DeliverAsync(first, connection.Install, workspace, cancellation: cancel);
        first.RecordIn(profile);
        Assert.Equal(Profiles.WeaponState.Applied, Status().State);
        var original = File.ReadAllBytes(clipFile);

        // Changed in the addon by hand: that is the live state now; not pending, not pushed over.
        File.WriteAllBytes(clipFile, [1, 2, 3]);
        Assert.Empty(Status().Reasons);
        var later = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Empty(later.Clips);
        Assert.Contains(later.Notes, note => note.Contains("has another version now", StringComparison.Ordinal));

        // Deleted by hand: pending again, and pushed back.
        File.Delete(clipFile);
        Assert.Equal(Profiles.WeaponState.NeedsApply, Status().State);
        var again = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Equal(["reload_ak"], again.Clips.Select(clip => clip.ClipId));
        await AddonDelivery.DeliverAsync(again, connection.Install, workspace, cancellation: cancel);
        Assert.Equal(original, File.ReadAllBytes(clipFile));

        // Left out of the push: nothing is removed; the addon keeps what it has.
        profile.EnsureWeapon("rifle_ak").Animations = Profiles.AnimationMode.Default;
        var skipped = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.True(skipped.IsEmpty);
        Assert.Empty(skipped.Removals);
        Assert.True(File.Exists(clipFile));
    }

    /// <summary>
    /// A custom skeleton made from the AK's model: every clip template moves
    /// onto it, the exported edit follows, Blender gets the new skeleton, and
    /// delivery ships the skeleton and every clip, but no model copy (0.23:
    /// the user points their model at the skeleton in ModelDoc; the model
    /// check warns until then, and a copy an earlier version shipped goes).
    /// Removing it moves the export back.
    /// Skipped without CS2.
    /// </summary>
    [Fact]
    public void CustomSkeletonIsDeliveredWithEveryClip()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Ak = "animation/skeletons/weapons/ak47.vnmskel";
        const string Model = "game:weapons/models/ak47/weapon_rif_ak47.vmdl_c";
        var store = new SandboxStore(Path.Combine(_root, "workspace"));
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(sandbox, connection);

        var path = Skeletons.CustomSkeletons.DefaultPath("rifle_ak");
        var setup = Skeletons.CustomSkeletons.Create(sandbox, connection, Model, targetAddon: null);
        Assert.Equal(path, setup.Path);
        Assert.Equal(Ak, setup.FromSkeleton);
        Assert.NotEmpty(setup.Templates);
        var export = ClipExporter.LastExport(sandbox, "reload_ak")!;
        Assert.Contains(path, Clips.ClipReader.Read(File.ReadAllBytes(export.SandboxOutput!), export.ResourcePath).SecondarySkeletons);
        Assert.Equal(export.OutputSha256, Sha(export.SandboxOutput!));

        var package = Blender.RigPackage.Write(sandbox, connection);
        var rig = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(package.InputPath, Blender.RigPackage.RigFileName)));
        Assert.Equal(path, rig.RootElement.GetProperty("skeletons").GetProperty("weapon").GetProperty("path").GetString());
        Assert.Equal(setup.Bones.Count, rig.RootElement.GetProperty("skeletons").GetProperty("weapon").GetProperty("bones").GetArrayLength());

        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profiles.Profile { Name = "test" };
        profile.EnsureWeapon("rifle_ak").CustomSkeleton = new Profiles.CustomSkeletonChoice(Model, path);
        // A model copy an earlier version shipped (listed in the manifest, named by no vdata) is removed.
        var staleCopy = Path.Combine(target.GamePath, (setup.ModelCopy + "_c").Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(staleCopy)!);
        File.WriteAllBytes(staleCopy, [1, 2, 3]);
        new AddonManifest { Profile = "test", Files = { [setup.ModelCopy + "_c"] = "old" } }.Write(target);

        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Equal(setup.Templates.Count, plan.Clips.Count);
        Assert.Contains(plan.Files, file => file.ManifestKey == path + "_c");
        Assert.DoesNotContain(plan.Files, file => file.ManifestKey == setup.ModelCopy + "_c");
        Assert.Contains(staleCopy, plan.Removals, StringComparer.OrdinalIgnoreCase);
        // CS2's AK model still targets ak47.vnmskel: the model check says to point it at the custom one.
        Assert.Contains(plan.Warnings, warning => warning.Subject == "rifle_ak" && warning.Message.Contains("NmSkeletonReference", StringComparison.Ordinal)
            && warning.Message.Contains(path, StringComparison.Ordinal));
        // Delivery leaves vdata to the VData tab. The vdata skeleton does not matter (verified in game
        // 2026-10-25: CS2's m4a4.vnmskel there with the pyramid model and clips worked), so no warning about it.
        Assert.Empty(plan.VDataValues);
        Assert.False(plan.CompilesVData);
        Assert.DoesNotContain(plan.Warnings, warning => warning.Subject == "weapon_ak47");

        // A clip unticked in the Deliver tree is left out.
        profile.EnsureWeapon("rifle_ak").ExcludedClips.Add("reload_ak");
        var without = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Equal(setup.Templates.Count - 1, without.Clips.Count);
        Assert.DoesNotContain(without.Clips, clip => clip.ClipId == "reload_ak");
        profile.EnsureWeapon("rifle_ak").ExcludedClips.Clear();

        profile.EnsureWeapon("rifle_ak").CustomSkeleton = null;
        Assert.Throws<DeliveryException>(() => DeliveryPlanner.Plan(store, connection, target, profile));

        Assert.True(Skeletons.CustomSkeletons.Remove(sandbox, connection));
        Assert.Null(Skeletons.CustomSkeletons.Load(sandbox));
        Assert.Contains(Ak, Clips.ClipReader.Read(File.ReadAllBytes(export.SandboxOutput!), export.ResourcePath).SecondarySkeletons);
    }

    /// <summary>
    /// The push model (user decision 2026-10-26), stressed with two profiles and
    /// two addons whose work intertwines:
    /// A pushes an AK edit; B pushes an M4 edit into the same addon and A's AK
    /// routing survives; B pushes its own AK version over A's (flagged first);
    /// A is not re-pushed until it exports again; A's work goes to a second
    /// addon on its own; B's graph edit survives A's later push; B sets the AK to
    /// CS2 default, which removes everyone's AK files but keeps the M4; and the
    /// last apply can be undone. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task TwoProfilesAndTwoAddonsPushWithoutStompingEachOther()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string AkGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
        const string M4Graph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+m4a4.vnmgraph";
        var cancel = TestContext.Current.CancellationToken;
        var workspace = Path.Combine(_root, "workspace");
        var profiles = new Profiles.ProfileStore(workspace);
        var a = profiles.Create("A", "addon_one");
        var b = profiles.Create("B", "addon_one");
        var storeA = profiles.SandboxesFor("A");
        var storeB = profiles.SandboxesFor("B");
        var one = new AddonTarget("addon_one", Path.Combine(_root, "game", "addon_one"), Path.Combine(_root, "content", "addon_one"));
        var two = new AddonTarget("addon_two", Path.Combine(_root, "game", "addon_two"), Path.Combine(_root, "content", "addon_two"));
        Directory.CreateDirectory(one.GamePath);
        Directory.CreateDirectory(two.GamePath);
        var akClip = AddonManifest.Resolve(one, "animation/anims/custom/rifle_ak/reload_ak.vnmclip_c");
        var root = AddonManifest.Resolve(one, GraphTree.CustomParent + "_c");
        async Task Push(SandboxStore store, Profiles.Profile profile, AddonTarget target, DeliveryPlan? plan = null)
        {
            plan ??= DeliveryPlanner.Plan(store, connection, target, profile);
            await AddonDelivery.DeliverAsync(plan, connection.Install, workspace, cancellation: cancel);
            plan.RecordIn(profile);
            profiles.Save(profile);
        }

        Profiles.WeaponStatus Status(SandboxStore store, Profiles.Profile profile, AddonTarget target, string weapon) =>
            Profiles.ProfileStatus.Load(profile, store, connection, target).For(weapon, []);

        // 1. A pushes its AK edit; afterwards nothing of A is pending in addon_one.
        var akA = storeA.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(akA, connection, "reload_ak", 0.2f);
        Assert.Equal(Profiles.WeaponState.NeedsApply, Status(storeA, a, one, "rifle_ak").State);
        await Push(storeA, a, one);
        var aBytes = File.ReadAllBytes(akClip);
        Assert.Equal(Profiles.WeaponState.Applied, Status(storeA, a, one, "rifle_ak").State);
        Assert.True(DeliveryPlanner.Plan(storeA, connection, one, a).IsEmpty);

        // 2. B pushes an M4 edit into the same addon: A's AK clip and its routing stay.
        var m4B = storeB.Create(connection, connection.FindWeapon("rifle_m4a4")!, targetAddon: null);
        ExportOneEdit(m4B, connection, "reload_m4a4", 0.3f);
        var m4Plan = DeliveryPlanner.Plan(storeB, connection, one, b);
        Assert.Equal(["reload_m4a4"], m4Plan.Clips.Select(clip => clip.ClipId));
        Assert.Empty(m4Plan.Overwrites);
        Assert.Empty(m4Plan.Removals);
        await Push(storeB, b, one, m4Plan);
        Assert.Equal(aBytes, File.ReadAllBytes(akClip));
        var rootGraph = Graphs.GraphDocument.Read(File.ReadAllBytes(root), GraphTree.CustomParent + "_c");
        Assert.Contains(GraphTree.CustomGraphPath(AkGraph), rootGraph.Resources);
        Assert.Contains(GraphTree.CustomGraphPath(M4Graph), rootGraph.Resources);
        Assert.Equal("A", AddonManifest.Read(one)!.FileProfiles["animation/anims/custom/rifle_ak/reload_ak.vnmclip_c"]);

        // 3. B makes its own AK edit: pushing it replaces A's clip, and the plan says so first.
        var akB = storeB.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        ExportOneEdit(akB, connection, "reload_ak", -0.4f);
        var overwrite = DeliveryPlanner.Plan(storeB, connection, one, b);
        var flagged = Assert.Single(overwrite.Overwrites);
        Assert.Contains("profile 'A'", flagged.Message, StringComparison.Ordinal);
        await Push(storeB, b, one, overwrite);
        Assert.NotEqual(aBytes, File.ReadAllBytes(akClip));

        // 4. A's AK is not pushed back over B's on A's next apply: A shows nothing pending for it.
        var again = DeliveryPlanner.Plan(storeA, connection, one, a);
        Assert.Empty(again.Clips);
        Assert.Contains(again.Notes, note => note.Contains("has another version now", StringComparison.Ordinal));
        Assert.Empty(Status(storeA, a, one, "rifle_ak").Reasons);

        // 5. A exports the AK again: pending again, and replacing B's version is flagged.
        ExportOneEdit(akA, connection, "reload_ak", 0.25f);
        var reExport = DeliveryPlanner.Plan(storeA, connection, one, a);
        Assert.Equal(["reload_ak"], reExport.Clips.Select(clip => clip.ClipId));
        Assert.Contains(reExport.Overwrites, warning => warning.Message.Contains("profile 'B'", StringComparison.Ordinal));

        // 6. A's work goes into a second addon on its own; addon_one is untouched by it.
        var oneBefore = File.ReadAllBytes(akClip);
        await Push(storeA, a, two);
        Assert.True(File.Exists(AddonManifest.Resolve(two, "animation/anims/custom/rifle_ak/reload_ak.vnmclip_c")));
        Assert.False(File.Exists(AddonManifest.Resolve(two, "animation/anims/custom/rifle_m4a4/reload_m4a4.vnmclip_c")));
        Assert.Equal(oneBefore, File.ReadAllBytes(akClip));
        Assert.Equal(["reload_ak"], DeliveryPlanner.Plan(storeA, connection, one, a).Clips.Select(clip => clip.ClipId)); // Still pending in addon_one.

        // 7. B edits the M4's graph and pushes; A's later push to the same addon keeps B's edit (it builds on the live graph).
        var m4Doc = Graphs.GraphDocument.Read(connection.Archive.ReadBytes(M4Graph + "_c"), M4Graph + "_c");
        var speedNode = m4Doc.Nodes.First(node => node.Class == "ClipNode" && node.Value("m_flSpeedMultiplier") is not null);
        b.Graphs[M4Graph] = new SortedDictionary<string, string>(StringComparer.Ordinal) { [Graphs.GraphEdits.Key(speedNode.Index, "m_flSpeedMultiplier")] = "1.5" };
        await Push(storeB, b, one);
        Assert.Empty(b.Graphs); // Pushed edits leave the profile: they are live now.
        await Push(storeA, a, one);
        var m4Live = Graphs.GraphStatus.ReadDelivered(one, M4Graph)!;
        Assert.Equal("1.5", m4Live[speedNode.Index].Value("m_flSpeedMultiplier"));

        // A edits the same value: last push wins, and the plan says which value it replaces.
        a.Graphs[M4Graph] = new SortedDictionary<string, string>(StringComparer.Ordinal) { [Graphs.GraphEdits.Key(speedNode.Index, "m_flSpeedMultiplier")] = "2" };
        var replace = DeliveryPlanner.Plan(storeA, connection, one, a);
        Assert.Contains(replace.Notes, note => note.Contains("from the addon's 1.5", StringComparison.Ordinal));
        await Push(storeA, a, one, replace);
        Assert.Equal("2", Graphs.GraphStatus.ReadDelivered(one, M4Graph)![speedNode.Index].Value("m_flSpeedMultiplier"));

        // 8. B sets the AK to CS2 default: every AK file goes (A's included), the M4 and its routing stay.
        b.EnsureWeapon("rifle_ak").ResetToStock = true;
        var reset = DeliveryPlanner.Plan(storeB, connection, one, b);
        Assert.Equal(["rifle_ak"], reset.StockResets);
        Assert.Empty(reset.Clips);
        await Push(storeB, b, one, reset);
        Assert.False(File.Exists(akClip));
        Assert.True(File.Exists(AddonManifest.Resolve(one, "animation/anims/custom/rifle_m4a4/reload_m4a4.vnmclip_c")));
        rootGraph = Graphs.GraphDocument.Read(File.ReadAllBytes(root), GraphTree.CustomParent + "_c");
        Assert.Contains(AkGraph, rootGraph.Resources);
        Assert.Contains(GraphTree.CustomGraphPath(M4Graph), rootGraph.Resources);
        Assert.Equal(Profiles.AnimationMode.Default, b.Weapon("rifle_ak").Animations); // B's AK work stays, left out of pushes.
        Assert.Empty(Status(storeB, b, one, "rifle_ak").Reasons);
        Assert.Contains(Status(storeA, a, one, "rifle_ak").Reasons, reason => reason.StartsWith("reload_ak", StringComparison.Ordinal)); // A still has AK work for addon_one.

        // 9. Undo the reset: the AK files are back as they were.
        AddonDelivery.Rollback(AddonDelivery.LastDelivery(workspace, "addon_one")!);
        Assert.True(File.Exists(akClip));
    }

    /// <summary>
    /// A skeleton file from outside the addon becomes the weapon's skeleton:
    /// the clips move onto it, and delivery copies the file into the addon at
    /// its own path with no model. The weapon's graphs are listed for the
    /// outliner. Skipped without CS2.
    /// </summary>
    [Fact]
    public async Task SkeletonFileIsUsedAndDeliveredAlone()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Ak = "animation/skeletons/weapons/ak47.vnmskel";
        const string Id = "animation/skeletons/weapons/test_ak_file.vnmskel";
        var ak = connection.ReadSkeleton(Ak);
        List<Clips.SkeletonBone> bones = [.. ak.Bones, new Clips.SkeletonBone(ak.Bones.Count, "test_extra", 1, new Clips.BonePose(new System.Numerics.Vector3(1, 0, 0), System.Numerics.Quaternion.Identity, 1))];
        var file = Path.Combine(_root, "made_elsewhere", "test_ak_file.vnmskel_c");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, Skeletons.SkeletonBuilder.Build(connection.Archive.ReadBytes(Ak + "_c"), Id, bones));

        var store = new SandboxStore(Path.Combine(_root, "workspace"));
        var sandbox = store.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
        var setup = Skeletons.CustomSkeletons.UseFile(sandbox, connection, targetAddon: null, file);
        Assert.Equal(Id, setup.Path);
        Assert.False(setup.FromAddon);
        Assert.Equal("", setup.ModelCopy);
        Assert.Equal(["test_extra"], setup.ExtraBones);

        var target = new AddonTarget("fake_addon", Path.Combine(_root, "game", "fake_addon"), Path.Combine(_root, "content", "fake_addon"));
        Directory.CreateDirectory(target.GamePath);
        var profile = new Profiles.Profile { Name = "test" };
        profile.EnsureWeapon("rifle_ak").CustomSkeleton = new Profiles.CustomSkeletonChoice(setup.SourceModel, setup.Path);
        var plan = DeliveryPlanner.Plan(store, connection, target, profile);
        Assert.Contains(plan.Files, delivered => delivered.ManifestKey == Id + "_c" && delivered.Bytes.AsSpan().SequenceEqual(File.ReadAllBytes(file)));
        Assert.DoesNotContain(plan.Files, delivered => delivered.ManifestKey.EndsWith(".vmdl_c", StringComparison.Ordinal));
        Assert.Equal(setup.Templates.Count, plan.Clips.Count);

        var graphs = Profiles.WeaponGraphs.For(connection, profile, null, null, "rifle_ak", weaponPending: false);
        Assert.Contains(graphs, graph => graph.Graph.EndsWith("viewmodel_gun.vnmgraph+ak47.vnmgraph", StringComparison.Ordinal) && graph.Link == Profiles.GraphLink.Own);
        Assert.Contains(graphs, graph => graph.Graph == GraphTree.Parent && graph.Link == Profiles.GraphLink.Route);
        Assert.All(graphs, graph => Assert.Equal(Profiles.WeaponState.Untouched, graph.State));

        // Another profile pushes one plain AK clip (CS2's skeleton) into the same addon: the AK would play on two
        // skeletons at once, so the plan warns.
        await AddonDelivery.DeliverAsync(plan, connection.Install, store.WorkspacePath, cancellation: TestContext.Current.CancellationToken);
        var other = new SandboxStore(Path.Combine(_root, "workspace"), Path.Combine(_root, "other_profile", "sandboxes"));
        ExportOneEdit(other.Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null), connection, "reload_ak", 0.5f);
        var mixed = DeliveryPlanner.Plan(other, connection, target, new Profiles.Profile { Name = "other" });
        Assert.Contains(mixed.Warnings, warning => warning.Subject == "rifle_ak" && warning.Message.Contains("on another skeleton", StringComparison.Ordinal));
    }

    private static string Sha(string file) => GameArchive.Sha256(File.ReadAllBytes(file));

    /// <summary>Exports one clip with an arm bone turned by <paramref name="angle"/>, as the Blender add-on would.</summary>
    private static void ExportOneEdit(SandboxInfo sandbox, GameConnection connection, string clipId = "reload_ak", float angle = 0.2f)
    {
        var package = Blender.RigPackage.Write(sandbox, connection);
        var stockFile = Path.Combine(package.InputPath, "poses", clipId + ".json.gz");
        Directory.CreateDirectory(Path.Combine(package.InputPath, "reference"));
        File.Copy(stockFile, Path.Combine(package.InputPath, "reference", clipId + ".json.gz"), overwrite: true);

        var poses = PoseFile.Read(stockFile);
        var turn = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitX, angle);
        foreach (var frame in poses["arms"])
        {
            frame[7] = frame[7] with { Rotation = System.Numerics.Quaternion.Normalize(turn * frame[7].Rotation) };
        }

        var edited = Path.Combine(sandbox.FolderPath, "edits", clipId + ".json.gz");
        PoseFile.Write(edited, new Dictionary<string, (string, Clips.BonePose[][])>
        {
            ["arms"] = (Clips.ClipInfo.ViewmodelSkeleton, poses["arms"]),
            ["weapon"] = (Skeletons.CustomSkeletons.WeaponSkeleton(sandbox, connection)!, poses["weapon"]),
        });
        ClipExporter.Export(sandbox, clipId, edited, toSandbox: true, extraOutputPath: null);
    }
}
