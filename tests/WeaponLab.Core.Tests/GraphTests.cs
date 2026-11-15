using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Graphs;

namespace WeaponLab.Core.Tests;

public sealed class GraphTests
{
    private const string AkGraph = "animation/graphs/viewmodel/viewmodel_gun.vnmgraph+ak47.vnmgraph";
    private const string AkIdle = "animation/anims/viewmodel/rifle/rifle_ak/idle_ak.vnmclip";
    private const string AkReload = "animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip";

    private static GameConnection? TryConnect() => GameLocator.Locate() is null ? null : GameConnection.Open();

    /// <summary>
    /// The Source 2 Viewer-style layout: every wire lands on a pin of the field
    /// it feeds, and no two boxes in a column overlap. Checked on every level
    /// of the AK graph. Skipped without CS2.
    /// </summary>
    [Fact]
    public void LayoutWiresEveryFieldIntoItsOwnPinWithoutOverlaps()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var graph = GraphDocument.Read(connection.Archive.ReadBytes(AkGraph + "_c"), AkGraph + "_c");
        var levels = graph.Nodes.Select(node => node.Parent).Append("").Distinct().ToList();
        foreach (var level in levels)
        {
            var canvas = GraphLayout.For(graph, level);
            var items = canvas.Items.ToDictionary(item => item.Id);
            if (!canvas.IsStateMachine)
            {
                Assert.All(canvas.Edges, edge => Assert.Contains(edge.Field, items[edge.To].Inputs));
            }

            foreach (var column in canvas.Items.GroupBy(item => item.X))
            {
                var stacked = column.OrderBy(item => item.Y).ToList();
                for (var index = 1; index < stacked.Count; index++)
                {
                    Assert.True(stacked[index].Y >= stacked[index - 1].Y + stacked[index - 1].Height, $"{level}: {stacked[index].Title} overlaps {stacked[index - 1].Title}");
                }
            }
        }

        // A node with several inputs shows one pin each, named by field.
        var root = GraphLayout.For(graph, "");
        Assert.Contains(root.Items, item => item.Inputs.Count > 1 && item.Height > GraphLayout.BoxHeight);
    }

    /// <summary>
    /// The Graph tab shows the addon's own version of a graph: its custom copy
    /// as found in the addon (here one that plays a custom AK idle), not CS2's.
    /// Skipped without CS2.
    /// </summary>
    [Fact]
    public void TheAddonsOwnGraphIsWhatTheGraphTabShows()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var target = new AddonTarget("fake_addon", Path.Combine(root, "game", "fake_addon"), Path.Combine(root, "content", "fake_addon"));
            Assert.Null(GraphStatus.ReadDelivered(target, AkGraph));

            var key = GraphTree.CustomGraphPath(AkGraph) + "_c";
            var customIdle = GraphTree.CustomClipPath("rifle_ak", AkIdle);
            var (bytes, _) = GraphPatcher.Patch(connection.Archive.ReadBytes(AkGraph + "_c"), key, new Dictionary<string, string> { [AkIdle] = customIdle });
            var file = Path.Combine(target.GamePath, key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);

            var own = GraphStatus.ReadDelivered(target, AkGraph);
            Assert.NotNull(own);
            Assert.Contains(customIdle, own.Resources);
            Assert.DoesNotContain(AkIdle, own.Resources);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    /// <summary>
    /// The viewport scene: the AK's skeleton in model space and CS2's AK mesh
    /// placed on it in game units (the muzzle bone sits at the barrel end of the
    /// mesh, not metres away or 39 times too small). Skipped without CS2.
    /// </summary>
    [Fact]
    public void ViewportPlacesTheWeaponMeshOnItsSkeleton()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var cache = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var scene = Viewport.ViewportScenes.Build(connection, connection.FindWeapon("rifle_ak")!, sandbox: null, previewModel: null, cache);
            Assert.Equal("animation/skeletons/weapons/ak47.vnmskel", scene.Skeleton);
            Assert.Contains(scene.Bones, bone => bone.Name == "muzzle");
            Assert.All(scene.Bones, bone => Assert.False(bone.Extra));
            var mesh = Assert.IsType<Viewport.ViewportMesh>(scene.Mesh);
            Assert.True(mesh.VertexCount > 1000, $"{mesh.VertexCount} vertices");
            Assert.Equal(mesh.VertexCount * 4, mesh.Weights.Length);

            // The mesh is about an AK long (tens of units) and the muzzle bone lies within its bounds.
            var points = Enumerable.Range(0, mesh.VertexCount).Select(vertex => new System.Numerics.Vector3(mesh.Positions[vertex * 3], mesh.Positions[vertex * 3 + 1], mesh.Positions[vertex * 3 + 2])).ToList();
            var min = points.Aggregate(System.Numerics.Vector3.Min);
            var max = points.Aggregate(System.Numerics.Vector3.Max);
            var size = (max - min).Length();
            Assert.InRange(size, 15, 80);
            var muzzle = scene.Bones.First(bone => bone.Name == "muzzle").Rest.Translation;
            var margin = new System.Numerics.Vector3(3);
            Assert.True(System.Numerics.Vector3.Min(muzzle, min - margin) == min - margin && System.Numerics.Vector3.Max(muzzle, max + margin) == max + margin,
                $"muzzle {muzzle} outside the mesh bounds {min}..{max}");

            // A second build reuses the exported mesh.
            Assert.Single(Directory.GetFiles(cache));
            Viewport.ViewportScenes.Build(connection, connection.FindWeapon("rifle_ak")!, sandbox: null, previewModel: null, cache);
            Assert.Single(Directory.GetFiles(cache));

            // With arms: the viewmodel rig first, the weapon hanging off its wpn bone, sleeves and gloves on the hands.
            var withArms = Viewport.ViewportScenes.Build(connection, connection.FindWeapon("rifle_ak")!, sandbox: null, previewModel: null, cache, Blender.ArmsPreview.Default);
            Assert.True(withArms.HasArms);
            var wpn = withArms.Bones.ToList().FindIndex(bone => bone.Name == "wpn");
            Assert.InRange(wpn, 0, withArms.ArmBoneCount - 1);
            Assert.Equal(wpn, withArms.Bones.First(bone => bone.Name == "weapon").Parent);
            Assert.Equal(2, withArms.ArmMeshes.Count);
            var hand = withArms.Bones.First(bone => bone.Name == "hand_R").Rest.Translation;
            foreach (var part in withArms.ArmMeshes)
            {
                var nearest = Enumerable.Range(0, part.VertexCount)
                    .Min(vertex => System.Numerics.Vector3.Distance(hand, new System.Numerics.Vector3(part.Positions[vertex * 3], part.Positions[vertex * 3 + 1], part.Positions[vertex * 3 + 2])));
                Assert.True(nearest < 4, $"an arm mesh is {nearest} units from hand_R");
            }

            // A reload moves the arms (primary stream) as well as the magazine (weapon stream).
            var reload = Viewport.ViewportClips.Load(connection, null, AkReload + "_c", withArms, exported: false)!;
            Assert.Equal(withArms.Bones.Count, reload.Frames[0].Length);
            var handIndex = withArms.Bones.ToList().FindIndex(bone => bone.Name == "hand_L");
            Assert.Contains(reload.Frames, frame => System.Numerics.Vector3.Distance(frame[handIndex].Translation, reload.Frames[0][handIndex].Translation) > 1);
            Assert.All(reload.Events, mark => Assert.InRange(mark.Frame, 0, reload.FrameCount - 1));

            // Gloves only, as the app shows them: the forearm twist bones share the hand's roll, a third and two thirds of it.
            var gloves = Viewport.ViewportScenes.Build(connection, connection.FindWeapon("rifle_ak")!, sandbox: null, previewModel: null, cache,
                new Blender.ArmsPreview("", Blender.ArmsPreview.Default.Gloves));
            var glove = Assert.Single(gloves.ArmMeshes);
            var bones = gloves.Bones.ToList();
            var (lower, handL) = (bones.FindIndex(bone => bone.Name == "arm_lower_L"), bones.FindIndex(bone => bone.Name == "hand_L"));
            var twists = glove.JointTwists.OfType<Viewport.ViewportTwist>().Where(twist => twist.Parent == lower && twist.Child == handL).ToList();
            Assert.Equal(2, twists.Count);
            var rest = gloves.RestPose();
            var rolled = gloves.RestPose();
            var (from, to) = (rest[lower].Translation, rest[handL].Translation);
            rolled[handL] = rest[handL] * System.Numerics.Matrix4x4.CreateTranslation(-to)
                * System.Numerics.Matrix4x4.CreateFromAxisAngle(System.Numerics.Vector3.Normalize(to - from), MathF.PI / 2) * System.Numerics.Matrix4x4.CreateTranslation(to);
            foreach (var twist in twists)
            {
                System.Numerics.Matrix4x4.Invert(twist.Current(rest), out var restInverse);
                var turn = System.Numerics.Quaternion.CreateFromRotationMatrix(restInverse * twist.Current(rolled));
                Assert.InRange(2 * MathF.Acos(Math.Min(1, Math.Abs(turn.W))), twist.Share * MathF.PI / 2 - 0.02f, twist.Share * MathF.PI / 2 + 0.02f);
            }
        }
        finally
        {
            if (Directory.Exists(cache))
            {
                Directory.Delete(cache, recursive: true);
            }
        }
    }

    [Fact]
    public void AkGraphReadsNodesLevelsAndStateMachines()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var graph = GraphDocument.Read(connection.Archive.ReadBytes(AkGraph + "_c"), AkGraph + "_c");

        Assert.Equal("ak47", graph.Variation);
        Assert.Contains(AkReload, graph.Resources);
        Assert.Contains("action", graph.ControlParameters);

        // Every clip node either plays nothing or one of the graph's resources.
        var clips = graph.Nodes.Where(node => node.Class == "ClipNode").ToList();
        Assert.NotEmpty(clips);
        Assert.Contains(clips, node => graph.PlayedResource(node) == AkIdle);

        // The root level holds the game values and the top state machine.
        var root = graph.Level("");
        Assert.Contains(root.Nodes, node => node.Name == "action" && node.Class == "ControlParameterIDNode");
        var actions = root.Nodes.Single(node => node.Name == "actions");
        Assert.Equal("StateMachineNode", actions.Class);
        Assert.True(graph.HasChildren(actions.Path));
        Assert.NotEmpty(graph.Transitions[actions.Index]);

        // Deeper levels: states inside the machine, folders without a node of their own.
        var inner = graph.Level("actions/Actions/SM");
        Assert.Contains(inner.Nodes, node => node.Name == "Reload" && node.Class == "StateNode");
        Assert.Contains("actions/Actions/SM/Global Transitions", inner.Folders);

        // Links are found and wiring values are read-only.
        var reloadState = inner.Nodes.Single(node => node.Name == "Reload");
        Assert.Contains(reloadState.Links, link => link.Field == "m_nChildNodeIdx");
        Assert.False(reloadState.Values.Single(value => value.Path == "m_nChildNodeIdx").Editable);
        Assert.Equal("Clip", GraphGlossary.Describe("ClipNode").Name);
    }

    [Fact]
    public void GraphEditsArePatchedVerifiedAndReadBack()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var bytes = connection.Archive.ReadBytes(AkGraph + "_c");
        var graph = GraphDocument.Read(bytes, AkGraph + "_c");
        var transition = graph.Nodes.First(node => node.Class == "TransitionNode");
        var idle = graph.Nodes.First(node => node.Class == "ClipNode" && graph.PlayedResource(node) == AkIdle);
        var draw = graph.Nodes.First(node => node.Class == "ClipNode" && node.Name == "draw0");
        const string NewClip = "animation/anims/viewmodel/rifle/rifle_ak/inspect_ak.vnmclip";
        Assert.DoesNotContain(NewClip, graph.Resources);

        Assert.Equal(("0.25", null), GraphEdits.Normalize(graph, transition.Index, "m_flDuration", " 0.25 "));
        Assert.NotNull(GraphEdits.Normalize(graph, transition.Index, "m_nTargetStateNodeIdx", "3").Error);
        Assert.NotNull(GraphEdits.Normalize(graph, transition.Index, "m_blendWeightEasing", "Bouncy").Error);

        var edits = new Dictionary<string, string>
        {
            [GraphEdits.Key(transition.Index, "m_flDuration")] = "0.25",
            [GraphEdits.Key(idle.Index, "m_flSpeedMultiplier")] = "1.5",
            [GraphEdits.Key(draw.Index, GraphDocument.ClipEditKey)] = AkReload,
            [GraphEdits.Key(idle.Index, GraphDocument.ClipEditKey)] = NewClip,
        };
        var map = new Dictionary<string, string> { [AkReload] = GraphTree.CustomClipPath("rifle_ak", AkReload) };
        var (patched, _) = GraphPatcher.Patch(bytes, AkGraph + "_c", map, edits);

        var result = GraphDocument.Read(patched, AkGraph + "_c");
        Assert.Equal("0.25", result[transition.Index].Value("m_flDuration"));
        Assert.Equal("1.5", result[idle.Index].Value("m_flSpeedMultiplier"));
        Assert.Equal(GraphTree.CustomClipPath("rifle_ak", AkReload), result.PlayedResource(result[draw.Index]));
        Assert.Equal(NewClip, result.PlayedResource(result[idle.Index]));
        Assert.Equal(graph.Resources.Count + 1, result.Resources.Count);
        foreach (var (key, value) in edits)
        {
            Assert.True(GraphStatus.Holds(result, key, value), key);
        }

        // Untouched nodes are unchanged.
        var untouched = graph.Nodes.First(node => node.Class == "IDComparisonNode");
        Assert.Equal(untouched.Values, result[untouched.Index].Values);
    }

    /// <summary>
    /// Regression (2026-11-02, the Annabelle's legacy graph node 29): a clip edit
    /// naming CS2's path, pushed onto the addon's graph that already plays the
    /// addon's copy, plays that copy's slot instead of adding the clip again
    /// (which left two equal slots and failed the check, or played CS2's clip).
    /// The other way round, an edit naming the copy finds CS2's slot. Skipped
    /// without CS2.
    /// </summary>
    [Fact]
    public void ClipEditsPlayTheSlotHoldingTheClipUnderEitherName()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var key = AkGraph + "_c";
        var stockBytes = connection.Archive.ReadBytes(key);
        var stock = GraphDocument.Read(stockBytes, key);
        var draw = stock.Nodes.First(node => node.Class == "ClipNode" && node.Name == "draw0");
        var customReload = GraphTree.CustomClipPath("rifle_ak", AkReload);
        var redirect = new Dictionary<string, string> { [AkReload] = customReload };
        var (liveBytes, _) = GraphPatcher.Patch(stockBytes, key, redirect);
        var edit = new Dictionary<string, string> { [GraphEdits.Key(draw.Index, GraphDocument.ClipEditKey)] = AkReload };

        void AssertPlaysCustomReload(byte[] patched)
        {
            var result = GraphDocument.Read(patched, key);
            Assert.Equal(customReload, result.PlayedResource(result[draw.Index]));
            Assert.Equal(stock.Resources.Count, result.Resources.Count);
            Assert.Single(result.Resources, resource => resource == customReload);
        }

        // The reload is pushed again now (in the map) ...
        AssertPlaysCustomReload(GraphPatcher.Patch(liveBytes, key, redirect, edit).Bytes);

        // ... or the addon already has it (an alias only).
        AssertPlaysCustomReload(GraphPatcher.Patch(liveBytes, key, new Dictionary<string, string>(), edit, redirect).Bytes);

        // An edit naming the addon's copy, on CS2's graph, takes CS2's slot that the map renames.
        var copyEdit = new Dictionary<string, string> { [GraphEdits.Key(draw.Index, GraphDocument.ClipEditKey)] = customReload };
        AssertPlaysCustomReload(GraphPatcher.Patch(stockBytes, key, redirect, copyEdit).Bytes);
    }

    /// <summary>
    /// An ID comparison can take more IDs (an edit one past the end of the
    /// list adds one) and read another parameter: the root graph's shotgun
    /// group then checks anim classes instead of the weapon category.
    /// </summary>
    [Fact]
    public void IdComparisonsTakeMoreIdsAndAnotherInput()
    {
        using var connection = TryConnect();
        if (connection is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var key = GraphTree.Parent + "_c";
        var bytes = connection.Archive.ReadBytes(key);
        var graph = GraphDocument.Read(bytes, key);
        var weaponType = graph.Nodes.First(node => node.Class == "ControlParameterIDNode" && node.Name == "weapon_type");
        var shotguns = graph.Nodes.First(node => node.Class == "IDComparisonNode" && node.Value("m_comparisionIDs[0]") == "weapon_category_shotgun");
        var edits = new Dictionary<string, string>
        {
            [GraphEdits.Key(shotguns.Index, "m_nInputValueNodeIdx")] = weaponType.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
            [GraphEdits.Key(shotguns.Index, "m_comparisionIDs[0]")] = "weapon_nova",
            [GraphEdits.Key(shotguns.Index, "m_comparisionIDs[1]")] = "weapon_xm1014",
            [GraphEdits.Key(shotguns.Index, "m_comparisionIDs[2]")] = "weapon_sawedoff",
        };

        var (patched, _) = GraphPatcher.Patch(bytes, key, new Dictionary<string, string>(), edits);
        var result = GraphDocument.Read(patched, key)[shotguns.Index];
        Assert.Equal(weaponType.Index.ToString(System.Globalization.CultureInfo.InvariantCulture), result.Value("m_nInputValueNodeIdx"));
        Assert.Equal(["weapon_nova", "weapon_xm1014", "weapon_sawedoff"],
            result.Values.Where(value => value.Path.StartsWith("m_comparisionIDs", StringComparison.Ordinal)).Select(value => value.Value));

        // A gap is still refused.
        var gap = new Dictionary<string, string> { [GraphEdits.Key(shotguns.Index, "m_comparisionIDs[2]")] = "weapon_mag7" };
        Assert.Throws<InvalidDataException>(() => GraphPatcher.Patch(bytes, key, new Dictionary<string, string>(), gap));
    }
}
