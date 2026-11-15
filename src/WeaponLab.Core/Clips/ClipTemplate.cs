using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes.ModelAnimation;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;

namespace WeaponLab.Core.Clips;

/// <summary>
/// One stream of pose data in a clip: the primary hand rig, or a paired
/// secondary stream such as the weapon.
/// </summary>
public sealed class PoseStream
{
    internal PoseStream(AnimationClip clip, KVObject data, int secondaryIndex)
    {
        Clip = clip;
        Data = data;
        SecondaryIndex = secondaryIndex;
    }

    internal AnimationClip Clip { get; }

    /// <summary>The stream's KV data inside the clip's DATA block. Mutated by the encoder.</summary>
    internal KVObject Data { get; }

    /// <summary>-1 for the primary stream, else the index in <c>m_secondaryAnimations</c>.</summary>
    public int SecondaryIndex { get; }

    public bool IsPrimary => SecondaryIndex < 0;
    public string Skeleton => Clip.SkeletonName;
    public int FrameCount => Clip.NumFrames;
    public int TrackCount => Clip.TrackCompressionSettings.Length;

    public string Label => IsPrimary ? "primary" : $"secondary {SecondaryIndex}";

    /// <summary>Decodes every frame: <c>[frame][track]</c>.</summary>
    public BonePose[][] ReadPoses()
    {
        var buffer = new FrameBone[TrackCount];
        var frames = new BonePose[FrameCount][];
        for (var frame = 0; frame < FrameCount; frame++)
        {
            Clip.ReadFrame(frame, buffer);
            frames[frame] = buffer.Select(BonePose.From).ToArray();
        }

        return frames;
    }

    /// <summary>Which of rotation, translation and scale each track animates.</summary>
    public IReadOnlyList<TrackChannels> ReadChannels()
    {
        return Clip.TrackCompressionSettings
            .Select(setting => new TrackChannels(!setting.IsRotationStatic, !setting.IsTranslationStatic, !setting.IsScaleStatic))
            .ToList();
    }
}

public readonly record struct TrackChannels(bool Rotation, bool Translation, bool Scale)
{
    public bool Any => Rotation || Translation || Scale;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Rotation) parts.Add("rotation");
        if (Translation) parts.Add("translation");
        if (Scale) parts.Add("scale");
        return parts.Count == 0 ? "static" : string.Join(" + ", parts);
    }
}

/// <summary>
/// A compiled clip opened for reading poses or writing a patched copy.
/// The original bytes are kept untouched.
/// </summary>
public sealed class ClipTemplate : IDisposable
{
    private readonly Resource _resource;

    private ClipTemplate(byte[] bytes, string resourcePath, Resource resource, AnimationClip clip)
    {
        Bytes = bytes;
        ResourcePath = resourcePath;
        _resource = resource;
        Clip = clip;

        var root = clip.Data.Root;
        Primary = new PoseStream(clip, root, -1);
        var secondaryData = root.TryGetValue("m_secondaryAnimations", out var array) && array.IsArray ? array : null;
        Secondary = clip.SecondaryAnimations
            .Select((secondary, index) => new PoseStream(
                secondary,
                secondaryData?[index] ?? throw new InvalidDataException("Secondary animation data is missing."),
                index))
            .ToList();
    }

    public byte[] Bytes { get; }
    public string ResourcePath { get; }
    internal AnimationClip Clip { get; }

    /// <summary>The clip's external references (RERL), changed in place by writers that add or swap resources.</summary>
    internal ValveResourceFormat.Blocks.ResourceExtRefList? References => _resource.ExternalReferences;

    public PoseStream Primary { get; }
    public IReadOnlyList<PoseStream> Secondary { get; }

    public IEnumerable<PoseStream> Streams => Secondary.Prepend(Primary);

    public static ClipTemplate Open(byte[] bytes, string resourcePath)
    {
        var resource = new Resource { FileName = resourcePath };
        try
        {
            resource.Read(new MemoryStream(bytes));
            if (resource.DataBlock is not AnimationClip clip)
            {
                throw new InvalidDataException($"Not an animation clip: {resourcePath}");
            }

            return new ClipTemplate(bytes, resourcePath, resource, clip);
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    public void Dispose() => _resource.Dispose();
}
