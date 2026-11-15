using System.Diagnostics;
using System.Text.Json;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Blender;

/// <summary>What the Blender script reports after saving the .blend.</summary>
public sealed record BlendBuildResult(
    string WeaponId,
    string Blend,
    int Actions,
    int Markers,
    int ArmsBones,
    int WeaponBones,
    IReadOnlyList<string> Previews,
    IReadOnlyList<string> Warnings,
    string Blender)
{
    /// <summary>What a rebuild carried over from the previous .blend (0.24), or null when it carried nothing.</summary>
    public CarriedKeys? Carried { get; init; }

    /// <summary>The third-person clips the .blend got (0.26), on its Body rig.</summary>
    public int ThirdPersonClips { get; init; }
}

/// <param name="Clips">Clips that got the previous .blend's keys.</param>
/// <param name="Bones">Bones whose keys were carried.</param>
/// <param name="NotCarried">Bones with keys in the previous .blend that kept the clip's motion: gone, or their rest or parent changed.</param>
public sealed record CarriedKeys(int Clips, int Bones, IReadOnlyList<string> NotCarried);

public sealed class BlendBuildException(string message, string log) : Exception(message)
{
    /// <summary>Blender's full output, for diagnosing failures.</summary>
    public string Log { get; } = log;
}

/// <summary>
/// Builds <c>blender/&lt;weapon&gt;.blend</c> inside a sandbox: writes the rig
/// package, then runs Blender headless with <c>build_weapon.py</c>.
/// </summary>
public static class BlendBuilder
{
    public const string ScriptRelativePath = "blender/weaponlab_bridge/build_weapon.py";
    private const string ProgressPrefix = "WEAPONLAB_PROGRESS ";
    private const string ResultPrefix = "WEAPONLAB_RESULT ";
    private const string ErrorPrefix = "WEAPONLAB_ERROR ";

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Where the sandbox's .blend lives: a location the user chose, else the sandbox's blender folder.</summary>
    public static string BlendPath(SandboxInfo sandbox)
    {
        var chosen = LocationFile(sandbox);
        if (File.Exists(chosen) && File.ReadAllText(chosen).Trim() is { Length: > 0 } path)
        {
            return path;
        }

        return DefaultBlendPath(sandbox);
    }

    public static string DefaultBlendPath(SandboxInfo sandbox) =>
        Path.Combine(sandbox.FolderPath, "blender", sandbox.Manifest.WeaponId + ".blend");

    /// <summary>Remembers where the user saves this sandbox's .blend; the default location forgets the choice.</summary>
    public static void SetBlendPath(SandboxInfo sandbox, string path)
    {
        var file = LocationFile(sandbox);
        if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(DefaultBlendPath(sandbox)), StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Path.GetFullPath(path) + "\n");
    }

    private static string LocationFile(SandboxInfo sandbox) => Path.Combine(sandbox.FolderPath, "blender", "location.txt");

    public static bool HasBlend(SandboxInfo sandbox) => File.Exists(BlendPath(sandbox));

    /// <summary>
    /// True when every buildable clip has a reference sample. .blend files
    /// built before Weapon Lab 0.5 have none and cannot export until updated.
    /// </summary>
    public static bool HasReferences(SandboxInfo sandbox)
    {
        var references = Path.Combine(RigPackage.InputPath(sandbox), "reference");
        return sandbox.Manifest.Clips
            .Where(clip => clip.Editable)
            .All(clip => File.Exists(Path.Combine(references, RigPackage.ClipId(clip.ResourcePath) + ".json.gz")));
    }

    /// <summary>A .blend exists but cannot export yet; <see cref="BuildAsync"/> with referencesOnly fixes it.</summary>
    public static bool NeedsUpdate(SandboxInfo sandbox) => HasBlend(sandbox) && !HasReferences(sandbox);

    private static string BackupsFolder(SandboxInfo sandbox) => Path.Combine(sandbox.FolderPath, "blender", "backups");

    /// <summary>The build script shipped next to the application.</summary>
    public static string ScriptPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? path : throw new FileNotFoundException($"The Blender build script is missing: {path}");
    }

    /// <param name="referencesOnly">
    /// Refresh the rig package and reference samples without touching the
    /// existing .blend, so edits saved in it are kept. The build is
    /// deterministic, so references from a throwaway build match the
    /// original one. Otherwise the .blend is rebuilt after a backup.
    /// </param>
    /// <param name="propBones">Weapon bones ticked "Keep apart from the gun" (the profile's <c>PropBones</c>).</param>
    /// <param name="carryKeys">A full rebuild carries the user's keys from the current .blend (its backup copy) onto bones that still exist with the same rest (0.24).</param>
    public static async Task<BlendBuildResult> BuildAsync(
        SandboxInfo sandbox,
        GameConnection connection,
        BlenderInstall blender,
        IProgress<string>? progress = null,
        CancellationToken cancellation = default,
        bool referencesOnly = false,
        string? previewModel = null,
        ArmsPreview? arms = null,
        IReadOnlyCollection<string>? propBones = null,
        bool carryKeys = false)
    {
        var target = BlendPath(sandbox);
        string? previous = null;
        var throwaway = Path.Combine(sandbox.FolderPath, "blender", ".update", sandbox.Manifest.WeaponId + ".blend");
        if (!referencesOnly && File.Exists(target))
        {
            var backup = Path.Combine(BackupsFolder(sandbox), $"{sandbox.Manifest.WeaponId}-{DateTime.Now:yyyyMMdd-HHmmss}.blend");
            Directory.CreateDirectory(BackupsFolder(sandbox));
            File.Copy(target, backup);
            progress?.Report($"Backed up the previous .blend to {backup}");
            previous = carryKeys ? backup : null;
        }

        try
        {
            var result = await RunBuildAsync(sandbox, connection, blender, referencesOnly ? throwaway : target, progress, previewModel, arms, propBones, previous, cancellation);
            return referencesOnly ? result with { Blend = target } : result;
        }
        finally
        {
            if (referencesOnly && Directory.Exists(Path.GetDirectoryName(throwaway)))
            {
                Directory.Delete(Path.GetDirectoryName(throwaway)!, recursive: true);
            }
        }
    }

    private static async Task<BlendBuildResult> RunBuildAsync(
        SandboxInfo sandbox,
        GameConnection connection,
        BlenderInstall blender,
        string output,
        IProgress<string>? progress,
        string? previewModel,
        ArmsPreview? arms,
        IReadOnlyCollection<string>? propBones,
        string? previous,
        CancellationToken cancellation)
    {
        var package = await Task.Run(() => RigPackage.Write(sandbox, connection, progress, previewModel, arms, propBones), cancellation);
        progress?.Report($"Rig package ready ({package.ClipCount} clips, {package.PreviewCount} previews). Starting {blender}…");

        var start = new ProcessStartInfo(blender.ExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "--background", "--factory-startup", "--python-exit-code", "1", "--python", ScriptPath(), "--", package.InputPath, output })
        {
            start.ArgumentList.Add(argument);
        }

        if (previous is not null)
        {
            start.ArgumentList.Add(previous);
        }

        using var process = new Process { StartInfo = start };
        var log = new System.Text.StringBuilder();
        BlendBuildResult? result = null;
        string? error = null;

        void OnLine(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (log)
            {
                log.AppendLine(line);
            }

            if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
            {
                progress?.Report(line[ProgressPrefix.Length..]);
            }
            else if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
            {
                result = JsonSerializer.Deserialize<BlendBuildResult>(line[ResultPrefix.Length..], Json);
            }
            else if (line.StartsWith(ErrorPrefix, StringComparison.Ordinal))
            {
                error = line[ErrorPrefix.Length..];
            }
        }

        process.OutputDataReceived += (_, args) => OnLine(args.Data);
        process.ErrorDataReceived += (_, args) => OnLine(args.Data);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        if (process.ExitCode != 0 || result is null)
        {
            throw new BlendBuildException(error ?? $"Blender exited with code {process.ExitCode} without a result.", log.ToString());
        }

        return result;
    }

    /// <summary>
    /// Opens the sandbox's .blend in Blender's normal UI with the Weapon Lab
    /// Bridge add-on loaded for that session only, and tells it where the
    /// weaponlab command-line tool is so it can export.
    /// </summary>
    public static void Open(SandboxInfo sandbox, BlenderInstall blender)
    {
        var start = new ProcessStartInfo(blender.ExecutablePath) { UseShellExecute = false };
        start.ArgumentList.Add(BlendPath(sandbox));
        start.ArgumentList.Add("--python");
        start.ArgumentList.Add(BridgeScriptPath("launch.py"));
        if (CliPath() is { } cli)
        {
            start.Environment["WEAPONLAB_CLI"] = cli;
        }

        // The add-on finds the sandbox beside the .blend; one saved elsewhere and built before 0.21 learns it from here.
        start.Environment["WEAPONLAB_SANDBOX"] = Path.GetFullPath(sandbox.FolderPath);
        Process.Start(start);
    }

    /// <summary>weaponlab.exe shipped next to the application, if present.</summary>
    public static string? CliPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "weaponlab.exe" : "weaponlab");
        return File.Exists(path) ? path : null;
    }

    private static string BridgeScriptPath(string fileName)
    {
        var path = Path.Combine(Path.GetDirectoryName(ScriptPath())!, fileName);
        return File.Exists(path) ? path : throw new FileNotFoundException($"The Blender add-on is missing: {path}");
    }
}
