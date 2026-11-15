using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WeaponLab.Core.Viewport;

namespace WeaponLab.App;

/// <summary>
/// The Animations tab's viewport: the selected weapon's model and the
/// skeleton its clips drive. A bone picked in the view is selected in the
/// outliner's Skeleton node and the other way round.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>What the viewport shows now (weapon, skeleton file, model choice), to skip rebuilding the same scene.</summary>
    private string? _viewportKey;
    private int _viewportRequest;

    private string ViewportCacheFolder => Path.Combine(_sandboxes.WorkspacePath, "cache", "viewport");

    private void WireViewport()
    {
        WeaponView.BonePicked += SelectBoneInOutliner;
        WeaponView.FrameChanged += OnViewportFrameChanged;
        WeaponView.SelectedBoneMoved += (name, screen) =>
        {
            ViewportBoneLabel.IsVisible = screen is not null;
            if (screen is { } point)
            {
                ViewportBoneLabelText.Text = name;
                Canvas.SetLeft(ViewportBoneLabel, point.X + 8);
                Canvas.SetTop(ViewportBoneLabel, point.Y - 22);
            }
        };
    }

    /// <summary>Shows the selected weapon in the viewport, building its scene in the background when it changed.</summary>
    private async void RefreshViewport()
    {
        if (_connection is not { } connection || _weapon is not { } weapon)
        {
            _viewportKey = null;
            WeaponView.Scene = null;
            ViewportEmpty.IsVisible = true;
            ViewportEmpty.Text = "Select a weapon in the outliner";
            ViewportTitle.Text = "Viewport";
            return;
        }

        var sandbox = CurrentSandbox();
        var setup = sandbox is null ? null : WeaponLab.Core.Skeletons.CustomSkeletons.Load(sandbox);
        var settings = _profile.Weapon(weapon.Id);
        var preview = settings.PreviewModel;
        // The arms are the gloved hands only (user decision 2026-10-27: no sleeves); the gloves follow the Build .blend choice ("" none).
        var arms = new WeaponLab.Core.Blender.ArmsPreview("", settings.GlovesModel ?? WeaponLab.Core.Blender.ArmsPreview.Default.Gloves);
        var key = $"{weapon.Id}|{setup?.Path}|{setup?.SkeletonSha256}|{preview}|{arms}";
        if (key == _viewportKey)
        {
            return;
        }

        _viewportKey = key;
        var request = ++_viewportRequest;
        ViewportEmpty.IsVisible = true;
        ViewportEmpty.Text = "Loading the model…";
        ViewportTitle.Text = weapon.Id;
        try
        {
            var cache = ViewportCacheFolder;
            var scene = await Task.Run(() => ViewportScenes.Build(connection, weapon, sandbox, preview, cache, arms));
            if (request != _viewportRequest)
            {
                return; // Another weapon was picked meanwhile.
            }

            WeaponView.Scene = scene;
            _vdataViewportKey = null; // The VData tab rebuilds its own scene when it opens again.
            LoadViewportClip(_clipPath is { } selected && weapon.ClipPaths.Contains(selected) ? selected : null);
            ViewportEmpty.IsVisible = scene.Bones.Count == 0;
            ViewportEmpty.Text = scene.Warnings.FirstOrDefault() ?? "Nothing to show";
            var extra = scene.Bones.Count(bone => bone.Extra);
            ViewportTitle.Text = $"{weapon.Id} · {Path.GetFileName(scene.Skeleton)} ({scene.Bones.Count} bones{(extra > 0 ? $", {extra} new" : "")})"
                + (scene.MeshSource is { } source ? $" · model {source}" : "");
            ViewportCaption.Text = scene.Warnings.Count > 0
                ? string.Join(" ", scene.Warnings)
                : "Left drag turns, right or middle drag pans, wheel zooms; click a joint to select its bone.";
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            if (request == _viewportRequest)
            {
                ViewportEmpty.IsVisible = true;
                ViewportEmpty.Text = "The viewport could not load this weapon: " + error.Message;
            }
        }
    }

    /// <summary>Forgets the shown scene so the next refresh rebuilds it (after the skeleton or model changed).</summary>
    private void InvalidateViewport()
    {
        _viewportKey = null;
        if (ViewportShown)
        {
            RefreshViewport();
        }
    }

    // ---- Playback ----

    private bool _updatingTimeline;
    private string? _viewportClipPath;

    /// <summary>The addon's version the viewport's clip was loaded from (null: not the addon's).</summary>
    private string? _viewportAddonClip;

    /// <summary>After the addon was checked again: reload the shown clip when the addon's version of it changed.</summary>
    private void RefreshViewportClipSource()
    {
        if (_viewportClipPath is { } shown && WeaponView.Scene is not null
            && WeaponLab.Core.Addons.AddonInventory.ClipFile(Target(), _inventory, shown) != _viewportAddonClip)
        {
            LoadViewportClip(shown);
        }
    }

    /// <summary>Loads the clip selected in the outliner into the viewport (the export when "Your export" is ticked and there is one).</summary>
    private async void LoadViewportClip(string? clipPath)
    {
        // The viewport shows the first-person rig; third-person clips (0.26) play on the body, in Blender only.
        clipPath = clipPath is not null && WeaponLab.Core.Weapons.WeaponCatalog.IsWorldClip(clipPath) ? null : clipPath;
        _viewportClipPath = clipPath;
        if (_connection is not { } connection || WeaponView.Scene is not { Bones.Count: > 0 } scene || clipPath is null)
        {
            ShowViewportClip(null);
            return;
        }

        var sandbox = ToolTabs.SelectedIndex == VDataTool ? _vdataViewportSandbox : CurrentSandbox();
        var exported = ViewportExportedToggle.IsChecked == true;
        try
        {
            // What the addon plays for it (its own version, the live state) unless your export is asked for.
            var addonClip = WeaponLab.Core.Addons.AddonInventory.ClipFile(Target(), _inventory, clipPath);
            _viewportAddonClip = addonClip;
            var clip = await Task.Run(() => ViewportClips.Load(connection, sandbox, clipPath, scene, exported, addonClip));
            if (clipPath == _viewportClipPath && WeaponView.Scene == scene)
            {
                ShowViewportClip(clip);
                if (clip is null)
                {
                    ViewportFrameText.Text = "no weapon motion";
                }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException)
        {
            ShowViewportClip(null);
            ViewportFrameText.Text = "could not read";
            Log($"Viewport: {Path.GetFileName(clipPath)} could not be read: {error.Message}");
        }
    }

    private void ShowViewportClip(ViewportClip? clip)
    {
        WeaponView.Animation = clip;
        ViewportPlayButton.IsEnabled = clip is not null;
        ViewportStepBackButton.IsEnabled = clip is not null;
        ViewportStepForwardButton.IsEnabled = clip is not null;
        ViewportTimeline.IsEnabled = clip is not null;
        ViewportPlayGlyph.Text = WeaponView.Playing ? "⏸" : "▶";
        _updatingTimeline = true;
        ViewportTimeline.Maximum = Math.Max(1, (clip?.FrameCount ?? 1) - 1);
        ViewportTimeline.Value = 0;
        _updatingTimeline = false;
        ViewportFrameText.Text = clip is null ? "no clip" : FrameText(clip, 0);
        ViewportEventMarks.Animation = clip;
        EventTimelineView.Playhead = 0;
        UpdateShellPlan();
        if (_events is { } session && _viewportClipPath == session.Path)
        {
            ShowShellPanel(session); // Its loop-seam check reads the clip's poses.
        }
    }

    private static string FrameText(ViewportClip clip, double frame) =>
        $"{clip.Name} · {Math.Floor(frame):0} / {clip.FrameCount - 1}{clip.Source switch { "exported" => " · yours", "addon" => " · the addon's", "CS2" => " · CS2's", _ => " · original" }}";


    private void OnViewportPlayClicked(object? sender, RoutedEventArgs e)
    {
        WeaponView.Playing = !WeaponView.Playing;
        ViewportPlayGlyph.Text = WeaponView.Playing ? "⏸" : "▶";
    }

    private void OnViewportTimelineChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingTimeline || WeaponView.Animation is not { } clip)
        {
            return;
        }

        // Dragging the scrubber pauses playback and shows that frame.
        WeaponView.Playing = false;
        ViewportPlayGlyph.Text = "▶";
        WeaponView.CurrentFrame = e.NewValue;
        ViewportFrameText.Text = FrameText(clip, e.NewValue);
        EventTimelineView.Playhead = e.NewValue;
    }

    private void OnViewportFrameChanged(double frame)
    {
        if (WeaponView.Animation is not { } clip)
        {
            return;
        }

        _updatingTimeline = true;
        ViewportTimeline.Value = frame;
        _updatingTimeline = false;
        ViewportFrameText.Text = FrameText(clip, frame);
        EventTimelineView.Playhead = frame;
    }

    private void OnViewportSpeedChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (WeaponView is not null)
        {
            WeaponView.Speed = ViewportSpeed.SelectedIndex switch { 0 => 0.25, 1 => 0.5, 3 => 2, _ => 1 };
        }
    }

    private void OnViewportExportedToggled(object? sender, RoutedEventArgs e) => LoadViewportClip(_viewportClipPath);

    private void OnViewportToggled(object? sender, RoutedEventArgs e)
    {
        WeaponView.ShowMesh = ViewportMeshToggle.IsChecked == true;
        WeaponView.ShowBones = ViewportBonesToggle.IsChecked == true;
        WeaponView.ShowArms = ViewportArmsToggle.IsChecked == true;
        if (!WeaponView.ShowBones)
        {
            ViewportBoneLabel.IsVisible = false;
        }
    }

    private void OnViewportFrameClicked(object? sender, RoutedEventArgs e) => WeaponView.Frame();

    /// <summary>Selects a bone picked in the viewport under the weapon's Skeleton node in the outliner.</summary>
    private void SelectBoneInOutliner(string bone)
    {
        var weaponNode = _tree.SelectMany(category => category.Children).FirstOrDefault(node => node.Weapon == _weapon);
        var skeletonNode = weaponNode?.Children.FirstOrDefault(node => node.Kind == ExplorerNodeKind.Skeleton);
        if (weaponNode is null || skeletonNode is null)
        {
            return;
        }

        FillSkeletonNode(skeletonNode);
        var path = PathTo(skeletonNode, bone);
        if (path is null)
        {
            return;
        }

        weaponNode.IsExpanded = true;
        skeletonNode.IsExpanded = true;
        foreach (var node in path.SkipLast(1))
        {
            node.IsExpanded = true;
        }

        ExplorerTree.SelectedItem = path[^1];
    }

    private static List<ExplorerNode>? PathTo(ExplorerNode parent, string bone)
    {
        foreach (var child in parent.Children)
        {
            if (child.BoneName == bone)
            {
                return [child];
            }

            if (PathTo(child, bone) is { } deeper)
            {
                return [child, .. deeper];
            }
        }

        return null;
    }
}
