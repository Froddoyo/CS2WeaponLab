namespace WeaponLab.Core.Clips;

public enum ClipEventKind
{
    /// <summary><c>CNmIDEvent</c>: gameplay markers such as <c>WPN_RELOAD_ADD_AMMO</c>.</summary>
    Gameplay,

    /// <summary><c>CNmSoundEvent</c>: plays a soundevent by name.</summary>
    Sound,

    /// <summary><c>CNmParticleEvent</c>: starts or stops a particle system, e.g. the C4's blinking light.</summary>
    Particle,

    /// <summary><c>CNmMaterialAttributeEvent</c>: animates a material attribute along curves, e.g. the C4 screen.</summary>
    Material,

    /// <summary><c>CNmLegacyEvent</c>: an old-style anim event (<c>AE_*</c>); kept as it is.</summary>
    Legacy,

    /// <summary>Any other event class; kept but not interpreted.</summary>
    Other,
}

/// <summary>
/// One entry of a clip's <c>m_events</c> array. Times are normalized to the
/// clip: 0 is the first frame and 1 the last.
/// </summary>
/// <param name="Index">Position in <c>m_events</c>.</param>
/// <param name="ClassName">The event's <c>_class</c>.</param>
/// <param name="Name">The event's main value: the soundevent (<c>m_name</c>), gameplay ID (<c>m_ID</c>), particle system, material attribute or legacy event class.</param>
/// <param name="SecondaryId">Gameplay events' <c>m_secondaryID</c>, e.g. <c>clip</c> for <c>WPN_DROP_MAG</c>; empty otherwise.</param>
public sealed record ClipEvent(
    int Index,
    ClipEventKind Kind,
    string ClassName,
    string Name,
    string SecondaryId,
    double StartTime,
    double Duration)
{
    /// <summary>The event's plain values (text, flags, numbers) by key, as text. Curves and other nested values are left out.</summary>
    public EventFields Fields { get; init; } = EventFields.Empty;

    public double StartSeconds(ClipInfo clip) => StartTime * clip.DurationSeconds;

    public double StartFrame(ClipInfo clip) => StartTime * clip.LastFrame;

    public double DurationFrames(ClipInfo clip) => Duration * clip.LastFrame;
}

/// <summary>An event's plain values, compared by content (so events compare by value).</summary>
public sealed class EventFields : IEquatable<EventFields>
{
    private readonly SortedDictionary<string, string> _values;

    public EventFields(IEnumerable<KeyValuePair<string, string>> values) =>
        _values = new SortedDictionary<string, string>(values.ToDictionary(), StringComparer.Ordinal);

    public static EventFields Empty { get; } = new([]);

    public IReadOnlyDictionary<string, string> Values => _values;

    public string this[string key] => _values.GetValueOrDefault(key, "");

    public bool Equals(EventFields? other) => other is not null && _values.SequenceEqual(other._values);

    public override bool Equals(object? obj) => Equals(obj as EventFields);

    public override int GetHashCode() => _values.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.Key, item.Value));
}

/// <summary>How an editable event field is shown and checked.</summary>
public enum EventFieldType
{
    Text,

    /// <summary>A soundevent name: no spaces.</summary>
    SoundName,

    /// <summary>A resource path, e.g. <c>particles/…/name.vpcf</c>.</summary>
    Resource,

    /// <summary>A true/false flag.</summary>
    Flag,
}

/// <param name="Hint">Typical values, shown next to the field.</param>
public sealed record EventFieldSpec(string Key, string Label, EventFieldType Type, string Hint = "");

/// <summary>
/// What may change on each event class. Every editable kind can be retimed
/// (start and duration); the listed fields can be edited; everything else,
/// including material curves, stays exactly as it is. Legacy and unknown
/// events are read-only.
/// </summary>
public static class EventSchema
{
    private const string AttachmentTypes = "PATTACH_POINT_FOLLOW, PATTACH_POINT, PATTACH_ABSORIGIN_FOLLOW, PATTACH_ABSORIGIN, PATTACH_WORLDORIGIN, PATTACH_INVALID";

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<EventFieldSpec>> Fields = new Dictionary<string, IReadOnlyList<EventFieldSpec>>
    {
        ["CNmSoundEvent"] =
        [
            new("m_name", "Soundevent", EventFieldType.SoundName, "e.g. Weapon_AK47.Clipout"),
            new("m_position", "Position", EventFieldType.Text, "EntityEyePos, Entity, Attachment"),
            new("m_attachmentName", "Attachment", EventFieldType.Text, "with Position = Attachment"),
        ],
        ["CNmParticleEvent"] =
        [
            new("m_hParticleSystem", "Particle system", EventFieldType.Resource, "particles/…/name.vpcf"),
            new("m_type", "Action", EventFieldType.Text, "Create, Create_CFG, Destroy"),
            new("m_target", "Target", EventFieldType.Text, "Self, Weapon"),
            new("m_attachmentPoint0", "Attachment", EventFieldType.Text, "an attachment of the model, e.g. muzzle_flash"),
            new("m_attachmentType0", "Attach as", EventFieldType.Text, AttachmentTypes),
            new("m_attachmentPoint1", "Second attachment", EventFieldType.Text, "usually empty"),
            new("m_attachmentType1", "Second attach as", EventFieldType.Text, "usually PATTACH_INVALID"),
            new("m_bStopImmediately", "Stop immediately", EventFieldType.Flag),
            new("m_bDetachFromOwner", "Detach from owner", EventFieldType.Flag),
            new("m_bPlayEndCap", "Play end cap", EventFieldType.Flag),
        ],
        ["CNmIDEvent"] =
        [
            new("m_ID", "Gameplay ID", EventFieldType.Text, "e.g. WPN_RELOAD_ADD_AMMO"),
            new("m_secondaryID", "Secondary ID", EventFieldType.Text, "e.g. clip"),
        ],
        ["CNmMaterialAttributeEvent"] =
        [
            new("m_attributeName", "Material attribute", EventFieldType.Text, "e.g. c4_light"),
            new("m_target", "Target", EventFieldType.Text, "Weapon, Self"),
        ],
    };

    /// <summary>The field each kind is named by (what <see cref="EventChange.Name"/> renames).</summary>
    public static string? NameKey(string className) => className switch
    {
        "CNmSoundEvent" => "m_name",
        "CNmIDEvent" => "m_ID",
        "CNmParticleEvent" => "m_hParticleSystem",
        "CNmMaterialAttributeEvent" => "m_attributeName",
        "CNmLegacyEvent" => "m_animEventClassName",
        _ => null,
    };

    public static IReadOnlyList<EventFieldSpec> Editable(string className) => Fields.GetValueOrDefault(className, []);

    public static bool CanRetime(string className) => Fields.ContainsKey(className);

    public static ClipEventKind KindOf(string className) => className switch
    {
        "CNmIDEvent" => ClipEventKind.Gameplay,
        "CNmSoundEvent" => ClipEventKind.Sound,
        "CNmParticleEvent" => ClipEventKind.Particle,
        "CNmMaterialAttributeEvent" => ClipEventKind.Material,
        "CNmLegacyEvent" => ClipEventKind.Legacy,
        _ => ClipEventKind.Other,
    };
}
