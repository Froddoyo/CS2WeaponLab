using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WeaponLab.Core.Clips;

namespace WeaponLab.App;

/// <summary>One event on the timeline, in frames of the clip.</summary>
/// <param name="Key">Stable key: <c>e:&lt;index&gt;</c> for the clip's own events, <c>n:&lt;id&gt;</c> for added ones.</param>
/// <param name="Editable">False for legacy and unknown events, which stay as they are.</param>
public sealed record TimelineItem(string Key, ClipEventKind Kind, string Name, double Start, double Duration, bool Edited, bool Added, bool Editable, string Tip);

/// <summary>A sync track section (or a borrowed slot's section for comparison), in frames.</summary>
public sealed record TimelineSection(string Id, double Start, double End);

/// <summary>A part of the game's timing the clip cannot change (0.24): drawn as a tinted band across the lanes.</summary>
/// <param name="Kind">intro, loop, outro, before, after or done (see WeaponLab.Core.Weapons.TimingBand).</param>
public sealed record TimelineBand(double Start, double End, string Label, string Kind);

/// <summary>
/// The Anim Events timeline: a frame ruler with the playhead, the clip's
/// sync sections (a shell reload's INTRO / LOOP / OUTRO), and a lane per
/// event kind. Click selects (Ctrl or Shift adds), drag moves the selection
/// (whole frames; hold Alt for fractions), drag a bar's right end to change
/// its length, drag a boundary between sync sections to move it, drag on
/// empty space to box-select, click the ruler to scrub. Keys: ←/→ nudge
/// (Shift ×10) or step the playhead, Delete removes, Ctrl+D duplicates,
/// Ctrl+Z / Ctrl+Y undo and redo, Space plays.
/// </summary>
public sealed class EventTimeline : Control
{
    private const double LabelWidth = 84, RulerHeight = 22, SyncHeight = 24, LaneHeight = 26, RowHeight = 20, Pad = 10, HitRadius = 7, EdgeGrab = 5;
    private const int MaxRows = 5;

    private static readonly (ClipEventKind Kind, string Label)[] KindLanes =
    [
        (ClipEventKind.Gameplay, "Gameplay"),
        (ClipEventKind.Sound, "Sounds"),
        (ClipEventKind.Particle, "Particles"),
        (ClipEventKind.Material, "Material"),
        (ClipEventKind.Legacy, "Legacy"),
        (ClipEventKind.Other, "Other"),
    ];

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#1B1B1F"));
    private static readonly IBrush LaneBrush = new SolidColorBrush(Color.Parse("#222228"));
    private static readonly IBrush LaneAltBrush = new SolidColorBrush(Color.Parse("#26262D"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#C8C8CE"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#7A7A84"));
    private static readonly IBrush PlayheadBrush = new SolidColorBrush(Color.Parse("#E05050"));
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#30303A")), 1);
    private static readonly IPen SelectedPen = new Pen(Brushes.White, 1.6);
    private static readonly IPen EditedPen = new Pen(new SolidColorBrush(Color.Parse("#E0B050")), 1.2);
    private static readonly IPen GhostPen = new Pen(new SolidColorBrush(Color.Parse("#A0A0FF")), 1, new DashStyle([3, 3], 0));
    private static readonly IPen BoxPen = new Pen(new SolidColorBrush(Color.Parse("#7FB8FF")), 1, new DashStyle([2, 2], 0));
    private static readonly Typeface Font = new("Segoe UI");

    private IReadOnlyList<TimelineItem> _items = [];
    private IReadOnlyList<TimelineSection> _sections = [];
    private IReadOnlyList<TimelineSection> _ghost = [];
    private HashSet<string> _selected = [];
    private double _playhead;
    private int _lastFrame = 1;

    // Pointer gesture in progress.
    private enum Gesture { None, Scrub, Move, Resize, Boundary, Box }

    private Gesture _gesture;
    private Point _pressPoint;
    private double _pressFrame;
    private double _moveDelta;
    private string? _resizeKey;
    private double _resizeDuration;
    private int _boundary;
    private double _boundaryFrame;
    private Rect _box;
    private HashSet<string> _boxStart = [];

    // The selection changed during a press; listeners hear it on release, so rebuilding the inspector cannot take the pointer mid-drag.
    private bool _selectionPending;

    public EventTimeline()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Raised with a frame when the ruler is clicked or dragged.</summary>
    public event Action<double>? ScrubRequested;

    public event Action<IReadOnlySet<string>>? SelectionChanged;

    /// <summary>The selected events moved by a number of frames (drag or nudge).</summary>
    public event Action<IReadOnlyList<string>, double>? ItemsMoved;

    /// <summary>An event's length changed (in frames).</summary>
    public event Action<string, double>? ItemResized;

    /// <summary>The boundary before section <c>index</c> moved to a frame.</summary>
    public event Action<int, double>? SectionBoundaryMoved;

    /// <summary>A double click on an empty spot of a lane: add an event of that kind at that frame.</summary>
    public event Action<ClipEventKind, double>? AddRequested;

    public event Action? DeleteRequested;

    public event Action? DuplicateRequested;

    public event Action? UndoRequested;

    public event Action? RedoRequested;

    public event Action? PlayToggleRequested;

    /// <summary>Frames in the clip: the timeline spans 0 to <c>FrameCount - 1</c>.</summary>
    public int FrameCount
    {
        get => _lastFrame + 1;
        set
        {
            _lastFrame = Math.Max(1, value - 1);
            InvalidateVisual();
        }
    }

    public IReadOnlyList<TimelineItem> Items
    {
        get => _items;
        set
        {
            _items = value;
            _selected.IntersectWith(value.Select(item => item.Key));
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public IReadOnlyList<TimelineSection> Sections
    {
        get => _sections;
        set
        {
            _sections = value;
            InvalidateVisual();
        }
    }

    /// <summary>Another slot's sections drawn dashed under the clip's, to compare timing (e.g. the Nova's reload).</summary>
    public IReadOnlyList<TimelineSection> Ghost
    {
        get => _ghost;
        set
        {
            _ghost = value;
            InvalidateVisual();
        }
    }

    public string GhostLabel { get; set; } = "";

    private IReadOnlyList<TimelineBand> _bands = [];

    /// <summary>The game's timing (the expected class's reload) as bands behind the lanes; shown, not edited.</summary>
    public IReadOnlyList<TimelineBand> Bands
    {
        get => _bands;
        set
        {
            _bands = value;
            InvalidateVisual();
        }
    }

    public string BandsLabel { get; set; } = "";

    public double Playhead
    {
        get => _playhead;
        set
        {
            _playhead = value;
            InvalidateVisual();
        }
    }

    public IReadOnlySet<string> Selected => _selected;

    public void Select(IEnumerable<string> keys)
    {
        _selected = keys.Where(key => _items.Any(item => item.Key == key)).ToHashSet();
        InvalidateVisual();
        SelectionChanged?.Invoke(_selected);
    }

    // ---- Layout ----

    // Each lane stacks events that would overlap (bar or label) into rows; recomputed when the items or the width change.
    private Dictionary<string, int> _rows = [];
    private List<(ClipEventKind Kind, string Label, double Top, double Height)> _lanes = [];
    private double _layoutWidth = -1;
    private IReadOnlyList<TimelineItem>? _layoutItems;

    private IReadOnlyList<(ClipEventKind Kind, string Label, double Top, double Height)> Lanes()
    {
        if (_layoutItems != _items || Math.Abs(_layoutWidth - Bounds.Width) > 0.5)
        {
            Layout();
        }

        return _lanes;
    }

    private void Layout()
    {
        _layoutItems = _items;
        _layoutWidth = Bounds.Width;
        _rows = [];
        _lanes = [];
        var top = RulerHeight + SyncHeight;
        foreach (var (kind, label) in KindLanes.Where(lane => lane.Kind is ClipEventKind.Gameplay or ClipEventKind.Sound || _items.Any(item => item.Kind == lane.Kind)))
        {
            var rowEnds = new List<double>();
            foreach (var item in _items.Where(item => item.Kind == kind).OrderBy(item => item.Start))
            {
                var left = X(item.Start) - 6;
                var right = Math.Max(X(item.Start + item.Duration), X(item.Start) + 10 + TextWidth(item.Name, 11)) + 4;
                var row = rowEnds.FindIndex(end => end <= left);
                if (row < 0)
                {
                    row = rowEnds.Count < MaxRows ? rowEnds.Count : rowEnds.IndexOf(rowEnds.Min());
                    if (row == rowEnds.Count)
                    {
                        rowEnds.Add(0);
                    }
                }

                rowEnds[row] = right;
                _rows[item.Key] = row;
            }

            var height = Math.Max(LaneHeight, rowEnds.Count * RowHeight + 6);
            _lanes.Add((kind, label, top, height));
            top += height;
        }
    }

    private static double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Brushes.White).Width;

    protected override Size MeasureOverride(Size availableSize)
    {
        var lanes = Lanes();
        var bottom = lanes.Count > 0 ? lanes[^1].Top + lanes[^1].Height : RulerHeight + SyncHeight;
        return new Size(Math.Min(availableSize.Width, 4000), bottom + 6);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged)
        {
            InvalidateMeasure(); // Rows depend on the width.
        }
    }

    private double TrackLeft => LabelWidth + Pad;

    private double TrackWidth => Math.Max(1, Bounds.Width - LabelWidth - 2 * Pad);

    private double X(double frame) => TrackLeft + frame / _lastFrame * TrackWidth;

    private double Frame(double x) => Math.Clamp((x - TrackLeft) / TrackWidth * _lastFrame, 0, _lastFrame);

    /// <summary>The top of an item's row.</summary>
    private double ItemTop(TimelineItem item)
    {
        var lane = Lanes().FirstOrDefault(lane => lane.Kind == item.Kind);
        return lane.Top + 3 + _rows.GetValueOrDefault(item.Key) * RowHeight;
    }

    private double LanesBottom => Lanes() is { Count: > 0 } lanes ? lanes[^1].Top + lanes[^1].Height : RulerHeight + SyncHeight;

    // ---- Drawing ----

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        context.FillRectangle(Background, bounds);
        DrawRuler(context);
        DrawSync(context);

        var lanes = Lanes();
        for (var lane = 0; lane < lanes.Count; lane++)
        {
            var (_, label, top, height) = lanes[lane];
            context.FillRectangle(lane % 2 == 0 ? LaneBrush : LaneAltBrush, new Rect(0, top, bounds.Width, height));
            Label(context, label, new Point(6, top + 5), MutedBrush, 11);
        }

        DrawBands(context);

        // Shell-loop sections shown faintly behind the lanes, so markers line up with them.
        foreach (var section in _sections.Where(section => section.Id.Length > 0))
        {
            var rect = new Rect(X(section.Start), RulerHeight + SyncHeight, Math.Max(1, X(section.End) - X(section.Start)), LanesBottom - RulerHeight - SyncHeight);
            context.FillRectangle(new SolidColorBrush(SectionColour(section.Id), 0.07), rect);
        }

        foreach (var item in _items.OrderBy(item => _selected.Contains(item.Key)))
        {
            DrawItem(context, item, ItemTop(item));
        }

        var x = X(_playhead);
        context.DrawLine(new Pen(PlayheadBrush, 1.4), new Point(x, RulerHeight - 6), new Point(x, bounds.Height));
        if (_gesture == Gesture.Box)
        {
            context.DrawRectangle(null, BoxPen, _box);
        }
    }

    private static readonly IPen BandEdgePen = new Pen(new SolidColorBrush(Color.Parse("#FF9E33"), 0.55), 1, new DashStyle([4, 3], 0));

    /// <summary>The game's timing bands behind the lanes: tinted per part, dashed edges, the label at the top of each.</summary>
    private void DrawBands(DrawingContext context)
    {
        if (_bands.Count == 0)
        {
            return;
        }

        var top = RulerHeight + SyncHeight;
        var height = LanesBottom - top;
        foreach (var band in _bands)
        {
            var left = X(Math.Max(0, band.Start));
            var right = X(Math.Min(_lastFrame, band.End));
            if (right <= left)
            {
                continue;
            }

            var (colour, alpha) = band.Kind switch
            {
                "loop" => (Color.Parse("#FF9E33"), 0.10),
                "done" => (Color.Parse("#000000"), 0.35),
                "after" => (Color.Parse("#5FD08A"), 0.06),
                _ => (Color.Parse("#7FB8FF"), 0.05),
            };
            context.FillRectangle(new SolidColorBrush(colour, alpha), new Rect(left, top, right - left, height));
            context.DrawLine(BandEdgePen, new Point(left, top), new Point(left, top + height));
            using (context.PushClip(new Rect(left, top, right - left, height)))
            {
                Label(context, band.Label, new Point(left + 3, LanesBottom - 14), new SolidColorBrush(Color.Parse("#FFB060"), 0.85), 9);
            }
        }

        if (BandsLabel.Length > 0)
        {
            Label(context, BandsLabel, new Point(Math.Max(TrackLeft, X(_lastFrame) - 6 - BandsLabel.Length * 5.3), LanesBottom - 26), new SolidColorBrush(Color.Parse("#FFB060"), 0.85), 9);
        }
    }

    private void DrawRuler(DrawingContext context)
    {
        context.FillRectangle(LaneAltBrush, new Rect(0, 0, Bounds.Width, RulerHeight));
        var step = new[] { 1, 2, 5, 10, 20, 50, 100, 200 }.FirstOrDefault(candidate => TrackWidth / _lastFrame * candidate >= 42, 500);
        for (var frame = 0; frame <= _lastFrame; frame += step)
        {
            var x = X(frame);
            context.DrawLine(GridPen, new Point(x, RulerHeight - 6), new Point(x, Bounds.Height));
            Label(context, frame.ToString(CultureInfo.InvariantCulture), new Point(x + 2, 3), MutedBrush, 10);
        }

        Label(context, "frame", new Point(6, 4), MutedBrush, 10);
        var playhead = X(_playhead);
        context.DrawGeometry(PlayheadBrush, null, new PolylineGeometry([new Point(playhead - 5, RulerHeight - 8), new Point(playhead + 5, RulerHeight - 8), new Point(playhead, RulerHeight - 2)], true));
    }

    private void DrawSync(DrawingContext context)
    {
        var top = RulerHeight;
        context.FillRectangle(LaneBrush, new Rect(0, top, Bounds.Width, SyncHeight));
        Label(context, "Sync", new Point(6, top + 5), MutedBrush, 11);
        foreach (var ghost in _ghost)
        {
            var rect = new Rect(X(ghost.Start), top + SyncHeight - 7, Math.Max(1, X(ghost.End) - X(ghost.Start)), 6);
            context.DrawRectangle(null, GhostPen, rect);
        }

        if (_ghost.Count > 0 && GhostLabel.Length > 0)
        {
            Label(context, GhostLabel, new Point(X(_ghost[^1].End) - 4 - GhostLabel.Length * 5.3, top + SyncHeight - 18), new SolidColorBrush(Color.Parse("#A0A0FF")), 9);
        }

        for (var index = 0; index < _sections.Count; index++)
        {
            var section = _sections[index];
            var start = index > 0 && _gesture == Gesture.Boundary && _boundary == index ? _boundaryFrame : section.Start;
            var end = index + 1 < _sections.Count && _gesture == Gesture.Boundary && _boundary == index + 1 ? _boundaryFrame : section.End;
            var rect = new Rect(X(start), top + 3, Math.Max(1, X(end) - X(start)), SyncHeight - 11);
            var colour = SectionColour(section.Id);
            context.FillRectangle(new SolidColorBrush(colour, section.Id.Length > 0 ? 0.55 : 0.18), rect);
            var name = section.Id.Length > 0 ? section.Id.Replace("WPN_RELOAD_", "").Replace("WPN_", "") : "(one section)";
            using (context.PushClip(rect))
            {
                Label(context, name, new Point(rect.X + 3, rect.Y), TextBrush, 10);
            }
        }
    }

    private void DrawItem(DrawingContext context, TimelineItem item, double top)
    {
        var start = item.Start;
        var duration = item.Duration;
        if (_selected.Contains(item.Key) && _gesture == Gesture.Move)
        {
            start += _moveDelta;
        }

        if (_gesture == Gesture.Resize && _resizeKey == item.Key)
        {
            duration = _resizeDuration;
        }

        var colour = KindColour(item.Kind);
        var brush = new SolidColorBrush(colour, item.Editable ? 1 : 0.45);
        var selected = _selected.Contains(item.Key);
        var pen = selected ? SelectedPen : item.Edited || item.Added ? EditedPen : null;
        var middle = top + RowHeight / 2 - 1;
        var x = X(start);
        double textLeft;
        if (duration > 0)
        {
            var rect = new Rect(x, top + 2, Math.Max(2, X(start + duration) - x), RowHeight - 6);
            context.DrawRectangle(new SolidColorBrush(colour, item.Editable ? 0.55 : 0.25), pen, rect, 3, 3);
            textLeft = x + 5;
        }
        else
        {
            var diamond = new PolylineGeometry([new Point(x, middle - 6), new Point(x + 6, middle), new Point(x, middle + 6), new Point(x - 6, middle)], true);
            context.DrawGeometry(brush, pen, diamond);
            textLeft = x + 8;
        }

        if (item.Added)
        {
            Label(context, "+", new Point(x - 10, top - 2), EditedPen.Brush!, 10);
        }

        Label(context, item.Name, new Point(textLeft, top + 1), selected ? Brushes.White : TextBrush, 11);
    }

    private static void Label(DrawingContext context, string text, Point at, IBrush brush, double size)
    {
        context.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, brush), at);
    }

    public static Color KindColour(ClipEventKind kind) => kind switch
    {
        ClipEventKind.Gameplay => Color.Parse("#E8A33D"),
        ClipEventKind.Sound => Color.Parse("#7FB8FF"),
        ClipEventKind.Particle => Color.Parse("#7FD68A"),
        ClipEventKind.Material => Color.Parse("#C58AF0"),
        _ => Color.Parse("#9A9AA4"),
    };

    private static Color SectionColour(string id) => id switch
    {
        ShellReload.Intro => Color.Parse("#3FA7A0"),
        ShellReload.Loop => Color.Parse("#E07B39"),
        ShellReload.Outro => Color.Parse("#8A6BD1"),
        "" => Color.Parse("#7A7A84"),
        _ => Color.Parse("#5C8FD6"),
    };

    // ---- Hit testing ----

    /// <summary>The lane under a point, or null outside the lanes.</summary>
    private (ClipEventKind Kind, string Label, double Top, double Height)? LaneAt(Point point)
    {
        foreach (var lane in Lanes())
        {
            if (point.Y >= lane.Top && point.Y < lane.Top + lane.Height)
            {
                return lane;
            }
        }

        return null;
    }

    private TimelineItem? ItemAt(Point point, out bool onRightEdge)
    {
        onRightEdge = false;
        if (LaneAt(point) is not { } lane)
        {
            return null;
        }

        var row = (int)Math.Floor((point.Y - lane.Top - 3) / RowHeight);
        TimelineItem? best = null;
        var bestDistance = double.MaxValue;
        foreach (var item in _items.Where(item => item.Kind == lane.Kind && _rows.GetValueOrDefault(item.Key) == row))
        {
            var left = X(item.Start);
            var right = X(item.Start + item.Duration);
            if (item.Duration > 0 && point.X >= left - 2 && point.X <= right + EdgeGrab)
            {
                var edge = Math.Abs(point.X - right) <= EdgeGrab;
                var distance = edge ? 0 : point.X < left ? left - point.X : 0.5;
                if (distance < bestDistance)
                {
                    (best, bestDistance, onRightEdge) = (item, distance, edge && item.Editable);
                }
            }
            else if (Math.Abs(point.X - left) <= HitRadius && Math.Abs(point.X - left) < bestDistance)
            {
                (best, bestDistance, onRightEdge) = (item, Math.Abs(point.X - left), false);
            }
        }

        return best;
    }

    /// <summary>The section boundary (index of the section after it) under the point in the sync lane, or -1.</summary>
    private int BoundaryAt(Point point)
    {
        if (point.Y < RulerHeight || point.Y > RulerHeight + SyncHeight)
        {
            return -1;
        }

        for (var index = 1; index < _sections.Count; index++)
        {
            if (Math.Abs(point.X - X(_sections[index].Start)) <= EdgeGrab)
            {
                return index;
            }
        }

        return -1;
    }

    // ---- Pointer ----

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        if (_gesture == Gesture.None)
        {
            var item = ItemAt(point, out var edge);
            Cursor = edge || BoundaryAt(point) >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default;
            ToolTip.SetTip(this, item?.Tip);
            return;
        }

        var snap = !e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        double Snap(double value) => snap ? Math.Round(value) : value;
        switch (_gesture)
        {
            case Gesture.Scrub:
                ScrubRequested?.Invoke(Snap(Frame(point.X)));
                break;
            case Gesture.Move:
                var moving = _items.Where(item => _selected.Contains(item.Key)).ToList();
                var delta = Snap(Frame(point.X) - _pressFrame);
                // Keep every moved event inside the clip.
                var low = moving.Count == 0 ? 0 : -moving.Min(item => item.Start);
                var high = moving.Count == 0 ? 0 : moving.Min(item => _lastFrame - item.Start - item.Duration);
                _moveDelta = Math.Clamp(delta, low, Math.Max(low, high));
                break;
            case Gesture.Resize:
                var resized = _items.First(item => item.Key == _resizeKey);
                _resizeDuration = Math.Clamp(Snap(Frame(point.X)) - resized.Start, 0, _lastFrame - resized.Start);
                break;
            case Gesture.Boundary:
                var before = _sections[_boundary - 1];
                var after = _sections[_boundary];
                _boundaryFrame = Math.Clamp(Snap(Frame(point.X)), before.Start + 1, after.End - 1);
                break;
            case Gesture.Box:
                _box = new Rect(_pressPoint, point).Normalize();
                var inside = _items
                    .Where(item => _box.Intersects(new Rect(X(item.Start) - 4, ItemTop(item) + 2, Math.Max(8, X(item.Start + item.Duration) - X(item.Start) + 4), RowHeight - 4)))
                    .Select(item => item.Key);
                _selected = [.. _boxStart, .. inside];
                break;
        }

        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        var point = e.GetPosition(this);
        _pressPoint = point;
        _pressFrame = Frame(point.X);
        e.Pointer.Capture(this);
        e.Handled = true;
        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (point.Y < RulerHeight)
        {
            _gesture = Gesture.Scrub;
            ScrubRequested?.Invoke(Math.Round(_pressFrame));
            return;
        }

        if (e.ClickCount == 2 && ItemAt(point, out _) is null && LaneAt(point) is { } lane)
        {
            AddRequested?.Invoke(lane.Kind, Math.Round(_pressFrame));
            _gesture = Gesture.None;
            return;
        }

        if (BoundaryAt(point) is var boundary and >= 0)
        {
            _gesture = Gesture.Boundary;
            _boundary = boundary;
            _boundaryFrame = _sections[boundary].Start;
            return;
        }

        if (ItemAt(point, out var edge) is { } item)
        {
            if (additive)
            {
                if (!_selected.Remove(item.Key))
                {
                    _selected.Add(item.Key);
                }
            }
            else if (!_selected.Contains(item.Key))
            {
                _selected = [item.Key];
            }

            _selectionPending = true;
            if (edge && !additive)
            {
                _gesture = Gesture.Resize;
                _resizeKey = item.Key;
                _resizeDuration = item.Duration;
            }
            else
            {
                _gesture = _items.Where(other => _selected.Contains(other.Key)).All(other => other.Editable) ? Gesture.Move : Gesture.None;
                _moveDelta = 0;
            }


            ScrubRequested?.Invoke(Math.Round(item.Start));
            InvalidateVisual();
            return;
        }

        _boxStart = additive ? [.. _selected] : [];
        if (!additive && _selected.Count > 0)
        {
            _selected = [];
            _selectionPending = true;
        }

        _gesture = Gesture.Box;
        _box = new Rect(point, point);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        e.Pointer.Capture(null);
        EndGesture();
    }

    // Avalonia drops the capture just before it reports the release, so whichever comes first finishes the gesture.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        EndGesture();
    }

    /// <summary>Finishes the gesture in progress: reports the move, resize, boundary or selection it made.</summary>
    private void EndGesture()
    {
        var gesture = _gesture;
        _gesture = Gesture.None;
        if (_selectionPending && gesture != Gesture.Box)
        {
            _selectionPending = false;
            SelectionChanged?.Invoke(_selected);
        }

        switch (gesture)
        {
            case Gesture.Move when Math.Abs(_moveDelta) > 1e-6:
                ItemsMoved?.Invoke(_selected.ToList(), _moveDelta);
                break;
            case Gesture.Resize when _resizeKey is { } key && _items.First(item => item.Key == key) is var resized && Math.Abs(resized.Duration - _resizeDuration) > 1e-6:
                ItemResized?.Invoke(key, _resizeDuration);
                break;
            case Gesture.Boundary when Math.Abs(_boundaryFrame - _sections[_boundary].Start) > 1e-6:
                SectionBoundaryMoved?.Invoke(_boundary, _boundaryFrame);
                break;
            case Gesture.Box:
                _selectionPending = false;
                SelectionChanged?.Invoke(_selected);
                break;
        }

        _moveDelta = 0;
        _resizeKey = null;
        InvalidateVisual();
    }

    // ---- Keyboard ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        switch (e.Key)
        {
            case Key.Left or Key.Right:
                var delta = e.Key == Key.Left ? -step : step;
                var moving = _items.Where(item => _selected.Contains(item.Key) && item.Editable).ToList();
                if (moving.Count > 0)
                {
                    var low = -moving.Min(item => item.Start);
                    var high = moving.Min(item => _lastFrame - item.Start - item.Duration);
                    var clamped = Math.Clamp(delta, low, Math.Max(low, high));
                    if (Math.Abs(clamped) > 1e-6)
                    {
                        ItemsMoved?.Invoke(moving.Select(item => item.Key).ToList(), clamped);
                    }
                }
                else
                {
                    ScrubRequested?.Invoke(Math.Clamp(Math.Round(_playhead) + delta, 0, _lastFrame));
                }

                break;
            case Key.Home or Key.End:
                ScrubRequested?.Invoke(e.Key == Key.Home ? 0 : _lastFrame);
                break;
            case Key.Delete or Key.Back:
                DeleteRequested?.Invoke();
                break;
            case Key.D when control:
                DuplicateRequested?.Invoke();
                break;
            case Key.Z when control && e.KeyModifiers.HasFlag(KeyModifiers.Shift):
            case Key.Y when control:
                RedoRequested?.Invoke();
                break;
            case Key.Z when control:
                UndoRequested?.Invoke();
                break;
            case Key.A when control:
                Select(_items.Select(item => item.Key));
                break;
            case Key.Space:
                PlayToggleRequested?.Invoke();
                break;
            case Key.Escape:
                Select([]);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
