using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

public enum ExplorerNodeKind
{
    Category,
    Weapon,
    Clip,

    /// <summary>The skeleton the weapon's clips drive; its children are the bones, filled when it opens.</summary>
    Skeleton,

    Bone,

    /// <summary>The graphs the weapon's animations go through; children are the graphs, coloured per graph.</summary>
    Graphs,

    Graph,

    /// <summary>The weapon's third-person clips (0.26); its children are clip rows.</summary>
    ThirdPerson,
}

/// <summary>A node in the outliner: category, weapon or clip, coloured by profile status.</summary>
public sealed class ExplorerNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private WeaponState _state;
    private string _statusTip = "";
    private string _detail = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public required ExplorerNodeKind Kind { get; init; }
    public required string Label { get; init; }
    public string Detail
    {
        get => _detail;
        set
        {
            if (_detail != value)
            {
                _detail = value;
                Changed(nameof(Detail));
            }
        }
    }

    public WeaponEntry? Weapon { get; init; }
    public string? ClipPath { get; init; }
    public string? BoneName { get; init; }
    public string? GraphPath { get; init; }
    public ObservableCollection<ExplorerNode> Children { get; } = [];

    public bool IsCategory => Kind == ExplorerNodeKind.Category;

    // Which right-click items a row offers.
    public bool IsWeaponRow => Kind == ExplorerNodeKind.Weapon;
    public bool IsClipRow => Kind == ExplorerNodeKind.Clip;
    public bool IsGraphRow => Kind is ExplorerNodeKind.Graphs or ExplorerNodeKind.Graph;
    public bool IsSkeletonRow => Kind == ExplorerNodeKind.Skeleton;
    public bool IsThirdPersonRow => Kind == ExplorerNodeKind.ThirdPerson;
    public bool HasName => Kind is not ExplorerNodeKind.Category and not ExplorerNodeKind.Graphs and not ExplorerNodeKind.Skeleton and not ExplorerNodeKind.ThirdPerson;

    /// <summary>A third-person clip row (0.26): no events, viewport, set-to-default or revert yet; animation files and Blender only.</summary>
    public bool IsWorldClip => Kind == ExplorerNodeKind.Clip && ClipPath is not null && WeaponCatalog.IsWorldClip(ClipPath);
    public bool IsFirstPersonClip => Kind == ExplorerNodeKind.Clip && !IsWorldClip;

    /// <summary>Rows whose resource path can be copied for ModelDoc and the like (0.23): a skeleton, a clip.</summary>
    public bool HasAssetPath => Kind is ExplorerNodeKind.Skeleton or ExplorerNodeKind.Clip;
    public bool CanSetToStock => Kind is ExplorerNodeKind.Weapon || IsFirstPersonClip;

    /// <summary>Yellow: the profile has work here the addon does not have yet ("Revert to addon default" shows).</summary>
    public bool IsPending => _state == WeaponState.NeedsApply && !IsWorldClip && Kind != ExplorerNodeKind.ThirdPerson;

    public bool IsExpanded
    {
        get => _isExpanded;
        set => Set(ref _isExpanded, value, nameof(IsExpanded));
    }

    public WeaponState State
    {
        get => _state;
        set
        {
            if (_state != value)
            {
                _state = value;
                Changed(nameof(State));
                Changed(nameof(HasStatus));
                Changed(nameof(IsPending));
            }
        }
    }

    public bool HasStatus => _state != WeaponState.Untouched;

    private bool _hasBlend;

    /// <summary>The weapon's sandbox has a .blend (shown with a Blender mark on the Animations tab).</summary>
    public bool HasBlend
    {
        get => _hasBlend;
        set => Set(ref _hasBlend, value, nameof(HasBlend));
    }

    public string StatusTip
    {
        get => _statusTip;
        set
        {
            if (_statusTip != value)
            {
                _statusTip = value;
                Changed(nameof(StatusTip));
            }
        }
    }

    /// <param name="isEdited">Whether a clip (weapon, clip id) has an export with edits; shown as "edited".</param>
    public static List<ExplorerNode> BuildTree(IReadOnlyList<WeaponEntry> weapons, string filter, Func<WeaponEntry, string, bool>? isEdited = null)
    {
        bool Matches(string text) => text.Contains(filter, StringComparison.OrdinalIgnoreCase);
        var filtering = filter.Length > 0;
        var roots = new List<ExplorerNode>();

        foreach (var group in weapons.GroupBy(weapon => weapon.Category.Length == 0 ? "(other)" : weapon.Category))
        {
            var category = new ExplorerNode { Kind = ExplorerNodeKind.Category, Label = group.Key, IsExpanded = filtering };
            foreach (var weapon in group)
            {
                var weaponMatches = !filtering || Matches(weapon.Id) || Matches(group.Key);
                var clips = weapon.ClipPaths
                    .Select(path => (Path: path, Name: ClipName(path)))
                    .Where(clip => weaponMatches || Matches(clip.Name))
                    .ToList();

                // Third person (0.26): the clips other players see, under their own group. The +non_additive twins
                // stay out: no graph plays them.
                var world = weapon.WorldClipPaths
                    .Where(path => !Path.GetFileName(path).Contains('+', StringComparison.Ordinal))
                    .Select(path => (Path: path, Id: ClipName(path), Name: Path.GetFileName(path).Replace(WeaponCatalog.ClipExtension, "")))
                    .Where(clip => weaponMatches || Matches(clip.Name))
                    .ToList();
                if (clips.Count == 0 && world.Count == 0)
                {
                    continue;
                }

                var weaponNode = new ExplorerNode
                {
                    Kind = ExplorerNodeKind.Weapon,
                    Label = weapon.Id,
                    Detail = weapon.ClipPaths.Count.ToString(CultureInfo.InvariantCulture),
                    Weapon = weapon,
                    IsExpanded = filtering && !weaponMatches,
                };
                if (weaponMatches)
                {
                    var skeleton = new ExplorerNode { Kind = ExplorerNodeKind.Skeleton, Label = "Skeleton", Detail = "", Weapon = weapon };
                    skeleton.Children.Add(new ExplorerNode { Kind = ExplorerNodeKind.Bone, Label = "(opening…)", Weapon = weapon });
                    weaponNode.Children.Add(skeleton);
                    weaponNode.Children.Add(new ExplorerNode { Kind = ExplorerNodeKind.Graphs, Label = "Graphs", Weapon = weapon });
                }

                foreach (var clip in clips)
                {
                    var edited = isEdited?.Invoke(weapon, clip.Name) == true;
                    weaponNode.Children.Add(new ExplorerNode { Kind = ExplorerNodeKind.Clip, Label = clip.Name, Detail = edited ? "edited" : "", Weapon = weapon, ClipPath = clip.Path });
                }

                if (world.Count > 0)
                {
                    var thirdPerson = new ExplorerNode
                    {
                        Kind = ExplorerNodeKind.ThirdPerson,
                        Label = "Third person",
                        Weapon = weapon,
                        IsExpanded = filtering && !weaponMatches,
                    };
                    foreach (var clip in world)
                    {
                        var edited = isEdited?.Invoke(weapon, clip.Id) == true;
                        thirdPerson.Children.Add(new ExplorerNode { Kind = ExplorerNodeKind.Clip, Label = clip.Name, Detail = edited ? "edited" : "", Weapon = weapon, ClipPath = clip.Path });
                    }

                    weaponNode.Children.Add(thirdPerson);
                }

                category.Children.Add(weaponNode);
            }

            if (category.Children.Count > 0)
            {
                roots.Add(category);
            }
        }

        return roots;
    }

    /// <summary>The clip's id: its file name, "world_" first for a third-person clip (<see cref="Core.Blender.RigPackage.ClipId"/>).</summary>
    public static string ClipName(string resourcePath) => Core.Blender.RigPackage.ClipId(resourcePath);

    private void Set(ref bool field, bool value, string name)
    {
        if (field != value)
        {
            field = value;
            Changed(name);
        }
    }

    private void Changed(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>A titled group of rows in the Property Browser.</summary>
public sealed record PropertyGroup(string Name, IReadOnlyList<PropertyRow> Rows);

/// <summary>One Property Browser row: read-only text, or a checkbox labelled with the value when <see cref="Toggled"/> is set.</summary>
public sealed record PropertyRow(string Name, string Value)
{
    public bool IsChecked { get; init; }

    /// <summary>Called with the new state when the user ticks or unticks the row's checkbox.</summary>
    public Action<bool>? Toggled { get; init; }

    public bool HasToggle => Toggled is not null;
}

/// <summary>One event of the selected clip, formatted for display.</summary>
public sealed record EventRow(string Frame, string Seconds, string Kind, string Name)
{
    public static EventRow From(ClipEvent clipEvent, ClipInfo clip)
    {
        var name = clipEvent.SecondaryId.Length > 0 ? $"{clipEvent.Name}  [{clipEvent.SecondaryId}]" : clipEvent.Name;
        return new EventRow(
            clipEvent.StartFrame(clip).ToString("0.##", CultureInfo.InvariantCulture),
            clipEvent.StartSeconds(clip).ToString("0.000 s", CultureInfo.InvariantCulture),
            clipEvent.Kind == ClipEventKind.Other ? clipEvent.ClassName : clipEvent.Kind.ToString(),
            name);
    }
}

/// <summary>One event in the Anim Events tab, across all of a weapon's clips.</summary>
/// <param name="State">"edited" when it differs from CS2's clip; empty otherwise.</param>
/// <param name="Event">The event as the clip plays it now (the sandbox export when there is one).</param>
/// <param name="Original">The same event in the clip's template (CS2's, or the addon's).</param>
/// <summary>A clip in the Anim Events overview (a weapon picked without a clip).</summary>
public sealed record EventClipRow(string Clip, string ClipPath, string Events, string State);

/// <summary>A graph that plays the selected weapon's clips.</summary>

/// <summary>One bone track of a pose stream.</summary>
public sealed record TrackRow(string Stream, string Index, string Bone, string Parent, string Channels, bool Animated);

/// <summary>One exported clip of the selected weapon.</summary>
public sealed record ExportRow(string Clip, string Time, string Changes, string File)
{
    public static ExportRow From(WeaponLab.Core.Export.ClipExportReport report)
    {
        return new ExportRow(report.ClipId, report.ExportedUtc.ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Summary(report), report.SandboxOutput ?? "");
    }

    public static string Summary(WeaponLab.Core.Export.ClipExportReport report)
    {
        if (report.IdenticalToTemplate)
        {
            return "no edits (original clip)";
        }

        var parts = report.ChangedBones.Select(pair => $"{pair.Key}: {string.Join(", ", pair.Value)}").ToList();
        var events = report.ChangedEvents ?? [];
        if (events.Count > 0)
        {
            parts.Add($"events: {string.Join(", ", events)}");
        }

        return string.Join(" · ", parts);
    }
}

/// <summary>A group heading in the VData property list, e.g. "Damage".</summary>
public sealed record VDataGroupRow(string Name)
{
    /// <summary>The block the group belongs to (the list shows every block, 0.22).</summary>
    public string Block { get; init; } = "";
}

/// <summary>A block heading in the VData list: its name, the weapon it is and how many of its values are changed or unsaved.</summary>
public sealed record VDataBlockRow(string Block, string Label, string Summary, WeaponState State)
{
    public bool HasStatus => State != WeaponState.Untouched;
}

/// <summary>
/// One vdata value: friendly name (raw key and description on hover), the
/// CS2 value, the addon's current value and the profile's value, editable.
/// Setting <see cref="Value"/> validates and stores it through the callback.
/// </summary>
public sealed class VDataEditRow : INotifyPropertyChanged
{
    private readonly Func<VDataEditRow, string, string?> _commit;
    private string _value;
    private string _error = "";

    /// <param name="classFrom">The donor block when the profile bases this block on another weapon.</param>
    /// <param name="choices">Values to offer in the selector; null shows a plain text box.</param>
    public VDataEditRow(VDataRow row, VDataTerm term, string? addonValue, Func<VDataEditRow, string, string?> commit, string? classFrom = null, IReadOnlyList<string>? choices = null)
    {
        Choices = choices;
        Row = row;
        Term = term;
        AddonValue = addonValue ?? "—";
        _commit = commit;
        _value = row.RemovedByClass ? "(removed)" : row.EffectiveValue;
        ClassFrom = classFrom;
    }

    public string? ClassFrom { get; }

    /// <summary>The vdata block this value belongs to (the list shows every block, 0.22).</summary>
    public string Block { get; init; } = "";

    public IReadOnlyList<string>? Choices { get; }

    public bool HasChoices => Choices is not null && IsEditable;

    public bool HasTextBox => Choices is null || !IsEditable;

    /// <summary>The selector's picked item; typing goes through <see cref="Value"/> instead.</summary>
    public string? PickedValue
    {
        get => Choices?.Contains(_value) == true ? _value : null;
        set
        {
            if (value is not null)
            {
                Value = value;
            }
        }
    }

    /// <summary>The value single edits start from: the class change's, else CS2's.</summary>
    public string Baseline => Row.Baseline;

    public bool IsEditable => !Row.RemovedByClass;

    public event PropertyChangedEventHandler? PropertyChanged;

    public VDataRow Row { get; }
    public VDataTerm Term { get; }
    public string Path => Row.Path;
    public string Name => Term.Name;
    public string StockValue => Row.StockValue;
    public string AddonValue { get; }

    public bool AddonDiffers => AddonValue != "—" && AddonValue != Row.StockValue;

    /// <summary>
    /// Yellow while edited and not saved to the addon yet; green when the
    /// addon has a value that differs from the CS2 column (the profile's or
    /// the addon's own); plain when it is CS2's (user decision 2026-10-30).
    /// </summary>
    public WeaponState ValueState =>
        Row.RemovedByClass ? WeaponState.Untouched
        : !VDataValues.Same(Row.AddonValue ?? Row.StockValue, _value) ? WeaponState.NeedsApply
        : !VDataValues.Same(_value, Row.StockValue) ? WeaponState.Applied
        : WeaponState.Untouched;

    public string Tip => $"{Row.Path}\n{Term.Description}"
        + (ValueState == WeaponState.NeedsApply ? $"\nThe addon has {AddonValue} now; save to write {_value}." : "");

    public string Source =>
        Row.RemovedByClass ? $"Removed: {ClassFrom} has none"
        : Row.ClassValue is not null ? $"From {ClassFrom} (class)"
        : Row.AddonOriginal is not null && !VDataValues.Same(Row.AddonOriginal, Row.StockValue) ? "Addon's own value"
        : Row.Source == VDataSource.Inherited ? $"Inherited ({Row.InheritedFrom})" : "This weapon";

    /// <summary>The profile sets this value (a single edit or a class change).</summary>
    public bool IsModified => _value != Row.Baseline || Row.ClassValue is not null;

    /// <summary>The ↺ button: the value differs from CS2's and can be edited.</summary>
    public bool CanResetToStock => IsEditable && !VDataValues.Same(_value, Row.StockValue);

    public string ResetTip => $"Back to CS2's value: {Row.StockValue}";

    /// <summary>Sets the value back to CS2's; like any edit it is yellow until saved.</summary>
    public void ResetToStock() => Value = Row.StockValue;

    public string Error
    {
        get => _error;
        private set
        {
            if (_error != value)
            {
                _error = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Error)));
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (value == _value)
            {
                return;
            }

            var accepted = _commit(this, value);
            if (accepted is null)
            {
                return;
            }

            _value = accepted;
            Error = "";
            foreach (var name in new[] { nameof(Value), nameof(PickedValue), nameof(IsModified), nameof(AddonDiffers), nameof(ValueState), nameof(CanResetToStock), nameof(Tip) })
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
    }

    public void ShowError(string message) => Error = message;
}

public sealed record OverrideRow(string Block, string Path, string Value)
{
    public string Label => $"{Block} · {VDataGlossary.Describe(Path).Name}";
}

/// <summary>One line of a delivery review.</summary>
public sealed record PlanRow(string Kind, string Text, bool IsWarning)
{
    /// <summary>What the row is about; its Show button opens it.</summary>
    public WeaponLab.Core.Checks.CheckTarget? Target { get; init; }

    public bool CanShow => Target is not null;
    public string ShowTip => Target?.Describe() ?? "";
}

/// <summary>What a delivery does to one entry, each with its own colour in the tree view.</summary>
public enum DeliveryAction
{
    Group,
    New,
    Updates,
    Patched,
    Replaces,
    Restores,
    Removes,
    Warning,
    Hooked,
}

/// <summary>A node of the delivery tree view: addon → weapons → clips and graphs, vdata, warnings.</summary>
public sealed class DeliveryTreeNode
{
    private static readonly Dictionary<DeliveryAction, string> Colours = new()
    {
        [DeliveryAction.Group] = "#D8D8D8",
        [DeliveryAction.New] = "#7FB8FF",
        [DeliveryAction.Updates] = "#9AA4FF",
        [DeliveryAction.Patched] = "#B48CFF",
        [DeliveryAction.Replaces] = "#E3A04F",
        [DeliveryAction.Restores] = "#6BCB77",
        [DeliveryAction.Removes] = "#E07070",
        [DeliveryAction.Warning] = "#E3C14F",
        [DeliveryAction.Hooked] = "#6BCB77",
    };

    public required string Label { get; init; }
    public string Detail { get; init; } = "";
    public DeliveryAction Action { get; init; } = DeliveryAction.Group;
    public bool IsExpanded { get; init; } = true;
    public List<DeliveryTreeNode> Children { get; init; } = [];

    public string Tag => Action == DeliveryAction.Group ? "" : Action.ToString().ToLowerInvariant();

    private bool _included = true;

    /// <summary>Shows a commit checkbox: whether this weapon or clip goes into the addon.</summary>
    public bool CanToggle { get; init; }

    /// <summary>The checkbox's starting state.</summary>
    public bool Included { init => _included = value; }

    /// <summary>Bound to the checkbox; a change after creation calls <see cref="Toggled"/>.</summary>
    public bool IsIncluded
    {
        get => _included;
        set
        {
            if (_included != value)
            {
                _included = value;
                Toggled?.Invoke(value);
            }
        }
    }

    public Action<bool>? Toggled { get; init; }

    /// <summary>What breaks when this item is not committed: the row turns red and this is its tooltip.</summary>
    public string? Problem { get; init; }

    /// <summary>The weapon a weapon row stands for (its right-click menu resets it).</summary>
    public string? WeaponId { get; init; }

    /// <summary>A resource path the row's right-click menu copies (no <c>_c</c>), e.g. a pushed file or the skeleton a warning names.</summary>
    public string? AssetPath { get; init; }

    public bool IsWeaponRow => WeaponId is not null;
    public bool HasAssetPath => AssetPath is not null;
    public bool HasMenu => IsWeaponRow || HasAssetPath;

    /// <summary>What a warning or error row is about; its Show button opens it.</summary>
    public WeaponLab.Core.Checks.CheckTarget? Target { get; init; }

    public bool CanShow => Target is not null;
    public string ShowTip => Target?.Describe() ?? "";

    public string Tip => Problem ?? Detail;

    public Avalonia.Media.IBrush Brush => Problem is not null
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#E07070"))
        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Colours[Action]));

    public static IReadOnlyList<(DeliveryAction Action, string Meaning)> Legend { get; } =
    [
        (DeliveryAction.New, "new file"),
        (DeliveryAction.Updates, "updates Weapon Lab's file"),
        (DeliveryAction.Patched, "patches the addon's own graph"),
        (DeliveryAction.Replaces, "replaces an addon file"),
        (DeliveryAction.Restores, "puts the addon's own back"),
        (DeliveryAction.Removes, "removes"),
        (DeliveryAction.Warning, "warning"),
    ];

    public static Avalonia.Media.IBrush BrushFor(DeliveryAction action) => new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(Colours[action]));
}

public sealed record DeliveryRow(string Title, string Detail)
{
    public static DeliveryRow From(WeaponLab.Core.Delivery.AddonDelivery.DeliveryRecord record)
    {
        var status = record.RolledBack ? (record.Error is null ? "undone" : "failed, rolled back") : "applied";
        var profile = record.Profile is null ? "" : $" · profile '{record.Profile}'";
        return new DeliveryRow(
            $"{record.Addon} · {status}",
            $"{record.DeliveredUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}{profile} · {record.Files} file(s){(record.Error is null ? "" : " · " + record.Error)}");
    }
}

public sealed record HistoryEntry(string Time, string Message)
{
    public static HistoryEntry Now(string message) => new(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture), message);
}
