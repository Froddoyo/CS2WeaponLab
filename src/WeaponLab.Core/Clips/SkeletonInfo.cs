using System.Globalization;
using System.Numerics;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace WeaponLab.Core.Clips;

/// <param name="ParentIndex">Index of the parent bone, or -1 for a root.</param>
/// <param name="Rest">Reference pose relative to the parent.</param>
public sealed record SkeletonBone(int Index, string Name, int ParentIndex, BonePose Rest);

/// <summary>
/// Bone names, hierarchy and reference pose of a <c>.vnmskel</c>. Clip
/// tracks follow this bone order.
/// </summary>
public sealed record SkeletonInfo(string ResourcePath, IReadOnlyList<SkeletonBone> Bones)
{
    public string BoneName(int index) => index >= 0 && index < Bones.Count ? Bones[index].Name : $"track {index}";

    /// <param name="resourcePath">Skeleton path as clips name it, without the <c>_c</c> suffix.</param>
    public static SkeletonInfo Read(byte[] compiledBytes, string resourcePath)
    {
        using var resource = new Resource { FileName = resourcePath + "_c" };
        resource.Read(new MemoryStream(compiledBytes));
        if (resource.DataBlock is not BinaryKV3 data)
        {
            throw new InvalidDataException($"Not a skeleton resource: {resourcePath}");
        }

        return FromData(data.Data.Root, resourcePath);
    }

    internal static SkeletonInfo FromData(KVObject root, string resourcePath)
    {
        var names = root["m_boneIDs"];
        var parents = root["m_parentIndices"];
        var rest = root["m_parentSpaceReferencePose"];
        if (names.Count != parents.Count || names.Count != rest.Count)
        {
            throw new InvalidDataException($"Skeleton {resourcePath} has mismatched bone arrays.");
        }

        var bones = new List<SkeletonBone>(names.Count);
        for (var index = 0; index < names.Count; index++)
        {
            bones.Add(new SkeletonBone(index, names[index].ToString() ?? "", parents[index].ToInt32(CultureInfo.InvariantCulture), RestPose(rest[index])));
        }

        return new SkeletonInfo(resourcePath, bones);
    }

    // Stored as [tx, ty, tz, scale, qx, qy, qz, qw].
    private static BonePose RestPose(KVObject values)
    {
        if (values.Count != 8)
        {
            throw new InvalidDataException($"Unexpected reference pose layout ({values.Count} values).");
        }

        float Value(int index) => values[index].ToSingle(CultureInfo.InvariantCulture);
        return new BonePose(
            new Vector3(Value(0), Value(1), Value(2)),
            new Quaternion(Value(4), Value(5), Value(6), Value(7)),
            Value(3));
    }
}
