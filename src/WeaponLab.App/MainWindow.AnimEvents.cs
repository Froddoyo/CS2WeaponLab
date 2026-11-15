using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Sounds;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>
/// The Anim Events tab: one clip at a time, the viewport above a timeline
/// of its events (a lane per kind, the sync sections on top) and an
/// inspector. Events can be moved, resized, added, duplicated, removed,
/// copied from another clip, and a shell-by-shell loop written in; sound
/// events play as playback passes them. Edits are kept in the sandbox
/// (<see cref="EventEdits"/>) and written into the clip's export, so a
/// Blender re-export keeps them and the next push delivers them.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The clip open in Anim Events: its template (what the edits apply to) and its edits.</summary>
    private sealed class EventsSession(WeaponEntry weapon, string path, ClipInfo template, byte[] templateBytes, SandboxInfo? sandbox, StoredEventEdits edits)
    {
        public WeaponEntry Weapon { get; } = weapon;
        public string Path { get; } = path;
        public string ClipId { get; } = RigPackage.ClipId(path);
        private (int Frames, ClipInfo Clip)? _atLength;

        /// <summary>CS2's clip (re-targeted on a custom skeleton), at its own length.</summary>
        public ClipInfo BaseTemplate { get; } = template;

        /// <summary>The clip at the length the edits set (see <see cref="ClipLength"/>): what the timeline, the edits' times and the shell tools work against.</summary>
        public ClipInfo Template
        {
            get
            {
                var frames = Edits.Frames ?? BaseTemplate.FrameCount;
                if (_atLength is not { } cached || cached.Frames != frames)
                {
                    _atLength = cached = (frames, ClipLength.At(BaseTemplate, frames));
                }

                return cached.Clip;
            }
        }

        public byte[] TemplateBytes { get; } = templateBytes;
        public SandboxInfo? Sandbox { get; set; } = sandbox;
        public StoredEventEdits Edits { get; set; } = edits;
        public Stack<StoredEventEdits> Undo { get; } = new();
        public Stack<StoredEventEdits> Redo { get; } = new();
        public int LastFrame => Math.Max(Template.LastFrame, 1);

        /// <summary>The gun whose timing the game runs this weapon with (the expected class, see <see cref="Core.Weapons.TimingGun"/>), or null.</summary>
        public Core.Weapons.TimingGun? TimingGun { get; set; }

        /// <summary>Clips to compare timing with: the expected gun's clip in this role first, then the stock shell reloads.</summary>
        public IReadOnlyList<TimingReference> References { get; set; } = [];

        /// <summary>The game's timing for this clip, keyed by gun and length (see <see cref="GameTimingOf"/>).</summary>
        public (string Key, Core.Weapons.GameTiming? Timing)? TimingCache { get; set; }
    }

    /// <param name="Label">The gun, as sentences name it ("the Nova's").</param>
    /// <param name="Text">The compare list's line.</param>
    private sealed record TimingReference(string Label, string Path, string Text);

    /// <summary>The expected gun's clip in this clip's role (reload_ssg08 → reload_nova) first, then the stock shell reloads.</summary>
    private IReadOnlyList<TimingReference> TimingReferences(WeaponEntry weapon, string path, Core.Weapons.TimingGun? timingGun)
    {
        var references = new List<TimingReference>();
        if (_connection is { } connection && timingGun?.Gun is { } gun
            && Core.Weapons.PlaysLike.WeaponOf(connection, gun) is { } gunWeapon
            && Core.Weapons.PlaysLike.MatchingClip(weapon, path, gunWeapon) is { } matching)
        {
            var name = System.IO.Path.GetFileName(matching).Replace(".vnmclip_c", "", StringComparison.Ordinal);
            references.Add(new TimingReference(gun.Name, matching, $"Game timing: the {gun.Name}'s {name}"));
        }

        return references;
    }

    private EventsSession? _events;
    private readonly SoundPlayer _soundPlayer = new();
    private SoundLibrary? _soundLibrary;
    private string? _soundLibraryKey;
    private Task<SoundLibrary>? _soundLoad;
    private readonly HashSet<string> _unknownSounds = new(StringComparer.OrdinalIgnoreCase);
    private DispatcherTimer? _eventWriteTimer;
    private int _shellReference;
    private string _lastSoundName = "";

    /// <summary>Until then the export watcher does not log: the tab logs the exports it writes itself.</summary>
    private DateTime _quietExportsUntil;

    private void WireAnimEvents()
    {
        EventTimelineView.ScrubRequested += frame =>
        {
            WeaponView.Playing = false;
            ViewportPlayGlyph.Text = "▶";
            WeaponView.CurrentFrame = frame;
            OnViewportFrameChanged(frame);
        };
        EventTimelineView.SelectionChanged += _ => ShowEventInspector();
        EventTimelineView.ItemsMoved += MoveEvents;
        EventTimelineView.ItemResized += ResizeEvent;
        EventTimelineView.SectionBoundaryMoved += MoveSectionBoundary;
        EventTimelineView.AddRequested += (kind, frame) => AddEvent(kind, frame);
        EventTimelineView.DeleteRequested += DeleteSelectedEvents;
        EventTimelineView.DuplicateRequested += DuplicateSelectedEvents;
        EventTimelineView.UndoRequested += () => UndoEvents(redo: false);
        EventTimelineView.RedoRequested += () => UndoEvents(redo: true);
        EventTimelineView.PlayToggleRequested += () => OnViewportPlayClicked(null, new RoutedEventArgs());
        WeaponView.Played += OnViewportPlayed;
        Closed += (_, _) => _soundPlayer.Dispose();
    }

    /// <summary>Moves the viewport block (view, transport) into the Anim Events or VData tab, or back into Animations.</summary>
    private void PlaceViewport(int tool)
    {
        Panel target = tool == SoundsTool ? EventsViewportHost : tool == VDataTool ? VDataViewportHost : AnimationViewportHost;
        if (ViewportPanel.Parent == target)
        {
            return;
        }

        (ViewportPanel.Parent as Panel)?.Children.Remove(ViewportPanel);
        target.Children.Add(ViewportPanel);
    }

    // ---- Opening a clip ----

    /// <summary>Shows the clip selected in the outliner, or the weapon's clip overview when none is.</summary>
    private async Task RefreshAnimEventsAsync()
    {
        if (_connection is null || _weapon is not { } weapon)
        {
            _events = null;
            SoundsTitle.Text = "Select a weapon in the outliner";
            EventTimelineView.IsVisible = false;
            EventClipList.IsVisible = false;
            ClipInfoPanel.Children.Clear();
            EventEditorPanel.Children.Clear();
            ShellReloadPanel.Children.Clear();
            SetEventButtons();
            return;
        }

        _ = EnsureSoundsAsync();
        if (_clipPath is { } path && weapon.ClipPaths.Contains(path))
        {
            await OpenEventsClipAsync(weapon, path);
        }
        else
        {
            ShowEventsOverview(weapon);
        }
    }

    private async Task OpenEventsClipAsync(WeaponEntry weapon, string path)
    {
        if (_connection is not { } connection)
        {
            return;
        }

        if (_events is { } open && open.Path == path && open.Weapon == weapon)
        {
            RenderEvents(); // Already open: keep its undo history.
            return;
        }

        SoundsTitle.Text = $"{weapon.Id} / {ExplorerNode.ClipName(path)}: reading…";
        try
        {
            var sandbox = CurrentSandbox();
            var addonClip = WeaponLab.Core.Addons.AddonInventory.ClipFile(Target(), _inventory, path);
            var (bytes, edits) = await Task.Run(() =>
            {
                var sandboxClip = sandbox?.Manifest.Clips.FirstOrDefault(clip => clip.ResourcePath == path);
                if (sandbox is null || sandboxClip is null)
                {
                    // No work yet: what the game plays, the addon's own version when it has one (a sandbox made
                    // on the first edit takes the addon's clips too, see EnsureSandboxAsync).
                    return (addonClip is not null ? File.ReadAllBytes(addonClip) : connection.Archive.ReadBytes(path), StoredEventEdits.Empty);
                }

                var (templatePath, _) = Core.Skeletons.CustomSkeletons.Template(sandbox, sandboxClip, Core.Skeletons.CustomSkeletons.Load(sandbox));
                return (File.ReadAllBytes(templatePath), EventEdits.Read(sandbox, RigPackage.ClipId(path)));
            });
            if (_weapon != weapon || _clipPath != path)
            {
                return;
            }

            var timingGun = TimingGunOf(weapon);
            _events = new EventsSession(weapon, path, ClipReader.Read(bytes, path), bytes, sandbox, edits)
            {
                TimingGun = timingGun,
                References = TimingReferences(weapon, path, timingGun),
            };
            _shellReference = 0; // The game's own timing first.
            EventTimelineView.Select([]);
            RenderEvents();
            WarnSandboxFromGame(weapon, path, sandbox, addonClip);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or ArgumentException or System.Text.Json.JsonException)
        {
            SoundsTitle.Text = $"{weapon.Id} / {ExplorerNode.ClipName(path)}: could not be read";
            Log($"Anim Events: {ExplorerNode.ClipName(path)} could not be read: {error.Message}");
        }
    }

    private readonly HashSet<string> _warnedFromGame = new(StringComparer.Ordinal);

    /// <summary>
    /// A sandbox made from CS2's clips while the addon plays its own version of this one (made before Weapon
    /// Lab, e.g. with CS2 Animation Lab): edits start from CS2's animation, and pushing them would replace the
    /// addon's. Said once per clip and session.
    /// </summary>
    private void WarnSandboxFromGame(WeaponEntry weapon, string path, SandboxInfo? sandbox, string? addonClip)
    {
        var clipId = RigPackage.ClipId(path);
        if (addonClip is null || sandbox?.Manifest.Clips.FirstOrDefault(clip => clip.ResourcePath == path) is not { AddonClip: null }
            || ClipExporter.LastExport(sandbox, clipId) is { IdenticalToTemplate: false } || !_warnedFromGame.Add(weapon.Id + "/" + clipId))
        {
            return;
        }

        Log($"Anim Events: {weapon.Id}'s sandbox was made from CS2's clips, but the addon plays its own {clipId}. The timeline shows CS2's version here, and "
            + "pushing an edit would replace the addon's animation with CS2's. To keep the addon's, take the sandbox's clips from the addon (Build .blend → From the addon).");
    }

    /// <summary>A weapon without a clip picked: its clips with their event counts; picking one opens it.</summary>
    private void ShowEventsOverview(WeaponEntry weapon)
    {
        _events = null;
        WeaponView.Plan = null;
        var sandbox = CurrentSandbox();
        var legacy = LegacyClipsToggle.IsChecked == true;
        var rows = new List<EventClipRow>();
        foreach (var path in weapon.ClipPaths.Where(path => legacy || !ExplorerNode.ClipName(path).EndsWith("_lgcy", StringComparison.Ordinal)))
        {
            try
            {
                var info = _connection!.ReadClip(path);
                var kinds = info.Events.GroupBy(clipEvent => clipEvent.Kind).Select(group => $"{group.Count()} {group.Key.ToString().ToLowerInvariant()}");
                var loop = info.SyncTrack.Any(section => section.Id == ShellReload.Loop) ? " · shell loop" : "";
                var edited = sandbox is not null && !EventEdits.Read(sandbox, RigPackage.ClipId(path)).IsEmpty ? "events edited" : "";
                rows.Add(new EventClipRow(info.Name, path, (info.Events.Count == 0 ? "no events" : string.Join(", ", kinds)) + loop, edited));
            }
            catch (Exception error) when (error is InvalidDataException or IOException or System.Text.Json.JsonException)
            {
                rows.Add(new EventClipRow(ExplorerNode.ClipName(path), path, "could not be read", ""));
            }
        }

        EventClipList.ItemsSource = rows;
        EventClipList.IsVisible = true;
        EventTimelineView.IsVisible = false;
        SoundsTitle.Text = $"{weapon.Id}: pick a clip ({rows.Count}{(legacy ? "" : ", legacy clips hidden")})";
        ClipInfoPanel.Children.Clear();
        ClipInfoPanel.Children.Add(Note("Pick a clip in the outliner or in the list to edit its events."));
        EventEditorPanel.Children.Clear();
        ShellReloadPanel.Children.Clear();
        SetEventButtons();
    }

    private void OnEventClipPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (EventClipList.SelectedItem is not EventClipRow row || _weapon is not { } weapon)
        {
            return;
        }

        // Select it in the outliner too, so both agree; that opens the clip here.
        var weaponNode = _tree.SelectMany(category => category.Children).FirstOrDefault(node => node.Weapon == weapon);
        if (weaponNode?.Children.FirstOrDefault(node => node.ClipPath == row.ClipPath) is { } clipNode)
        {
            weaponNode.IsExpanded = true;
            ExplorerTree.SelectedItem = clipNode;
        }
    }

    private void OnLegacyClipsToggled(object? sender, RoutedEventArgs e)
    {
        if (_events is null && _weapon is { } weapon)
        {
            ShowEventsOverview(weapon);
        }
    }

    // ---- What the clip looks like with its edits ----

    private static IReadOnlyList<TimelineItem> TimelineItems(EventsSession session)
    {
        var (template, edits, last) = (session.Template, session.Edits, session.LastFrame);
        var removed = edits.List.Removed.ToHashSet();
        var changes = edits.Changes.ToDictionary(change => change.Index);
        var items = new List<TimelineItem>();
        foreach (var clipEvent in template.Events.Where(clipEvent => !removed.Contains(clipEvent.Index)))
        {
            var change = changes.GetValueOrDefault(clipEvent.Index);
            var start = (change?.StartTime ?? clipEvent.StartTime) * last;
            var duration = (change?.Duration ?? clipEvent.Duration) * last;
            var name = change?.Name ?? (EventSchema.NameKey(clipEvent.ClassName) is { } key && change?.Fields.TryGetValue(key, out var renamed) == true ? renamed : clipEvent.Name);
            items.Add(new TimelineItem("e:" + clipEvent.Index, clipEvent.Kind, name, start, duration, change is not null, false, EventSchema.CanRetime(clipEvent.ClassName),
                Tip(clipEvent.Kind, name, start, duration, clipEvent.SecondaryId, change is not null ? "edited" : "")));
        }

        foreach (var added in edits.List.Added)
        {
            var (start, duration) = (added.StartTime * last, added.Duration * last);
            items.Add(new TimelineItem("n:" + added.Id, added.Kind, added.Name, start, duration, false, true, true,
                Tip(added.Kind, added.Name, start, duration, added.Fields.GetValueOrDefault("m_secondaryID", ""), "added")));
        }

        return items;
    }

    private static string Tip(ClipEventKind kind, string name, double start, double duration, string secondary, string state)
    {
        var meaning = kind == ClipEventKind.Gameplay && GameplayIds.Find(name) is { } id ? "\n" + id.Meaning : "";
        return $"{kind} · {name}{(secondary.Length > 0 ? $" [{secondary}]" : "")}\nframe {start:0.##}{(duration > 0 ? $" to {start + duration:0.##}" : "")}{(state.Length > 0 ? $" · {state}" : "")}{meaning}";
    }

    private static IReadOnlyList<SyncSection> SyncTrack(EventsSession session) => session.Edits.List.Sync ?? session.Template.SyncTrack;

    private static ShellLoop? Loop(EventsSession session)
    {
        var items = TimelineItems(session);
        var addAmmo = items.FirstOrDefault(item => item.Kind == ClipEventKind.Gameplay && item.Name == ShellReload.AddAmmo);
        var loop = ShellReload.Read(SyncTrack(session), [], session.LastFrame);
        return loop is null ? null : loop with { AddAmmo = addAmmo is null ? loop.AddAmmo : (int)Math.Round(addAmmo.Start) };
    }

    // ---- Showing it ----

    private void RenderEvents()
    {
        if (_events is not { } session)
        {
            return;
        }

        var last = session.LastFrame;
        EventClipList.IsVisible = false;
        EventTimelineView.IsVisible = true;
        EventTimelineView.FrameCount = session.Template.FrameCount;
        EventTimelineView.Items = TimelineItems(session);
        EventTimelineView.Sections = SyncTrack(session).Select(section => new TimelineSection(section.Id, section.StartTime * last, section.EndTime * last)).ToList();
        EventTimelineView.Playhead = WeaponView.CurrentFrame;
        ShowGhost(session);

        var count = EventTimelineView.Items.Count;
        var edited = session.Edits.IsEmpty ? "" : " · edited";
        SoundsTitle.Text = $"{session.Weapon.Id} / {session.Template.Name}: {count} event{(count == 1 ? "" : "s")}{edited}"
            + (session.Sandbox is null ? " · no sandbox yet (your first edit makes one)" : "");
        ShowClipInfo(session);
        ShowEventInspector();
        ShowShellPanel(session);
        SetEventButtons();
        UpdateShellPlan();
    }

    private void SetEventButtons()
    {
        var editable = _events is not null;
        AddSoundButton.IsEnabled = editable;
        AddGameplayButton.IsEnabled = editable;
        CopyEventsButton.IsEnabled = editable;
        UndoEventsButton.IsEnabled = _events is { Undo.Count: > 0 };
        RedoEventsButton.IsEnabled = _events is { Redo.Count: > 0 };
    }

    private void ShowClipInfo(EventsSession session)
    {
        var panel = ClipInfoPanel;
        panel.Children.Clear();
        var template = session.Template;
        panel.Children.Add(Header(template.Name));
        panel.Children.Add(Note($"{template.FrameCount} frames · {template.DurationSeconds:0.00} s at {template.FramesPerSecond:0.#} fps · {template.Events.Count} events in CS2's clip"));
        if (session.Sandbox is null)
        {
            // 0.24: no button to press first. The first edit makes the sandbox (MakeSandboxForEditAsync).
            panel.Children.Add(Note($"{session.Weapon.Id} has no sandbox yet. Your first edit here creates it (the weapon's own folder in the profile); nothing reaches the game until you push."));
        }

        // The clip's length: shorter cuts the end, longer holds the last pose; events keep their frames.
        var lengthRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,70,Auto,*"), ColumnSpacing = 6 };
        var lengthText = new TextBlock { Text = "Length", VerticalAlignment = VerticalAlignment.Center };
        var lengthBox = new TextBox { Text = template.FrameCount.ToString(CultureInfo.InvariantCulture) };
        var lengthSet = new Button { Content = "Set" };
        var lengthNote = new TextBlock { Text = session.Edits.Frames is null ? "frames (CS2's)" : $"frames (CS2's is {session.BaseTemplate.FrameCount})", VerticalAlignment = VerticalAlignment.Center, Classes = { "muted" }, FontSize = 11 };
        ToolTip.SetTip(lengthBox, "The clip's frame count. Shorter cuts the end, longer holds the last pose; events and loop sections keep their frames. Export from Blender again after changing it.");
        lengthSet.Click += (_, _) =>
        {
            if (!int.TryParse(lengthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
            {
                ShowEventsStatus("The length is a whole number of frames.", error: true);
                return;
            }

            if (frames == template.FrameCount)
            {
                return;
            }

            try
            {
                CommitEvents(ClipLength.SetFrames(session.BaseTemplate, session.Edits, frames),
                    $"clip length {frames} frames ({(frames - 1) / Math.Max(template.FramesPerSecond, 1):0.00} s); export from Blender again for poses at that length");
            }
            catch (ClipWriteException error)
            {
                ShowEventsStatus(error.Message, error: true);
            }
        };
        foreach (var (control, column) in new (Control, int)[] { (lengthText, 0), (lengthBox, 1), (lengthSet, 2), (lengthNote, 3) })
        {
            Grid.SetColumn(control, column);
            lengthRow.Children.Add(control);
        }

        panel.Children.Add(lengthRow);

        var edits = session.Edits;
        if (!edits.IsEmpty)
        {
            panel.Children.Add(Note($"Edits: {edits.Changes.Count} changed, {edits.List.Added.Count} added, {edits.List.Removed.Count} removed{(edits.List.Sync is null ? "" : ", sync track")}{(edits.Frames is { } length ? $", {length} frames long" : "")}. "
                + "They are written into the clip's export (kept by Blender re-exports) and reach the addon with the next push."));
            var reset = new Button { Content = "Reset clip to CS2's events", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            reset.Classes.Add("danger");
            ToolTip.SetTip(reset, "Puts the events back to CS2's; the clip keeps the length you set.");
            reset.Click += (_, _) => CommitEvents(StoredEventEdits.Empty with { Frames = session.Edits.Frames }, "events back to CS2's");
            panel.Children.Add(reset);
        }
    }

    /// <summary>The inspector for the selected events: one event's fields, or a summary for several.</summary>
    private void ShowEventInspector()
    {
        var panel = EventEditorPanel;
        panel.Children.Clear();
        if (_events is not { } session)
        {
            return;
        }

        var selected = EventTimelineView.Items.Where(item => EventTimelineView.Selected.Contains(item.Key)).ToList();
        if (selected.Count == 0)
        {
            panel.Children.Add(Note("Select an event on the timeline (or double-click a lane to add one)."));
            return;
        }

        if (selected.Count > 1)
        {
            panel.Children.Add(Header($"{selected.Count} events"));
            panel.Children.Add(Note("Drag or ←/→ to move them together; Delete removes them; Ctrl+D duplicates them."));
            return;
        }

        var item = selected[0];
        var template = item.Key.StartsWith("e:", StringComparison.Ordinal) ? session.Template.Events[int.Parse(item.Key[2..], CultureInfo.InvariantCulture)] : null;
        var added = item.Added ? session.Edits.List.Added.First(candidate => "n:" + candidate.Id == item.Key) : null;
        var className = template?.ClassName ?? added!.ClassName;
        panel.Children.Add(Header($"{item.Kind} event{(item.Added ? " (added)" : template is not null ? $" #{template.Index}" : "")}"));
        if (!item.Editable)
        {
            panel.Children.Add(Note("Read-only: legacy and unknown events are kept exactly as they are."));
            foreach (var (key, value) in template?.Fields.Values ?? new Dictionary<string, string>())
            {
                panel.Children.Add(Note($"{key}: {value}"));
            }

            return;
        }

        if (item.Kind == ClipEventKind.Gameplay)
        {
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#E0B050")),
                Text = GameplayIds.Find(item.Name)?.Meaning ?? "Gameplay events drive the game (ammo, reload stages, actions); an unknown ID does nothing.",
            });
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowSpacing = 4, ColumnSpacing = 6 };
        void Row(string label, Control input)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetRow(text, row);
            Grid.SetRow(input, row);
            Grid.SetColumn(input, 1);
            grid.Children.Add(text);
            grid.Children.Add(input);
        }

        static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        var nameInput = NameInput(item.Kind, item.Name);
        var start = new TextBox { Text = Number(item.Start) };
        var duration = new TextBox { Text = Number(item.Duration) };
        Row(item.Kind == ClipEventKind.Sound ? "Soundevent" : item.Kind == ClipEventKind.Gameplay ? "Gameplay ID" : "Name", nameInput.Control);
        Row($"Start (0–{session.LastFrame})", start);
        Row("Length (frames)", duration);

        var fields = new Dictionary<string, Control>(StringComparer.Ordinal);
        var currentFields = CurrentFields(session, template, added);
        foreach (var spec in EventSchema.Editable(className).Where(spec => spec.Key != EventSchema.NameKey(className)))
        {
            Control input = spec.Type == EventFieldType.Flag
                ? new CheckBox { IsChecked = currentFields.GetValueOrDefault(spec.Key) == "true" }
                : new TextBox { Text = currentFields.GetValueOrDefault(spec.Key, ""), PlaceholderText = spec.Hint };
            ToolTip.SetTip(input, spec.Hint.Length > 0 ? $"{spec.Key}: {spec.Hint}" : spec.Key);
            fields[spec.Key] = input;
            Row(spec.Label, input);
        }

        panel.Children.Add(grid);
        if (template is not null)
        {
            panel.Children.Add(Note($"CS2: {template.Name} at frame {template.StartFrame(session.Template):0.##}{(template.Duration > 0 ? $", {template.DurationFrames(session.Template):0.##} frames long" : "")}"));
        }

        var apply = new Button { Content = "Apply", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        apply.Classes.Add("suggested");
        apply.Click += (_, _) =>
        {
            if (!TryReadFrames(start.Text, out var startFrame) || !TryReadFrames(duration.Text, out var lengthFrames))
            {
                ShowEventsStatus("Start and length are frame numbers, e.g. 12 or 12.5.", error: true);
                return;
            }

            var values = fields.ToDictionary(
                pair => pair.Key,
                pair => pair.Value is CheckBox box ? (box.IsChecked == true ? "true" : "false") : ((TextBox)pair.Value).Text?.Trim() ?? "",
                StringComparer.Ordinal);
            CommitEvents(SetEvent(session, item.Key, nameInput.Value().Trim(), startFrame, lengthFrames, values), $"{item.Name} updated");
        };
        var remove = new Button { Content = "Remove", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
        remove.Classes.Add("danger");
        remove.Click += (_, _) => DeleteSelectedEvents();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { apply, remove } };
        if (template is not null && session.Edits.Changes.Any(change => change.Index == template.Index))
        {
            var restore = new Button { Content = "Back to CS2", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };
            restore.Click += (_, _) => CommitEvents(session.Edits with { Changes = session.Edits.Changes.Where(change => change.Index != template.Index).ToList() }, $"{item.Name} back to CS2's");
            buttons.Children.Add(restore);
        }

        panel.Children.Add(buttons);
    }

    /// <summary>The name input: a sound picker with a play button, a gameplay ID picker, or plain text.</summary>
    private (Control Control, Func<string> Value) NameInput(ClipEventKind kind, string current)
    {
        if (kind == ClipEventKind.Sound)
        {
            var box = new AutoCompleteBox
            {
                Text = current,
                ItemsSource = _soundLibrary?.Names ?? [],
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                MinimumPrefixLength = 2,
                MaxDropDownHeight = 320,
            };
            var play = new Button { Content = "▶", Width = 28, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
            ToolTip.SetTip(play, "Listen to this soundevent");
            play.Click += async (_, _) => await PlaySoundEventAsync(box.Text?.Trim() ?? "", report: true);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
            Grid.SetColumn(play, 1);
            row.Children.Add(box);
            row.Children.Add(play);
            return (row, () => box.Text ?? "");
        }

        if (kind == ClipEventKind.Gameplay)
        {
            var box = new AutoCompleteBox
            {
                Text = current,
                ItemsSource = GameplayIds.All.Select(id => id.Id).ToList(),
                FilterMode = AutoCompleteFilterMode.ContainsOrdinal,
                MinimumPrefixLength = 1,
                MaxDropDownHeight = 320,
            };
            return (box, () => box.Text ?? "");
        }

        var text = new TextBox { Text = current };
        return (text, () => text.Text ?? "");
    }

    private static Dictionary<string, string> CurrentFields(EventsSession session, ClipEvent? template, AddedEvent? added)
    {
        if (added is not null)
        {
            return new Dictionary<string, string>(added.Fields, StringComparer.Ordinal);
        }

        var fields = new Dictionary<string, string>(template!.Fields.Values, StringComparer.Ordinal);
        if (session.Edits.Changes.FirstOrDefault(change => change.Index == template.Index) is { } change)
        {
            foreach (var (key, value) in change.Fields)
            {
                fields[key] = value;
            }
        }

        return fields;
    }

    private void ShowShellPanel(EventsSession session)
    {
        var panel = ShellReloadPanel;
        panel.Children.Clear();
        var isReload = session.Template.Name.Contains("reload", StringComparison.OrdinalIgnoreCase);
        var loop = Loop(session);
        if (!isReload && loop is null)
        {
            return;
        }

        // The game's timing (0.24 "expected class"): the gun whose reload the game counts ammo with. Shown, not edited:
        // the timeline's bands mark the parts this clip cannot change; sounds, particles and the loop are this clip's.
        panel.Children.Add(Header("Game timing"));
        panel.Children.Add(TimingGunPicker(session));
        panel.Children.Add(Note(session.TimingGun is { } timingGun
            ? $"The game counts ammo with {timingGun.Describe()}'s reload; the bands on the timeline show its parts, which this clip cannot change. "
                + "This clip decides what you see and hear: animate it, and time its sounds and particles, to those bands."
            : "No gun's timing is known for this weapon: pick the one its vdata makes CS2 run."));
        foreach (var warning in session.TimingGun?.Warnings() ?? [])
        {
            panel.Children.Add(Warning(warning));
        }

        panel.Children.Add(Header("Shell-by-shell reload"));
        panel.Children.Add(Note(loop is null
            ? "Turn this reload into a loop the game repeats once per shell: pick the frames of one shell going in."
            : "The game repeats the LOOP section once per shell. Drag the section edges on the Sync lane, or set the frames here."));

        var fallback = loop ?? new ShellLoop(session.LastFrame / 3, session.LastFrame / 3 + 12, session.LastFrame / 3 + 8);
        var start = new TextBox { Text = fallback.LoopStart.ToString(CultureInfo.InvariantCulture) };
        var end = new TextBox { Text = fallback.LoopEnd.ToString(CultureInfo.InvariantCulture) };
        var shell = new TextBox { Text = fallback.AddAmmo.ToString(CultureInfo.InvariantCulture) };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*"), RowSpacing = 4, ColumnSpacing = 6 };
        foreach (var (label, input, tip) in new[]
        {
            ("Loop starts", start, "First frame of the shell insert that repeats"),
            ("Loop ends", end, "The outro starts here; the pose here should match the loop start"),
            ("Shell counts", shell, $"WPN_RELOAD_ADD_AMMO: inside the loop, at least {ShellReload.MinAddAmmoLead} frames before its end"),
        })
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(input, tip);
            Grid.SetRow(text, row);
            Grid.SetRow(input, row);
            Grid.SetColumn(input, 1);
            grid.Children.Add(text);
            grid.Children.Add(input);
        }

        panel.Children.Add(grid);
        var apply = new Button { Content = loop is null ? "Make it a shell reload" : "Update the loop", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        apply.Classes.Add("suggested");
        apply.IsEnabled = true;
        apply.Click += (_, _) =>
        {
            if (!int.TryParse(start.Text, out var loopStart) || !int.TryParse(end.Text, out var loopEnd) || !int.TryParse(shell.Text, out var addAmmo))
            {
                ShowEventsStatus("The loop frames are whole frame numbers.", error: true);
                return;
            }

            ApplyShellLoop(session, new ShellLoop(loopStart, loopEnd, addAmmo));
        };
        panel.Children.Add(apply);

        if (loop is not null)
        {
            foreach (var problem in ShellReload.Problems(loop, session.LastFrame))
            {
                panel.Children.Add(Warning(problem));
            }

            var seam = PoseSeam(loop);
            if (seam is not null)
            {
                panel.Children.Add(seam.Value.Ok ? Note(seam.Value.Text) : Warning(seam.Value.Text));
            }

            var remove = new Button { Content = "Remove the loop", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
            remove.Click += (_, _) => RemoveShellLoop(session);
            panel.Children.Add(remove);
        }

        // Against the game timing only (0.25: the compare list, its two lines and the dashed outline are gone; user 2026-11-14).
        if (session.References.Count == 0)
        {
            return;
        }

        foreach (var line in TimingLines(session, loop))
        {
            panel.Children.Add(line);
        }

        if (Reference() is { } reference && ShellReload.Read(reference.SyncTrack, reference.Events, reference.LastFrame) is { } theirs)
        {
            var scale = session.Template.FramesPerSecond > 0 && reference.FramesPerSecond > 0 ? session.Template.FramesPerSecond / reference.FramesPerSecond : 1;
            var target = ShellReload.Scale(theirs, scale);
            var outroEnd = reference.LastFrame * scale;
            var label = ReferenceLabel(session);
            var fit = new Button
            {
                Content = $"Put my loop on the game timing (loop {target.LoopStart}-{target.LoopEnd}, shell at {target.AddAmmo})",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                IsEnabled = ShellReload.Problems(target, session.LastFrame).Count == 0,
            };
            ToolTip.SetTip(fit, $"Puts the loop and the shell on the {label}'s frames and moves every other event (sounds too) with its part: intro, loop or outro. The outro ends at frame {outroEnd:0}; after that the clip holds. Animate the same frames in Blender.");
            fit.Click += (_, _) => FitToReference(session, target, outroEnd, label);
            panel.Children.Add(fit);
        }
    }

    /// <summary>
    /// The expected-class picker: "from the vdata" (what CS2 runs) or any gun of CS2's root graph, for display only.
    /// Saved in the profile; Blender's bands follow it (game_timing.json is written again).
    /// </summary>
    private Control TimingGunPicker(EventsSession session)
    {
        var guns = _connection is { } connection ? Core.Weapons.GameTimings.Guns(connection) : [];
        var fromVData = session.TimingGun?.FromVData?.Gun;
        var items = new List<string> { fromVData is null ? "From the vdata (none found)" : $"From the vdata: {fromVData.Name}" };
        items.AddRange(guns.Select(gun => $"{gun.Group} · {gun.Name}"));
        var chosen = session.TimingGun is { Chosen: true } picked ? guns.ToList().FindIndex(gun => Core.Weapons.TimingGun.KeyOf(gun) == picked.Key) : -1;
        var combo = new ComboBox { ItemsSource = items, SelectedIndex = chosen + 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        ToolTip.SetTip(combo, "Expected class: whose reload timing to show. Display only: it changes no vdata, so the game still runs what the vdata gives.");
        combo.SelectionChanged += (_, _) =>
        {
            var key = combo.SelectedIndex <= 0 ? null : Core.Weapons.TimingGun.KeyOf(guns[combo.SelectedIndex - 1]);
            if (_events != session || key == _profile.Weapon(session.Weapon.Id).TimingGun)
            {
                return;
            }

            _profile.EnsureWeapon(session.Weapon.Id).TimingGun = key;
            SaveProfile();
            session.TimingGun = TimingGunOf(session.Weapon);
            session.References = TimingReferences(session.Weapon, session.Path, session.TimingGun);
            _shellReference = 0;
            if (_sandboxes.Exists(session.Weapon.Id))
            {
                WriteGameTiming(_sandboxes.Open(session.Weapon.Id)); // Blender's bands follow.
            }

            Log($"{session.Weapon.Id}: game timing shown as {session.TimingGun?.Describe() ?? "none"}.");
            RenderEvents();
        };
        return combo;
    }

    /// <summary>Plain-words warnings against the game timing, in what the player sees: when the counter goes up against the animation (0.25: warnings only; the side-by-side numbers are gone).</summary>
    private IEnumerable<Control> TimingLines(EventsSession session, ShellLoop? loop)
    {
        if (Reference() is not { } reference)
        {
            yield break;
        }

        var label = ReferenceLabel(session);
        if (ShellReload.Timing(ShellReload.Read(reference.SyncTrack, reference.Events, reference.LastFrame), reference) is { } theirs)
        {
            var mine = ShellReload.Timing(loop, session.Template);
            if (mine is null)
            {
                yield break;
            }

            if (mine.Intro > theirs.Intro + 0.05)
            {
                yield return Warning($"The intro is {mine.Intro - theirs.Intro:0.00} s longer than the {label}'s, so the game may load every shell before your loop starts, and the loop does not repeat on screen.");
            }

            var firstShell = mine.Intro + mine.AddAmmoIntoLoop - (theirs.Intro + theirs.AddAmmoIntoLoop);
            var perShell = mine.Loop - theirs.Loop;
            if (Math.Abs(firstShell) > 0.05 || Math.Abs(perShell) > 0.05)
            {
                var drift = Math.Abs(perShell) > 0.05 ? $", and {Math.Abs(perShell):0.00} s {(perShell > 0 ? "more" : "less")} with each shell after that" : "";
                yield return Warning($"When the game runs the {label}'s logic, the ammo counter adds the first shell {Math.Abs(firstShell):0.00} s {(firstShell > 0 ? "before" : "after")} your animation shows it{drift}.");
            }

            yield break;
        }

        // A magazine reload: the game refills at the reference's WPN_RELOAD_ADD_AMMO.
        var refill = reference.Events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == ShellReload.AddAmmo);
        if (refill is null)
        {
            yield break;
        }

        var refillSeconds = refill.StartSeconds(reference);
        var mark = TimelineItems(session).FirstOrDefault(item => item.Kind == ClipEventKind.Gameplay && item.Name == ShellReload.AddAmmo);
        if (mark is not null && session.Template.FramesPerSecond > 0)
        {
            var markSeconds = mark.Start / session.Template.FramesPerSecond;
            if (Math.Abs(markSeconds - refillSeconds) > 0.1)
            {
                yield return Warning($"When the game runs the {label}'s logic, the magazine refills {Math.Abs(markSeconds - refillSeconds):0.00} s {(markSeconds > refillSeconds ? "before" : "after")} the moment this clip marks.");
            }
        }
    }

    private ClipInfo? Reference()
    {
        if (_connection is not { } connection || _events is not { References.Count: > 0 } session)
        {
            return null;
        }

        var path = session.References[Math.Clamp(_shellReference, 0, session.References.Count - 1)].Path;
        try
        {
            return connection.Archive.Contains(path) ? connection.ReadClip(path) : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private string ReferenceLabel(EventsSession session) =>
        session.References.Count == 0 ? "" : session.References[Math.Clamp(_shellReference, 0, session.References.Count - 1)].Label;

    /// <summary>Moves the loop onto the reference's frames and every other event with its part (see <see cref="ShellReload.Fit"/>).</summary>
    private void FitToReference(EventsSession session, ShellLoop target, double outroEnd, string label)
    {
        try
        {
            var silent = session.Template.Name.Contains("silent", StringComparison.OrdinalIgnoreCase);
            CommitEvents(ShellReload.Fit(session.Template, session.Edits, Loop(session), target, outroEnd, silent),
                $"events on the {label}'s timing: loop {target.LoopStart}-{target.LoopEnd}, shell at {target.AddAmmo}, outro ends at {outroEnd:0}");
        }
        catch (ClipWriteException error)
        {
            ShowEventsStatus(error.Message, error: true);
        }
    }

    /// <summary>The game's timing for the open clip at its current length (cached until the gun or the length changes), or null.</summary>
    private Core.Weapons.GameTiming? GameTimingOf(EventsSession session)
    {
        if (_connection is not { } connection || session.TimingGun is not { } gun)
        {
            return null;
        }

        var key = gun.Key + "@" + session.Template.FrameCount.ToString(CultureInfo.InvariantCulture);
        if (session.TimingCache is { } cached && cached.Key == key)
        {
            return cached.Timing;
        }

        Core.Weapons.GameTiming? timing;
        try
        {
            timing = Core.Weapons.GameTimings.For(connection, gun.Gun, session.Weapon, session.Template);
        }
        catch (InvalidDataException)
        {
            timing = null;
        }

        session.TimingCache = (key, timing);
        return timing;
    }

    /// <summary>The game's timing as bands behind the lanes (0.24). The dashed compare outline on the Sync lane is gone (0.25).</summary>
    private void ShowGhost(EventsSession session)
    {
        // The game's timing as bands (0.24): the expected class's reload, on this clip's frames.
        var timing = GameTimingOf(session);
        EventTimelineView.BandsLabel = timing is null ? "" : $"game timing: the {timing.Gun}'s {timing.Reference.Name} (fixed)";
        EventTimelineView.Bands = timing?.Bands.Select(band => new TimelineBand(band.Start, band.End, band.Label, band.Kind)).ToList() ?? [];
        EventTimelineView.Ghost = [];
    }

    /// <summary>How far the weapon's bones are apart between the loop's start and end (where the graph jumps back), from the viewport clip.</summary>
    private (bool Ok, string Text)? PoseSeam(ShellLoop loop)
    {
        if (WeaponView.Animation is not { } clip || loop.LoopEnd >= clip.FrameCount || loop.LoopStart >= clip.FrameCount)
        {
            return null;
        }

        var a = clip.Frames[loop.LoopStart];
        var b = clip.Frames[loop.LoopEnd];
        var worst = a.Zip(b).Select(pair => System.Numerics.Vector3.Distance(pair.First.Translation, pair.Second.Translation)).DefaultIfEmpty(0).Max();
        return worst < 0.25
            ? (true, $"Loop seam: frames {loop.LoopStart} and {loop.LoopEnd} match (bones within {worst:0.00} units).")
            : (false, $"Loop seam: frames {loop.LoopStart} and {loop.LoopEnd} differ by up to {worst:0.0} units, so each pass jumps. Animate the loop to end where it starts.");
    }

    // ---- Edits ----

    private void MoveEvents(IReadOnlyList<string> keys, double deltaFrames)
    {
        if (_events is not { } session)
        {
            return;
        }

        var items = EventTimelineView.Items.ToDictionary(item => item.Key);
        var next = session.Edits;
        foreach (var key in keys.Where(items.ContainsKey))
        {
            var item = items[key];
            next = SetTiming(session, next, key, item.Start + deltaFrames, item.Duration);
        }

        CommitEvents(next, $"{keys.Count} event{(keys.Count == 1 ? "" : "s")} moved {deltaFrames:+0.##;-0.##} frames");
    }

    private void ResizeEvent(string key, double lengthFrames)
    {
        if (_events is { } session && EventTimelineView.Items.FirstOrDefault(item => item.Key == key) is { } item)
        {
            CommitEvents(SetTiming(session, session.Edits, key, item.Start, lengthFrames), $"{item.Name} now {lengthFrames:0.##} frames long");
        }
    }

    /// <summary>The edits with one event at a new start and length (frames); a template event that ends up as in CS2 loses its change.</summary>
    private static StoredEventEdits SetTiming(EventsSession session, StoredEventEdits edits, string key, double startFrames, double lengthFrames)
    {
        var last = (double)session.LastFrame;
        var (start, duration) = (startFrames / last, lengthFrames / last);
        if (key.StartsWith("n:", StringComparison.Ordinal))
        {
            var id = key[2..];
            return edits with { List = edits.List with { Added = edits.List.Added.Select(added => added.Id == id ? added with { StartTime = start, Duration = duration } : added).ToList() } };
        }

        var index = int.Parse(key[2..], CultureInfo.InvariantCulture);
        var original = session.Template.Events[index];
        var change = edits.Changes.FirstOrDefault(item => item.Index == index) ?? new EventChange(index, null, null);
        change = change with
        {
            StartTime = Math.Abs(start - original.StartTime) > 1e-6 ? start : null,
            Duration = Math.Abs(duration - original.Duration) > 1e-6 ? duration : null,
        };
        return WithChange(edits, change);
    }

    /// <summary>The edits with one event's name, timing and fields as given (only what differs from CS2's event is stored).</summary>
    private static StoredEventEdits SetEvent(EventsSession session, string key, string name, double startFrames, double lengthFrames, IReadOnlyDictionary<string, string> fields)
    {
        var edits = SetTiming(session, session.Edits, key, startFrames, lengthFrames);
        if (key.StartsWith("n:", StringComparison.Ordinal))
        {
            var id = key[2..];
            return edits with
            {
                List = edits.List with
                {
                    Added = edits.List.Added.Select(added => added.Id == id
                        ? added with { Name = name, Fields = new Dictionary<string, string>(added.Fields.Concat(fields).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value)) }
                        : added).ToList(),
                },
            };
        }

        var index = int.Parse(key[2..], CultureInfo.InvariantCulture);
        var original = session.Template.Events[index];
        var change = edits.Changes.FirstOrDefault(item => item.Index == index) ?? new EventChange(index, null, null);
        var changedFields = fields.Where(pair => pair.Value != original.Fields[pair.Key]).ToDictionary(pair => pair.Key, pair => pair.Value);
        change = change with { Name = name != original.Name ? name : null, Fields = changedFields };
        return WithChange(edits, change);
    }

    private static StoredEventEdits WithChange(StoredEventEdits edits, EventChange change)
    {
        var others = edits.Changes.Where(item => item.Index != change.Index);
        var empty = change.Name is null && change.StartTime is null && change.Duration is null && change.Fields.Count == 0;
        return edits with { Changes = empty ? others.ToList() : [.. others, change] };
    }

    private void AddEvent(ClipEventKind kind, double frame)
    {
        if (_events is not { } session || kind is not (ClipEventKind.Sound or ClipEventKind.Gameplay))
        {
            ShowEventsStatus("Only sound and gameplay events can be added for now.", error: true);
            return;
        }

        var sound = kind == ClipEventKind.Sound;
        var name = sound
            ? _lastSoundName.Length > 0 ? _lastSoundName : session.Template.Events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Sound)?.Name ?? "WeaponLab.NewSound"
            : ShellReload.AddAmmo;
        var added = new AddedEvent(ShellReload.NextId(session.Edits.List.Added), sound ? "CNmSoundEvent" : "CNmIDEvent", name, Math.Clamp(frame, 0, session.LastFrame) / session.LastFrame, 0);
        CommitEvents(session.Edits with { List = session.Edits.List with { Added = [.. session.Edits.List.Added, added] } }, $"{kind} event added at frame {frame:0}");
        EventTimelineView.Select(["n:" + added.Id]);
    }

    private void OnAddSoundClicked(object? sender, RoutedEventArgs e) => AddEvent(ClipEventKind.Sound, Math.Round(WeaponView.CurrentFrame));

    private void OnAddGameplayClicked(object? sender, RoutedEventArgs e) => AddEvent(ClipEventKind.Gameplay, Math.Round(WeaponView.CurrentFrame));

    private void DeleteSelectedEvents()
    {
        if (_events is not { } session || EventTimelineView.Selected.Count == 0)
        {
            return;
        }

        var keys = EventTimelineView.Selected.Where(key => EventTimelineView.Items.First(item => item.Key == key).Editable).ToList();
        var removed = keys.Where(key => key.StartsWith("e:", StringComparison.Ordinal)).Select(key => int.Parse(key[2..], CultureInfo.InvariantCulture)).ToHashSet();
        var addedIds = keys.Where(key => key.StartsWith("n:", StringComparison.Ordinal)).Select(key => key[2..]).ToHashSet();
        var edits = session.Edits;
        var next = edits with
        {
            Changes = edits.Changes.Where(change => !removed.Contains(change.Index)).ToList(),
            List = edits.List with
            {
                Removed = edits.List.Removed.Concat(removed).Distinct().Order().ToList(),
                Added = edits.List.Added.Where(added => !addedIds.Contains(added.Id)).ToList(),
            },
        };
        CommitEvents(next, $"{keys.Count} event{(keys.Count == 1 ? "" : "s")} removed");
        EventTimelineView.Select([]);
    }

    private void DuplicateSelectedEvents()
    {
        if (_events is not { } session)
        {
            return;
        }

        var added = session.Edits.List.Added.ToList();
        var newKeys = new List<string>();
        foreach (var item in EventTimelineView.Items.Where(item => EventTimelineView.Selected.Contains(item.Key) && item.Kind is ClipEventKind.Sound or ClipEventKind.Gameplay))
        {
            var className = item.Kind == ClipEventKind.Sound ? "CNmSoundEvent" : "CNmIDEvent";
            var fields = item.Key.StartsWith("n:", StringComparison.Ordinal)
                ? session.Edits.List.Added.First(candidate => "n:" + candidate.Id == item.Key).Fields
                : CurrentFields(session, session.Template.Events[int.Parse(item.Key[2..], CultureInfo.InvariantCulture)], null)
                    .Where(pair => pair.Key is "m_secondaryID" or "m_position" or "m_attachmentName" or "m_relevance").ToDictionary();
            var start = Math.Min(item.Start + 2, session.LastFrame - item.Duration);
            var copy = new AddedEvent(ShellReload.NextId(added), className, item.Name, start / session.LastFrame, item.Duration / session.LastFrame) { Fields = fields };
            added.Add(copy);
            newKeys.Add("n:" + copy.Id);
        }

        if (newKeys.Count > 0)
        {
            CommitEvents(session.Edits with { List = session.Edits.List with { Added = added } }, $"{newKeys.Count} event{(newKeys.Count == 1 ? "" : "s")} duplicated");
            EventTimelineView.Select(newKeys);
        }
    }

    private void MoveSectionBoundary(int index, double frame)
    {
        if (_events is not { } session)
        {
            return;
        }

        var sections = SyncTrack(session).ToList();
        if (Loop(session) is { } loop && sections.Count == 3 && sections[1].Id == ShellReload.Loop)
        {
            // A shell loop moves as a whole: the shell keeps its place in the loop.
            var shellOffset = loop.AddAmmo - loop.LoopStart;
            var moved = index == 1 ? loop with { LoopStart = (int)Math.Round(frame) } : loop with { LoopEnd = (int)Math.Round(frame) };
            var addAmmo = Math.Clamp(moved.LoopStart + shellOffset, moved.LoopStart, moved.LoopEnd - ShellReload.MinAddAmmoLead);
            ApplyShellLoop(session, moved with { AddAmmo = addAmmo });
            return;
        }

        var at = frame / session.LastFrame;
        var before = sections[index - 1];
        var after = sections[index];
        sections[index - 1] = before with { Duration = at - before.StartTime };
        sections[index] = after with { StartTime = at, Duration = after.EndTime - at };
        CommitEvents(session.Edits with { List = session.Edits.List with { Sync = sections } }, "sync section moved");
    }

    private void ApplyShellLoop(EventsSession session, ShellLoop loop)
    {
        try
        {
            var silent = session.Template.Name.Contains("silent", StringComparison.OrdinalIgnoreCase);
            CommitEvents(ShellReload.Apply(session.Template, session.Edits, loop, silent), $"shell loop {loop.LoopStart}-{loop.LoopEnd}, shell at {loop.AddAmmo}");
        }
        catch (ClipWriteException error)
        {
            ShowEventsStatus(error.Message, error: true);
        }
    }

    private void RemoveShellLoop(EventsSession session)
    {
        string[] markers = [ShellReload.Intro, ShellReload.Loop, ShellReload.Outro];
        var edits = session.Edits;
        var next = edits with
        {
            List = edits.List with
            {
                Sync = null,
                Added = edits.List.Added.Where(added => !markers.Contains(added.Name)).ToList(),
            },
        };
        CommitEvents(next, "shell loop removed (the clip's own sync track is back)");
    }

    private async void OnCopyEventsClicked(object? sender, RoutedEventArgs e)
    {
        if (_events is not { } session || _connection is not { } connection)
        {
            return;
        }

        var reload = session.Template.Name.Contains("reload", StringComparison.OrdinalIgnoreCase);
        var picked = await Dialogs.CopyEventsAsync(this, connection, reload ? "rifle_nova" : session.Weapon.Id, reload ? "reload_nova" : session.Template.Name);
        if (picked is not { } choice || _events != session)
        {
            return;
        }

        try
        {
            CommitEvents(EventCopy.Into(session.Edits, choice.Source, choice.Events, choice.SyncTrack),
                $"{choice.Events.Count} event{(choice.Events.Count == 1 ? "" : "s")} copied from {choice.Source.Name}{(choice.SyncTrack ? " with its sync track" : "")}");
        }
        catch (ClipWriteException error)
        {
            ShowEventsStatus(error.Message, error: true);
        }
    }

    private void OnUndoEventsClicked(object? sender, RoutedEventArgs e) => UndoEvents(redo: false);

    private void OnRedoEventsClicked(object? sender, RoutedEventArgs e) => UndoEvents(redo: true);

    private void UndoEvents(bool redo)
    {
        if (_events is not { } session)
        {
            return;
        }

        var (from, to) = redo ? (session.Redo, session.Undo) : (session.Undo, session.Redo);
        if (from.Count == 0)
        {
            return;
        }

        to.Push(session.Edits);
        session.Edits = from.Pop();
        RenderEvents();
        ScheduleEventsWrite(redo ? "redo" : "undo");
    }

    /// <summary>
    /// Takes new edits for the open clip: checked by writing the template
    /// with them (so a bad edit is refused at once), shown, then written into
    /// the sandbox and the clip's export shortly after. A weapon without a
    /// sandbox gets one first (0.24, see <see cref="MakeSandboxForEditAsync"/>).
    /// </summary>
    private void CommitEvents(StoredEventEdits next, string what)
    {
        if (_events is not { } session)
        {
            return;
        }

        try
        {
            ClipWriter.Write(session.TemplateBytes, session.Path, new ClipEdit { Events = next.Changes, EventList = next.List, FrameCount = next.Frames });
        }
        catch (ClipWriteException error)
        {
            ShowEventsStatus("Not changed: " + error.Message, error: true);
            RenderEvents(); // Put dragged marks back.
            return;
        }

        if (session.Sandbox is null)
        {
            _ = MakeSandboxForEditAsync(session, next, what);
            return;
        }

        session.Undo.Push(session.Edits);
        session.Redo.Clear();
        session.Edits = next;
        ShowEventsStatus(what, error: false);
        RenderEvents();
        ScheduleEventsWrite(what);
    }

    private bool _makingSandbox;

    /// <summary>
    /// The first edit on a weapon without a sandbox (0.24, user 2026-11-08): creates the sandbox the way Build
    /// .blend would (the addon's clips when it has its own), opens the clip again from it, then applies the edit.
    /// The edit is dropped, with a message, when the sandbox's copy of the clip is not the one the edit was made
    /// on (the weapon is set to build from CS2's clips while the addon has its own), since event numbers may differ.
    /// </summary>
    private async Task MakeSandboxForEditAsync(EventsSession session, StoredEventEdits next, string what)
    {
        if (_makingSandbox || _connection is not { } connection)
        {
            return;
        }

        _makingSandbox = true;
        try
        {
            ShowEventsStatus($"Creating {session.Weapon.Id}'s sandbox for your first edit…", error: false);
            if (await EnsureSandboxAsync(connection, session.Weapon) is null)
            {
                ShowEventsStatus("Not changed: the weapon's sandbox could not be created (see the log).", error: true);
                RenderEvents();
                return;
            }

            RefreshWeaponActions();
            if (_events != session)
            {
                return; // Another clip was picked meanwhile; the sandbox stays, the edit is dropped.
            }

            _events = null;
            await OpenEventsClipAsync(session.Weapon, session.Path);
            if (_events is not { Sandbox: not null } reopened || reopened.Path != session.Path)
            {
                return;
            }

            if (!reopened.TemplateBytes.AsSpan().SequenceEqual(session.TemplateBytes) || !reopened.Edits.IsEmpty)
            {
                ShowEventsStatus($"Created {session.Weapon.Id}'s sandbox, but its copy of {session.ClipId} is not the one you edited "
                    + "(Build .blend takes CS2's clips for this weapon), so the edit was not applied. Make it again on the clip shown now.", error: true);
                return;
            }

            CommitEvents(next, what);
            ShowEventsStatus($"{what} · created {session.Weapon.Id}'s sandbox for it", error: false);
        }
        finally
        {
            _makingSandbox = false;
        }
    }

    /// <summary>Writes the open clip's edits a moment after the last change (a burst of nudges writes once).</summary>
    private void ScheduleEventsWrite(string what)
    {
        _eventWriteTimer?.Stop();
        _eventWriteTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _eventWriteTimer.Tick += async (_, _) =>
        {
            _eventWriteTimer?.Stop();
            await WriteEventsAsync(what);
        };
        _eventWriteTimer.Start();
    }

    private async Task WriteEventsAsync(string what)
    {
        if (_events is not { Sandbox: { } sandbox } session)
        {
            return;
        }

        var edits = session.Edits;
        try
        {
            _quietExportsUntil = DateTime.UtcNow.AddSeconds(4);
            EventEdits.Save(sandbox, session.ClipId, edits);
            ClipLengths.Write(sandbox); // Blender shows and samples the length set here.
            var report = await Task.Run(() => EventEdits.Apply(sandbox, session.ClipId));
            Log(report is null
                ? $"{session.ClipId}: no event edits left; its export is back to the template."
                : $"{session.ClipId}: {what}. Written into the sandbox export; push it in the Deliver tab.");
            await RefreshStatusAsync();
            if (_viewportClipPath == session.Path)
            {
                LoadViewportClip(session.Path); // The export changed: play it.
            }
        }
        catch (Exception error) when (error is ClipWriteException or InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            ShowEventsStatus("Kept in the sandbox but the export failed: " + error.Message, error: true);
            Log($"{session.ClipId}: the export with the new events failed: {error.Message}");
        }
    }

    private void ShowEventsStatus(string text, bool error)
    {
        EventsStatus.Text = text;
        EventsStatus.Foreground = error ? Brushes.IndianRed : Brushes.Gray;
        EventsStatus.IsVisible = true;
    }

    private static bool TryReadFrames(string? text, out double frames) =>
        double.TryParse(text?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out frames) && double.IsFinite(frames) && frames >= 0;

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Note(string text) => new() { Text = text, Classes = { "muted" }, FontSize = 11, TextWrapping = TextWrapping.Wrap };

    private static TextBlock Warning(string text) => new() { Text = text, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#E0B050")) };

    // ---- Playback: shells and sounds ----

    /// <summary>The viewport plays a shell reload as the game does (intro, the loop once per shell, outro) when shells are picked.</summary>
    private void UpdateShellPlan()
    {
        var loop = _events is { } session && _viewportClipPath == session.Path ? Loop(session) : null;
        ViewportShells.IsEnabled = loop is not null;
        var shells = ViewportShells.SelectedIndex switch { 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 6, 6 => 8, _ => 0 };
        if (loop is null || shells == 0 || WeaponView.Animation is not { } clip)
        {
            WeaponView.Plan = null;
            return;
        }

        var last = clip.FrameCount - 1;
        var plan = new List<PlaybackSegment> { new(0, loop.LoopEnd) };
        plan.AddRange(Enumerable.Repeat(new PlaybackSegment(loop.LoopStart, loop.LoopEnd), shells - 1));
        plan.Add(new PlaybackSegment(loop.LoopEnd, last));
        WeaponView.Plan = plan;
    }

    private void OnViewportShellsChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WeaponView is not null)
        {
            UpdateShellPlan();
        }
    }

    private void OnViewportSoundToggled(object? sender, RoutedEventArgs e)
    {
        _soundPlayer.Muted = ViewportSoundToggle.IsChecked != true;
        if (_soundPlayer.Muted)
        {
            _soundPlayer.StopAll();
        }
        else
        {
            _ = EnsureSoundsAsync();
        }
    }

    private void OnViewportStepClicked(object? sender, RoutedEventArgs e)
    {
        if (WeaponView.Animation is not { } clip || sender is not Button { Tag: string step })
        {
            return;
        }

        WeaponView.Playing = false;
        ViewportPlayGlyph.Text = "▶";
        var frame = Math.Clamp(Math.Round(WeaponView.CurrentFrame) + int.Parse(step, CultureInfo.InvariantCulture), 0, clip.FrameCount - 1);
        WeaponView.CurrentFrame = frame;
        OnViewportFrameChanged(frame);
    }

    /// <summary>Plays the sound events playback just passed: the open clip's events with their edits, else the clip's own.</summary>
    private void OnViewportPlayed(IReadOnlyList<PlaybackSegment> played)
    {
        if (_soundPlayer.Muted || played.Count == 0 || WeaponView.Animation is not { } clip)
        {
            return;
        }

        var sounds = _events is { } session && _viewportClipPath == session.Path
            ? EventTimelineView.Items.Where(item => item.Kind == ClipEventKind.Sound).Select(item => (Frame: item.Start, item.Name)).ToList()
            : clip.Events.Where(clipEvent => clipEvent.IsSound).Select(clipEvent => (clipEvent.Frame, clipEvent.Name)).ToList();
        foreach (var segment in played)
        {
            foreach (var sound in sounds.Where(sound => sound.Frame >= segment.From && sound.Frame < segment.To))
            {
                _ = PlaySoundEventAsync(sound.Name, report: false);
            }
        }
    }

    private async Task PlaySoundEventAsync(string name, bool report)
    {
        if (name.Length == 0 || await EnsureSoundsAsync() is not { } library)
        {
            return;
        }

        if (library.Find(name) is not { Files.Count: > 0 } info)
        {
            if (report || _unknownSounds.Add(name))
            {
                Log($"Sound: no soundevent named {name} in CS2 or the addon (it plays nothing in game either).");
            }

            return;
        }

        var file = info.Files[Random.Shared.Next(info.Files.Count)];
        var decoded = await Task.Run(() => library.Decode(file));
        if (decoded is null)
        {
            Log($"Sound: {file} ({name}) could not be read.");
            return;
        }

        if (report)
        {
            _lastSoundName = name;
        }

        if (_soundPlayer.Play(decoded, info.Volume) is { } problem)
        {
            Log("Sound: " + problem);
        }
    }

    /// <summary>The soundevents of CS2 and the target addon, loaded once in the background.</summary>
    private Task<SoundLibrary?> EnsureSoundsAsync()
    {
        if (_connection is not { } connection)
        {
            return Task.FromResult<SoundLibrary?>(null);
        }

        var addon = Target()?.GamePath;
        var key = connection.Install.RootPath + "|" + addon;
        if (_soundLibraryKey != key || _soundLoad is null)
        {
            _soundLibraryKey = key;
            _soundLoad = Task.Run(() => SoundLibrary.Load(connection, addon));
            _soundLoad.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && _soundLibraryKey == key)
                {
                    _soundLibrary = task.Result;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        return Loaded(_soundLoad);

        static async Task<SoundLibrary?> Loaded(Task<SoundLibrary> load)
        {
            try
            {
                return await load;
            }
            catch (Exception error) when (error is IOException or InvalidDataException)
            {
                return null;
            }
        }
    }
}
