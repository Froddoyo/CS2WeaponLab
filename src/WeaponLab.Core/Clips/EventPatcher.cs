using System.Globalization;
using ValveKeyValue;
using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Clips;

/// <summary>
/// A change to one existing clip event. Times are normalized (0 = first
/// frame, 1 = last). Sound, particle, gameplay and material events can be
/// retimed and have the fields <see cref="EventSchema"/> lists edited;
/// legacy and unknown events stay read-only.
/// </summary>
/// <param name="Index">Position in <c>m_events</c>.</param>
/// <param name="Name">New main value (see <see cref="EventSchema.NameKey"/>, e.g. the soundevent), or null to keep it.</param>
/// <param name="StartTime">New normalized start time, or null to keep it.</param>
public sealed record EventChange(int Index, string? Name, double? StartTime)
{
    /// <summary>New normalized duration, or null to keep it.</summary>
    public double? Duration { get; init; }

    /// <summary>Other field edits: key → new value as text (flags as true/false).</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Applies <see cref="EventChange"/>s and an <see cref="EventListEdit"/> to a
/// clip's <c>m_events</c> (and <c>m_syncTrack</c>), and verifies afterwards
/// that nothing else about any event changed. When events are added or
/// removed the list is sorted by start time, as CS2's own clips are.
/// Started as a port of CS2AL's SoundEventTools (sound events only).
/// </summary>
internal sealed class EventPatcher
{
    private const string EventsKey = "m_events";
    private const string SyncTrackKey = "m_syncTrack";
    private const string SyncEventsKey = "m_syncEvents";
    private const string StartKey = "m_flStartTime";
    private const string DurationKey = "m_flDuration";
    private const string ValueKey = "m_flValue";
    private const double Epsilon = 1e-6;

    /// <param name="Fields">Field key → new text.</param>
    private sealed record Effective(int Index, double? StartTime, double? Duration, IReadOnlyDictionary<string, string> Fields)
    {
        public string[] TouchedKeys => [.. Fields.Keys, .. StartTime is null ? Array.Empty<string>() : [StartKey], .. Duration is null ? Array.Empty<string>() : [DurationKey]];
    }

    /// <summary>One event of the result: a template event (with its change, if any) or a new one.</summary>
    private sealed record Slot(int? TemplateIndex, Effective? Change, KVObject? Added, string Fingerprint);

    private readonly List<Slot> _slots;
    private readonly KVObject? _syncTrack;
    private readonly string? _syncFingerprint;
    private readonly bool _rebuildsList;

    private EventPatcher(List<Slot> slots, bool rebuildsList, KVObject? syncTrack, IReadOnlyList<string> newResources, IReadOnlyList<string> removed)
    {
        _slots = slots;
        _rebuildsList = rebuildsList;
        _syncTrack = syncTrack;
        _syncFingerprint = syncTrack is null ? null : KVFingerprint.Of(syncTrack);
        NewResources = newResources;
        RemovedEvents = removed;
    }

    /// <summary>Positions in the written <c>m_events</c> of events that were changed or added.</summary>
    public IReadOnlyList<int> ChangedEvents => _slots.Select((slot, index) => (slot, index)).Where(item => item.slot.Change is not null || item.slot.Added is not null).Select(item => item.index).ToList();

    /// <summary>The removed events, described for reports.</summary>
    public IReadOnlyList<string> RemovedEvents { get; }

    public bool SyncTrackChanged => _syncTrack is not null;

    /// <summary>Clip DATA keys this patcher rewrites (the clip writer leaves them to <see cref="Verify"/>).</summary>
    public IReadOnlyList<string> RootKeys => _syncTrack is null ? [EventsKey] : [EventsKey, SyncTrackKey];

    /// <summary>Resource paths edits point at (new particle systems); the clip's external references must list them.</summary>
    public IReadOnlyList<string> NewResources { get; }

    /// <summary>
    /// Validates the requested changes and keeps only those that differ from
    /// the clip. Returns null when nothing would change.
    /// </summary>
    public static EventPatcher? Prepare(KVObject clipData, IReadOnlyList<EventChange> requested, EventListEdit? list = null)
    {
        list ??= EventListEdit.None;
        if (requested.Count == 0 && list.IsEmpty)
        {
            return null;
        }

        var events = clipData.TryGetValue(EventsKey, out var array) && array.IsArray ? array : throw new ClipWriteException("The clip has no event list.");
        var removed = RemovedIndices(list, events.Count);
        var resources = new List<string>();
        var effective = Changes(events, requested, removed, resources);
        var added = list.Added.Select(item => (Event: item, Value: NewEvent(item, events))).ToList();
        var syncTrack = list.Sync is { } sections ? NewSyncTrack(clipData, sections) : null;

        var rebuildsList = removed.Count > 0 || added.Count > 0;
        if (effective.Count == 0 && !rebuildsList && syncTrack is null)
        {
            return null;
        }

        var byIndex = effective.ToDictionary(change => change.Index);
        var slots = Enumerable.Range(0, events.Count)
            .Where(index => !removed.Contains(index))
            .Select(index => (Start: byIndex.TryGetValue(index, out var change) && change.StartTime is { } start ? start : Wrapped(events[index], StartKey),
                Slot: new Slot(index, byIndex.GetValueOrDefault(index), null,
                    byIndex.TryGetValue(index, out var edit) ? KVFingerprint.OfExcept(events[index], edit.TouchedKeys) : KVFingerprint.Of(events[index]))))
            .Concat(added.Select(item => (Start: item.Event.StartTime, Slot: new Slot(null, null, item.Value, KVFingerprint.Of(item.Value)))))
            .ToList();
        if (rebuildsList)
        {
            slots = slots.OrderBy(item => item.Start).ToList(); // Stable: equal start times keep their order.
        }

        var removedNames = removed.Order().Select(index => Describe(events[index], index)).ToList();
        return new EventPatcher(slots.Select(item => item.Slot).ToList(), rebuildsList, syncTrack, resources.Distinct(StringComparer.Ordinal).ToList(), removedNames);
    }

    public void Apply(KVObject clipData)
    {
        var events = clipData[EventsKey];
        foreach (var change in _slots.Select(slot => slot.Change).OfType<Effective>())
        {
            var clipEvent = events[change.Index];
            foreach (var (key, text) in change.Fields)
            {
                clipEvent[key] = Like(clipEvent[key], text);
            }

            if (change.StartTime is { } start)
            {
                KVValues.Set(clipEvent[StartKey], ValueKey, start);
            }

            if (change.Duration is { } duration)
            {
                KVValues.Set(clipEvent[DurationKey], ValueKey, duration);
            }
        }

        if (_rebuildsList)
        {
            var rebuilt = KVObject.Array();
            rebuilt.Flag = events.Flag;
            foreach (var slot in _slots)
            {
                rebuilt.Add(slot.TemplateIndex is { } index ? events[index] : slot.Added!);
            }

            clipData[EventsKey] = rebuilt;
        }

        if (_syncTrack is not null)
        {
            clipData[SyncTrackKey] = _syncTrack;
        }
    }

    /// <summary>Checks the decoded clip: edited events carry the new values, new events and the sync track are as built, all else is untouched.</summary>
    public void Verify(KVObject decodedClipData)
    {
        var events = decodedClipData[EventsKey];
        if (events.Count != _slots.Count)
        {
            throw new ClipWriteException("The number of clip events is not what the edit asked for.");
        }

        for (var index = 0; index < events.Count; index++)
        {
            var clipEvent = events[index];
            var slot = _slots[index];
            if (slot.Change is not { } change)
            {
                if (KVFingerprint.Of(clipEvent) != slot.Fingerprint)
                {
                    throw new ClipWriteException(slot.Added is null ? $"Unedited event #{slot.TemplateIndex} changed." : $"New event #{index} was not written as built.");
                }

                continue;
            }

            if (KVFingerprint.OfExcept(clipEvent, change.TouchedKeys) != slot.Fingerprint)
            {
                throw new ClipWriteException($"Event #{change.Index} changed beyond its edited values.");
            }

            foreach (var (key, text) in change.Fields)
            {
                if (ClipReader.FieldText(clipEvent[key]) != text)
                {
                    throw new ClipWriteException($"Event #{change.Index} did not take its new {key}.");
                }
            }

            if (change.StartTime is { } start && Math.Abs(Wrapped(clipEvent, StartKey) - start) > Epsilon)
            {
                throw new ClipWriteException($"Event #{change.Index} did not take its new start time.");
            }

            if (change.Duration is { } duration && Math.Abs(Wrapped(clipEvent, DurationKey) - duration) > Epsilon)
            {
                throw new ClipWriteException($"Event #{change.Index} did not take its new duration.");
            }
        }

        if (_syncFingerprint is not null && (!decodedClipData.TryGetValue(SyncTrackKey, out var track) || KVFingerprint.Of(track) != _syncFingerprint))
        {
            throw new ClipWriteException("The clip's sync track was not written as asked.");
        }
    }

    private static HashSet<int> RemovedIndices(EventListEdit list, int count)
    {
        var removed = new HashSet<int>();
        foreach (var index in list.Removed)
        {
            if (index < 0 || index >= count || !removed.Add(index))
            {
                throw new ClipWriteException($"Event #{index} cannot be removed: it does not exist or is listed twice.");
            }
        }

        return removed;
    }

    private static List<Effective> Changes(KVObject events, IReadOnlyList<EventChange> requested, HashSet<int> removed, List<string> resources)
    {
        var effective = new List<Effective>();
        var seen = new HashSet<int>();
        foreach (var change in requested)
        {
            if (!seen.Add(change.Index) || change.Index < 0 || change.Index >= events.Count)
            {
                throw new ClipWriteException($"Event #{change.Index} does not exist or is listed twice.");
            }

            if (removed.Contains(change.Index))
            {
                throw new ClipWriteException($"Event #{change.Index} is removed, so it cannot be changed.");
            }

            var clipEvent = events[change.Index];
            var className = clipEvent["_class"].ToString() ?? "";
            var specs = EventSchema.Editable(className).ToDictionary(spec => spec.Key);

            // The requested field values: Name stands for the class's main field.
            var wanted = new Dictionary<string, string>(change.Fields, StringComparer.Ordinal);
            if (change.Name is not null)
            {
                var nameKey = EventSchema.NameKey(className) ?? throw new ClipWriteException($"Event #{change.Index} ({className}) has no name to change.");
                wanted[nameKey] = change.Name;
            }

            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, text) in wanted)
            {
                if (!clipEvent.TryGetValue(key, out var current) || current.IsArray || current.IsCollection)
                {
                    throw new ClipWriteException($"Event #{change.Index} ({className}) has no value '{key}'.");
                }

                if (ClipReader.FieldText(current) == text)
                {
                    continue;
                }

                if (!specs.TryGetValue(key, out var spec))
                {
                    throw new ClipWriteException(specs.Count > 0
                        ? $"'{key}' cannot be changed on event #{change.Index} ({className})."
                        : $"Event #{change.Index} ({className}) is read-only.");
                }

                Validate($"Event #{change.Index}", spec, current.ValueType, text);
                fields[key] = text;
                if (spec.Type == EventFieldType.Resource)
                {
                    resources.Add(text);
                }
            }

            var currentStart = Wrapped(clipEvent, StartKey);
            var currentDuration = Wrapped(clipEvent, DurationKey);
            var retimes = change.StartTime is { } start && Math.Abs(start - currentStart) > Epsilon;
            var resizes = change.Duration is { } duration && Math.Abs(duration - currentDuration) > Epsilon;
            if ((retimes || resizes) && !EventSchema.CanRetime(className))
            {
                throw new ClipWriteException($"Event #{change.Index} ({className}) is read-only and cannot be moved.");
            }

            var newStart = retimes ? change.StartTime!.Value : currentStart;
            var newDuration = resizes ? change.Duration!.Value : currentDuration;
            if ((retimes || resizes) && !InClip(newStart, newDuration))
            {
                throw new ClipWriteException($"Event #{change.Index} would start or end outside the clip.");
            }

            if (fields.Count == 0 && !retimes && !resizes)
            {
                continue;
            }

            effective.Add(new Effective(change.Index, retimes ? newStart : null, resizes ? newDuration : null, fields));
        }

        return effective;
    }

    private static KVObject NewEvent(AddedEvent added, KVObject events)
    {
        var label = $"New event '{added.Name}'";
        if (EventSchema.NameKey(added.ClassName) is null || EventSchema.Editable(added.ClassName).Count == 0)
        {
            throw new ClipWriteException($"{label}: {added.ClassName} events cannot be added.");
        }

        if (!InClip(added.StartTime, added.Duration))
        {
            throw new ClipWriteException($"{label} would start or end outside the clip.");
        }

        var specs = EventSchema.Editable(added.ClassName).ToDictionary(spec => spec.Key);
        var nameSpec = specs[EventSchema.NameKey(added.ClassName)!];
        if (added.Name.Length == 0)
        {
            throw new ClipWriteException($"{label}: give it a {nameSpec.Label.ToLowerInvariant()}.");
        }

        Validate(label, nameSpec, KVValueType.String, added.Name);
        foreach (var (key, text) in added.Fields.Where(item => specs.ContainsKey(item.Key)))
        {
            Validate(label, specs[key], specs[key].Type == EventFieldType.Flag ? KVValueType.Boolean : KVValueType.String, text);
        }

        return EventFactory.Event(added, events);
    }

    /// <summary>The clip's <c>m_syncTrack</c> with the given sections, or null when it already has exactly those.</summary>
    private static KVObject? NewSyncTrack(KVObject clipData, IReadOnlyList<SyncSection> sections)
    {
        if (sections.Count == 0)
        {
            throw new ClipWriteException("A sync track needs at least one section.");
        }

        for (var index = 0; index < sections.Count; index++)
        {
            var section = sections[index];
            if (!InClip(section.StartTime, section.Duration) || (index > 0 && section.StartTime < sections[index - 1].EndTime - Epsilon))
            {
                throw new ClipWriteException($"Sync section {section.Id} overlaps another or runs outside the clip; sections run in order from 0 to 1.");
            }
        }

        var current = ClipReader.ReadSyncTrack(clipData);
        if (current.Count == sections.Count && current.Zip(sections).All(pair => pair.First.Id == pair.Second.Id
                && Math.Abs(pair.First.StartTime - pair.Second.StartTime) < Epsilon && Math.Abs(pair.First.Duration - pair.Second.Duration) < Epsilon))
        {
            return null;
        }

        var track = clipData.TryGetValue(SyncTrackKey, out var existing) ? KVValues.Clone(existing) : KVObject.Collection();
        var templateSections = track.TryGetValue(SyncEventsKey, out var array) && array.IsArray ? array : KVObject.Array();
        track[SyncEventsKey] = EventFactory.SyncEvents(sections, templateSections);
        return track;
    }

    private static bool InClip(double start, double duration) =>
        double.IsFinite(start) && double.IsFinite(duration) && start >= 0 && duration >= 0 && start + duration <= 1 + 1e-7;

    private static string Describe(KVObject clipEvent, int index)
    {
        var className = clipEvent["_class"].ToString() ?? "";
        var name = EventSchema.NameKey(className) is { } key && clipEvent.TryGetValue(key, out var value) ? value.ToString() : className;
        return $"#{index} {EventSchema.KindOf(className)} {name} @ {Wrapped(clipEvent, StartKey):0.###}";
    }

    private static double Wrapped(KVObject clipEvent, string key) =>
        clipEvent.TryGetValue(key, out var wrapper) && wrapper.TryGetValue(ValueKey, out var number)
            ? number.ToDouble(CultureInfo.InvariantCulture)
            : 0;

    private static void Validate(string label, EventFieldSpec spec, KVValueType currentType, string text)
    {
        var problem = spec.Type switch
        {
            EventFieldType.Flag when text is not ("true" or "false") => "must be true or false",
            EventFieldType.SoundName when !IsToken(text) || text.Length == 0 => "is not a valid soundevent name",
            EventFieldType.Resource when !IsToken(text) || !text.Contains('/') || !text.Contains('.') || text.EndsWith("_c", StringComparison.Ordinal) =>
                "must be a resource path such as particles/folder/name.vpcf (no _c)",
            EventFieldType.Text when text.Any(char.IsControl) || text.Length > 512 => "is not valid text",
            _ => null,
        };
        if (problem is null && spec.Type != EventFieldType.Flag && currentType == KVValueType.Boolean)
        {
            problem = "must be true or false";
        }

        if (problem is not null)
        {
            throw new ClipWriteException($"{label}: {spec.Label} '{text}' {problem}.");
        }
    }

    // Soundevent names and resource paths: no whitespace or control characters.
    private static bool IsToken(string text) => text.Length <= 512 && !text.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));

    /// <summary>A replacement value of the same type and flag as the one it replaces.</summary>
    private static KVObject Like(KVObject original, string text)
    {
        if (original.ValueType == KVValueType.Boolean)
        {
            return KVValues.Like(original, text == "true");
        }

        if (original.ValueType == KVValueType.String)
        {
            return new KVObject(text) { Flag = original.Flag };
        }

        return KVValues.Like(original, double.Parse(text, CultureInfo.InvariantCulture));
    }
}
