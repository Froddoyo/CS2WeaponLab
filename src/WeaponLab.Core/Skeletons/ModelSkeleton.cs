using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using WeaponLab.Core.Clips;

namespace WeaponLab.Core.Skeletons;

/// <summary>
/// A compiled model's own bones: names, parents, parent-space rest values
/// (<c>m_bonePosParent</c> / <c>m_boneRotParent</c>), the same rest composed
/// into model space, and the animation skeletons it targets
/// (<c>m_vecNmSkeletonRefs</c>).
/// </summary>
/// <param name="ModelSpace">
/// Each bone's rest in model space, composed from the parent-space values.
/// (VRF's per-bone inverse bind matrix is only the parent-space value
/// inverted, not a model-space bind; verified 2026-10-24.)
/// </param>
public sealed record ModelSkeleton(IReadOnlyList<SkeletonBone> Bones, IReadOnlyList<BonePose> ModelSpace, IReadOnlyList<string> SkeletonRefs)
{
    /// <param name="fileName">The model's path, e.g. <c>weapons/models/ak47/weapon_rif_ak47.vmdl_c</c>.</param>
    public static ModelSkeleton Read(byte[] bytes, string fileName)
    {
        using var resource = new Resource { FileName = Path.GetFileName(fileName) };
        resource.Read(new MemoryStream(bytes));
        if (resource.DataBlock is not Model model)
        {
            throw new InvalidDataException($"{fileName} is not a model.");
        }

        var list = model.Skeleton.Bones;
        if (list.Length == 0)
        {
            throw new InvalidDataException($"{fileName} has no bones.");
        }

        // VRF's Bone.Position / Angle are the parent-space values the model stores.
        var bones = new List<SkeletonBone>(list.Length);
        foreach (var bone in list)
        {
            var parent = bone.Parent is null ? -1 : Array.IndexOf(list, bone.Parent);
            bones.Add(new SkeletonBone(bones.Count, bone.Name, parent, new BonePose(bone.Position, bone.Angle, 1f)));
        }

        var refs = model.Data.TryGetValue("m_vecNmSkeletonRefs", out var value)
            ? value.Values.Select(item => item.ToString() ?? "").Where(item => item.Length > 0).ToList()
            : [];
        return new ModelSkeleton(bones, SkeletonBuilder.ModelSpace(bones), refs);
    }
}
