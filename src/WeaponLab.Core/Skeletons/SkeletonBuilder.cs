using System.Numerics;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Skeletons;

/// <summary>
/// Builds a compiled <c>.vnmskel_c</c> by patching a game skeleton: the bone
/// list, parents and reference poses are replaced, every other setting is
/// kept. The standalone resourcecompiler cannot compile skeleton sources
/// (no NmSkeleton compiler is registered outside the tools), so this is how
/// a custom weapon skeleton is made. Experimental (2026-10-24): whether the
/// game accepts such a skeleton is what the skeleton test addon checks.
/// </summary>
public static class SkeletonBuilder
{
    /// <param name="id">Resource path without <c>_c</c>, e.g. <c>animation/skeletons/weapons/ar2_test.vnmskel</c>.</param>
    public static byte[] Build(byte[] templateBytes, string id, IReadOnlyList<SkeletonBone> bones)
    {
        Validate(bones);
        using var resource = new Resource { FileName = id + "_c" };
        resource.Read(new MemoryStream(templateBytes));
        var data = resource.DataBlock as BinaryKV3 ?? throw new InvalidDataException("The template is not a KV3 skeleton.");
        var root = data.Data.Root;

        root["m_ID"] = new KVObject(id) { Flag = root["m_ID"].Flag };
        root["m_boneIDs"] = Array(root["m_boneIDs"], bones.Select(bone => new KVObject(bone.Name) { Flag = root["m_boneIDs"][0].Flag }));
        root["m_parentIndices"] = Array(root["m_parentIndices"], bones.Select(bone => KVValues.Like(root["m_parentIndices"][0], bone.ParentIndex)));
        root["m_parentSpaceReferencePose"] = Poses(root["m_parentSpaceReferencePose"], bones.Select(bone => bone.Rest));
        root["m_modelSpaceReferencePose"] = Poses(root["m_modelSpaceReferencePose"], ModelSpace(bones));
        KVValues.Set(root, "m_numBonesToSampleAtLowLOD", bones.Count);

        // Masks and attachments name the template's bones; a fresh skeleton starts without them.
        foreach (var key in new[] { "m_maskDefinitions", "m_secondarySkeletons", "m_floatChannelSets", "m_contactConfigs", "m_gameplayRelevantBoneIndices" })
        {
            if (root.TryGetValue(key, out var value) && value.IsArray)
            {
                root[key] = Array(value, []);
            }
        }

        var bytes = ResourceSplicer.ReplaceData(templateBytes, data);
        Verify(bytes, id, bones);
        return bytes;
    }

    private static void Validate(IReadOnlyList<SkeletonBone> bones)
    {
        if (bones.Count == 0 || bones[0].ParentIndex != -1)
        {
            throw new ArgumentException("The first bone must be the root (parent -1).");
        }

        for (var index = 1; index < bones.Count; index++)
        {
            if (bones[index].ParentIndex < 0 || bones[index].ParentIndex >= index)
            {
                throw new ArgumentException($"Bone {bones[index].Name} must come after its parent.");
            }
        }

        if (bones.Select(bone => bone.Name).Distinct(StringComparer.Ordinal).Count() != bones.Count)
        {
            throw new ArgumentException("Bone names must be unique.");
        }
    }

    private static KVObject Array(KVObject template, IEnumerable<KVObject> items)
    {
        var array = KVObject.Array();
        foreach (var item in items)
        {
            array.Add(item);
        }

        array.Flag = template.Flag;
        return array;
    }

    // Each pose is [tx, ty, tz, scale, qx, qy, qz, qw].
    private static KVObject Poses(KVObject template, IEnumerable<BonePose> poses)
    {
        var element = template[0][0];
        return Array(template, poses.Select(pose =>
        {
            float[] values = [pose.Translation.X, pose.Translation.Y, pose.Translation.Z, pose.Scale, pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W];
            return Array(template[0], values.Select(value => KVValues.Like(element, value)));
        }));
    }

    /// <summary>
    /// A custom skeleton for a model: every bone of the animation skeleton the
    /// clips already use, unchanged and in its order (so every existing track
    /// keeps its motion), followed by the model's bones that skeleton lacks.
    /// An extra bone keeps the model's own parent-space value, so at rest it
    /// sits exactly where the mesh was bound to it relative to its parent
    /// (verified in game 2026-10-24: 32 extra bones on the M4A4). When a shared
    /// parent rests differently in the model than in the skeleton (the AR2's
    /// econ and muzzle are turned 120° from the Galil's), the extra bone is
    /// placed relative to the weapon body instead, and listed in
    /// <see cref="AlignedSkeleton.Corrected"/>. A second parentless bone in
    /// the model hangs under the model's root (<see cref="ModelRoot"/>).
    /// </summary>
    public static AlignedSkeleton AlignTo(ModelSkeleton model, SkeletonInfo animated)
    {
        var bones = animated.Bones.ToList();
        var animatedSpace = ModelSpace(bones);
        var corrected = new List<string>();

        // How the skeleton's rest differs from the model's for a shared bone: animated · model⁻¹.
        BonePose Correction(int modelIndex, int animatedIndex) => Compose(animatedSpace[animatedIndex], Inverse(model.ModelSpace[modelIndex]));
        var root = ModelRoot(model, animated);
        var bodyModel = model.Bones.FirstOrDefault(bone => bone.Name == "weapon_offset") ?? root;
        var bodyAnimated = bones.FindIndex(bone => bone.Name == bodyModel.Name);
        var body = bodyAnimated < 0 ? (BonePose?)null : Correction(bodyModel.Index, bodyAnimated);

        var leaves = LeafBones(model).ToHashSet(StringComparer.Ordinal);
        foreach (var bone in model.Bones.Where(bone => !leaves.Contains(bone.Name) && animated.Bones.All(known => known.Name != bone.Name)))
        {
            if (bone.Index == root.Index)
            {
                throw new InvalidDataException($"The model's root bone {bone.Name} is not in {animated.ResourcePath}, so its animation cannot reach the model.");
            }

            // A second parentless bone (a prop kept apart from the gun) hangs under the model's root.
            var parentModel = bone.ParentIndex < 0 ? root.Index : bone.ParentIndex;
            var parentName = model.Bones[parentModel].Name;
            var parent = bones.FindIndex(item => item.Name == parentName);
            var local = bone.ParentIndex < 0 ? UnderRoot(model, root, bone) : bone.Rest;
            if (parent < animated.Bones.Count && body is { } reference)
            {
                var own = Correction(parentModel, parent);
                if (!Same(own, reference))
                {
                    // At rest, the bone lands where the body's correction puts its model-space rest.
                    local = Compose(Inverse(animatedSpace[parent]), Compose(reference, model.ModelSpace[bone.Index]));
                    corrected.Add(bone.Name);
                }
            }

            bones.Add(new SkeletonBone(bones.Count, bone.Name, parent, local));
            animatedSpace = [.. animatedSpace, Compose(animatedSpace[parent], local)];
        }

        return new AlignedSkeleton(bones, corrected);
    }

    /// <summary>
    /// A custom skeleton that follows the model's own hierarchy, for a full
    /// custom animset: the model's bones in its order, with its parents and
    /// rest values (so a bone the model hangs elsewhere than CS2's skeleton,
    /// like a bolt under weapon_offset instead of slide, keeps the model's
    /// parent), followed by CS2's bones the model lacks at their CS2 rest.
    /// Blender's FBX leaf bones (<see cref="LeafBones"/>) are left out.
    /// The root (<see cref="ModelRoot"/>) comes first and keeps CS2's rest:
    /// every CS2 weapon model binds its root <c>weapon</c> bone off the
    /// origin, but the animation skeletons rest it at the origin and the game
    /// drops that offset. Taking the model's would lift the whole gun out of
    /// the hands in every clip. Any other parentless bone hangs under the root.
    /// </summary>
    public static AlignedSkeleton FromModel(ModelSkeleton model, SkeletonInfo animated)
    {
        var leaves = LeafBones(model).ToHashSet(StringComparer.Ordinal);
        var root = ModelRoot(model, animated);
        var cs2Root = animated.Bones.FirstOrDefault(known => known.Name == root.Name);
        var bones = new List<SkeletonBone> { new(0, root.Name, -1, cs2Root?.Rest ?? new BonePose(Vector3.Zero, Quaternion.Identity, 1)) };
        var placed = new Dictionary<int, int> { [root.Index] = 0 };
        foreach (var bone in model.Bones.Where(bone => bone.Index != root.Index && !leaves.Contains(bone.Name)))
        {
            placed[bone.Index] = bones.Count;
            var parent = bone.ParentIndex >= 0 ? placed.GetValueOrDefault(bone.ParentIndex, 0) : 0;
            bones.Add(new SkeletonBone(bones.Count, bone.Name, parent, bone.ParentIndex >= 0 ? bone.Rest : UnderRoot(model, root, bone)));
        }

        foreach (var bone in animated.Bones.Where(bone => bones.All(known => known.Name != bone.Name)))
        {
            var parentName = bone.ParentIndex < 0 ? null : animated.Bones[bone.ParentIndex].Name;
            var parent = parentName is null ? 0 : Math.Max(0, bones.FindIndex(item => item.Name == parentName));
            bones.Add(new SkeletonBone(bones.Count, bone.Name, parent, bone.Rest));
        }

        return new AlignedSkeleton(bones, []);
    }

    /// <summary>
    /// The model's root bone: the parentless bone named like the root of
    /// CS2's skeleton (<c>weapon</c>), else a parentless bone CS2's skeleton
    /// has, else the first parentless one. A model can have more than one
    /// parentless bone (a prop bone kept apart from <c>weapon</c>, like a
    /// lighter next to the gun), and the compiler decides their order, so
    /// taking the first could hang the whole gun under the prop.
    /// </summary>
    public static SkeletonBone ModelRoot(ModelSkeleton model, SkeletonInfo animated)
    {
        var leaves = LeafBones(model).ToHashSet(StringComparer.Ordinal);
        var roots = model.Bones.Where(bone => bone.ParentIndex < 0 && !leaves.Contains(bone.Name)).ToList();
        var cs2Root = animated.Bones.FirstOrDefault(bone => bone.ParentIndex < 0)?.Name;
        return roots.FirstOrDefault(bone => bone.Name == cs2Root)
            ?? roots.FirstOrDefault(bone => animated.Bones.Any(known => known.Name == bone.Name))
            ?? roots.FirstOrDefault()
            ?? throw new InvalidDataException("The model has no root bone.");
    }

    /// <summary>A second parentless bone's rest under the model's root, where the model has it relative to that root.</summary>
    private static BonePose UnderRoot(ModelSkeleton model, SkeletonBone root, SkeletonBone bone) =>
        Compose(Inverse(model.ModelSpace[root.Index]), model.ModelSpace[bone.Index]);

    /// <summary>
    /// Bones Blender's FBX exporter adds at each chain tip ("Add Leaf Bones"):
    /// <c>&lt;bone&gt;_end</c> with no children, where <c>&lt;bone&gt;</c> exists.
    /// Nothing is weighted to them, so custom skeletons leave them out.
    /// </summary>
    public static IReadOnlyList<string> LeafBones(ModelSkeleton model)
    {
        var names = model.Bones.Select(bone => bone.Name).ToHashSet(StringComparer.Ordinal);
        return model.Bones
            .Where(bone => bone.Name.EndsWith("_end", StringComparison.Ordinal)
                && names.Contains(bone.Name[..^4])
                && model.Bones.All(child => child.ParentIndex != bone.Index))
            .Select(bone => bone.Name)
            .ToList();
    }

    private static bool Same(BonePose a, BonePose b) =>
        Vector3.Distance(a.Translation, b.Translation) < 1e-3f && BonePose.RotationDegrees(a.Rotation, b.Rotation) < 0.01;

    private static BonePose Inverse(BonePose pose) => BonePose.Inverse(pose);

    private static BonePose Compose(BonePose parent, BonePose local) => BonePose.Compose(parent, local);

    public static IReadOnlyList<BonePose> ModelSpace(IReadOnlyList<SkeletonBone> bones)
    {
        var result = new BonePose[bones.Count];
        for (var index = 0; index < bones.Count; index++)
        {
            var local = bones[index].Rest;
            if (bones[index].ParentIndex < 0)
            {
                result[index] = local;
                continue;
            }

            result[index] = Compose(result[bones[index].ParentIndex], local);
        }

        return result;
    }

    private static void Verify(byte[] bytes, string id, IReadOnlyList<SkeletonBone> bones)
    {
        using var resource = new Resource { FileName = id + "_c" };
        resource.Read(new MemoryStream(bytes));
        var root = ((BinaryKV3)resource.DataBlock!).Data.Root;
        var names = root["m_boneIDs"].Values.Select(value => value.ToString()).ToList();
        var parents = root["m_parentIndices"].Values.Select(value => (int)value.ToInt64(null)).ToList();
        if (root["m_ID"].ToString() != id || !names.SequenceEqual(bones.Select(bone => bone.Name)) || !parents.SequenceEqual(bones.Select(bone => bone.ParentIndex)))
        {
            throw new InvalidDataException("The built skeleton does not decode to the requested bones.");
        }
    }
}

/// <param name="Bones">The animation skeleton's bones followed by the model's extra bones.</param>
/// <param name="Corrected">Extra bones placed relative to the weapon body because their parent rests differently in the model.</param>
public sealed record AlignedSkeleton(IReadOnlyList<SkeletonBone> Bones, IReadOnlyList<string> Corrected);
