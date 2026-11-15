namespace WeaponLab.Core.Clips;

/// <summary>A piecewise-linear frame map through anchor pairs (old frame → new frame), clamped at both ends.</summary>
public sealed class FrameMap
{
    private readonly (double From, double To)[] _anchors;

    public FrameMap(IEnumerable<(double From, double To)> anchors)
    {
        _anchors = anchors.OrderBy(anchor => anchor.From).ToArray();
        if (_anchors.Length < 2 || _anchors.Zip(_anchors.Skip(1)).Any(pair => pair.Second.From <= pair.First.From || pair.Second.To < pair.First.To))
        {
            throw new ArgumentException("A frame map needs at least two anchors, rising in both old and new frames.");
        }
    }

    public double Map(double frame)
    {
        if (frame <= _anchors[0].From)
        {
            return _anchors[0].To;
        }

        for (var index = 1; index < _anchors.Length; index++)
        {
            var (from, to) = (_anchors[index - 1], _anchors[index]);
            if (frame <= to.From)
            {
                return from.To + (frame - from.From) * (to.To - from.To) / (to.From - from.From);
            }
        }

        return _anchors[^1].To;
    }
}

/// <summary>Moves every event of a clip's edits through a <see cref="FrameMap"/>, keeping names and fields.</summary>
public static class EventRetime
{
    public static StoredEventEdits Remap(ClipInfo template, StoredEventEdits current, FrameMap map)
    {
        var last = (double)Math.Max(template.LastFrame, 1);
        (double Start, double Duration) Move(double start, double duration)
        {
            var from = map.Map(start * last) / last;
            var to = map.Map((start + duration) * last) / last;
            return (from, Math.Max(0, to - from));
        }

        var changes = current.Changes.ToDictionary(change => change.Index);
        var removed = current.List.Removed.ToHashSet();
        foreach (var clipEvent in template.Events.Where(clipEvent => !removed.Contains(clipEvent.Index)))
        {
            var change = changes.GetValueOrDefault(clipEvent.Index);
            var (start, duration) = (change?.StartTime ?? clipEvent.StartTime, change?.Duration ?? clipEvent.Duration);
            var (newStart, newDuration) = Move(start, duration);
            if (Math.Abs(newStart - start) < 1e-9 && Math.Abs(newDuration - duration) < 1e-9)
            {
                continue;
            }

            changes[clipEvent.Index] = (change ?? new EventChange(clipEvent.Index, null, null)) with
            {
                StartTime = newStart,
                Duration = duration > 0 || change?.Duration is not null ? newDuration : null,
            };
        }

        var added = current.List.Added.Select(item =>
        {
            var (start, duration) = Move(item.StartTime, item.Duration);
            return item with { StartTime = start, Duration = duration };
        }).ToList();
        var sync = current.List.Sync?.Select(section =>
        {
            var (start, duration) = Move(section.StartTime, section.Duration);
            return section with { StartTime = start, Duration = duration };
        }).ToList();

        return current with { Changes = changes.Values.OrderBy(change => change.Index).ToList(), List = current.List with { Added = added, Sync = sync } };
    }
}
