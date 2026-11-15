using System.Numerics;
using WeaponLab.Core.Clips;

namespace WeaponLab.Core.Export;

/// <summary>
/// Turns a Blender sample into desired engine poses by applying only the
/// difference between the edited sample and the build's reference sample to
/// the exact stock poses. Unedited bones come out bit-identical to stock even
/// though Blender's matrix maths adds float noise. Ported from CS2AL.
/// </summary>
public static class EditDeltas
{
    // Below these, a difference is Blender decomposition noise, not an edit.
    private const float MoveThreshold = 2e-5f;
    private const float RotateThreshold = 2e-6f;
    private const float ScaleThreshold = 1e-5f;
    private const float CollapsedScale = 1e-8f;

    /// <summary>
    /// The noise allowance for third-person clips (0.26): the body stands about 60 units from its origin, so
    /// Blender's float32 matrices add about ten times the noise of the arms around the camera (2026-11-14: an
    /// untouched world reload_ak came out 2e-5 units off on wpnHand_L). Still far below anything visible.
    /// </summary>
    public const float WorldTolerance = 10f;

    /// <param name="additive">Additive clips store scale as a delta, so scale edits add instead of multiply.</param>
    /// <param name="tolerance">Scales the noise thresholds (<see cref="WorldTolerance"/> for third-person clips).</param>
    public static BonePose[][] Apply(BonePose[][] stock, BonePose[][] reference, BonePose[][] edited, bool additive, string label, float tolerance = 1f)
    {
        CheckShape(stock, reference, $"{label} reference");
        CheckShape(stock, edited, $"{label} edit");

        var desired = new BonePose[stock.Length][];
        for (var frame = 0; frame < stock.Length; frame++)
        {
            desired[frame] = (BonePose[])stock[frame].Clone();
            for (var track = 0; track < stock[frame].Length; track++)
            {
                var result = ApplyOne(stock[frame][track], reference[frame][track], edited[frame][track], additive, tolerance);
                if (result is { } pose)
                {
                    desired[frame][track] = pose;
                }
            }
        }

        return desired;
    }

    /// <returns>The edited pose, or null when the bone was not edited on this frame.</returns>
    internal static BonePose? ApplyOne(BonePose stock, BonePose reference, BonePose edited, bool additive, float tolerance = 1f)
    {
        if (!edited.IsFinite)
        {
            throw new InvalidDataException("The edited sample contains a non-finite value.");
        }

        var referenceRotation = Quaternion.Normalize(reference.Rotation);
        var editedRotation = Quaternion.Normalize(edited.Rotation);

        // A collapsed bone has no recoverable orientation: use the stock rotation.
        if (reference.Scale < CollapsedScale)
        {
            referenceRotation = stock.Rotation;
            if (edited.Scale < CollapsedScale)
            {
                editedRotation = referenceRotation;
            }
        }

        var deltaTranslation = edited.Translation - reference.Translation;
        var move = deltaTranslation.Length() > MoveThreshold * tolerance;
        var rotate = BonePose.RotationDistance(referenceRotation, editedRotation) > RotateThreshold * tolerance;

        var desiredScale = additive ? stock.Scale + (edited.Scale - reference.Scale)
            : reference.Scale < CollapsedScale ? stock.Scale + edited.Scale
            : stock.Scale * (edited.Scale / reference.Scale);
        var scale = Math.Abs(desiredScale - stock.Scale) > ScaleThreshold * tolerance;

        if (!move && !rotate && !scale)
        {
            return null;
        }

        var deltaRotation = rotate ? Quaternion.Normalize(editedRotation * Quaternion.Inverse(referenceRotation)) : Quaternion.Identity;
        return new BonePose(
            stock.Translation + (move ? deltaTranslation : Vector3.Zero),
            rotate ? Quaternion.Normalize(deltaRotation * stock.Rotation) : stock.Rotation,
            scale ? desiredScale : stock.Scale);
    }

    private static void CheckShape(BonePose[][] stock, BonePose[][] other, string label)
    {
        if (other.Length != stock.Length)
        {
            throw new ClipWriteException($"{label} has {other.Length} frames; the clip has {stock.Length}. Retiming is not supported.");
        }

        if (other.Length > 0 && other[0].Length != stock[0].Length)
        {
            throw new ClipWriteException($"{label} has {other[0].Length} bones; the clip has {stock[0].Length}.");
        }
    }
}
