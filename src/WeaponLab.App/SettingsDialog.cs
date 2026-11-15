using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Settings;

namespace WeaponLab.App;

/// <summary>
/// The Settings window (0.24): where the workspace (profiles and everything in them) lives and where new .blend
/// files go. Saved in <see cref="AppSettings"/>; nothing is moved. A new workspace takes effect on the next start.
/// </summary>
public static class SettingsDialog
{
    /// <returns>True when the workspace folder changed (the caller asks for a restart).</returns>
    public static async Task<bool> ShowAsync(Window owner, string workspaceInUse)
    {
        var settings = AppSettings.Load();
        var changedWorkspace = false;
        var dialog = new Window
        {
            Title = "Settings",
            Icon = owner.Icon,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };

        var workspace = new TextBox { Text = settings.WorkspaceFolder ?? workspaceInUse, MinWidth = 420 };
        var workspaceBrowse = new Button { Content = "Browse…" };
        var workspaceDefault = new Button { Content = "Default" };
        ToolTip.SetTip(workspaceDefault, "workspace beside the application: " + SandboxStore.BesideApplication());
        workspaceBrowse.Click += async (_, _) => workspace.Text = await PickFolderAsync(owner, "Workspace folder", workspace.Text) ?? workspace.Text;
        workspaceDefault.Click += (_, _) => workspace.Text = SandboxStore.BesideApplication();

        var blendFolder = new TextBox { Text = settings.BlendFolder ?? "", MinWidth = 420, PlaceholderText = "In each weapon's sandbox (blender\\<weapon>.blend)" };
        var blendBrowse = new Button { Content = "Browse…" };
        var blendClear = new Button { Content = "In the sandbox" };
        blendBrowse.Click += async (_, _) => blendFolder.Text = await PickFolderAsync(owner, "Folder for new .blend files", blendFolder.Text) ?? blendFolder.Text;
        blendClear.Click += (_, _) => blendFolder.Text = "";

        var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, IsVisible = false };
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => dialog.Close();
        save.Click += (_, _) =>
        {
            var chosenWorkspace = (workspace.Text ?? "").Trim().Trim('"');
            var chosenBlend = (blendFolder.Text ?? "").Trim().Trim('"');
            if (!Path.IsPathFullyQualified(chosenWorkspace) || (chosenBlend.Length > 0 && !Path.IsPathFullyQualified(chosenBlend)))
            {
                error.Text = "Use full folder paths, e.g. D:\\WeaponLab\\workspace.";
                error.IsVisible = true;
                return;
            }

            try
            {
                var full = Path.GetFullPath(chosenWorkspace);
                changedWorkspace = !string.Equals(full.TrimEnd('\\'), Path.GetFullPath(workspaceInUse).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                settings.WorkspaceFolder = full;
                settings.BlendFolder = chosenBlend.Length == 0 ? null : Path.GetFullPath(chosenBlend);
                settings.Save();
                dialog.Close();
            }
            catch (Exception problem) when (problem is IOException or UnauthorizedAccessException or ArgumentException)
            {
                error.Text = "Could not save the settings: " + problem.Message;
                error.IsVisible = true;
            }
        };

        var envOverride = Environment.GetEnvironmentVariable("WEAPONLAB_WORKSPACE") is { Length: > 0 } variable
            ? $"The WEAPONLAB_WORKSPACE environment variable ({variable}) wins over this setting while it is set."
            : null;
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 8,
            Children =
            {
                Header("Workspace folder"),
                Note("Your profiles live here, with their sandboxes, Blender exports, backups and deliveries. In use now: " + workspaceInUse),
                Row(workspace, workspaceBrowse, workspaceDefault),
                Note("Changing it moves nothing: copy the old folder there first if you want your profiles along. It takes effect when Weapon Lab starts again."
                    + (envOverride is null ? "" : " " + envOverride)),
                Header("New .blend files"),
                Row(blendFolder, blendBrowse, blendClear),
                Note("Build .blend suggests <folder>\\<profile>\\<weapon>.blend for a weapon that has no .blend yet. Existing .blend files stay where they are; "
                    + "Move .blend… (right-click a weapon, or the Animations tab) moves one."),
                Note("Settings file: " + AppSettings.FilePath),
                error,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), Children = { cancel, save } },
            },
        };
        await dialog.ShowDialog(owner);
        return changedWorkspace;
    }

    private static async Task<string?> PickFolderAsync(Window owner, string title, string? current)
    {
        var start = current is { Length: > 0 } && Directory.Exists(current) ? await owner.StorageProvider.TryGetFolderFromPathAsync(new Uri(current)) : null;
        var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false, SuggestedStartLocation = start });
        return picked.Count > 0 ? picked[0].TryGetLocalPath() : null;
    }

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 560, FontSize = 11, Opacity = 0.75 };

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }
}
