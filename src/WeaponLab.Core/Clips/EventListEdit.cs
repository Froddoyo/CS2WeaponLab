using ValveKeyValue;
using WeaponLab.Core.Clips.Encoding;

namespace WeaponLab.Core.Clips;

/// <summary>
/// A new clip event. Times are normalized like <see cref="ClipEvent"/>'s
/// (0 = first frame, 1 = last).
/// </summary>
/// <param name="Id">A stable name for edits and the app (not written to the clip), e.g. <c>a1</c>.</param>
/// <param name="ClassName"><c>CNmIDEvent</c> or <c>CNmSoundEvent</c> (other classes only when the clip already has one to copy).</param>
/// <param name="Name">The main value (see <see cref="EventSchema.NameKey"/>): the gameplay ID or the soundevent.</param>
public sealed record AddedEvent(string Id, string ClassName, string Name, double StartTime, double Duration)
{
    /// <summary>Other plain fields as text, e.g. <c>m_secondaryID</c> or <c>m_position</c>. A gameplay event's <c>m_syncID</c> defaults to its ID.</summary>
    public IReadOnlyDictionary<string, string> Fields { get; init; } = new Dictionary<string, string>();

    public ClipEventKind Kind => EventSchema.KindOf(ClassName);
}

/// <summary>
/// One section of a clip's sync track (<c>m_syncTrack.m_syncEvents</c>).
/// A shell-by-shell reload splits its clip into WPN_RELOAD_INTRO, _LOOP and
/// _OUTRO sections; the graph jumps back to the LOOP section while the game
/// wants more shells. Most clips have a single unnamed section over the whole clip.
/// </summary>
public sealed record SyncSection(string Id, double StartTime, double Duration)
{
    public double EndTime => StartTime + Duration;
}

/// <summary>Events added to or removed from a clip, and its sync track; changes to existing events are <see cref="EventChange"/>s.</summary>
public sealed record EventListEdit
{
    /// <summary>Positions in the template's <c>m_events</c> to remove.</summary>
    public IReadOnlyList<int> Removed { get; init; } = [];

    public IReadOnlyList<AddedEvent> Added { get; init; } = [];

    /// <summary>The clip's new sync track, or null to keep the template's.</summary>
    public IReadOnlyList<SyncSection>? Sync { get; init; }

    public bool IsEmpty => Removed.Count == 0 && Added.Count == 0 && Sync is null;

    public static EventListEdit None { get; } = new();
}

/// <summary>A clip's stored event edits: changes to its own events, plus events added or removed and its sync track.</summary>
public sealed record StoredEventEdits(IReadOnlyList<EventChange> Changes, EventListEdit List)
{
    public static StoredEventEdits Empty { get; } = new([], EventListEdit.None);

    /// <summary>
    /// The clip's frame count when the user changed it (see <see cref="ClipLength"/>), else null.
    /// Every normalized time in these edits is then against this length.
    /// </summary>
    public int? Frames { get; init; }

    public bool IsEmpty => Changes.Count == 0 && List.IsEmpty && Frames is null;
}

/// <summary>
/// Builds the KV values of new events and sync sections. A new event copies
/// an event of the same class from the clip when there is one (so it keeps
/// whatever fields that game version writes), else it starts from the fields
/// CS2's own clips use (checked against reload_nova, 2026-10-31).
/// </summary>
internal static class EventFactory
{
    private const string ValueKey = "m_flValue";

    /// <summary>Classes a new event can be made of without a same-class event in the clip.</summary>
    public static readonly IReadOnlyList<string> BuiltInClasses = ["CNmIDEvent", "CNmSoundEvent"];

    public static KVObject Event(AddedEvent added, KVObject existingEvents)
    {
        var template = existingEvents.Values.FirstOrDefault(item => item.TryGetValue("_class", out var className) && className.ToString() == added.ClassName);
        var value = template is not null ? KVValues.Clone(template) : BuiltIn(added.ClassName);
        var nameKey = EventSchema.NameKey(added.ClassName) ?? throw new ClipWriteException($"{added.ClassName} events cannot be added.");
        value[nameKey] = new KVObject(added.Name) { Flag = value[nameKey].Flag };
        if (added.ClassName == "CNmIDEvent" && !added.Fields.ContainsKey("m_syncID"))
        {
            value["m_syncID"] = new KVObject(added.Name) { Flag = value["m_syncID"].Flag };
        }

        // A copied event keeps its own extra values unless the new event names them; the copy's ID-like fields are reset.
        foreach (var key in new[] { "m_secondaryID", "m_attachmentName", "m_tags" }.Where(key => value.TryGetValue(key, out _) && !added.Fields.ContainsKey(key)))
        {
            value[key] = new KVObject("") { Flag = value[key].Flag };
        }

        foreach (var (key, text) in added.Fields)
        {
            if (!value.TryGetValue(key, out var current) || current.IsArray || current.IsCollection)
            {
                throw new ClipWriteException($"New {added.ClassName} event '{added.Name}' has no value '{key}'.");
            }

            value[key] = current.ValueType switch
            {
                KVValueType.String => new KVObject(text) { Flag = current.Flag },
                KVValueType.Boolean => KVValues.Like(current, text == "true"),
                _ => KVValues.Like(current, double.Parse(text, System.Globalization.CultureInfo.InvariantCulture)),
            };
        }

        SetTime(value, "m_flStartTime", added.StartTime);
        SetTime(value, "m_flDuration", added.Duration);
        return value;
    }

    /// <summary>A sync track array of the given sections, each built like the template's first entry.</summary>
    public static KVObject SyncEvents(IReadOnlyList<SyncSection> sections, KVObject templateSyncEvents)
    {
        var prototype = templateSyncEvents.Count > 0 ? templateSyncEvents[0] : null;
        var array = KVObject.Array();
        array.Flag = templateSyncEvents.Flag;
        foreach (var section in sections)
        {
            var entry = prototype is not null ? KVValues.Clone(prototype) : SyncEntry();
            entry["m_ID"] = new KVObject(section.Id) { Flag = entry["m_ID"].Flag };
            SetTime(entry, "m_startTime", section.StartTime);
            SetTime(entry, "m_duration", section.Duration);
            array.Add(entry);
        }

        return array;
    }

    private static void SetTime(KVObject parent, string key, double value)
    {
        var wrapper = KVValues.Clone(parent[key]);
        wrapper[ValueKey] = KVValues.Like(wrapper[ValueKey], value);
        parent[key] = wrapper;
    }

    private static KVObject BuiltIn(string className)
    {
        var value = KVObject.Collection();
        value["_class"] = new KVObject(className);
        value["m_flStartTime"] = Wrapped(0);
        value["m_flDuration"] = Wrapped(0);
        value["m_syncID"] = new KVObject("");
        switch (className)
        {
            case "CNmIDEvent":
                value["m_ID"] = new KVObject("");
                value["m_secondaryID"] = new KVObject("");
                break;
            case "CNmSoundEvent":
                value["m_relevance"] = new KVObject("ClientAndServer");
                value["m_name"] = new KVObject("");
                value["m_position"] = new KVObject("EntityEyePos");
                value["m_attachmentName"] = new KVObject("");
                value["m_tags"] = new KVObject("");
                value["m_bContinuePlayingSoundAtDurationEnd"] = new KVObject(false);
                value["m_flDurationInterruptionThreshold"] = new KVObject((double)0.9f);
                break;
            default:
                throw new ClipWriteException($"The clip has no {className} event to copy, so one cannot be added.");
        }

        return value;
    }

    private static KVObject SyncEntry()
    {
        var value = KVObject.Collection();
        value["m_ID"] = new KVObject("");
        value["m_startTime"] = Wrapped(0);
        value["m_duration"] = Wrapped(1);
        return value;
    }

    private static KVObject Wrapped(double number)
    {
        var wrapper = KVObject.Collection();
        wrapper[ValueKey] = new KVObject(number);
        return wrapper;
    }
}
