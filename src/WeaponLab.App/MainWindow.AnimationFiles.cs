using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;

namespace WeaponLab.App;

/// <summary>
/// Export and import one clip's animation from the outliner (0.25): compiled clips (.vnmclip_c), baked poses
/// (.json.gz), Blender keys (.blend) and FBX. An import becomes the clip's export (its events stay); Blender then
/// offers "Load Weapon Lab's newer version" for that clip. See <see cref="AnimationTransfer"/>.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly FilePickerFileType[] AnimationFileTypes =
    [
        .. AnimationTransfer.Formats.Select(format => new FilePickerFileType($"{format.Name} ({format.Pattern})") { Patterns = [format.Pattern] }),
    ];

    private async void OnExportAnimationMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is not { Weapon: { } weapon, ClipPath: { } path } || _connection is not { } connection)
        {
            return;
        }

        var clipId = ExplorerNode.ClipName(path);
        if (!_sandboxes.Exists(weapon.Id))
        {
            await Dialogs.ShowAsync(this, "Export animation", $"{weapon.Id} has no sandbox yet, so there is nothing of yours to export. Copy the game's clip instead, or build the .blend first.");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export {clipId}'s animation",
            SuggestedFileName = clipId + ".vnmclip_c",
            FileTypeChoices = AnimationFileTypes,
            ShowOverwritePrompt = true,
        });
        if (file?.TryGetLocalPath() is not { } target)
        {
            return;
        }

        try
        {
            var message = await AnimationTransfer.ExportAsync(_sandboxes.Open(weapon.Id), connection, Target(), _blender, clipId, target, new Progress<string>(SetStatus));
            Log($"{weapon.Id}/{message} {target}");
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or BlendBuildException)
        {
            await Dialogs.ShowAsync(this, "Export animation", $"Could not export {clipId}: {error.Message}");
        }
    }

    private async void OnImportAnimationMenuClicked(object? sender, RoutedEventArgs e)
    {
        if (MenuNode(sender) is not { Weapon: { } weapon, ClipPath: { } path } || _connection is not { } connection)
        {
            return;
        }

        var clipId = ExplorerNode.ClipName(path);
        if (!_sandboxes.Exists(weapon.Id) || !BlendBuilder.HasBlend(_sandboxes.Open(weapon.Id)))
        {
            await Dialogs.ShowAsync(this, "Import animation", $"Build {weapon.Id}'s .blend first: an import is matched against its rigs and exported like a Blender export.");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = $"Import an animation onto {clipId}",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Animation files") { Patterns = [.. AnimationTransfer.Formats.Select(format => format.Pattern)] }, .. AnimationFileTypes],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } source)
        {
            return;
        }

        if (!await Dialogs.ConfirmAsync(this, "Import animation",
                $"Put {Path.GetFileName(source)} on {weapon.Id}/{clipId}?\n\nBones are matched by name and parent; matched bones take the file's motion, the rest keep {clipId}'s. "
                + $"It becomes {clipId}'s export (its events stay) and replaces the current export. In Blender, \"Load Weapon Lab's newer version\" then brings it into the .blend.",
                "Import"))
        {
            return;
        }

        try
        {
            var sandbox = _sandboxes.Open(weapon.Id);
            var result = await AnimationTransfer.ImportAsync(sandbox, connection, Target(), _blender, clipId, source, new Progress<string>(SetStatus));
            Log($"{weapon.Id}/{clipId}: {result.Taken.Count} bone(s) took {Path.GetFileName(source)}'s motion over {result.Frames} frames"
                + (result.Skipped.Count == 0 ? "" : $"; kept their own: {string.Join(", ", result.Skipped.Take(10))}{(result.Skipped.Count > 10 ? " …" : "")}")
                + ". Written as its export; push it in the Deliver tab. In Blender: Load Weapon Lab's newer version.");
            InvalidateViewport();
            await RefreshStatusAsync();
            RefreshCurrentTool();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or InvalidOperationException or ArgumentException or UnauthorizedAccessException
                                         or BlendBuildException or ClipWriteException or System.Text.Json.JsonException)
        {
            await Dialogs.ShowAsync(this, "Import animation", $"Could not import onto {clipId}: {error.Message}");
        }
    }
}
