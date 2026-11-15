using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.Blocks;
using WeaponLab.Core.Clips.Encoding;
using WeaponLab.Core.Delivery;

namespace WeaponLab.Core.Clips;

/// <summary>
/// Moves a clip's weapon animation (a secondary animation) onto another
/// skeleton: bones with the same name keep their motion, new bones start at
/// their rest pose and can be given motion of their own. The arms animation
/// and everything else in the clip stay as they are. Experimental
/// (2026-10-24), made for the custom skeleton test.
/// </summary>
public static class ClipRetarget
{
    /// <param name="fromSkeleton">The secondary animation's current skeleton, e.g. <c>animation/skeletons/weapons/galil.vnmskel</c>.</param>
    /// <param name="fromBones">That skeleton's bone names in track order.</param>
    /// <param name="adjust">Optional motion per (frame, bone name, pose); return the pose to write.</param>
    public static byte[] Retarget(
        byte[] templateBytes,
        string resourcePath,
        string fromSkeleton,
        IReadOnlyList<string> fromBones,
        string toSkeleton,
        IReadOnlyList<SkeletonBone> toBones,
        Func<int, string, BonePose, BonePose>? adjust = null)
    {
        using var template = ClipTemplate.Open(templateBytes, resourcePath);
        var stream = template.Secondary.FirstOrDefault(item => item.Skeleton == fromSkeleton)
            ?? throw new InvalidDataException($"{resourcePath} has no animation on {fromSkeleton}.");
        if (stream.TrackCount != fromBones.Count)
        {
            throw new InvalidDataException($"{resourcePath}: {fromSkeleton} has {stream.TrackCount} tracks but {fromBones.Count} bone names were given.");
        }

        var stock = stream.ReadPoses();
        var poses = new BonePose[stream.FrameCount][];
        for (var frame = 0; frame < poses.Length; frame++)
        {
            poses[frame] = new BonePose[toBones.Count];
            for (var bone = 0; bone < toBones.Count; bone++)
            {
                var source = IndexOf(fromBones, toBones[bone].Name);
                var pose = source >= 0 ? stock[frame][source] : toBones[bone].Rest;
                poses[frame][bone] = adjust?.Invoke(frame, toBones[bone].Name, pose) ?? pose;
            }
        }

        var skeleton = stream.Data["m_skeleton"];
        stream.Data["m_skeleton"] = new KVObject(toSkeleton) { Flag = skeleton.Flag };
        TrackEncoder.EncodeFresh(stream.Data, poses);

        var references = template.References ?? throw new InvalidDataException($"{resourcePath} has no RERL.");
        var reference = references.ResourceRefInfoList.FirstOrDefault(item => item.Name == fromSkeleton);
        if (reference is not null)
        {
            reference.Name = toSkeleton;
            reference.Id = ResourceId.Of(toSkeleton);
        }
        else
        {
            references.ResourceRefInfoList.Add(new ResourceExtRefList.ResourceReferenceInfo { Name = toSkeleton, Id = ResourceId.Of(toSkeleton) });
        }

        using var data = new MemoryStream();
        template.Clip.Serialize(data);
        using var rerl = new MemoryStream();
        references.Serialize(rerl);
        var bytes = ResourceSplicer.ReplaceBlocks(templateBytes, new Dictionary<string, byte[]> { ["DATA"] = data.ToArray(), ["RERL"] = rerl.ToArray() });

        Verify(bytes, resourcePath, toSkeleton, poses);
        return bytes;
    }

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (var index = 0; index < names.Count; index++)
        {
            if (names[index] == name)
            {
                return index;
            }
        }

        return -1;
    }

    private static void Verify(byte[] bytes, string resourcePath, string toSkeleton, BonePose[][] expected)
    {
        using var decoded = ClipTemplate.Open(bytes, resourcePath);
        var stream = decoded.Secondary.FirstOrDefault(item => item.Skeleton == toSkeleton)
            ?? throw new InvalidDataException("The retargeted animation lost its skeleton.");
        var poses = stream.ReadPoses();
        var tolerance = PoseTolerance.Default;
        for (var frame = 0; frame < expected.Length; frame++)
        {
            for (var track = 0; track < expected[frame].Length; track++)
            {
                if (!tolerance.Accepts(expected[frame][track], poses[frame][track]))
                {
                    throw new InvalidDataException($"Retargeted pose of track {track} on frame {frame} did not survive encoding.");
                }
            }
        }
    }
}
