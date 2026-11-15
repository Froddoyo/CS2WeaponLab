namespace WeaponLab.Core.Clips;

/// <summary>A shell-by-shell reload's loop, in frames of its clip.</summary>
/// <param name="LoopStart">First frame of the shell insert that repeats (end of the intro).</param>
/// <param name="LoopEnd">Frame where the loop ends and the outro starts; the graph jumps back to <paramref name="LoopStart"/> here while the game wants more shells.</param>
/// <param name="AddAmmo">Frame where the game counts the shell.</param>
public sealed record ShellLoop(int LoopStart, int LoopEnd, int AddAmmo);

/// <summary>Seconds of each part of a shell-by-shell reload, to compare a clip with the slot whose logic the game runs.</summary>
public sealed record ShellTiming(double Intro, double Loop, double AddAmmoIntoLoop, double Outro);

/// <summary>
/// Writes and reads the marks of a shell-by-shell reload (CS2's Nova
/// recipe, verified in game 2026-10-31): sync sections WPN_RELOAD_INTRO,
/// _LOOP and _OUTRO with matching gameplay events, WPN_RELOAD_ADD_AMMO inside
/// the loop at least <see cref="MinAddAmmoLead"/> frames before its end,
/// WPN_RELOAD_SILENT over the normal reload, and no WPN_DROP_MAG.
/// </summary>
public static class ShellReload
{
    public const string Intro = "WPN_RELOAD_INTRO";
    public const string Loop = "WPN_RELOAD_LOOP";
    public const string Outro = "WPN_RELOAD_OUTRO";
    public const string AddAmmo = "WPN_RELOAD_ADD_AMMO";
    public const string Silent = "WPN_RELOAD_SILENT";
    public const string DropMag = "WPN_DROP_MAG";

    /// <summary>Frames the shell must count before the loop ends: 1 frame gave an extra empty pass in game; reload_nova uses 4.</summary>
    public const int MinAddAmmoLead = 4;

    /// <summary>How far WPN_RELOAD_SILENT runs on a normal reload (reload_nova: 0.92, the SSG08 edit 0.85).</summary>
    public const double SilentLength = 0.85;

    /// <summary>The loop a clip's sync track describes, or null when it is not a shell-by-shell reload.</summary>
    public static ShellLoop? Read(IReadOnlyList<SyncSection> syncTrack, IReadOnlyList<ClipEvent> events, int lastFrame)
    {
        var loop = syncTrack.FirstOrDefault(section => section.Id == Loop);
        if (loop is null || lastFrame <= 0)
        {
            return null;
        }

        var addAmmo = events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == AddAmmo);
        var end = (int)Math.Round(loop.EndTime * lastFrame);
        return new ShellLoop((int)Math.Round(loop.StartTime * lastFrame), end, addAmmo is null ? end - MinAddAmmoLead : (int)Math.Round(addAmmo.StartTime * lastFrame));
    }

    /// <summary>The loop of a clip with its stored edits applied: their sync track and their ADD_AMMO.</summary>
    public static ShellLoop? Read(ClipInfo template, StoredEventEdits edits)
    {
        var loop = Read(edits.List.Sync ?? template.SyncTrack, [], template.LastFrame);
        if (loop is null)
        {
            return null;
        }

        var added = edits.List.Added.FirstOrDefault(item => item.ClassName == "CNmIDEvent" && item.Name == AddAmmo);
        var own = template.Events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == AddAmmo && !edits.List.Removed.Contains(clipEvent.Index));
        var start = added?.StartTime ?? (own is null ? null : edits.Changes.FirstOrDefault(change => change.Index == own.Index)?.StartTime ?? own.StartTime);
        return start is null ? loop : loop with { AddAmmo = (int)Math.Round(start.Value * template.LastFrame) };
    }

    public static ShellTiming? Timing(ShellLoop? loop, ClipInfo clip)
    {
        if (loop is null || clip.FramesPerSecond <= 0)
        {
            return null;
        }

        var frame = 1 / clip.FramesPerSecond;
        return new ShellTiming(loop.LoopStart * frame, (loop.LoopEnd - loop.LoopStart) * frame, (loop.AddAmmo - loop.LoopStart) * frame, (clip.LastFrame - loop.LoopEnd) * frame);
    }

    /// <summary>Why a loop would not work in game, as sentences; empty when it is fine.</summary>
    public static IReadOnlyList<string> Problems(ShellLoop loop, int lastFrame)
    {
        var problems = new List<string>();
        if (loop.LoopStart < 1 || loop.LoopEnd <= loop.LoopStart || loop.LoopEnd >= lastFrame)
        {
            problems.Add($"The loop must start after frame 0 and end before the last frame ({lastFrame}).");
        }

        if (loop.AddAmmo < loop.LoopStart || loop.AddAmmo > loop.LoopEnd - MinAddAmmoLead)
        {
            problems.Add($"The shell must count inside the loop and at least {MinAddAmmoLead} frames before its end (frames {loop.LoopStart}-{loop.LoopEnd - MinAddAmmoLead}); later, the game plays an extra empty pass.");
        }

        return problems;
    }

    /// <summary>
    /// The clip's event edits with a shell-by-shell loop written in: the
    /// three sync sections and their gameplay events, ADD_AMMO moved (or
    /// added) to the shell frame, SILENT added to a normal reload that lacks
    /// it, DROP_MAG removed. Template events are retimed rather than duplicated.
    /// </summary>
    public static StoredEventEdits Apply(ClipInfo template, StoredEventEdits current, ShellLoop loop, bool silentReload)
    {
        if (Problems(loop, template.LastFrame) is { Count: > 0 } problems)
        {
            throw new ClipWriteException(string.Join(" ", problems));
        }

        double Time(int frame) => frame / (double)template.LastFrame;
        var (start, end) = (Time(loop.LoopStart), Time(loop.LoopEnd));
        var sections = new List<SyncSection> { new(Intro, 0, start), new(Loop, start, end - start), new(Outro, end, 1 - end) };

        var changes = current.Changes.ToDictionary(change => change.Index);
        var added = current.List.Added.ToList();
        var removed = current.List.Removed.ToHashSet();

        void Place(string id, double at, double duration)
        {
            var own = template.Events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == id && !removed.Contains(clipEvent.Index));
            if (own is not null)
            {
                changes[own.Index] = (changes.GetValueOrDefault(own.Index) ?? new EventChange(own.Index, null, null)) with { StartTime = at, Duration = duration };
                return;
            }

            var index = added.FindIndex(item => item.ClassName == "CNmIDEvent" && item.Name == id);
            var marker = new AddedEvent(index >= 0 ? added[index].Id : NextId(added), "CNmIDEvent", id, at, duration);
            if (index >= 0)
            {
                added[index] = marker;
            }
            else
            {
                added.Add(marker);
            }
        }

        foreach (var section in sections)
        {
            Place(section.Id, section.StartTime, section.Duration);
        }

        Place(AddAmmo, Time(loop.AddAmmo), 0);
        var hasSilent = template.Events.Any(clipEvent => clipEvent.Name == Silent && !removed.Contains(clipEvent.Index)) || added.Any(item => item.Name == Silent);
        if (!silentReload && !hasSilent)
        {
            Place(Silent, 0, Math.Min(SilentLength, end + (1 - end) / 2));
        }

        foreach (var dropMag in template.Events.Where(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == DropMag))
        {
            removed.Add(dropMag.Index);
            changes.Remove(dropMag.Index);
        }

        return current with
        {
            Changes = changes.Values.OrderBy(change => change.Index).ToList(),
            List = current.List with { Removed = removed.Order().ToList(), Added = added, Sync = sections },
        };
    }

    /// <summary>
    /// The clip's events moved onto another reload's timing (the gun the game
    /// plays it like): each event keeps its place within its part, so a sound
    /// half way through the old intro lands half way through the new one, then
    /// the loop marks are written for <paramref name="target"/>. The outro is
    /// squeezed to end at <paramref name="outroEnd"/>; the clip holds after it.
    /// Without a current loop only the marks are written.
    /// </summary>
    public static StoredEventEdits Fit(ClipInfo template, StoredEventEdits current, ShellLoop? from, ShellLoop target, double outroEnd, bool silentReload)
    {
        if (Problems(target, template.LastFrame) is { Count: > 0 } problems)
        {
            throw new ClipWriteException(string.Join(" ", problems));
        }

        var edits = current;
        if (from is not null && Problems(from, template.LastFrame).Count == 0)
        {
            var end = Math.Clamp(outroEnd, target.LoopEnd + 1, template.LastFrame);
            var anchors = new List<(double From, double To)> { (0, 0) };
            foreach (var anchor in new (double From, double To)[] { (from.LoopStart, target.LoopStart), (from.AddAmmo, target.AddAmmo), (from.LoopEnd, target.LoopEnd), (template.LastFrame, end) })
            {
                if (anchor.From > anchors[^1].From)
                {
                    anchors.Add(anchor);
                }
            }

            edits = EventRetime.Remap(template, current, new FrameMap(anchors));
        }

        return Apply(template, edits, target, silentReload);
    }

    /// <summary>A loop in another clip's frames, at this clip's frame rate (both CS2 clips are 30 fps today).</summary>
    public static ShellLoop Scale(ShellLoop loop, double factor) =>
        new((int)Math.Round(loop.LoopStart * factor), (int)Math.Round(loop.LoopEnd * factor), (int)Math.Round(loop.AddAmmo * factor));

    /// <summary>A new added event's id: <c>a1</c>, <c>a2</c>, …, not used by the clip yet.</summary>
    public static string NextId(IEnumerable<AddedEvent> added)
    {
        var used = added.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var number = 1;
        while (used.Contains("a" + number))
        {
            number++;
        }

        return "a" + number;
    }
}

/// <summary>Copies events from another clip into a clip's edits, at the same normalized times.</summary>
public static class EventCopy
{
    /// <summary>Fields copied along with the main value; the rest of a new event keeps the defaults.</summary>
    private static readonly string[] CopiedFields = ["m_secondaryID", "m_syncID", "m_position", "m_attachmentName", "m_relevance"];

    /// <param name="indices">The source clip's events to copy.</param>
    /// <param name="syncTrack">Also take the source's sync track (its loop sections).</param>
    public static StoredEventEdits Into(StoredEventEdits current, ClipInfo source, IEnumerable<int> indices, bool syncTrack)
    {
        var added = current.List.Added.ToList();
        foreach (var index in indices.Distinct().Order())
        {
            var clipEvent = source.Events[index];
            if (clipEvent.Kind is not (ClipEventKind.Gameplay or ClipEventKind.Sound))
            {
                throw new ClipWriteException($"{clipEvent.Kind} events cannot be copied yet; copy gameplay and sound events.");
            }

            var fields = CopiedFields.Where(key => clipEvent.Fields.Values.ContainsKey(key))
                .ToDictionary(key => key, key => clipEvent.Fields[key]);
            added.Add(new AddedEvent(ShellReload.NextId(added), clipEvent.ClassName, clipEvent.Name, clipEvent.StartTime, clipEvent.Duration) { Fields = fields });
        }

        return current with { List = current.List with { Added = added, Sync = syncTrack ? source.SyncTrack : current.List.Sync } };
    }
}
