using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes.ModelAnimation2;
using WeaponLab.Core.Game;

namespace WeaponLab.Core.Clips;

/// <summary>
/// Decodes compiled clips with ValveResourceFormat into <see cref="ClipInfo"/>.
/// </summary>
public static class ClipReader
{
    public static ClipInfo Read(GameArchive archive, string resourcePath)
    {
        return Read(archive.ReadBytes(resourcePath), resourcePath);
    }

    public static ClipInfo Read(byte[] bytes, string resourcePath)
    {
        using var resource = new Resource { FileName = resourcePath };
        try
        {
            resource.Read(new MemoryStream(bytes));
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Cannot decode clip {resourcePath}: {error.Message}", error);
        }

        if (resource.DataBlock is not AnimationClip clip)
        {
            throw new InvalidDataException($"Not an animation clip: {resourcePath}");
        }

        if (clip.NumFrames < 1 || !float.IsFinite(clip.Duration))
        {
            throw new InvalidDataException($"Invalid clip timing in {resourcePath}");
        }

        return new ClipInfo(
            resourcePath,
            GameArchive.Sha256(bytes),
            (int)clip.NumFrames,
            clip.Duration,
            clip.IsAdditive,
            clip.SkeletonName,
            clip.SecondaryAnimations.Select(secondary => secondary.SkeletonName).ToList(),
            clip.TrackCompressionSettings.Length,
            ReadEvents(clip.Data.Root))
        {
            SyncTrack = ReadSyncTrack(clip.Data.Root),
        };
    }

    internal static IReadOnlyList<SyncSection> ReadSyncTrack(KVObject clipData)
    {
        if (!clipData.TryGetValue("m_syncTrack", out var track) || !track.TryGetValue("m_syncEvents", out var sections) || !sections.IsArray)
        {
            return [];
        }

        return sections.Values
            .Select(section => new SyncSection(Text(section, "m_ID"), WrappedNumber(section, "m_startTime"), WrappedNumber(section, "m_duration")))
            .ToList();
    }

    internal static IReadOnlyList<ClipEvent> ReadEvents(KVObject clipData)
    {
        if (!clipData.TryGetValue("m_events", out var events) || !events.IsArray)
        {
            return [];
        }

        var result = new List<ClipEvent>(events.Count);
        for (var index = 0; index < events.Count; index++)
        {
            result.Add(ReadEvent(index, events[index]));
        }

        return result;
    }

    private static ClipEvent ReadEvent(int index, KVObject value)
    {
        var className = Text(value, "_class");
        var kind = EventSchema.KindOf(className);
        var name = EventSchema.NameKey(className) is { } nameKey ? Text(value, nameKey) : Text(value, "m_syncID");
        var fields = value.Children
            .Where(child => child.Key != "_class" && !child.Value.IsArray && !child.Value.IsCollection && !child.Value.IsNull)
            .Select(child => KeyValuePair.Create(child.Key, FieldText(child.Value)));

        return new ClipEvent(
            index,
            kind,
            className,
            name,
            kind == ClipEventKind.Gameplay ? Text(value, "m_secondaryID") : "",
            WrappedNumber(value, "m_flStartTime"),
            WrappedNumber(value, "m_flDuration"))
        {
            Fields = new EventFields(fields),
        };
    }

    /// <summary>A plain value as text: flags as true/false, numbers in invariant culture.</summary>
    internal static string FieldText(KVObject value) => value.ValueType switch
    {
        KVValueType.Boolean => value.ToBoolean(System.Globalization.CultureInfo.InvariantCulture) ? "true" : "false",
        _ => value.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static string Text(KVObject value, string key)
    {
        return value.TryGetValue(key, out var child) && !child.IsNull ? child.ToString() ?? "" : "";
    }

    // Event times are stored as { m_flValue = ... }.
    private static double WrappedNumber(KVObject value, string key)
    {
        if (value.TryGetValue(key, out var wrapper) && wrapper.TryGetValue("m_flValue", out var number))
        {
            return number.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
        }

        return 0;
    }
}
