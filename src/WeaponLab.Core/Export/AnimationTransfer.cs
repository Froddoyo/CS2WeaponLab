using System.Diagnostics;
using System.Text;
using System.Text.Json;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.Core.Export;

/// <summary>What importing an animation onto a clip did.</summary>
/// <param name="Taken">Bones that took the file's motion, per stream (from the name match, or what Blender copied).</param>
/// <param name="Skipped">Bones of the file (or of the clip) that kept the clip's motion.</param>
public sealed record AnimationImportResult(string ClipId, int Frames, IReadOnlyList<string> Taken, IReadOnlyList<string> Skipped, ClipExportReport Export);

/// <summary>
/// Export and import of one clip's animation in four formats (0.25), for the app's clip rows and the CLI:
/// compiled clips and baked pose files are handled here (<see cref="AnimationFiles"/>); keys (.blend) and FBX
/// need Blender, which runs <c>animation_job.py</c> in the background on a copy of the weapon's .blend. An import
/// ends as the clip's sandbox export with a pose sample beside it (Anim Events and Blender's "Load Weapon Lab's
/// version" read it); the clip keeps its own events.
/// </summary>
public static class AnimationTransfer
{
    public const string JobScriptRelativePath = "blender/weaponlab_bridge/animation_job.py";
    private const string ResultPrefix = "WEAPONLAB_RESULT ";
    private const string ErrorPrefix = "WEAPONLAB_ERROR ";

    public static readonly IReadOnlyList<(string Name, string Pattern)> Formats =
    [
        ("Compiled clip", "*.vnmclip_c"),
        ("Baked poses", "*.json.gz"),
        ("Blender keys", "*.blend"),
        ("FBX animation", "*.fbx"),
    ];

    public static string FormatOf(string file)
    {
        var name = file.ToLowerInvariant();
        return name.EndsWith(".vnmclip_c", StringComparison.Ordinal) ? "compiled"
            : name.EndsWith(".json.gz", StringComparison.Ordinal) || name.EndsWith(".gz", StringComparison.Ordinal) ? "poses"
            : name.EndsWith(".blend", StringComparison.Ordinal) ? "keys"
            : name.EndsWith(".fbx", StringComparison.Ordinal) ? "fbx"
            : throw new ArgumentException($"{Path.GetFileName(file)}: pick a .vnmclip_c, .json.gz, .blend or .fbx file.");
    }

    /// <summary>Writes the clip (as it plays now: its export, else its template; keys and FBX: as saved in the .blend) to <paramref name="file"/>.</summary>
    public static async Task<string> ExportAsync(SandboxInfo sandbox, GameConnection connection, AddonTarget? target, BlenderInstall? blender, string clipId, string file,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        var clip = sandbox.Manifest.FindClip(clipId)
            ?? throw new ArgumentException($"{clipId} is not a clip of {sandbox.Manifest.WeaponId}.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        switch (FormatOf(file))
        {
            case "compiled":
                var export = ClipExporter.LastExport(sandbox, clipId) is { SandboxOutput: { } output } && File.Exists(output) ? output : null;
                File.Copy(export ?? CustomSkeletons.Template(sandbox, clip, CustomSkeletons.Load(sandbox)).Path, file, overwrite: true);
                return export is null ? $"{clipId}: wrote the unedited clip (no export yet)." : $"{clipId}: wrote its export.";
            case "poses":
                AnimationFiles.Write(file, AnimationFiles.Current(sandbox, clipId, AnimationFiles.Skeletons(connection, sandbox, target)));
                return $"{clipId}: wrote its poses with bone names.";
            default:
                var mode = FormatOf(file) == "keys" ? "export-keys" : "export-fbx";
                await RunJobAsync(sandbox, RequireBlender(blender), mode, clipId, file, null, progress, cancellation);
                return $"{clipId}: wrote {(mode == "export-keys" ? "its keys" : "an FBX")} from the saved .blend.";
        }
    }

    /// <summary>
    /// Puts an animation file on one clip: mapped by bone name (compiled and pose files here, keys and FBX through
    /// Blender), written as the clip's sandbox export with its pose sample. The clip's events stay.
    /// </summary>
    public static async Task<AnimationImportResult> ImportAsync(SandboxInfo sandbox, GameConnection connection, AddonTarget? target, BlenderInstall? blender, string clipId, string file,
        IProgress<string>? progress = null, CancellationToken cancellation = default)
    {
        if (RigPackage.ReadRig(sandbox) is null)
        {
            throw new InvalidOperationException($"{sandbox.Manifest.WeaponId} has no .blend build yet. Build .blend first: an import is checked against it.");
        }

        var edits = Path.Combine(sandbox.FolderPath, ClipExporter.EditsFolder);
        Directory.CreateDirectory(edits);
        var sample = Path.Combine(edits, clipId + ".json.gz.import");
        try
        {
            int frames;
            IReadOnlyList<string> taken, skipped;
            switch (FormatOf(file))
            {
                case "compiled" or "poses":
                    var weaponSkeleton = AnimationFiles.WeaponSkeletonFor(sandbox, clipId);
                    var source = AnimationFiles.Read(file, AnimationFiles.Skeletons(connection, sandbox, target), weaponSkeleton);
                    var match = AnimationFiles.ToSample(sandbox, clipId, source, sample);
                    (frames, taken, skipped) = (match.Frames, match.Matched.Values.SelectMany(names => names).ToList(), match.Kept.Values.SelectMany(names => names).ToList());
                    break;
                default:
                    var mode = FormatOf(file) == "keys" ? "import-keys" : "import-fbx";
                    var result = await RunJobAsync(sandbox, RequireBlender(blender), mode, clipId, file, sample, progress, cancellation);
                    taken = Names(result, "copied");
                    skipped = Names(result, "skipped");
                    frames = PoseFile.Read(sample).Values.Select(stream => stream.Length).DefaultIfEmpty(0).Max();
                    break;
            }

            progress?.Report($"Exporting {clipId} with the imported animation");
            var report = ClipExporter.Export(sandbox, clipId, sample, toSandbox: true, extraOutputPath: null);
            File.Move(sample, Path.Combine(edits, clipId + ".json.gz"), overwrite: true); // The export's sample: Anim Events and Blender read it.
            return new AnimationImportResult(clipId, frames, taken, skipped, report);
        }
        finally
        {
            if (File.Exists(sample))
            {
                File.Delete(sample);
            }
        }
    }

    private static IReadOnlyList<string> Names(JsonElement result, string key) =>
        result.TryGetProperty(key, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().Select(value => value.GetString() ?? "").ToList() : [];

    private static BlenderInstall RequireBlender(BlenderInstall? blender) =>
        blender ?? throw new InvalidOperationException("Keys (.blend) and FBX files go through Blender, and Blender was not found.");

    /// <summary>Runs animation_job.py in the background on a copy of the weapon's saved .blend; returns its result.</summary>
    private static async Task<JsonElement> RunJobAsync(SandboxInfo sandbox, BlenderInstall blender, string mode, string clipId, string file, string? sample,
        IProgress<string>? progress, CancellationToken cancellation)
    {
        if (!BlendBuilder.HasBlend(sandbox))
        {
            throw new InvalidOperationException($"{sandbox.Manifest.WeaponId} has no .blend yet. Build .blend first.");
        }

        var script = Path.Combine(AppContext.BaseDirectory, JobScriptRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(script))
        {
            throw new FileNotFoundException($"The Blender job script is missing: {script}");
        }

        var jobs = Path.Combine(sandbox.FolderPath, "blender", ".jobs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(jobs);
        var copy = Path.Combine(jobs, sandbox.Manifest.WeaponId + ".blend");
        File.Copy(BlendBuilder.BlendPath(sandbox), copy);
        try
        {
            progress?.Report($"Blender: {mode} {clipId}");
            var start = new ProcessStartInfo(blender.ExecutablePath) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "--background", "--factory-startup", copy, "--python-exit-code", "1", "--python", script, "--", mode, RigPackage.InputPath(sandbox), clipId, Path.GetFullPath(file) })
            {
                start.ArgumentList.Add(argument);
            }

            if (sample is not null)
            {
                start.ArgumentList.Add(Path.GetFullPath(sample));
            }

            using var process = new Process { StartInfo = start };
            var log = new StringBuilder();
            string? result = null, error = null;
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

                if (line.StartsWith(ResultPrefix, StringComparison.Ordinal))
                {
                    result = line[ResultPrefix.Length..];
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
            await process.WaitForExitAsync(cancellation);
            if (process.ExitCode != 0 || result is null)
            {
                throw new BlendBuildException(error ?? $"Blender exited with code {process.ExitCode} without a result.", log.ToString());
            }

            return JsonDocument.Parse(result).RootElement.Clone();
        }
        finally
        {
            try
            {
                Directory.Delete(jobs, recursive: true);
            }
            catch (IOException)
            {
                // A copy Blender still holds is left for the next job to sweep.
            }
        }
    }
}
