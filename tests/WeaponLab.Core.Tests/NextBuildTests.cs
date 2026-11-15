using System.Numerics;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.Core.Tests;

/// <summary>Event edit merging, hand glue, leaf bones and model-hierarchy skeletons; no game needed.</summary>
public sealed class NextBuildTests
{
    private static BonePose At(float x, float y = 0, float z = 0) => new(new Vector3(x, y, z), Quaternion.Identity, 1);

    [Fact]
    public void BlenderMarkersOnlyOverrideTheAppWhenMoved()
    {
        ClipEvent[] template =
        [
            new(0, ClipEventKind.Sound, "CNmSoundEvent", "Weapon_AK47.Clipout", "", 0.10, 0),
            new(1, ClipEventKind.Sound, "CNmSoundEvent", "Weapon_AK47.Clipin", "", 0.40, 0),
        ];
        EventChange[] stored =
        [
            new(0, "WeaponLab.Out", 0.20),
            new(1, null, 0.50) { Fields = new Dictionary<string, string> { ["m_attachmentName"] = "mag" } },
        ];

        // Blender sends every sound marker: #0 left alone, #1 moved to 0.30.
        EventChange[] markers = [new(0, "Weapon_AK47.Clipout", 0.10), new(1, "Weapon_AK47.Clipin", 0.30)];
        var merged = EventEdits.Merge(template, stored, markers).ToDictionary(change => change.Index);

        Assert.Equal("WeaponLab.Out", merged[0].Name);
        Assert.Equal(0.20, merged[0].StartTime);
        Assert.Equal(0.30, merged[1].StartTime);
        Assert.Equal("mag", merged[1].Fields["m_attachmentName"]);
    }

    [Fact]
    public void HandGlueKeepsUntouchedClipsAndMovesTheWeaponHandWithTheHand()
    {
        RigBone Bone(string name, int parent) => new(name, parent, [0, 0, 0], [0, 0, 0, 1], 1);
        RigBone[] bones = [Bone("root", -1), Bone("wpn", 0), Bone("hand_L", 0), Bone("wpnHand_L", 1)];

        // CS2's own small offset: wpnHand sits 0.1 from the hand.
        BonePose[][] reference = [[At(0), At(5), At(10), At(5.1f)]];
        Assert.Equal(reference[0], HandGlue.Apply(reference, reference, bones)[0]);

        // The hand moves up 2: the weapon hand follows with the same offset; the gun moved too.
        BonePose[][] edited = [[At(0), At(6), At(10, 2), At(4.1f)]];
        var glued = BonePose.ModelSpace(HandGlue.Apply(reference, edited, bones)[0], [-1, 0, 0, 1]);
        Assert.Equal(new Vector3(10.1f, 2, 0), glued[3].Translation);
    }

    [Fact]
    public void ModelHierarchyKeepsTheModelsParentsAndLeavesOutLeafBones()
    {
        // CS2's skeleton: bolt under slide. The model: bolt under weapon_offset, plus Blender leaf bones.
        var animated = new SkeletonInfo("animation/skeletons/weapons/test.vnmskel",
        [
            new SkeletonBone(0, "weapon", -1, At(0)),
            new SkeletonBone(1, "weapon_offset", 0, At(0)),
            new SkeletonBone(2, "slide", 1, At(1)),
            new SkeletonBone(3, "bolt", 2, At(1)),
            new SkeletonBone(4, "muzzle", 1, At(20)),
        ]);
        SkeletonBone[] modelBones =
        [
            new(0, "weapon", -1, At(4, 0, 5)), // CS2's models bind the root off the origin.
            new(1, "weapon_offset", 0, At(0)),
            new(2, "slide", 1, At(3)),
            new(3, "bolt", 1, At(4)),
            new(4, "lever", 1, At(5)),
            new(5, "lever_end", 4, At(1)),
            new(6, "bolt_end", 3, At(1)),
        ];
        var model = new ModelSkeleton(modelBones, SkeletonBuilder.ModelSpace(modelBones), []);

        Assert.Equal(["lever_end", "bolt_end"], SkeletonBuilder.LeafBones(model));

        var stock = SkeletonBuilder.AlignTo(model, animated).Bones;
        Assert.DoesNotContain(stock, bone => bone.Name.EndsWith("_end", StringComparison.Ordinal));
        Assert.Equal("slide", stock[stock.Single(bone => bone.Name == "bolt").ParentIndex].Name);

        var own = SkeletonBuilder.FromModel(model, animated).Bones;
        Assert.Equal(["weapon", "weapon_offset", "slide", "bolt", "lever", "muzzle"], own.Select(bone => bone.Name));
        Assert.Equal(At(0), own[0].Rest); // The root keeps CS2's rest, or every clip would lift the gun by the model's offset.
        Assert.Equal("weapon_offset", own[own.Single(bone => bone.Name == "bolt").ParentIndex].Name);
        Assert.Equal(At(4), own.Single(bone => bone.Name == "bolt").Rest);
        Assert.Equal(At(20), own.Single(bone => bone.Name == "muzzle").Rest); // CS2's bone the model lacks keeps CS2's rest.
    }

    [Fact]
    public void ASecondRootBoneHangsUnderWeaponWhicheverComesFirst()
    {
        // The user's bong (2026-11-07): lighter_offset, a parentless bone kept apart from weapon, listed first.
        var animated = new SkeletonInfo("animation/skeletons/weapons/test.vnmskel",
        [
            new SkeletonBone(0, "weapon", -1, At(0)),
            new SkeletonBone(1, "weapon_offset", 0, At(0)),
            new SkeletonBone(2, "slide", 1, At(1)),
        ]);
        SkeletonBone[] modelBones =
        [
            new(0, "lighter_offset", -1, At(6, 0, 1)),
            new(1, "weapon", -1, At(4, 0, 5)),
            new(2, "weapon_offset", 1, At(0)),
            new(3, "slide", 2, At(1)),
        ];
        var model = new ModelSkeleton(modelBones, SkeletonBuilder.ModelSpace(modelBones), []);
        Assert.Equal("weapon", SkeletonBuilder.ModelRoot(model, animated).Name);

        foreach (var built in new[] { SkeletonBuilder.FromModel(model, animated).Bones, SkeletonBuilder.AlignTo(model, animated).Bones })
        {
            Assert.Equal("weapon", built[0].Name);
            Assert.Equal(-1, built[0].ParentIndex);
            Assert.Equal(At(0), built[0].Rest); // CS2's rest, so the gun stays in the hands.
            Assert.All(built.Skip(1), bone => Assert.InRange(bone.ParentIndex, 0, bone.Index - 1));
            var lighter = built.Single(bone => bone.Name == "lighter_offset");
            Assert.Equal("weapon", built[lighter.ParentIndex].Name);
            Assert.Equal(At(2, 0, -4), lighter.Rest); // Where the model has it relative to weapon.
        }
    }

    /// <summary>
    /// CS2 applies each track on the compiled model's hierarchy (PC2, in game, 2026-11-07), so the model check
    /// flags every shared bone the model parents differently: a model root (the user's bong lighter, read from
    /// the camera in game), and a parent ModelDoc dropped because no mesh is weighted to it.
    /// </summary>
    [Fact]
    public void TheModelCheckFindsBonesTheModelParentsDifferently()
    {
        var skeleton = new SkeletonInfo("animation/skeletons/weapons/weaponlab_knife_ursus.vnmskel",
        [
            new SkeletonBone(0, "weapon", -1, At(0)),
            new SkeletonBone(1, "weapon_offset", 0, At(0)),
            new SkeletonBone(2, "slide", 1, At(1)),
            new SkeletonBone(3, "lighter_offset", 0, At(2)),
            new SkeletonBone(4, "econ", 1, At(0)),
        ]);
        ModelSkeleton Model(params SkeletonBone[] bones) => new(bones, SkeletonBuilder.ModelSpace(bones), [skeleton.ResourcePath]);

        // Matching: nothing to say (econ, which the model lacks, is not a mismatch).
        var matching = Model(new(0, "weapon", -1, At(0)), new(1, "weapon_offset", 0, At(0)), new(2, "slide", 1, At(1)), new(3, "lighter_offset", 0, At(2)));
        Assert.Empty(ModelCheck.Mismatches(matching, skeleton));

        // The bong before the fix: the lighter is a second root in the model.
        var bong = Model(new(0, "lighter_offset", -1, At(2)), new(1, "weapon", -1, At(0)), new(2, "weapon_offset", 1, At(0)), new(3, "slide", 2, At(1)));
        var root = Assert.Single(ModelCheck.Mismatches(bong, skeleton));
        Assert.Equal(new ParentMismatch("lighter_offset", null, "weapon", true), root);

        // ModelDoc dropped the unweighted weapon_offset, so slide hangs under weapon in the compiled model.
        var dropped = Model(new(0, "weapon", -1, At(0)), new(1, "slide", 0, At(1)), new(2, "lighter_offset", 0, At(2)));
        var moved = Assert.Single(ModelCheck.Mismatches(dropped, skeleton));
        Assert.Equal(new ParentMismatch("slide", "weapon", "weapon_offset", false), moved);

        var result = new ModelCheckResult("models/weapons/x/bong.vmdl", true, true, skeleton.ResourcePath, false, [root, moved]);
        var problems = result.Problems();
        Assert.Equal(3, problems.Count);
        Assert.Contains("NmSkeletonReference", problems[0], StringComparison.Ordinal);
        Assert.Contains(skeleton.ResourcePath, problems[0], StringComparison.Ordinal); // The path to paste, without _c.
        Assert.Contains("from the camera", problems[1], StringComparison.Ordinal);
        Assert.Contains("do_not_discard", problems[2], StringComparison.Ordinal);
        Assert.False(result.IsClean);
        Assert.True((result with { TargetsSkeleton = true, Mismatches = [] }).IsClean);
    }

    /// <summary>Props are opt-in (0.23): only ticked bones below the root, and not a bone under another ticked one.</summary>
    [Fact]
    public void OnlyTickedBonesBelowTheRootBecomeProps()
    {
        var skeleton = new SkeletonInfo("animation/skeletons/weapons/test.vnmskel",
        [
            new SkeletonBone(0, "weapon", -1, At(0)),
            new SkeletonBone(1, "weapon_offset", 0, At(0)),
            new SkeletonBone(2, "clip", 1, At(1)),
            new SkeletonBone(3, "lighter_offset", 0, At(2)),
            new SkeletonBone(4, "lighter_cap", 3, At(0, 1)),
        ]);
        Assert.Empty(RigPackage.PropBones(skeleton, null));
        Assert.Empty(RigPackage.PropBones(skeleton, []));
        Assert.Equal(["clip", "lighter_offset"], RigPackage.PropBones(skeleton, ["lighter_cap", "weapon", "missing", "lighter_offset", "clip"]));
        Assert.Equal(["lighter_cap"], RigPackage.PropBones(skeleton, ["lighter_cap"]));
    }

    [Fact]
    public void StoredEventEditsBecomeAnExportAndResetRemovesIt()
    {
        if (Game.GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        var root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var connection = Game.GameConnection.Open();
            var sandbox = new Sandbox.SandboxStore(root).Create(connection, connection.FindWeapon("rifle_ak")!, targetAddon: null);
            var path = sandbox.Manifest.Clips.First(clip => clip.ResourcePath.EndsWith("/reload_ak.vnmclip_c", StringComparison.Ordinal)).ResourcePath;
            var dropMag = connection.ReadClip(path).Events.First(clipEvent => clipEvent.Name == "WPN_DROP_MAG");

            EventEdits.Save(sandbox, "reload_ak", [new EventChange(dropMag.Index, null, 0.5)]);
            var report = EventEdits.Apply(sandbox, "reload_ak");
            Assert.NotNull(report);
            Assert.Empty(report.ChangedBones);
            Assert.Single(report.ChangedEvents);
            Assert.Equal(0.5, ClipReader.Read(File.ReadAllBytes(report.SandboxOutput!), path).Events[dropMag.Index].StartTime, precision: 6);

            EventEdits.Save(sandbox, "reload_ak", []);
            Assert.Null(EventEdits.Apply(sandbox, "reload_ak"));
            Assert.False(File.Exists(report.SandboxOutput));
            Assert.Null(ClipExporter.LastExport(sandbox, "reload_ak"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ModelHierarchySkeletonsLeaveTheGunWhereCs2PutsIt()
    {
        if (Game.GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        // The user's case: SSG08 clips on a model built on the Nova's rig, whose root sits 5 units up.
        var root = Path.Combine(Path.GetTempPath(), "weaponlab-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var connection = Game.GameConnection.Open();
            var sandbox = new Sandbox.SandboxStore(root).Create(connection, connection.FindWeapon("rifle_ssg08")!, targetAddon: null);
            var setup = CustomSkeletons.Create(sandbox, connection, "game:weapons/models/nova/weapon_shot_nova.vmdl_c", null, modelHierarchy: true);
            Assert.Equal(connection.ReadSkeleton("animation/skeletons/weapons/ssg08.vnmskel").Bones[0].Rest, CustomSkeletons.ReadSkeleton(sandbox, setup).Bones[0].Rest);

            var clip = sandbox.Manifest.Clips.First(clip => clip.ResourcePath.EndsWith("/draw_ssg08.vnmclip_c", StringComparison.Ordinal));
            using var moved = ClipTemplate.Open(File.ReadAllBytes(CustomSkeletons.Template(sandbox, clip, setup).Path), clip.ResourcePath);
            using var stock = connection.OpenClipTemplate(clip.ResourcePath);
            var movedPoses = moved.Secondary.Single().ReadPoses();
            var stockPoses = stock.Secondary.Single().ReadPoses();
            for (var frame = 0; frame < stockPoses.Length; frame++)
            {
                Assert.True(Vector3.Distance(stockPoses[frame][0].Translation, movedPoses[frame][0].Translation) < 0.02f, $"frame {frame}: the weapon root moved");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ModelPickersTakeCompiledModelsOnly()
    {
        var error = Assert.Throws<ArgumentException>(() => PreviewModels.FromFile(null!, @"C:\addons\content\x\models\gun.vmdl"));
        Assert.Contains("compiled", error.Message);
        Assert.Equal("addon:x:models/gun.vmdl_c", PreviewModels.FromFile(null!, "addon:x:models/gun.vmdl_c"));
    }

    [Fact]
    public void FreshnessIgnoresHalfTypedKeys()
    {
        if (Game.GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        // The picker asks on every keystroke; this crashed the Build .blend window before.
        using var connection = Game.GameConnection.Open();
        foreach (var key in new[] { "a", "addon:", "addon:c", "addon:cs_hl_template:", "addon:nope", "addon:nope:models/x.vmdl_c", "game:weapons/models/ak47/weapon_rif_ak47.vmdl_c" })
        {
            Assert.Null(PreviewModels.Freshness(connection, key));
        }
    }

    [Fact]
    public void FreshnessWarnsWhenTheSourceIsNewer()
    {
        var compiled = new DateTime(2026, 10, 29, 21, 59, 0);
        Assert.False(new ModelFreshness(compiled, @"C:\a\gun.vmdl", compiled.AddMinutes(-5)).SourceIsNewer);
        var stale = new ModelFreshness(compiled, @"C:\a\gun.vmdl", compiled.AddMinutes(10));
        Assert.True(stale.SourceIsNewer);
        Assert.StartsWith("⚠", stale.Describe("gun"));
    }
}
