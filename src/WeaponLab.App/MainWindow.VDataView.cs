using System.Text;
using Avalonia.Controls;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.VData;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>
/// The VData tab's block in view (0.22): the info panel on the right (what is
/// changed, unsaved or set outside Weapon Lab, its buy-menu slot, recoil
/// pattern and animation slot) and the viewport in the middle, which shows
/// the model its m_szWorldModel names playing its animation slot's clips.
/// The viewport block itself is the Animations tab's, moved in.
/// </summary>
public sealed partial class MainWindow
{
    private string? _vdataViewportKey;
    private string? _vdataClipPath;
    private SandboxInfo? _vdataViewportSandbox;
    private Dictionary<string, WeaponEntry>? _slotWeapons;

    /// <summary>A block's value as the list shows it: the profile's edit, else the addon's, else CS2's.</summary>
    private string? VDataValue(VDataBlock block, string path) =>
        _vdataOverrides.For(block.Name).GetValueOrDefault(path) ?? _addonVData?[block.Name]?.Value(path) ?? block.Value(path);

    /// <summary>The weapon folder whose clips a slot (anim class, else entity class) plays.</summary>
    private WeaponEntry? WeaponForSlot(string? slot)
    {
        if (slot is null || _connection is not { } connection)
        {
            return null;
        }

        if (_slotWeapons is null)
        {
            var stock = connection.ReadVData();
            _slotWeapons = new Dictionary<string, WeaponEntry>(StringComparer.Ordinal);
            foreach (var weapon in connection.Weapons)
            {
                foreach (var name in BlocksFor(weapon))
                {
                    // Knives play their own clips, not a class slot (all of them have class weapon_knife): left out here.
                    if (stock[name] is { } own && !WeaponSlots.PicksPerKnife(own) && WeaponSlots.SlotOf(own) is { } ownSlot)
                    {
                        _slotWeapons.TryAdd(ownSlot, weapon);
                    }
                }
            }
        }

        return _slotWeapons.GetValueOrDefault(slot);
    }

    /// <summary>
    /// Which clips a block plays, as the list has it (profile edit, else addon, else CS2): a knife its own weapon's
    /// (knives pick their animations per knife, not by class; <see cref="WeaponSlots.PicksPerKnife"/>), any other
    /// weapon the slot of its anim class, else its class. <c>Weapon</c> is null when no slot Weapon Lab knows matches.
    /// </summary>
    private (WeaponEntry? Weapon, string? Slot, bool PerKnife) AnimationsOf(VDataBlock block)
    {
        var animClass = VDataValue(block, WeaponSlots.AnimClassKey);
        var entityClass = (_addonVData?[block.Name] ?? block).Class;
        if (animClass is not { Length: > 0 } && entityClass == WeaponSlots.KnifeClass)
        {
            return (WeaponOfBlock(block.Name), entityClass, true);
        }

        var slot = animClass is { Length: > 0 } ? animClass : entityClass;
        return (WeaponForSlot(slot), slot, false);
    }

    /// <summary>The info panel: the block in view's changes, where its values come from, and what it is in game.</summary>
    private void RefreshVDataInfo()
    {
        if (_connection is not { } connection || _vdataBlock is not { } block)
        {
            VDataInfoText.Text = "Scroll to a block.";
            return;
        }

        var stock = connection.ReadVData();
        var rows = VDataView.Rows(stock, block, _addonVData, _vdataOverrides.For(block.Name), _profile.VDataClasses.GetValueOrDefault(block.Name), _addonOriginalVData, WrittenByLab(block.Name));
        var written = WrittenByLab(block.Name);
        string Name(VDataRow row) => VDataGlossary.Describe(row.Path).Name;
        var unsaved = rows.Where(row => !row.RemovedByClass && !VDataValues.Same(row.AddonValue ?? row.StockValue, row.EffectiveValue)).ToList();
        var changed = rows.Where(row => row.AddonValue is { } addon && !VDataValues.Same(addon, row.StockValue)).ToList();
        var outside = changed.Where(row => row.Override is null && !written.Contains(row.Path)).ToList();

        var text = new StringBuilder();
        var (slotWeapon, slot, perKnife) = AnimationsOf(block);
        var slotName = slotWeapon is null ? slot ?? "none" : $"{WeaponLabels.For(slotWeapon.Id)} ({slotWeapon.Id})";
        text.AppendLine(perKnife
            ? $"Animations: its own, {slotName}. Knives pick their animations per knife, not by class (every knife has class {WeaponSlots.KnifeClass})."
            : $"Animation slot: {slotName}, by its anim class {slot ?? "?"}.");
        if (VDataValue(block, VDataBlock.WorldModelKey) is { Length: > 0 } model)
        {
            text.AppendLine($"Model: {Path.GetFileName(model)}.");
        }

        var gear = VDataValue(block, "m_GearSlot");
        var price = VDataValue(block, "m_nPrice");
        if (gear is not null || price is not null)
        {
            text.AppendLine($"Buy menu: {gear?.Replace("GEAR_SLOT_", "").ToLowerInvariant() ?? "?"} slot, position {VDataValue(block, "m_GearSlotPosition") ?? "?"}, price ${price ?? "?"}.");
        }

        if (rows.FirstOrDefault(row => row.Path == "m_nRecoilSeed") is { } seed)
        {
            text.AppendLine($"Recoil pattern: seed {seed.EffectiveValue}"
                + (seed.Source == VDataSource.Inherited ? $", inherited from {seed.InheritedFrom}" : ", its own")
                + (VDataValues.Same(seed.EffectiveValue, seed.StockValue) ? " (CS2's)." : " (changed from CS2's)."));
        }

        if (_profile.VDataClasses.TryGetValue(block.Name, out var swap))
        {
            text.AppendLine($"Based on another weapon: {swap.Summary()}.");
        }

        text.AppendLine();
        text.AppendLine($"{changed.Count} value(s) changed from CS2 in the addon; {unsaved.Count} not saved yet; {outside.Count} set outside Weapon Lab.");
        AppendRows(text, "Not saved yet", unsaved, row => $"{Name(row)}: {row.AddonValue ?? row.StockValue} → {row.EffectiveValue}");
        AppendRows(text, "Set outside Weapon Lab (the addon's own)", outside, row => $"{Name(row)}: {row.AddonValue} (CS2 {row.StockValue})");
        AppendRows(text, "Changed from CS2", changed.Except(outside).ToList(), row => $"{Name(row)}: {row.AddonValue} (CS2 {row.StockValue})");
        VDataInfoText.Text = text.ToString().TrimEnd();
    }

    private static void AppendRows(StringBuilder text, string title, IReadOnlyList<VDataRow> rows, Func<VDataRow, string> line)
    {
        if (rows.Count == 0)
        {
            return;
        }

        text.AppendLine();
        text.AppendLine(title + ":");
        foreach (var row in rows.Take(10))
        {
            text.AppendLine("  " + line(row));
        }

        if (rows.Count > 10)
        {
            text.AppendLine($"  … and {rows.Count - 10} more");
        }
    }

    /// <summary>
    /// The viewport on the VData tab: the block's m_szWorldModel (as the list has it) on the skeleton of the
    /// slot its anim class plays, with that slot's clips in the Clip selector.
    /// </summary>
    private async void RefreshVDataViewport()
    {
        if (ToolTabs.SelectedIndex != VDataTool || _connection is not { } connection || _vdataBlock is not { } block)
        {
            return;
        }

        var (slotWeapon, slot, perKnife) = AnimationsOf(block);
        var weapon = slotWeapon ?? WeaponOfBlock(block.Name);
        var model = VDataValue(block, VDataBlock.WorldModelKey);
        string? key = null;
        try
        {
            key = PreviewModels.ForVDataModel(connection, Target()?.Name, model);
        }
        catch (Exception error) when (error is IOException or ArgumentException)
        {
            // Shown below as "not found".
        }

        VDataModelText.Text = model is null ? "no m_szWorldModel" : key is null ? $"{Path.GetFileName(model)} (not found compiled; showing CS2's)" : Path.GetFileName(model);
        if (weapon is null)
        {
            VDataClipCombo.ItemsSource = null;
            ViewportEmpty.IsVisible = true;
            ViewportEmpty.Text = $"{block.Name} plays no animation slot Weapon Lab knows ({slot ?? "no class"}).";
            WeaponView.Scene = null;
            _vdataViewportKey = null;
            return;
        }

        // The slot's clips, the weapon's own first; the selection stays when the slot does.
        var clips = weapon.ClipPaths.OrderBy(path => ExplorerNode.ClipName(path).EndsWith("_lgcy", StringComparison.Ordinal)).ThenBy(path => path, StringComparer.Ordinal).ToList();
        _updatingControls = true;
        VDataClipCombo.ItemsSource = clips.Select(ExplorerNode.ClipName).ToList();
        _vdataClipPath = _vdataClipPath is not null && clips.Contains(_vdataClipPath) ? _vdataClipPath
            : clips.FirstOrDefault(path => ExplorerNode.ClipName(path).StartsWith("idle", StringComparison.Ordinal)) ?? clips.FirstOrDefault();
        VDataClipCombo.SelectedItem = _vdataClipPath is null ? null : ExplorerNode.ClipName(_vdataClipPath);
        _updatingControls = false;

        var sceneKey = $"vdata|{weapon.Id}|{key}";
        if (sceneKey == _vdataViewportKey && WeaponView.Scene is not null)
        {
            return;
        }

        _vdataViewportKey = sceneKey;
        _viewportKey = null; // The Animations tab rebuilds its own scene when it opens again.
        var request = ++_viewportRequest;
        ViewportEmpty.IsVisible = true;
        ViewportEmpty.Text = "Loading the model…";
        ViewportTitle.Text = block.Name;
        try
        {
            var sandbox = _sandboxes.Exists(weapon.Id) ? _sandboxes.Open(weapon.Id) : null;
            var settings = _profile.Weapon(weapon.Id);
            var arms = new ArmsPreview("", settings.GlovesModel ?? ArmsPreview.Default.Gloves);
            var cache = ViewportCacheFolder;
            var scene = await Task.Run(() => WeaponLab.Core.Viewport.ViewportScenes.Build(connection, weapon, sandbox, key, cache, arms));
            if (request != _viewportRequest)
            {
                return;
            }

            _vdataViewportSandbox = sandbox;
            WeaponView.Scene = scene;
            ViewportEmpty.IsVisible = scene.Bones.Count == 0;
            ViewportEmpty.Text = scene.Warnings.FirstOrDefault() ?? "Nothing to show";
            ViewportTitle.Text = $"{block.Name} · " + (perKnife ? $"its own knife animations ({WeaponLabels.For(weapon.Id)})" : $"{WeaponLabels.For(weapon.Id)} slot") + (scene.MeshSource is { } source ? $" · model {source}" : "");
            ViewportCaption.Text = scene.Warnings.Count > 0 ? string.Join(" ", scene.Warnings) : "The model the vdata names, playing its animation slot's clip. Left drag turns, right or middle drag pans, wheel zooms.";
            LoadViewportClip(_vdataClipPath);
            WeaponView.Playing = true;
            ViewportPlayGlyph.Text = "⏸";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            if (request == _viewportRequest)
            {
                ViewportEmpty.IsVisible = true;
                ViewportEmpty.Text = "The viewport could not load this block's model: " + error.Message;
            }
        }
    }

    private void OnVDataClipSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingControls || VDataClipCombo.SelectedItem is not string name || _vdataBlock is null || WeaponView.Scene is null)
        {
            return;
        }

        var weapon = AnimationsOf(_vdataBlock).Weapon ?? WeaponOfBlock(_vdataBlock.Name);
        _vdataClipPath = weapon?.ClipPaths.FirstOrDefault(path => ExplorerNode.ClipName(path) == name);
        LoadViewportClip(_vdataClipPath);
        WeaponView.Playing = true;
        ViewportPlayGlyph.Text = "⏸";
    }
}
