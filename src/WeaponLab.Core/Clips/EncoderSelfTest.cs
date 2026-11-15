using System.Numerics;

namespace WeaponLab.Core.Clips;

/// <param name="Check">Which check failed, or "all" when every check passed.</param>
/// <param name="MaxRotationErrorDegrees">Worst rotation error of the full re-encode.</param>
public sealed record EncoderCheckResult(
    string ResourcePath,
    bool Passed,
    string Check,
    string Message,
    double MaxTranslationError,
    double MaxRotationErrorDegrees,
    double MaxScaleError);

/// <summary>
/// Proves the clip writer against real game clips:
/// <list type="number">
/// <item>No edit: the output is byte-identical to the template.</item>
/// <item>Full re-encode: every track of every stream re-encoded stays within tolerance.</item>
/// <item>Edit: one bone rotated 10° lands within tolerance and every other track decodes bit for bit.</item>
/// </list>
/// </summary>
public static class EncoderSelfTest
{
    public const float EditDegrees = 10f;

    public static EncoderCheckResult Run(byte[] templateBytes, string resourcePath)
    {
        var check = "no-op";
        try
        {
            var noOp = ClipWriter.Write(templateBytes, resourcePath, ClipEdit.None);
            if (!noOp.IdenticalToTemplate || !noOp.Bytes.AsSpan().SequenceEqual(templateBytes))
            {
                return Fail(resourcePath, check, "Output differs from the template although nothing was edited.");
            }

            check = "re-encode";
            var full = ClipWriter.Write(templateBytes, resourcePath, ClipEdit.None, PoseTolerance.Default, reencodeAll: true);

            check = "edit";
            var (edit, track) = RotateOneBone(templateBytes, resourcePath);
            var edited = ClipWriter.Write(templateBytes, resourcePath, edit);
            var primary = edited.Streams[0];
            if (edited.IdenticalToTemplate || !primary.ChangedTracks.SequenceEqual([track]))
            {
                return Fail(resourcePath, check, $"Expected only track {track} to change, got [{string.Join(", ", primary.ChangedTracks)}].");
            }

            return new EncoderCheckResult(
                resourcePath,
                Passed: true,
                "all",
                $"{full.Bytes.Length} bytes after full re-encode",
                full.Streams.Max(stream => stream.MaxTranslationError),
                full.Streams.Max(stream => stream.MaxRotationErrorDegrees),
                full.Streams.Max(stream => stream.MaxScaleError));
        }
        catch (Exception error) when (error is ClipWriteException or InvalidDataException)
        {
            return Fail(resourcePath, check, error.Message);
        }
    }

    // Rotates the middle track of the primary stream on every frame.
    private static (ClipEdit Edit, int Track) RotateOneBone(byte[] templateBytes, string resourcePath)
    {
        using var template = ClipTemplate.Open(templateBytes, resourcePath);
        var poses = template.Primary.ReadPoses();
        var track = template.Primary.TrackCount / 2;
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitX, EditDegrees * MathF.PI / 180f);
        foreach (var frame in poses)
        {
            var pose = frame[track];
            frame[track] = pose with { Rotation = Quaternion.Normalize(turn * pose.Rotation) };
        }

        return (new ClipEdit { Primary = poses }, track);
    }

    private static EncoderCheckResult Fail(string resourcePath, string check, string message)
    {
        return new EncoderCheckResult(resourcePath, Passed: false, check, message, 0, 0, 0);
    }
}
