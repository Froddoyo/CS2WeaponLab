using System.Numerics;
using ValveResourceFormat.ResourceTypes.ModelAnimation;

namespace WeaponLab.Core.Clips;

/// <summary>
/// One bone's pose on one frame, in engine parent-local space.
/// </summary>
public readonly record struct BonePose(Vector3 Translation, Quaternion Rotation, float Scale)
{
    internal static BonePose From(FrameBone bone) => new(bone.Position, bone.Angle, bone.Scale);

    public bool IsFinite =>
        float.IsFinite(Translation.X) && float.IsFinite(Translation.Y) && float.IsFinite(Translation.Z)
        && float.IsFinite(Rotation.X) && float.IsFinite(Rotation.Y) && float.IsFinite(Rotation.Z) && float.IsFinite(Rotation.W)
        && float.IsFinite(Scale);

    /// <summary>
    /// Distance between two rotations that treats q and -q as equal.
    /// </summary>
    public static float RotationDistance(Quaternion a, Quaternion b)
    {
        var same = new Vector4(a.X - b.X, a.Y - b.Y, a.Z - b.Z, a.W - b.W).Length();
        var flipped = new Vector4(a.X + b.X, a.Y + b.Y, a.Z + b.Z, a.W + b.W).Length();
        return Math.Min(same, flipped);
    }

    /// <summary>Angle between two rotations in degrees.</summary>
    public static double RotationDegrees(Quaternion a, Quaternion b)
    {
        var chord = RotationDistance(Quaternion.Normalize(a), Quaternion.Normalize(b));
        return 4 * Math.Asin(Math.Clamp(chord / 2.0, 0, 1)) * 180 / Math.PI;
    }

    /// <summary>A child's pose in its parent's space taken into the parent's own space (model space when the parent's is).</summary>
    public static BonePose Compose(BonePose parent, BonePose local) => new(
        parent.Translation + Vector3.Transform(local.Translation * parent.Scale, parent.Rotation),
        Quaternion.Normalize(parent.Rotation * local.Rotation),
        parent.Scale * local.Scale);

    public static BonePose Inverse(BonePose pose)
    {
        var rotation = Quaternion.Inverse(pose.Rotation);
        return new BonePose(Vector3.Transform(-pose.Translation, rotation) / pose.Scale, rotation, 1 / pose.Scale);
    }

    /// <summary>Model-space poses from parent-local ones; parents come before their children.</summary>
    public static BonePose[] ModelSpace(IReadOnlyList<BonePose> local, IReadOnlyList<int> parents)
    {
        var result = new BonePose[local.Count];
        for (var index = 0; index < local.Count; index++)
        {
            result[index] = parents[index] < 0 ? local[index] : Compose(result[parents[index]], local[index]);
        }

        return result;
    }

    /// <summary>True when the poses differ by more than float decomposition noise.</summary>
    public static bool Differ(BonePose a, BonePose b, float epsilon = 1e-6f)
    {
        return Vector3.Distance(a.Translation, b.Translation) > epsilon
            || RotationDistance(a.Rotation, b.Rotation) > epsilon
            || Math.Abs(a.Scale - b.Scale) > epsilon;
    }
}
