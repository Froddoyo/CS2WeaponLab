using System.Numerics;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace WeaponLab.Core.Clips.Encoding;

/// <summary>
/// Re-encodes selected tracks of a pose stream in Valve's compressed format
/// and copies the other tracks' bytes unchanged.
/// </summary>
/// <remarks>
/// Each frame stores, per track, only its animated channels as 16-bit words:
/// rotation (3 words, 48-bit smallest-three quaternion), then translation
/// (3 words quantized over per-axis ranges), then scale (1 word). Static
/// channels come from the track settings: <c>m_constantRotation</c> and the
/// range starts. Ported from CS2AL's CSALClip, which is based on VRF's
/// decoder (MIT).
/// </remarks>
internal static class TrackEncoder
{
    private const float QuaternionComponentMin = -0.7071067811865475244f;
    private const float QuaternionComponentRange = 1.4142135623730950488f;
    private const float MinimumRangeLength = 1e-6f;
    private const float FallbackRangeLength = 0.1f;
    private const float StaticEpsilon = 1e-6f;

    private static readonly string[] TranslationRangeKeys = ["m_translationRangeX", "m_translationRangeY", "m_translationRangeZ"];

    public static int WordsPerTrack(TrackCompressionSetting setting)
    {
        return (setting.IsRotationStatic ? 0 : 3) + (setting.IsTranslationStatic ? 0 : 3) + (setting.IsScaleStatic ? 0 : 1);
    }

    /// <param name="poses">Desired poses, <c>[frame][track]</c>, for every track.</param>
    /// <param name="reencode">Tracks to encode from <paramref name="poses"/>; the rest keep their bytes.</param>
    public static void Encode(PoseStream stream, BonePose[][] poses, bool[] reencode)
    {
        var clip = stream.Clip;
        var trackCount = reencode.Length;
        var settings = stream.Data["m_trackCompressionSettings"];

        var oldOffsets = new int[trackCount];
        var oldStride = 0;
        for (var track = 0; track < trackCount; track++)
        {
            oldOffsets[track] = oldStride;
            oldStride += WordsPerTrack(clip.TrackCompressionSettings[track]);
        }

        var layouts = new TrackLayout[trackCount];
        var newStride = 0;
        for (var track = 0; track < trackCount; track++)
        {
            var setting = settings[track];
            KVValues.Set(setting, "m_nTrackReadOffset", newStride);
            layouts[track] = reencode[track]
                ? WriteSettings(setting, poses, track)
                : TrackLayout.From(clip.TrackCompressionSettings[track]);
            newStride += layouts[track].Words;
        }

        using var data = new MemoryStream();
        using var writer = new BinaryWriter(data);
        var offsets = KVObject.Array();
        var offsetTemplate = stream.Data["m_compressedPoseOffsets"][0];

        for (var frame = 0; frame < poses.Length; frame++)
        {
            offsets.Add(KVValues.Like(offsetTemplate, checked((uint)(data.Position / 2))));
            for (var track = 0; track < trackCount; track++)
            {
                if (!reencode[track])
                {
                    var bytes = layouts[track].Words * 2;
                    var start = checked(((int)clip.CompressedPoseOffsets[frame] + oldOffsets[track]) * 2);
                    writer.Write(clip.CompressedPoseData.AsSpan(start, bytes));
                    continue;
                }

                WritePose(writer, settings[track], layouts[track], poses[frame][track]);
            }
        }

        if (data.Length != (long)poses.Length * newStride * 2)
        {
            throw new InvalidDataException("Internal error: encoded pose data has the wrong size.");
        }

        stream.Data["m_compressedPoseOffsets"] = offsets;
        stream.Data["m_compressedPoseData"] = KVObject.Blob(data.ToArray());
    }

    /// <summary>
    /// Writes a stream's tracks from scratch, for a skeleton with a different
    /// bone list: one compression setting per track (copied from the stream's
    /// first setting, then fitted to the poses) and all pose data.
    /// </summary>
    /// <param name="data">The stream's KV data (a clip root or one of its secondary animations).</param>
    /// <param name="poses">Poses <c>[frame][track]</c> for the new bone list; the frame count must stay the same.</param>
    public static void EncodeFresh(KVObject data, BonePose[][] poses)
    {
        var trackCount = poses[0].Length;
        var old = data["m_trackCompressionSettings"];
        var settings = KVObject.Array();
        settings.Flag = old.Flag;
        var layouts = new TrackLayout[trackCount];
        var stride = 0;
        for (var track = 0; track < trackCount; track++)
        {
            var setting = KVValues.Clone(old[0]);
            KVValues.Set(setting, "m_nTrackReadOffset", stride);
            layouts[track] = WriteSettings(setting, poses, track);
            stride += layouts[track].Words;
            settings.Add(setting);
        }

        data["m_trackCompressionSettings"] = settings;

        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes);
        var offsets = KVObject.Array();
        offsets.Flag = data["m_compressedPoseOffsets"].Flag;
        var offsetTemplate = data["m_compressedPoseOffsets"][0];
        foreach (var frame in poses)
        {
            offsets.Add(KVValues.Like(offsetTemplate, checked((uint)(bytes.Position / 2))));
            for (var track = 0; track < trackCount; track++)
            {
                WritePose(writer, settings[track], layouts[track], frame[track]);
            }
        }

        data["m_compressedPoseOffsets"] = offsets;
        data["m_compressedPoseData"] = KVObject.Blob(bytes.ToArray());
    }

    private static TrackLayout WriteSettings(KVObject setting, BonePose[][] poses, int track)
    {
        var first = poses[0][track];
        var layout = new TrackLayout(
            Rotation: poses.Any(frame => BonePose.RotationDistance(first.Rotation, frame[track].Rotation) > StaticEpsilon),
            Translation: poses.Any(frame => Vector3.Distance(first.Translation, frame[track].Translation) > StaticEpsilon),
            Scale: poses.Any(frame => Math.Abs(first.Scale - frame[track].Scale) > StaticEpsilon));

        KVValues.Set(setting, "m_bIsRotationStatic", !layout.Rotation);
        KVValues.Set(setting, "m_bIsTranslationStatic", !layout.Translation);
        KVValues.Set(setting, "m_bIsScaleStatic", !layout.Scale);

        var constant = setting["m_constantRotation"];
        float[] components = [first.Rotation.X, first.Rotation.Y, first.Rotation.Z, first.Rotation.W];
        var newConstant = KVObject.Array();
        for (var index = 0; index < 4; index++)
        {
            newConstant.Add(KVValues.Like(constant[index], components[index]));
        }

        setting["m_constantRotation"] = newConstant;

        SetRange(setting["m_translationRangeX"], poses.Select(frame => frame[track].Translation.X));
        SetRange(setting["m_translationRangeY"], poses.Select(frame => frame[track].Translation.Y));
        SetRange(setting["m_translationRangeZ"], poses.Select(frame => frame[track].Translation.Z));
        SetRange(setting["m_scaleRange"], poses.Select(frame => frame[track].Scale));
        return layout;
    }

    // Static channels decode as the range start, so a constant channel gets start = value.
    private static void SetRange(KVObject range, IEnumerable<float> values)
    {
        var list = values.ToList();
        var low = list.Min();
        var length = list.Max() - low;
        KVValues.Set(range, "m_flRangeStart", low);
        KVValues.Set(range, "m_flRangeLength", length < MinimumRangeLength ? FallbackRangeLength : length);
    }

    private static void WritePose(BinaryWriter writer, KVObject setting, TrackLayout layout, BonePose pose)
    {
        if (layout.Rotation)
        {
            WriteQuaternion(writer, pose.Rotation);
        }

        if (layout.Translation)
        {
            float[] values = [pose.Translation.X, pose.Translation.Y, pose.Translation.Z];
            for (var axis = 0; axis < 3; axis++)
            {
                writer.Write(Quantize(values[axis], setting[TranslationRangeKeys[axis]]));
            }
        }

        if (layout.Scale)
        {
            writer.Write(Quantize(pose.Scale, setting["m_scaleRange"]));
        }
    }

    // Read the range back as float, as the decoder does, so quantization matches it.
    private static ushort Quantize(float value, KVObject range)
    {
        var start = range["m_flRangeStart"].ToSingle(null);
        var length = range["m_flRangeLength"].ToSingle(null);
        return (ushort)Math.Clamp((int)MathF.Round((value - start) / length * 65535), 0, 65535);
    }

    /// <summary>
    /// Smallest-three packing: drop the largest component (made positive),
    /// store the other three as 15-bit values, and put the dropped index's
    /// two bits in the top bits of the first two words.
    /// </summary>
    internal static void WriteQuaternion(BinaryWriter writer, Quaternion rotation)
    {
        var q = Quaternion.Normalize(rotation);
        float[] components = [q.X, q.Y, q.Z, q.W];

        var dropped = 0;
        for (var index = 1; index < 4; index++)
        {
            if (Math.Abs(components[index]) > Math.Abs(components[dropped]))
            {
                dropped = index;
            }
        }

        if (components[dropped] < 0)
        {
            for (var index = 0; index < 4; index++)
            {
                components[index] = -components[index];
            }
        }

        var packed = components
            .Where((_, index) => index != dropped)
            .Select(value => (ushort)Math.Clamp((int)MathF.Round((value - QuaternionComponentMin) / QuaternionComponentRange * 32767), 0, 32767))
            .ToArray();
        packed[0] |= (ushort)((dropped & 2) << 14);
        packed[1] |= (ushort)((dropped & 1) << 15);

        foreach (var word in packed)
        {
            writer.Write(word);
        }
    }

    private readonly record struct TrackLayout(bool Rotation, bool Translation, bool Scale)
    {
        public int Words => (Rotation ? 3 : 0) + (Translation ? 3 : 0) + (Scale ? 1 : 0);

        public static TrackLayout From(TrackCompressionSetting setting) =>
            new(!setting.IsRotationStatic, !setting.IsTranslationStatic, !setting.IsScaleStatic);
    }
}
