using System.Numerics;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;

namespace WeaponLab.Core.Export;

/// <summary>
/// Keeps the weapon's hand targets on the hands. In game the arms reach for
/// <c>wpnHand_L/R</c> (children of <c>wpn</c>), so a gun moved without them
/// drags the hands along, and a hand moved without them snaps back
/// (verified in game on PC2, 2026-10-28). On export each <c>wpnHand_*</c>
/// follows its <c>hand_*</c> with CS2's own per-frame offset taken from the
/// reference poses, so an untouched clip stays byte-identical and edits to
/// <c>wpnHand_*</c> themselves are replaced by the hand's motion.
/// </summary>
public static class HandGlue
{
    private const float SameEpsilon = 1e-5f;

    /// <param name="reference">The arms stream as the .blend was built (parent-local poses per frame).</param>
    /// <param name="edited">The arms stream Blender sampled for export.</param>
    /// <param name="bones">The arms skeleton in track order (the body's in third person).</param>
    /// <param name="tolerance">Scales the same-pose allowance (<see cref="EditDeltas.WorldTolerance"/> for third-person clips).</param>
    public static BonePose[][] Apply(BonePose[][] reference, BonePose[][] edited, IReadOnlyList<RigBone> bones, float tolerance = 1f)
    {
        if (reference.Length != edited.Length)
        {
            throw new InvalidDataException($"The arms sample has {edited.Length} frames but the reference has {reference.Length}.");
        }

        var parents = bones.Select(bone => bone.Parent).ToArray();
        var pairs = new[] { "L", "R" }
            .Select(side => (Hand: IndexOf(bones, "hand_" + side), Target: IndexOf(bones, "wpnHand_" + side)))
            .Where(pair => pair.Hand >= 0 && pair.Target >= 0)
            .ToList();
        var result = edited.Select(frame => (BonePose[])frame.Clone()).ToArray();
        if (pairs.Count == 0)
        {
            return result;
        }

        var identity = new BonePose(Vector3.Zero, Quaternion.Identity, 1);
        for (var frame = 0; frame < result.Length; frame++)
        {
            var referenceModel = BonePose.ModelSpace(reference[frame], parents);
            var editedModel = BonePose.ModelSpace(result[frame], parents);
            foreach (var (hand, target) in pairs)
            {
                // CS2's own offset of the target from the hand on this frame.
                var offset = BonePose.Compose(BonePose.Inverse(referenceModel[hand]), referenceModel[target]);
                var wanted = BonePose.Compose(editedModel[hand], offset);
                var parent = parents[target] < 0 ? identity : editedModel[parents[target]];
                var local = BonePose.Compose(BonePose.Inverse(parent), wanted);
                result[frame][target] = BonePose.Differ(local, reference[frame][target], SameEpsilon * tolerance) ? local : reference[frame][target];
            }
        }

        return result;
    }

    private static int IndexOf(IReadOnlyList<RigBone> bones, string name)
    {
        for (var index = 0; index < bones.Count; index++)
        {
            if (bones[index].Name == name)
            {
                return index;
            }
        }

        return -1;
    }
}
