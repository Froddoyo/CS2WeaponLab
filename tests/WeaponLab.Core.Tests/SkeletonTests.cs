using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Tests;

public sealed class SkeletonTests
{
    /// <summary>
    /// Our reading of m_parentSpaceReferencePose must agree with VRF's own
    /// skeleton decoder on bone order, parents and rest transforms.
    /// </summary>
    [Theory]
    [InlineData("animation/skeletons/characters/viewmodel.vnmskel")]
    [InlineData("animation/skeletons/weapons/ak47.vnmskel")]
    public void RestPosesMatchVrf(string skeletonPath)
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        var ours = connection.ReadSkeleton(skeletonPath);

        using var resource = new Resource { FileName = skeletonPath + "_c" };
        resource.Read(new MemoryStream(connection.Archive.ReadBytes(skeletonPath + "_c")));
        var vrf = Skeleton.FromSkeletonData(((BinaryKV3)resource.DataBlock!).Data.Root);

        Assert.Equal(vrf.Bones.Length, ours.Bones.Count);
        foreach (var bone in vrf.Bones)
        {
            var mine = ours.Bones[bone.Index];
            Assert.Equal(bone.Name, mine.Name);
            Assert.Equal(bone.Parent?.Index ?? -1, mine.ParentIndex);
            Assert.True(Vector3.Distance(bone.Position, mine.Rest.Translation) < 1e-4f, $"{bone.Name} translation");
            Assert.True(Clips.BonePose.RotationDistance(bone.Angle, mine.Rest.Rotation) < 1e-4f, $"{bone.Name} rotation");
        }
    }

    /// <summary>
    /// Weapons are animation slots: the root graph switches on weapon_type,
    /// which comes from a block's anim class, else its class. Without an addon
    /// each stock weapon plays its own slot. Skipped without CS2.
    /// </summary>
    [Fact]
    public void EachStockWeaponPlaysItsOwnSlot()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        var aug = Weapons.WeaponSlots.For(connection, connection.FindWeapon("rifle_aug")!, addonVData: null, addonName: null);
        Assert.Equal("weapon_aug", aug.WeaponType);
        Assert.Contains(aug.PlayedBy, player => player.Block == "weapon_aug" && player.Source == "CS2");
        Assert.DoesNotContain(aug.PlayedBy, player => player.Block == "weapon_m249");
        Assert.Equal("weapon_cz75a", Weapons.WeaponSlots.SlotOf(connection.ReadVData()["weapon_cz75a"]!));

        var (sleeves, gloves) = Blender.ArmsPreview.Choices(connection);
        Assert.Contains(Blender.ArmsPreview.Default.Sleeves, sleeves);
        Assert.Contains(Blender.ArmsPreview.Default.Gloves, gloves);
    }

    /// <summary>
    /// Knives are not slots (0.23, bug 2026-11-07: the Ursus showed as the CT knife's slot and played its clips).
    /// Every knife block has class weapon_knife and no anim class; each knife plays its own animations.
    /// Skipped without CS2.
    /// </summary>
    [Fact]
    public void EachKnifePlaysItsOwnAnimations()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        var stock = connection.ReadVData();
        Assert.True(Weapons.WeaponSlots.PicksPerKnife(stock["weapon_knife_ursus"]!));
        Assert.False(Weapons.WeaponSlots.PicksPerKnife(stock["weapon_ak47"]!));

        var ursus = Weapons.WeaponSlots.For(connection, connection.FindWeapon("knife_ursus")!, addonVData: null, addonName: null);
        Assert.True(ursus.PerKnife);
        Assert.Equal(["weapon_knife_ursus"], ursus.PlayedBy.Select(player => player.Block));
        Assert.Contains("per knife", Weapons.WeaponSlots.Describe(ursus), StringComparison.Ordinal);

        var karambit = Weapons.WeaponSlots.For(connection, connection.FindWeapon("knife_karambit")!, addonVData: null, addonName: null);
        Assert.True(karambit.PerKnife);
        Assert.DoesNotContain(karambit.PlayedBy, player => player.Block == "weapon_knife_ursus");

        // Guns keep the class rule.
        Assert.False(Weapons.WeaponSlots.For(connection, connection.FindWeapon("rifle_aug")!, addonVData: null, addonName: null).PerKnife);
    }

    /// <summary>
    /// A model picked on disk becomes a model key: compiled files in an addon's
    /// game folder and sources in its content folder map to the same key;
    /// files outside the addons are refused. Skipped without CS2 or cs_hl_template.
    /// </summary>
    [Fact]
    public void PickedModelFilesBecomeAddonKeys()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        var compiled = Path.Combine(connection.Install.GameAddonsPath, "cs_hl_template", "models", "weapons", "weapon_ar2", "weapon_ar2.vmdl_c");
        if (!File.Exists(compiled))
        {
            Assert.Skip("cs_hl_template's AR2 is not installed.");
        }

        const string Key = "addon:cs_hl_template:models/weapons/weapon_ar2/weapon_ar2.vmdl_c";
        Assert.Equal(Key, Blender.PreviewModels.FromFile(connection, compiled));
        // Compiled models only (2026-10-29): a .vmdl source used to stand for its possibly stale compile.
        Assert.Throws<ArgumentException>(() => Blender.PreviewModels.FromFile(connection, Path.Combine(connection.Install.ContentAddonsPath, "cs_hl_template", "models", "weapons", "weapon_ar2", "weapon_ar2.vmdl")));
        Assert.NotNull(Blender.PreviewModels.Freshness(connection, Key));
        Assert.Equal("game:weapons/models/ak47/weapon_rif_ak47.vmdl_c", Blender.PreviewModels.FromFile(connection, "game:weapons/models/ak47/weapon_rif_ak47.vmdl_c"));
        Assert.Throws<ArgumentException>(() => Blender.PreviewModels.FromFile(connection, Path.Combine(Path.GetTempPath(), "model.vmdl_c")));
    }

    /// <summary>
    /// The rule verified in game with 32 extra bones on the M4A4: a custom
    /// skeleton keeps every animation bone as it is and adds the model's
    /// extra bones at the model's own parent-space values. CS2's M4A4 model
    /// stores its root 22 units from the skeleton's, which must not leak into
    /// the extra bones. Skipped without CS2.
    /// </summary>
    [Fact]
    public void CustomSkeletonKeepsTheAnimationBonesAndTheModelsExtraBones()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        using var connection = GameConnection.Open();
        const string Model = "weapons/models/m4a4/weapon_rif_m4a4.vmdl_c";
        var model = Skeletons.ModelSkeleton.Read(connection.Archive.ReadBytes(Model), Model);
        var animated = connection.ReadSkeleton("animation/skeletons/weapons/m4a4.vnmskel");
        var aligned = Skeletons.SkeletonBuilder.AlignTo(model, animated);

        Assert.Equal(animated.Bones, aligned.Bones.Take(animated.Bones.Count));
        Assert.Equal(["sight", "ag1_hand_r"], aligned.Bones.Skip(animated.Bones.Count).Select(bone => bone.Name));
        Assert.Empty(aligned.Corrected);
        var sight = aligned.Bones.Single(bone => bone.Name == "sight");
        Assert.Equal("weapon_offset", aligned.Bones[sight.ParentIndex].Name);
        Assert.True(Vector3.Distance(model.Bones.Single(bone => bone.Name == "sight").Rest.Translation, sight.Rest.Translation) < 1e-4f);
    }

    /// <summary>
    /// The custom skeleton experiment's building blocks: a Galil skeleton with
    /// one extra bone, a Galil clip moved onto it (the extra bone spins), and a
    /// model pointed at it. Skipped without CS2.
    /// </summary>
    [Fact]
    public void CustomSkeletonClipAndModelReferenceAreBuilt()
    {
        if (GameLocator.Locate() is null)
        {
            Assert.Skip("CS2 is not installed on this machine.");
        }

        const string Galil = "animation/skeletons/weapons/galil.vnmskel";
        const string Custom = "animation/skeletons/weapons/test_custom.vnmskel";
        const string Clip = "animation/anims/viewmodel/rifle/rifle_galilar/reload_galilar.vnmclip_c";
        using var connection = GameConnection.Open();
        var galil = connection.ReadSkeleton(Galil);
        var extra = new Clips.SkeletonBone(galil.Bones.Count, "test_spinner", 1, new Clips.BonePose(new Vector3(2, 0, 1), Quaternion.Identity, 1));
        List<Clips.SkeletonBone> bones = [.. galil.Bones, extra];

        var skeletonBytes = Skeletons.SkeletonBuilder.Build(connection.Archive.ReadBytes(Galil + "_c"), Custom, bones);
        var rebuilt = Clips.SkeletonInfo.Read(skeletonBytes, Custom);
        Assert.Equal(bones.Select(bone => bone.Name), rebuilt.Bones.Select(bone => bone.Name));
        Assert.True(Vector3.Distance(extra.Rest.Translation, rebuilt.Bones[^1].Rest.Translation) < 1e-4f);

        var frames = connection.ReadClip(Clip).FrameCount;
        Clips.BonePose Spin(int frame, string bone, Clips.BonePose pose) => bone == extra.Name
            ? pose with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.Tau * frame / (frames - 1)) }
            : pose;
        var clipBytes = Clips.ClipRetarget.Retarget(connection.Archive.ReadBytes(Clip), Clip, Galil, galil.Bones.Select(bone => bone.Name).ToList(), Custom, bones, Spin);

        using var clip = Clips.ClipTemplate.Open(clipBytes, Clip);
        var weapon = clip.Secondary.Single();
        Assert.Equal(Custom, weapon.Skeleton);
        Assert.Equal(bones.Count, weapon.TrackCount);
        var poses = weapon.ReadPoses();
        var stock = connection.OpenClipTemplate(Clip).Secondary.Single().ReadPoses();
        Assert.True(Vector3.Distance(stock[frames / 2][2].Translation, poses[frames / 2][2].Translation) < 0.02f, "a Galil bone keeps its motion");
        Assert.InRange(Clips.BonePose.RotationDegrees(poses[0][^1].Rotation, poses[frames / 2][^1].Rotation), 170, 190);
        Assert.Equal(clip.Primary.TrackCount, connection.OpenClipTemplate(Clip).Primary.TrackCount);

        const string Model = "weapons/models/galilar/weapon_rif_galilar.vmdl_c";
        var (modelBytes, replaced) = Delivery.ReferencePatcher.Patch(connection.Archive.ReadBytes(Model), Model, new Dictionary<string, string> { [Galil] = Custom });
        Assert.Equal(1, replaced);
        using var model = new Resource { FileName = Model };
        model.Read(new MemoryStream(modelBytes));
        Assert.Equal(Custom, ((KeyValuesOrNTRO)model.DataBlock!).Data["m_vecNmSkeletonRefs"][0].ToString());
        Assert.Contains(model.ExternalReferences!.ResourceRefInfoList, reference => reference.Name == Custom);
    }
}
