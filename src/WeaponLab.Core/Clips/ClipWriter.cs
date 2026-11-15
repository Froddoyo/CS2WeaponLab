using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Clips;

/// <summary>
/// Desired poses for a clip. A stream left out keeps its template data.
/// Track counts must match the template; frame counts too, unless
/// <see cref="FrameCount"/> changes the clip's length.
/// </summary>
public sealed class ClipEdit
{
    public BonePose[][]? Primary { get; init; }

    /// <summary>
    /// A new length in frames (see <see cref="ClipLength"/>), or null to keep
    /// the template's. Every track of every stream is then re-encoded; a stream
    /// left out gets its template poses cut or held to the new length.
    /// </summary>
    public int? FrameCount { get; init; }

    /// <summary>Desired poses per secondary stream index.</summary>
    public IReadOnlyDictionary<int, BonePose[][]> Secondary { get; init; } = new Dictionary<int, BonePose[][]>();

    /// <summary>Changes to the template's events (retimes, renames, field edits).</summary>
    public IReadOnlyList<EventChange> Events { get; init; } = [];

    /// <summary>Events added or removed, and a new sync track.</summary>
    public EventListEdit EventList { get; init; } = EventListEdit.None;

    public static ClipEdit None { get; } = new();
}

/// <summary>Maximum allowed difference between desired and encoded poses.</summary>
public sealed record PoseTolerance(double Translation, double RotationDegrees, double Scale)
{
    /// <summary>The limits CS2AL used: 0.02 units, 0.05°, 0.001 scale.</summary>
    public static PoseTolerance Default { get; } = new(0.02, 0.05, 0.001);

    public bool Accepts(BonePose expected, BonePose actual) =>
        System.Numerics.Vector3.Distance(expected.Translation, actual.Translation) <= Translation
        && BonePose.RotationDegrees(expected.Rotation, actual.Rotation) <= RotationDegrees
        && Math.Abs(expected.Scale - actual.Scale) <= Scale;
}

/// <param name="ChangedTracks">Track indices that were re-encoded.</param>
public sealed record StreamWriteReport(
    string Label,
    string Skeleton,
    IReadOnlyList<int> ChangedTracks,
    double MaxTranslationError,
    double MaxRotationErrorDegrees,
    double MaxScaleError);

/// <param name="IdenticalToTemplate">True when nothing changed and the template bytes were returned.</param>
/// <param name="ChangedEvents">Positions in the written clip of events that were changed or added.</param>
public sealed record ClipWriteResult(
    byte[] Bytes,
    bool IdenticalToTemplate,
    IReadOnlyList<StreamWriteReport> Streams,
    IReadOnlyList<int> ChangedEvents)
{
    /// <summary>The template's events that were removed, described for reports.</summary>
    public IReadOnlyList<string> RemovedEvents { get; init; } = [];

    public bool SyncTrackChanged { get; init; }
}

public sealed class ClipWriteException(string message) : Exception(message);

/// <summary>
/// Writes a patched copy of a compiled clip. Only edited tracks are
/// re-encoded; header, RERL, RED2, events and all other DATA values are
/// kept, and the result is decoded again to prove it.
/// </summary>
public static class ClipWriter
{
    private static readonly string[] PoseKeys = ["m_compressedPoseData", "m_compressedPoseOffsets", "m_trackCompressionSettings"];
    private const string SecondaryKey = "m_secondaryAnimations";
    private const string ReadOffsetKey = "m_nTrackReadOffset";

    public static ClipWriteResult Write(byte[] templateBytes, string resourcePath, ClipEdit edit, PoseTolerance? tolerance = null)
    {
        return Write(templateBytes, resourcePath, edit, tolerance ?? PoseTolerance.Default, reencodeAll: false);
    }

    /// <param name="reencodeAll">Re-encode every track of every stream even when unchanged. Used to test the encoder.</param>
    internal static ClipWriteResult Write(byte[] templateBytes, string resourcePath, ClipEdit edit, PoseTolerance tolerance, bool reencodeAll)
    {
        using var template = ClipTemplate.Open(templateBytes, resourcePath);
        var frames = edit.FrameCount is { } count && count != template.Primary.FrameCount ? count : (int?)null;
        List<StreamPlan> plans;
        if (frames is { } length)
        {
            if (length < ClipLength.MinFrames || length > ClipLength.MaxFrames)
            {
                throw new ClipWriteException($"A clip's length must be {ClipLength.MinFrames} to {ClipLength.MaxFrames} frames.");
            }

            // Poses are read before the frame values change; then events and sync sections move to keep their frames.
            plans = template.Streams.Select(stream => PlanLength(stream, DesiredFor(stream, edit), length)).ToList();
            ClipLength.Apply(template.Primary.Data, template.Primary.FrameCount, length);
        }
        else
        {
            plans = template.Streams.Select(stream => Plan(stream, DesiredFor(stream, edit), reencodeAll)).ToList();
        }

        var events = EventPatcher.Prepare(template.Primary.Data, edit.Events, edit.EventList);

        if (frames is null && !plans.Any(plan => plan.AnyChanged) && events is null)
        {
            var unchanged = plans.Select(plan => new StreamWriteReport(plan.Stream.Label, plan.Stream.Skeleton, [], 0, 0, 0)).ToList();
            return new ClipWriteResult(templateBytes, IdenticalToTemplate: true, unchanged, []);
        }

        // Fingerprint everything that must survive before mutating the KV tree.
        foreach (var plan in plans)
        {
            plan.TakeFingerprints(plan.Stream.IsPrimary && events is not null ? events.RootKeys : []);
        }

        foreach (var plan in plans.Where(plan => plan.AnyChanged))
        {
            TrackEncoder.Encode(plan.Stream, plan.Desired, plan.Changed);
        }

        events?.Apply(template.Primary.Data);
        var output = events is { NewResources.Count: > 0 }
            ? WithReferences(templateBytes, template, events.NewResources)
            : ResourceSplicer.ReplaceData(templateBytes, template.Clip);
        var result = Verify(output, resourcePath, plans, events, tolerance);
        if (events is { NewResources.Count: > 0 })
        {
            VerifyReferences(output, resourcePath, events.NewResources);
        }

        return result;
    }

    /// <summary>
    /// The patched DATA plus an external reference list that also names the
    /// resources edited events now point at (a new particle system), so the
    /// game loads them with the clip.
    /// </summary>
    private static byte[] WithReferences(byte[] templateBytes, ClipTemplate template, IReadOnlyList<string> resources)
    {
        var references = template.References ?? throw new ClipWriteException($"{template.ResourcePath} has no external reference list.");
        foreach (var resource in resources)
        {
            if (!references.ResourceRefInfoList.Any(item => item.Name == resource))
            {
                references.ResourceRefInfoList.Add(new ValveResourceFormat.Blocks.ResourceExtRefList.ResourceReferenceInfo
                {
                    Name = resource,
                    Id = Delivery.ResourceId.Of(resource),
                });
            }
        }

        using var data = new MemoryStream();
        template.Clip.Serialize(data);
        using var rerl = new MemoryStream();
        references.Serialize(rerl);
        return ResourceSplicer.ReplaceBlocks(templateBytes, new Dictionary<string, byte[]> { ["DATA"] = data.ToArray(), ["RERL"] = rerl.ToArray() });
    }

    private static void VerifyReferences(byte[] output, string resourcePath, IReadOnlyList<string> resources)
    {
        using var decoded = ClipTemplate.Open(output, resourcePath);
        var names = decoded.References?.ResourceRefInfoList.Select(item => item.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        if (resources.FirstOrDefault(resource => !names.Contains(resource)) is { } missing)
        {
            throw new ClipWriteException($"{missing} did not reach the clip's external references.");
        }
    }

    private static BonePose[][]? DesiredFor(PoseStream stream, ClipEdit edit)
    {
        return stream.IsPrimary ? edit.Primary : edit.Secondary.GetValueOrDefault(stream.SecondaryIndex);
    }

    private static StreamPlan Plan(PoseStream stream, BonePose[][]? requested, bool reencodeAll)
    {
        var stock = stream.ReadPoses();
        var changed = new bool[stream.TrackCount];
        if (requested is null && !reencodeAll)
        {
            return new StreamPlan(stream, stock, stock, changed);
        }

        var desired = requested ?? stock;
        ValidateShape(stream, desired, stream.FrameCount);

        // Unchanged tracks keep their exact stock values so they verify bit for bit.
        var merged = new BonePose[stream.FrameCount][];
        for (var frame = 0; frame < stream.FrameCount; frame++)
        {
            merged[frame] = new BonePose[stream.TrackCount];
            for (var track = 0; track < stream.TrackCount; track++)
            {
                var wanted = desired[frame][track];
                if (BonePose.Differ(stock[frame][track], wanted))
                {
                    changed[track] = true;
                }

                merged[frame][track] = wanted;
            }
        }

        for (var track = 0; track < stream.TrackCount; track++)
        {
            if (reencodeAll)
            {
                changed[track] = true;
            }

            if (!changed[track])
            {
                for (var frame = 0; frame < stream.FrameCount; frame++)
                {
                    merged[frame][track] = stock[frame][track];
                }
            }
        }

        return new StreamPlan(stream, stock, merged, changed);
    }

    /// <summary>A plan for a clip at a new length: every track is written, from the requested poses or the template's cut or held.</summary>
    private static StreamPlan PlanLength(PoseStream stream, BonePose[][]? requested, int frames)
    {
        var stock = ClipLength.Resample(stream.ReadPoses(), frames);
        var desired = requested ?? stock;
        ValidateShape(stream, desired, frames);
        return new StreamPlan(stream, stock, desired, Enumerable.Repeat(true, stream.TrackCount).ToArray());
    }

    private static void ValidateShape(PoseStream stream, BonePose[][] poses, int frames)
    {
        if (poses.Length != frames)
        {
            throw new ClipWriteException($"{stream.Label}: expected {frames} frames, got {poses.Length}. Set the clip's length to change it.");
        }

        for (var frame = 0; frame < poses.Length; frame++)
        {
            if (poses[frame].Length != stream.TrackCount)
            {
                throw new ClipWriteException($"{stream.Label}: frame {frame} has {poses[frame].Length} tracks, expected {stream.TrackCount}.");
            }

            foreach (var pose in poses[frame])
            {
                var length = pose.Rotation.LengthSquared();
                if (!pose.IsFinite || length < 0.5f || length > 1.5f)
                {
                    throw new ClipWriteException($"{stream.Label}: frame {frame} has an invalid pose value.");
                }
            }
        }
    }

    private static ClipWriteResult Verify(byte[] output, string resourcePath, List<StreamPlan> plans, EventPatcher? events, PoseTolerance tolerance)
    {
        using var decoded = ClipTemplate.Open(output, resourcePath);
        events?.Verify(decoded.Primary.Data);
        var decodedStreams = decoded.Streams.ToList();
        if (decodedStreams.Count != plans.Count)
        {
            throw new ClipWriteException("The number of pose streams changed.");
        }

        var reports = new List<StreamWriteReport>();
        for (var index = 0; index < plans.Count; index++)
        {
            reports.Add(plans[index].Verify(decodedStreams[index], tolerance));
        }

        return new ClipWriteResult(output, IdenticalToTemplate: false, reports, events?.ChangedEvents ?? [])
        {
            RemovedEvents = events?.RemovedEvents ?? [],
            SyncTrackChanged = events?.SyncTrackChanged ?? false,
        };
    }

    private sealed class StreamPlan(PoseStream stream, BonePose[][] stock, BonePose[][] desired, bool[] changed)
    {
        private Dictionary<string, string> _preserved = [];
        private string?[] _trackFingerprints = [];

        public PoseStream Stream { get; } = stream;
        public BonePose[][] Desired { get; } = desired;
        public bool[] Changed { get; } = changed;
        public bool AnyChanged => Changed.Any(value => value);

        /// <param name="eventKeys">Keys the <see cref="EventPatcher"/> rewrites and verifies itself (events, sync track).</param>
        public void TakeFingerprints(IReadOnlyList<string> eventKeys)
        {
            // Secondary streams are checked by their own plans.
            var excluded = (AnyChanged ? PoseKeys.Append(SecondaryKey) : [SecondaryKey])
                .Concat(eventKeys)
                .ToArray();
            _preserved = Stream.Data.Children
                .Where(child => !excluded.Contains(child.Key))
                .ToDictionary(child => child.Key, child => KVFingerprint.Of(child.Value));

            var settings = Stream.Data["m_trackCompressionSettings"];
            _trackFingerprints = Enumerable.Range(0, Stream.TrackCount)
                .Select(track => Changed[track] ? null : KVFingerprint.OfExcept(settings[track], ReadOffsetKey))
                .ToArray();
        }

        public StreamWriteReport Verify(PoseStream decoded, PoseTolerance tolerance)
        {
            var label = Stream.Label;
            if (decoded.Skeleton != Stream.Skeleton || decoded.FrameCount != Desired.Length || decoded.TrackCount != Changed.Length)
            {
                throw new ClipWriteException($"{label}: skeleton, frame count or track count changed.");
            }

            foreach (var (key, fingerprint) in _preserved)
            {
                if (!decoded.Data.TryGetValue(key, out var value) || KVFingerprint.Of(value) != fingerprint)
                {
                    throw new ClipWriteException($"{label}: preserved value '{key}' changed.");
                }
            }

            var settings = decoded.Data["m_trackCompressionSettings"];
            for (var track = 0; track < _trackFingerprints.Length; track++)
            {
                if (_trackFingerprints[track] is { } fingerprint && KVFingerprint.OfExcept(settings[track], ReadOffsetKey) != fingerprint)
                {
                    throw new ClipWriteException($"{label}: settings of unedited track {track} changed.");
                }
            }

            var actual = decoded.ReadPoses();
            double maxTranslation = 0, maxRotation = 0, maxScale = 0;
            for (var frame = 0; frame < actual.Length; frame++)
            {
                for (var track = 0; track < Changed.Length; track++)
                {
                    var wanted = Desired[frame][track];
                    var got = actual[frame][track];
                    if (!Changed[track])
                    {
                        if (got != stock[frame][track])
                        {
                            throw new ClipWriteException($"{label}: unedited track {track} decodes differently on frame {frame}.");
                        }

                        continue;
                    }

                    maxTranslation = Math.Max(maxTranslation, System.Numerics.Vector3.Distance(wanted.Translation, got.Translation));
                    maxRotation = Math.Max(maxRotation, BonePose.RotationDegrees(wanted.Rotation, got.Rotation));
                    maxScale = Math.Max(maxScale, Math.Abs(wanted.Scale - got.Scale));
                }
            }

            if (maxTranslation > tolerance.Translation || maxRotation > tolerance.RotationDegrees || maxScale > tolerance.Scale)
            {
                throw new ClipWriteException(
                    $"{label}: encoding error over tolerance (translation {maxTranslation:0.#####}, rotation {maxRotation:0.#####}°, scale {maxScale:0.#####}).");
            }

            var changedTracks = Enumerable.Range(0, Changed.Length).Where(track => Changed[track]).ToList();
            return new StreamWriteReport(label, Stream.Skeleton, changedTracks, maxTranslation, maxRotation, maxScale);
        }
    }
}
