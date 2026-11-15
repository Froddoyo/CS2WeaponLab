using System.Diagnostics;

namespace WeaponLab.Core.Blender;

/// <param name="Installed">Whether the loader is in Blender's user add-ons.</param>
/// <param name="Path">Where the loader file is (or would be).</param>
public sealed record BlenderAddonState(bool Installed, string Path);

/// <summary>
/// Optionally installs the Weapon Lab Bridge into the user's Blender, so the
/// panel is there when a .blend is opened directly instead of through Open in
/// Blender (which loads the bridge for that session only). What is installed
/// is a loader pointing at this Weapon Lab's bridge and weaponlab.exe (see
/// blender/weaponlab_bridge/install_addon.py), so it never goes out of date.
/// </summary>
public static class BlenderAddon
{
    private const string ResultPrefix = "WEAPONLAB_ADDON ";

    public static Task<BlenderAddonState> StatusAsync(BlenderInstall blender, CancellationToken cancellation = default) =>
        RunAsync(blender, ["status"], cancellation);

    public static Task<BlenderAddonState> InstallAsync(BlenderInstall blender, CancellationToken cancellation = default)
    {
        var bridgeParent = Path.GetDirectoryName(Path.GetDirectoryName(BlendBuilder.ScriptPath()))!;
        return RunAsync(blender, ["install", bridgeParent, BlendBuilder.CliPath() ?? ""], cancellation);
    }

    public static Task<BlenderAddonState> RemoveAsync(BlenderInstall blender, CancellationToken cancellation = default) =>
        RunAsync(blender, ["remove"], cancellation);

    private static async Task<BlenderAddonState> RunAsync(BlenderInstall blender, IReadOnlyList<string> arguments, CancellationToken cancellation)
    {
        var script = Path.Combine(Path.GetDirectoryName(BlendBuilder.ScriptPath())!, "install_addon.py");
        var start = new ProcessStartInfo(blender.ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        // No --factory-startup: the user's preferences must load so saving them keeps everything else.
        foreach (var argument in new[] { "--background", "--python-exit-code", "1", "--python", script, "--" }.Concat(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new IOException("Blender did not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        await process.WaitForExitAsync(cancellation);
        var log = await output + await errors;
        var line = log.Split('\n').Select(item => item.Trim()).LastOrDefault(item => item.StartsWith(ResultPrefix, StringComparison.Ordinal));
        if (process.ExitCode != 0 || line is null)
        {
            throw new IOException($"Blender could not change the add-on (exit code {process.ExitCode}).\n{log[Math.Max(0, log.Length - 1500)..]}");
        }

        var result = line[ResultPrefix.Length..];
        var space = result.IndexOf(' ');
        return new BlenderAddonState(result[..space] == "installed", result[(space + 1)..]);
    }
}
