using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.App;

/// <summary>
/// The VData tab (0.22): every block of weapons.vdata in one scroll with
/// friendly names, CS2's value, the addon's value (its source file) and the
/// profile's value; a selector jumps to a block. The block at the top of the
/// list is the one in view: the viewport shows its model playing its slot's
/// clips, and the info panel on the right sums up its changes. Saving writes
/// the addon's scripts/weapons.vdata and compiles it.
/// </summary>
public sealed partial class MainWindow
{
    private VDataFile? _addonVData;
    private VDataFile? _addonOriginalVData;
    private VDataChoices? _vdataChoices;
    private string? _vdataChoicesFor;
    private string? _addonVDataFor;

    /// <summary>The block in view: the first one at the top of the list.</summary>
    private VDataBlock? _vdataBlock;

    /// <summary>Block name → the index of its heading row in the list.</summary>
    private readonly Dictionary<string, int> _vdataBlockRows = new(StringComparer.Ordinal);
    private bool _vdataScrollHooked;

    private void RefreshVData()
    {
        if (_connection is not { } connection)
        {
            VDataTitle.Text = "Connect to CS2 first";
            return;
        }

        HookVDataScroll();
        LoadAddonVData();
        var stock = connection.ReadVData();
        _updatingControls = true;
        VDataBlockCombo.ItemsSource = stock.Blocks.Select(block => BlockLabel(block)).ToList();
        _updatingControls = false;

        // Open on the selected weapon's block the first time (or after the outliner picked another weapon).
        var wanted = _weapon is null ? null : BlocksFor(_weapon).FirstOrDefault();
        var keep = _vdataBlock?.Name;
        ApplyVDataPropertyFilter();
        var show = keep ?? wanted ?? stock.Blocks.FirstOrDefault(block => WeaponOfBlock(block.Name) is not null)?.Name;
        if (show is not null)
        {
            JumpToVDataBlock(show);
        }

        RefreshOverrideList();
        RefreshAddonVDataStatus();
    }

    private Dictionary<string, WeaponLab.Core.Weapons.WeaponEntry>? _blockWeapons;

    /// <summary>The weapon (clip folder) a block belongs to, by its skeleton; null for base blocks and blocks no folder uses.</summary>
    private WeaponLab.Core.Weapons.WeaponEntry? WeaponOfBlock(string block)
    {
        if (_blockWeapons is null && _connection is { } connection)
        {
            _blockWeapons = new Dictionary<string, WeaponLab.Core.Weapons.WeaponEntry>(StringComparer.Ordinal);
            foreach (var weapon in connection.Weapons)
            {
                foreach (var name in BlocksFor(weapon))
                {
                    _blockWeapons.TryAdd(name, weapon);
                }
            }
        }

        return _blockWeapons?.GetValueOrDefault(block);
    }

    private string WeaponLabelOf(string block) => WeaponOfBlock(block) is { } weapon ? WeaponLab.Core.Blender.WeaponLabels.For(weapon.Id) : "";

    /// <summary>"weapon_ak47 · AK-47": the selector's line for a block.</summary>
    private string BlockLabel(VDataBlock block) => WeaponLabelOf(block.Name) is { Length: > 0 } label ? $"{block.Name}  ·  {label}" : block.Name;

    private static string BlockOfLabel(string label) => label.Split("  ·  ")[0];

    private void OnVDataBlockSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || VDataBlockCombo.SelectedItem is not string label)
        {
            return;
        }

        JumpToVDataBlock(BlockOfLabel(label));
    }

    /// <summary>Scrolls the list so the block's heading is at the top, and makes it the block in view.</summary>
    private void JumpToVDataBlock(string block)
    {
        if (_connection?.ReadVData()[block] is { } found)
        {
            ShowVDataBlock(found);
        }

        if (!_vdataBlockRows.TryGetValue(block, out var index) || VDataRowList.ItemsSource is not IList<object> items)
        {
            return;
        }

        // Past the end first, then back to the heading, so it lands at the top rather than the bottom.
        VDataRowList.ScrollIntoView(Math.Min(items.Count - 1, index + 40));
        VDataRowList.ScrollIntoView(index);
        Dispatcher.UIThread.Post(() =>
        {
            if (VDataRowList.ContainerFromIndex(index) is { } container && VDataRowList.Scroll is ScrollViewer scroller
                && container.TranslatePoint(new Avalonia.Point(0, 0), scroller) is { } point)
            {
                scroller.Offset = scroller.Offset.WithY(Math.Max(0, scroller.Offset.Y + point.Y));
            }
        }, DispatcherPriority.Background);
    }

    private void HookVDataScroll()
    {
        if (_vdataScrollHooked || VDataRowList.Scroll is not ScrollViewer scroller)
        {
            return;
        }

        _vdataScrollHooked = true;
        scroller.ScrollChanged += (_, _) => FollowVDataScroll();
    }

    /// <summary>The block whose rows are at the top of the list becomes the block in view.</summary>
    private void FollowVDataScroll()
    {
        if (_connection is not { } connection || VDataRowList.Scroll is not ScrollViewer scroller)
        {
            return;
        }

        string? top = null;
        var best = double.MaxValue;
        foreach (var container in VDataRowList.GetRealizedContainers())
        {
            if (container.TranslatePoint(new Avalonia.Point(0, container.Bounds.Height), scroller) is not { } bottom || bottom.Y <= 0 || bottom.Y >= best)
            {
                continue;
            }

            var block = container.DataContext switch
            {
                VDataBlockRow row => row.Block,
                VDataGroupRow group => group.Block,
                VDataEditRow edit => edit.Block,
                _ => null,
            };
            if (block is not null)
            {
                best = bottom.Y;
                top = block;
            }
        }

        if (top is not null && top != _vdataBlock?.Name && connection.ReadVData()[top] is { } found)
        {
            ShowVDataBlock(found);
            _updatingControls = true;
            VDataBlockCombo.SelectedItem = BlockLabel(found);
            _updatingControls = false;
        }
    }

    /// <summary>Makes a block the one in view: the info panel and the viewport follow it.</summary>
    private void ShowVDataBlock(VDataBlock block)
    {
        if (_connection is null)
        {
            return;
        }

        _vdataBlock = block;
        var stock = _connection.ReadVData();
        _updatingControls = true;
        VDataBlockCombo.SelectedItem = BlockLabel(block);
        _updatingControls = false;
        VDataTitle.Text = BlockLabel(block);
        var chain = string.Join(" → ", stock.BaseChain(block).Select(item => item.Name).Prepend(block.Name));
        VDataSubtitle.Text = $"Inherits {chain}.";
        VDataResetBlockButton.IsEnabled = true;
        RefreshVDataClass();
        RefreshVDataInfo();
        RefreshVDataViewport();
        RefreshAddonVDataStatus();
    }

    private void OnVDataPropertyFilterChanged(object? sender, TextChangedEventArgs e) => ApplyVDataPropertyFilter();

    /// <summary>Fills the list with every block (headings, groups, values), filtered by the text box.</summary>
    private void ApplyVDataPropertyFilter()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        var filter = VDataPropertyFilter.Text?.Trim() ?? "";
        var stock = connection.ReadVData();
        var choices = VDataChoicesFor(connection);
        var items = new List<object>();
        _vdataBlockRows.Clear();
        foreach (var block in stock.Blocks)
        {
            var classSwap = _profile.VDataClasses.GetValueOrDefault(block.Name);
            var all = VDataView.Rows(stock, block, _addonVData, _vdataOverrides.For(block.Name), classSwap, _addonOriginalVData, WrittenByLab(block.Name))
                .Select(row => (Row: row, Term: VDataGlossary.Describe(row.Path)))
                .ToList();
            var shown = all
                .Where(item => filter.Length == 0
                    || block.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.Row.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.Term.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    || item.Term.Group.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => item.Term.Group)
                .OrderBy(group => IndexOf(VDataGlossary.GroupOrder, group.Key))
                .ToList();
            if (shown.Count == 0)
            {
                continue;
            }

            var (pending, changed) = VDataCounts(all.Select(item => item.Row));
            var summary = string.Join(", ", new[] { pending > 0 ? $"{pending} not saved" : null, changed > 0 ? $"{changed} changed from CS2" : null }.OfType<string>());
            _vdataBlockRows[block.Name] = items.Count;
            items.Add(new VDataBlockRow(block.Name, WeaponLabelOf(block.Name), summary,
                pending > 0 ? WeaponState.NeedsApply : changed > 0 ? WeaponState.Applied : WeaponState.Untouched));
            foreach (var group in shown)
            {
                items.Add(new VDataGroupRow(group.Key) { Block = block.Name });
                items.AddRange(group.Select(item => new VDataEditRow(item.Row, item.Term, item.Row.AddonValue, CommitVDataEdit, classSwap?.From, choices.For(item.Row.Path, item.Row.Type)) { Block = block.Name }));
            }
        }

        VDataRowList.ItemsSource = items;
    }

    /// <summary>A block's values not saved to the addon yet, and those the addon changed from CS2 (as the rows colour them).</summary>
    private static (int Pending, int Changed) VDataCounts(IEnumerable<VDataRow> rows)
    {
        var list = rows.Where(row => !row.RemovedByClass).ToList();
        var pending = list.Count(row => !VDataValues.Same(row.AddonValue ?? row.StockValue, row.EffectiveValue));
        var changed = list.Count(row => VDataValues.Same(row.AddonValue ?? row.StockValue, row.EffectiveValue) && !VDataValues.Same(row.EffectiveValue, row.StockValue));
        return (pending, changed);
    }

    /// <summary>Selector values from the game's and the addon's vdata, plus the addon's own weapon models; rebuilt when the addon changes.</summary>
    private VDataChoices VDataChoicesFor(Core.Game.GameConnection connection)
    {
        if (_vdataChoices is { } cached && _vdataChoicesFor == _addonVDataFor)
        {
            return cached;
        }

        var models = _inventory.Assets.Where(asset => asset.Kind == Core.Addons.AddonAssetKind.Model).Select(asset => asset.Path);
        _vdataChoicesFor = _addonVDataFor;
        return _vdataChoices = VDataChoices.Build(connection.ReadVData(), _addonVData,
            new Dictionary<string, IEnumerable<string>> { [VDataBlock.WorldModelKey] = models.ToList() });
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (var index = 0; index < list.Count; index++)
        {
            if (list[index] == value)
            {
                return index;
            }
        }

        return list.Count;
    }

    /// <summary>Validates and stores an edit in the profile; returns the canonical value, or null when rejected.</summary>
    private string? CommitVDataEdit(VDataEditRow row, string input)
    {
        if (_connection?.ReadVData()[row.Block] is not { } block)
        {
            return null;
        }

        var (value, error) = VDataView.Normalize(row.Row.Type, input);
        if (value is null)
        {
            row.ShowError(error ?? "Invalid value.");
            return null;
        }

        _vdataOverrides.Set(block.Name, row.Path, value, row.Baseline);
        if (row.Path == VDataBlock.WorldModelKey && _connection is { } connection && Target() is { } target)
        {
            // The model must be set and exist (the vdata skeleton does not matter; verified in game 2026-10-25).
            foreach (var warning in Core.Addons.AddonChecks.Models(connection, target, _profile).Where(item => item.Subject == block.Name))
            {
                Log($"Warning, {warning.Subject}: {warning.Message}");
            }
        }

        Log(value == row.Baseline
            ? $"VData: {row.Name} on {block.Name} back to {(row.ClassFrom is not null ? row.ClassFrom + "'s" : row.Row.AddonOriginal is not null ? "the addon's own" : "CS2's")} value ({value})."
            : $"VData: {row.Name} on {block.Name} = {value} (CS2 {row.StockValue}). Save to addon to write it.");
        RefreshOverrideList();
        RefreshAddonVDataStatus();
        RefreshVDataInfo();
        if (row.Path is VDataBlock.WorldModelKey or WeaponLab.Core.Weapons.WeaponSlots.AnimClassKey && block.Name == _vdataBlock?.Name)
        {
            RefreshVDataViewport(); // The model or the slot it plays changed.
        }

        _ = RefreshStatusAsync();
        return value;
    }

    private void RefreshOverrideList()
    {
        var rows = _vdataOverrides.All().Select(item => new OverrideRow(item.Block, item.Path, item.Value)).ToList();
        VDataOverrideList.ItemsSource = rows;
        VDataOverridesHeader.Text = rows.Count == 0 ? "Profile vdata edits (none)" : $"Profile vdata edits ({rows.Count})";
    }

    /// <summary>When the addon's compiled vdata was read: compiling outside Weapon Lab is picked up (it was cached per session).</summary>
    private DateTime _addonVDataStamp;

    /// <summary>The paths Weapon Lab wrote into the addon's vdata, per block (the delivery manifest).</summary>
    private IReadOnlyDictionary<string, SortedDictionary<string, string>> _vdataWrittenByLab = new Dictionary<string, SortedDictionary<string, string>>();

    private IReadOnlyCollection<string> WrittenByLab(string block) =>
        _vdataWrittenByLab.TryGetValue(block, out var values) ? values.Keys : [];

    private void LoadAddonVData()
    {
        var target = Target();
        var stamp = target is null ? default : AddonVData.Stamp(target); // Saving or compiling outside Weapon Lab is picked up.
        if (target?.Name == _addonVDataFor && _addonVData is not null && stamp == _addonVDataStamp)
        {
            return;
        }

        _addonVData = null;
        _addonOriginalVData = null;
        _vdataChoices = null;
        _addonVDataFor = target?.Name;
        _addonVDataStamp = stamp;
        _vdataWrittenByLab = new Dictionary<string, SortedDictionary<string, string>>();
        if (target is not null)
        {
            // What the addon had before Weapon Lab changed it: keys Weapon Lab wrote start from these values;
            // every other key starts from the addon's current one (an edit made outside Weapon Lab is the addon's own).
            var manifest = TryReadManifest(target);
            _vdataWrittenByLab = manifest?.VData ?? _vdataWrittenByLab;
            _addonOriginalVData = DeliveryPlanner.OriginalVData(AddonOriginals.Load(_sandboxes.WorkspacePath, target.Name), target, manifest);
        }

        if (target is not null)
        {
            try
            {
                // The source is the addon's truth (user decision 2026-11-07): the compiled values with the source's on top.
                _addonVData = AddonVData.Read(target);
            }
            catch (InvalidDataException error)
            {
                Log($"Cannot read {target.Name}'s weapons.vdata: {error.Message}");
            }
        }
    }

    private void RefreshAddonVDataStatus()
    {
        var target = Target();
        VDataSaveButton.IsEnabled = target is not null && _connection is not null;
        VDataCompileButton.IsEnabled = target is not null && _connection?.Install.HasWorkshopTools == true;
        VDataRefreshButton.IsEnabled = VDataCompileButton.IsEnabled;
        if (target is null)
        {
            VDataAddonStatus.Text = "Pick a target addon in the status bar.";
            return;
        }

        if (_addonVData is null)
        {
            VDataAddonStatus.Text = $"{target.Name} has no weapons.vdata yet. Saving creates one from the current CS2 data with this profile's values.";
            VDataSaveButton.Content = "Create source in addon";
            VDataCompileButton.Content = "Create and compile";
            return;
        }

        VDataSaveButton.Content = "Save to addon source";
        VDataCompileButton.Content = "Save and compile";
        var stale = _addonVData.Blocks.Any(block => block.Name.EndsWith("_prefab", StringComparison.Ordinal) || block.Name == "statted_item_base");
        var pending = _vdataOverrides.All().Count(item => _addonVData[item.Block]?.Value(item.Path) is not { } actual || actual != item.Value);
        var text = $"{target.Name}'s weapons.vdata has {_addonVData.Blocks.Count} blocks. ";
        text += pending == 0 ? "It holds all of this profile's values." : $"{pending} profile value(s) are not in it yet.";
        if (stale)
        {
            text += " It uses an old format (with _prefab blocks) the current game removed; 'Refresh from CS2' rebuilds it from current data.";
        }

        // The colours follow the source; the game runs the compiled file.
        if (AddonVData.SourceIsNewer(target))
        {
            text += " Its source was saved after the last compile: the colours show the source, but the game still runs the older compiled values until it is compiled ('Save and compile').";
        }

        VDataAddonStatus.Text = text;
    }

    private async void OnVDataSaveClicked(object? sender, RoutedEventArgs e)
    {
        await SaveVDataAsync(rebase: _addonVData is null && Target() is { } target && !File.Exists(target.VDataSourcePath), compile: false);
    }

    private async void OnVDataCompileClicked(object? sender, RoutedEventArgs e)
    {
        await SaveVDataAsync(rebase: _addonVData is null && Target() is { } target && !File.Exists(target.VDataSourcePath), compile: true);
    }

    private async void OnVDataRefreshClicked(object? sender, RoutedEventArgs e)
    {
        if (Target() is not { } target || !await Dialogs.ConfirmAsync(this, "Refresh vdata from CS2",
                $"This replaces {target.Name}'s weapons.vdata with the current CS2 data plus the values in profile '{_profile.Name}'.\n\n" +
                "Anything else the addon's weapons.vdata contains (other changed values, custom weapons) will no longer be in it. " +
                "Both the source and compiled files are backed up first and can be restored with Undo in the Deliver tab.\n\n" +
                "Do you want to refresh it?", "Refresh", destructive: true))
        {
            return;
        }

        await SaveVDataAsync(rebase: true);
    }

    private async Task SaveVDataAsync(bool rebase, bool compile = true)
    {
        if (_connection is not { } connection || Target() is not { } target)
        {
            return;
        }

        VDataSaveButton.IsEnabled = false;
        VDataCompileButton.IsEnabled = false;
        VDataRefreshButton.IsEnabled = false;
        try
        {
            var plan = await Task.Run(() => DeliveryPlanner.Plan(_sandboxes, connection, target, _profile, DeliveryScope.VDataOnly, rebase, compile));
            if (!plan.Files.Any(file => file.ManifestKey == AddonManifest.VDataSourceKey))
            {
                Log($"Nothing to save: {target.Name}'s weapons.vdata already matches profile '{_profile.Name}'.");
                return;
            }

            if (!await ConfirmRiskyDeliveryAsync(plan))
            {
                Log($"Saving vdata to {target.Name} cancelled; nothing was written.");
                return;
            }

            var result = await AddonDelivery.DeliverAsync(plan, connection.Install, _sandboxes.WorkspacePath, new Progress<string>(SetStatus));
            Log(compile
                ? $"Saved vdata to {target.Name} and compiled it ({plan.VDataEdits} value(s), {plan.VDataResets} reset). Backup: {result.BackupFolder}"
                : $"Saved {plan.VDataEdits} value(s) and {plan.VDataResets} reset(s) to {target.Name}'s weapons.vdata source; not compiled yet. Backup: {result.BackupFolder}");
            _addonVDataFor = null;
            LoadAddonVData();
            ApplyVDataPropertyFilter();
            await RefreshStatusAsync();
        }
        catch (DeliveryException error)
        {
            Log(error.Message);
            await SaveCompilerLogAsync(error.Log);
            await Dialogs.ShowAsync(this, "Saving vdata failed", error.Message + "\n\nNothing was changed: the addon's files were put back.");
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log($"Could not save vdata: {error.Message}");
            await Dialogs.ShowAsync(this, "Saving vdata failed", error.Message);
        }
        finally
        {
            RefreshAddonVDataStatus();
        }
    }

    /// <summary>↺ on a row: the value goes back to CS2's (an edit like any other, yellow until saved).</summary>
    private void OnVDataResetValueClicked(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.DataContext is VDataEditRow row)
        {
            row.ResetToStock();
        }
    }

    /// <summary>
    /// Reset block…: discard this block's unsaved changes (the addon keeps its
    /// saved values), or queue CS2's values for every key the addon changed.
    /// Either way nothing is written until you save.
    /// </summary>
    private async void OnVDataResetBlockClicked(object? sender, RoutedEventArgs e)
    {
        if (_vdataBlock is not { } block || _connection is not { } connection)
        {
            return;
        }

        var count = _vdataOverrides.For(block.Name).Count;
        var hasClass = _profile.VDataClasses.ContainsKey(block.Name);
        var choice = await Dialogs.ChooseAsync(this, "Reset block",
            $"Reset {block.Name}.\n\n" +
            $"Discard unsaved changes: drops the {count} unsaved value(s){(hasClass ? " and the class change" : "")}; the addon keeps the values it has saved.\n" +
            "Back to CS2's values: queues CS2's value for every key the addon's saved vdata changed, as unsaved changes you can review.\n\n" +
            "Nothing is written to the addon until you save.",
            ["Discard unsaved changes", "Back to CS2's values"]);
        if (choice < 0)
        {
            return;
        }

        _vdataOverrides.ClearBlock(block.Name);
        _profile.VDataClasses.Remove(block.Name);
        if (choice == 1 && connection.ReadVData()[block.Name] is { } stock && _addonVData?[block.Name] is { } saved)
        {
            var differing = stock.Properties.Where(property => saved.Value(property.Path) is { } value && value != property.Value).ToList();
            if (differing.Count > 0)
            {
                _profile.VData[block.Name] = new SortedDictionary<string, string>(differing.ToDictionary(property => property.Path, property => property.Value), StringComparer.Ordinal);
            }

            Log($"VData: {block.Name}: CS2's value queued for {differing.Count} key(s) the addon changed. Save to write them.");
        }
        else
        {
            Log($"VData: {block.Name}: unsaved changes discarded.");
        }

        SaveProfile();
        await AfterVDataProfileChangeAsync();
    }

    private async Task AfterVDataProfileChangeAsync()
    {
        RefreshOverrideList();
        RefreshVDataClass();
        ApplyVDataPropertyFilter();
        RefreshAddonVDataStatus();
        await RefreshStatusAsync();
    }

    // ---- Weapon class (base on another weapon) ----

    private void RefreshVDataClass()
    {
        VDataClassButton.IsEnabled = _vdataBlock is not null;
        if (_vdataBlock is null)
        {
            VDataClassText.Text = "Pick a block.";
            return;
        }

        var stock = _connection?.ReadVData();
        var own = stock?[_vdataBlock.Name];
        var text = $"Entity class {own?.Class ?? "none"}, inherits from {own?.Base ?? "nothing"}, animates as {own?.Value("m_szAnimClass") ?? "?"}.";
        if (_profile.VDataClasses.TryGetValue(_vdataBlock.Name, out var swap))
        {
            text += $"\nProfile: {swap.Summary()}.";
            VDataClassButton.Content = "Change class…";
        }
        else
        {
            VDataClassButton.Content = "Base on another weapon…";
        }

        if (_addonVData?[_vdataBlock.Name] is { } addon && own is not null
            && (addon.Base != own.Base || addon.Class != own.Class || addon.Value("m_szAnimClass") != own.Value("m_szAnimClass")))
        {
            text += $"\nAddon now: class {addon.Class}, inherits from {addon.Base}, animates as {addon.Value("m_szAnimClass")}.";
        }

        VDataClassText.Text = text;
    }

    private async void OnVDataClassClicked(object? sender, RoutedEventArgs e)
    {
        if (_connection is not { } connection || _vdataBlock is not { } block)
        {
            return;
        }

        var donors = connection.ReadVData().Blocks
            .Where(item => item.IsWeapon && item.Name != block.Name)
            .Select(item => (item.Name, $"{item.Name}   ({item.Value("m_WeaponType")?.Replace("WEAPONTYPE_", "").ToLowerInvariant() ?? "?"})"))
            .OrderBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
        var (choice, swap) = await Dialogs.ClassSwapAsync(this, block.Name, donors, _profile.VDataClasses.GetValueOrDefault(block.Name));
        switch (choice)
        {
            case Dialogs.ClassChoice.Apply when swap is not null:
                _profile.VDataClasses[block.Name] = swap;
                Log($"VData: {block.Name} is now {swap.Summary()} in '{_profile.Name}'. Save to addon to write it.");
                break;
            case Dialogs.ClassChoice.Remove:
                if (!_profile.VDataClasses.Remove(block.Name))
                {
                    return;
                }

                Log($"VData: class change on {block.Name} removed from '{_profile.Name}'; its values go back to CS2's when you save.");
                break;
            default:
                return;
        }

        // Single edits the class change made pointless (now equal to its value) are dropped.
        var rows = VDataView.Rows(connection.ReadVData(), block, null, _vdataOverrides.For(block.Name), _profile.VDataClasses.GetValueOrDefault(block.Name));
        foreach (var row in rows.Where(row => row.Override is not null && (row.Override == row.Baseline || row.RemovedByClass)))
        {
            _vdataOverrides.Remove(block.Name, row.Path);
        }

        SaveProfile();
        await AfterVDataProfileChangeAsync();
    }

    private async Task SaveCompilerLogAsync(string? log)
    {
        if (log is not { Length: > 0 })
        {
            return;
        }

        var path = Path.Combine(AddonDelivery.BackupsFolder(_sandboxes.WorkspacePath), "last-error.log");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, log);
        Log($"Compiler output saved to {path}");
    }
}
