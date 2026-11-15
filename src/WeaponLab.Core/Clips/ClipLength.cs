using ValveKeyValue;
using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Clips;

/// <summary>
/// Changing a clip's frame count, e.g. the Annabelle's 112-frame SSG08 reload
/// cut to the Nova's 50. The frame rate stays; a shorter clip drops its last
/// frames, a longer one holds its last pose; events and sync sections keep
/// their frames (times are normalized, so they are rescaled), and anything
/// past the new end moves to it. The last sync section always ends at the end.
/// </summary>
public static class ClipLength
{
    public const int MinFrames = 2;
    public const int MaxFrames = 3000;

    private const string ValueKey = "m_flValue";

    /// <summary>Frames cut to <paramref name="count"/>, or the last one held up to it; the same array when the length matches.</summary>
    public static T[] Resample<T>(T[] frames, int count)
    {
        if (frames.Length == count)
        {
            return frames;
        }

        if (frames.Length == 0)
        {
            throw new ClipWriteException("A clip with no frames cannot change its length.");
        }

        return Enumerable.Range(0, count).Select(frame => frames[Math.Min(frame, frames.Length - 1)]).ToArray();
    }

    /// <summary>A normalized time on the same frame in a clip whose last frame is <paramref name="toLast"/> instead of <paramref name="fromLast"/>.</summary>
    public static double Rescale(double time, int fromLast, int toLast) =>
        toLast <= 0 ? 0 : Math.Clamp(time * fromLast / toLast, 0, 1);

    /// <summary>A span on the same frames, cut at the clip's end.</summary>
    public static (double Start, double Duration) Rescale(double start, double duration, int fromLast, int toLast)
    {
        var newStart = Rescale(start, fromLast, toLast);
        var newEnd = Rescale(start + duration, fromLast, toLast);
        return (newStart, Math.Max(0, newEnd - newStart));
    }

    /// <summary>Sync sections on the same frames: sections starting at or past the end are dropped and the last one ends at the end.</summary>
    public static IReadOnlyList<SyncSection> Rescale(IReadOnlyList<SyncSection> sections, int fromLast, int toLast)
    {
        var moved = new List<SyncSection>();
        foreach (var section in sections)
        {
            var start = Rescale(section.StartTime, fromLast, toLast);
            if (moved.Count > 0 && start >= 1)
            {
                break;
            }

            moved.Add(section with { StartTime = start, Duration = Rescale(section.StartTime + section.Duration, fromLast, toLast) - start });
        }

        if (moved.Count > 0)
        {
            moved[^1] = moved[^1] with { Duration = 1 - moved[^1].StartTime };
        }

        return moved;
    }

    /// <summary>The clip as it reads at another length: same frame rate, its events and sync sections on the same frames.</summary>
    public static ClipInfo At(ClipInfo clip, int frames)
    {
        if (frames == clip.FrameCount)
        {
            return clip;
        }

        var (fromLast, toLast) = (clip.LastFrame, Math.Max(frames - 1, 0));
        var seconds = clip.FramesPerSecond > 0 ? toLast / clip.FramesPerSecond : clip.DurationSeconds;
        var events = clip.Events.Select(clipEvent =>
        {
            var (start, duration) = Rescale(clipEvent.StartTime, clipEvent.Duration, fromLast, toLast);
            return clipEvent with { StartTime = start, Duration = duration };
        }).ToList();
        return clip with { FrameCount = frames, DurationSeconds = seconds, Events = events, SyncTrack = Rescale(clip.SyncTrack, fromLast, toLast) };
    }

    /// <summary>
    /// The clip's edits at another length: every edited time keeps its frame,
    /// and the length is stored with them (null when it is the template's again).
    /// </summary>
    public static StoredEventEdits SetFrames(ClipInfo template, StoredEventEdits current, int frames)
    {
        if (frames < MinFrames || frames > MaxFrames)
        {
            throw new ClipWriteException($"A clip's length must be {MinFrames} to {MaxFrames} frames.");
        }

        if (template.IsAdditive)
        {
            throw new ClipWriteException("Additive clips cannot change their length yet.");
        }

        var fromLast = (current.Frames ?? template.FrameCount) - 1;
        var toLast = frames - 1;
        var changes = current.Changes.Select(change =>
        {
            if (change.StartTime is null && change.Duration is null)
            {
                return change;
            }

            var templateEvent = change.Index >= 0 && change.Index < template.Events.Count ? template.Events[change.Index] : null;
            var start = change.StartTime ?? Rescale(templateEvent?.StartTime ?? 0, template.LastFrame, fromLast);
            var (newStart, newDuration) = Rescale(start, change.Duration ?? 0, fromLast, toLast);
            return change with { StartTime = change.StartTime is null ? null : newStart, Duration = change.Duration is null ? null : newDuration };
        }).ToList();
        var added = current.List.Added.Select(item =>
        {
            var (start, duration) = Rescale(item.StartTime, item.Duration, fromLast, toLast);
            return item with { StartTime = start, Duration = duration };
        }).ToList();
        var sync = current.List.Sync is { } sections ? Rescale(sections, fromLast, toLast) : null;
        return current with
        {
            Changes = changes,
            List = current.List with { Added = added, Sync = sync },
            Frames = frames == template.FrameCount ? null : frames,
        };
    }

    /// <summary>
    /// Rewrites a clip's frame-count values (every stream's frame count and
    /// duration, root motion's frame count) and its event and sync times in its
    /// KV data, before the writer re-encodes its poses and applies event edits.
    /// </summary>
    internal static void Apply(KVObject root, int fromFrames, int frames)
    {
        var (fromLast, toLast) = (fromFrames - 1, frames - 1);
        foreach (var stream in Streams(root))
        {
            if (stream.TryGetValue("m_floatChannelData", out var channels) && channels.IsArray && channels.Count > 0)
            {
                throw new ClipWriteException("This clip has float channels, which cannot change length yet.");
            }

            if (stream.TryGetValue("m_rootMotion", out var rootMotion) && rootMotion.TryGetValue("m_transforms", out var transforms) && transforms.IsArray && transforms.Count > 1)
            {
                throw new ClipWriteException("This clip has root motion, which cannot change length yet.");
            }

            var oldDuration = stream["m_flDuration"].ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            KVValues.Set(stream, "m_nNumFrames", frames);
            KVValues.Set(stream, "m_flDuration", fromLast > 0 ? oldDuration * toLast / fromLast : oldDuration);
            if (rootMotion is not null && rootMotion.TryGetValue("m_nNumFrames", out _))
            {
                KVValues.Set(rootMotion, "m_nNumFrames", frames);
            }

            if (stream.TryGetValue("m_events", out var events) && events.IsArray)
            {
                foreach (var clipEvent in events.Values)
                {
                    var (start, duration) = Rescale(Wrapped(clipEvent, "m_flStartTime"), Wrapped(clipEvent, "m_flDuration"), fromLast, toLast);
                    SetWrapped(clipEvent, "m_flStartTime", start);
                    SetWrapped(clipEvent, "m_flDuration", duration);
                }
            }

            if (stream.TryGetValue("m_syncTrack", out var track) && track.TryGetValue("m_syncEvents", out var syncEvents) && syncEvents.IsArray && syncEvents.Count > 0)
            {
                var sections = syncEvents.Values.Select(entry => new SyncSection("", Wrapped(entry, "m_startTime"), Wrapped(entry, "m_duration"))).ToList();
                var moved = Rescale(sections, fromLast, toLast);
                var kept = KVObject.Array();
                kept.Flag = syncEvents.Flag;
                for (var index = 0; index < moved.Count; index++)
                {
                    var entry = syncEvents[index];
                    SetWrapped(entry, "m_startTime", moved[index].StartTime);
                    SetWrapped(entry, "m_duration", moved[index].Duration);
                    kept.Add(entry);
                }

                track["m_syncEvents"] = kept;
            }
        }
    }

    private static IEnumerable<KVObject> Streams(KVObject root)
    {
        yield return root;
        if (root.TryGetValue("m_secondaryAnimations", out var secondary) && secondary.IsArray)
        {
            foreach (var stream in secondary.Values)
            {
                yield return stream;
            }
        }
    }

    private static double Wrapped(KVObject parent, string key) =>
        parent.TryGetValue(key, out var wrapper) && wrapper.TryGetValue(ValueKey, out var number) ? number.ToDouble(System.Globalization.CultureInfo.InvariantCulture) : 0;

    private static void SetWrapped(KVObject parent, string key, double value)
    {
        if (!parent.TryGetValue(key, out var original))
        {
            return;
        }

        var wrapper = KVValues.Clone(original);
        wrapper[ValueKey] = KVValues.Like(wrapper[ValueKey], value);
        parent[key] = wrapper;
    }
}
