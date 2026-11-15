using System.Text.Json;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Export;

/// <param name="ChangedBones">Edited bone names per stream ("arms", "weapon").</param>
/// <param name="ChangedEvents">Edited events, e.g. "#2 Sound Weapon_AK47.Clipout @ frame 14".</param>
/// <param name="SandboxOutput">The clip written into the sandbox's exports folder, if any.</param>
/// <param name="ExtraOutput">The clip saved to a user-chosen path, if any.</param>
public sealed record ClipExportReport(
    string ClipId,
    string ResourcePath,
    bool IdenticalToTemplate,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ChangedBones,
    IReadOnlyList<string> ChangedEvents,
    double MaxTranslationError,
    double MaxRotationErrorDegrees,
    double MaxScaleError,
    string OutputSha256,
    string? SandboxOutput,
    string? ExtraOutput,
    DateTimeOffset ExportedUtc)
{
    /// <summary>The clip whose animation this export carries when Blender exported it as another clip (0.25 "Exported as"), else null.</summary>
    public string? SourceClip { get; init; }
}

/// <summary>
/// Exports one clip edited in Blender: applies the edit as deltas onto the
/// sandbox's template clip, writes it with <see cref="ClipWriter"/> (which
/// verifies it), and saves it into the sandbox and/or a chosen file.
/// </summary>
public static class ClipExporter
{
    public const string ExportsFolder = "exports";
    public const string EditsFolder = "edits";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <param name="editedPosesPath">The sample Blender wrote for the edited clip.</param>
    /// <param name="toSandbox">Write into <c>exports/&lt;resource path&gt;</c> in the sandbox.</param>
    /// <param name="extraOutputPath">Also save the clip to this file.</param>
    /// <param name="keepEditedExport">
    /// Refuse (<see cref="ExportGuardException"/>) when this export has no edits but the sandbox's current export of
    /// the clip has: a .blend built from the clips only (or reset) would otherwise replace your animation with
    /// CS2's motion (0.24). The Blender add-on sets it unless the user confirmed.
    /// </param>
    public static ClipExportReport Export(SandboxInfo sandbox, string clipId, string editedPosesPath, bool toSandbox, string? extraOutputPath, bool keepEditedExport = false, string? sourceClip = null)
    {
        if (!toSandbox && extraOutputPath is null)
        {
            throw new ArgumentException("Choose where to export: the sandbox, a file, or both.");
        }

        var input = RigPackage.InputPath(sandbox);
        var rig = JsonSerializer.Deserialize<RigDocument>(File.ReadAllText(Path.Combine(input, RigPackage.RigFileName)), Json)
            ?? throw new InvalidDataException("rig.json is empty.");
        var clip = rig.Clips.FirstOrDefault(candidate => candidate.Id == clipId)
            ?? throw new ArgumentException($"Clip '{clipId}' is not part of this sandbox's Blender build.");
        var sandboxClip = sandbox.Manifest.AllClips().First(candidate => candidate.ResourcePath == clip.ResourcePath);
        // A third-person clip (0.26) moves the body and its own weapon rig: their skeletons, not the arms'.
        var (primarySkeleton, weaponSkeleton) = rig.Skeletons.For(clip);
        if (primarySkeleton is null)
        {
            throw new InvalidDataException($"This .blend has no rig for {clipId}. Build the .blend again.");
        }

        // A weapon with a custom skeleton exports onto its re-targeted template, so the extra bones are kept.
        var (templatePath, _) = Skeletons.CustomSkeletons.Template(sandbox, sandboxClip, Skeletons.CustomSkeletons.Load(sandbox));
        var templateBytes = File.ReadAllBytes(templatePath);
        if (GameArchive.Sha256(templateBytes) != clip.Sha256)
        {
            throw new InvalidDataException($"The template for {clipId} changed since the .blend was built. Rebuild the .blend.");
        }

        var referencePath = Path.Combine(input, "reference", clipId + ".json.gz");
        if (!File.Exists(referencePath))
        {
            throw new InvalidDataException($"This .blend was built by an older Weapon Lab. In Weapon Lab, select the weapon and click 'Update for export' (your .blend and its edits are kept), then export again.");
        }

        // At a changed length (Anim Events or `events length`) CS2's frames and the build's reference are cut or held to it,
        // and a sample taken at another length too, so edits still apply as differences and unedited bones stay CS2's.
        var stored = EventEdits.Read(sandbox, clipId);
        var frames = stored.Frames ?? clip.Frames;
        var reference = Fit(PoseFile.Read(referencePath), frames);
        var edited = Fit(PoseFile.Read(editedPosesPath), frames);

        using var template = ClipTemplate.Open(templateBytes, clip.ResourcePath);

        // The weapon's hand targets always follow the hands (additive clips hold offsets, not poses).
        var tolerance = clip.View == RigPackage.WorldView ? EditDeltas.WorldTolerance : 1f;
        var arms = clip.Additive ? Stream(edited, "arms") : HandGlue.Apply(Stream(reference, "arms"), Stream(edited, "arms"), primarySkeleton.Bones, tolerance);
        var edit = new ClipEdit
        {
            Primary = EditDeltas.Apply(ClipLength.Resample(template.Primary.ReadPoses(), frames), Stream(reference, "arms"), arms, clip.Additive, "arms", tolerance),
            Secondary = WeaponEdit(template, weaponSkeleton, clip, reference, edited, frames, tolerance),
            Events = stored.Changes,
            EventList = stored.List,
            FrameCount = frames == clip.Frames ? null : frames,
        };

        var result = ClipWriter.Write(templateBytes, clip.ResourcePath, edit);
        var changedEvents = DescribeEvents(result, clip.ResourcePath);
        if (keepEditedExport && toSandbox && result.IdenticalToTemplate && LastExport(sandbox, clipId) is { IdenticalToTemplate: false } previous)
        {
            var bones = previous.ChangedBones.Values.SelectMany(names => names).Take(6).ToList();
            throw new ExportGuardException($"{clipId}: this .blend has no edits on it, but the sandbox's export from {previous.ExportedUtc.ToLocalTime():yyyy-MM-dd HH:mm} has "
                + (bones.Count > 0 ? $"edited bones ({string.Join(", ", bones)})" : "edits")
                + ". Exporting would replace them with the clip's own motion. Rebuild the .blend with 'Keep my animation' to get them back, or replace them anyway.");
        }


        string? sandboxOutput = null;
        if (toSandbox)
        {
            sandboxOutput = AtomicFile.SafeJoin(Path.Combine(sandbox.FolderPath, ExportsFolder), clip.ResourcePath);
            AtomicFile.WriteAllBytes(sandboxOutput, result.Bytes);
        }

        if (extraOutputPath is not null)
        {
            AtomicFile.WriteAllBytes(Path.GetFullPath(extraOutputPath), result.Bytes);
        }

        var report = new ClipExportReport(
            clipId,
            clip.ResourcePath,
            result.IdenticalToTemplate,
            ChangedBones(result, primarySkeleton, weaponSkeleton),
            changedEvents,
            result.Streams.Select(stream => stream.MaxTranslationError).DefaultIfEmpty(0).Max(),
            result.Streams.Select(stream => stream.MaxRotationErrorDegrees).DefaultIfEmpty(0).Max(),
            result.Streams.Select(stream => stream.MaxScaleError).DefaultIfEmpty(0).Max(),
            GameArchive.Sha256(result.Bytes),
            sandboxOutput,
            extraOutputPath is null ? null : Path.GetFullPath(extraOutputPath),
            DateTimeOffset.UtcNow)
        {
            SourceClip = sourceClip is not null && sourceClip != clipId ? sourceClip : null,
        };

        if (toSandbox)
        {
            SaveReport(sandbox, report);
        }

        return report;
    }

    /// <summary>The edited events of a written clip, e.g. "#2 Sound Weapon_AK47.Clipout @ frame 14".</summary>
    internal static IReadOnlyList<string> DescribeEvents(ClipWriteResult result, string resourcePath)
    {
        if (result.ChangedEvents.Count == 0 && result.RemovedEvents.Count == 0 && !result.SyncTrackChanged)
        {
            return [];
        }

        var info = ClipReader.Read(result.Bytes, resourcePath);
        var lines = result.ChangedEvents
            .Select(index => info.Events[index])
            .Select(clipEvent => $"#{clipEvent.Index} {clipEvent.Kind} {clipEvent.Name} @ frame {clipEvent.StartFrame(info):0.##}")
            .Concat(result.RemovedEvents.Select(removed => "removed " + removed))
            .ToList();
        if (result.SyncTrackChanged)
        {
            lines.Add("sync track: " + string.Join(", ", info.SyncTrack.Select(section =>
                $"{(section.Id.Length > 0 ? section.Id : "(unnamed)")} {section.StartTime * info.LastFrame:0.#}-{section.EndTime * info.LastFrame:0.#}")));
        }

        return lines;
    }

    internal static void SaveReport(SandboxInfo sandbox, ClipExportReport report) =>
        AtomicFile.WriteAllText(ReportPath(sandbox, report.ClipId), JsonSerializer.Serialize(report, Json) + "\n");

    public static string ReportPath(SandboxInfo sandbox, string clipId) =>
        Path.Combine(sandbox.FolderPath, ExportsFolder, clipId + ".export.json");

    /// <summary>The last sandbox export of a clip, or null if it was never exported.</summary>
    public static ClipExportReport? LastExport(SandboxInfo sandbox, string clipId)
    {
        var path = ReportPath(sandbox, clipId);
        return File.Exists(path) ? JsonSerializer.Deserialize<ClipExportReport>(File.ReadAllText(path), Json) : null;
    }

    /// <summary>
    /// Removes a clip's sandbox export (the exported clip and its report), so
    /// the clip counts as unedited again. Files outside the sandbox (a "Save
    /// Clip As" copy) are left alone. Returns false when there was no export.
    /// </summary>
    public static bool DeleteExport(SandboxInfo sandbox, string clipId)
    {
        var reportPath = ReportPath(sandbox, clipId);
        if (!File.Exists(reportPath))
        {
            return false;
        }

        var report = LastExport(sandbox, clipId);
        var exports = Path.GetFullPath(Path.Combine(sandbox.FolderPath, ExportsFolder)) + Path.DirectorySeparatorChar;
        if (report?.SandboxOutput is { } output && Path.GetFullPath(output).StartsWith(exports, StringComparison.OrdinalIgnoreCase) && File.Exists(output))
        {
            File.Delete(output);
        }

        File.Delete(reportPath);
        return true;
    }

    /// <summary>Every stream of a sample cut or held to the clip's length.</summary>
    private static Dictionary<string, BonePose[][]> Fit(IReadOnlyDictionary<string, BonePose[][]> sample, int frames) =>
        sample.ToDictionary(stream => stream.Key, stream => ClipLength.Resample(stream.Value, frames));

    private static Dictionary<int, BonePose[][]> WeaponEdit(
        ClipTemplate template,
        RigSkeleton? weapon,
        RigClip clip,
        IReadOnlyDictionary<string, BonePose[][]> reference,
        IReadOnlyDictionary<string, BonePose[][]> edited,
        int frames,
        float tolerance)
    {
        var edits = new Dictionary<int, BonePose[][]>();
        if (!clip.HasWeapon || weapon is null || !edited.ContainsKey("weapon"))
        {
            return edits;
        }

        var stream = template.Secondary.FirstOrDefault(candidate => candidate.Skeleton == weapon.Path)
            ?? throw new InvalidDataException($"{clip.Id} has no weapon stream on {weapon.Path}.");
        edits[stream.SecondaryIndex] = EditDeltas.Apply(ClipLength.Resample(stream.ReadPoses(), frames), Stream(reference, "weapon"), Stream(edited, "weapon"), clip.Additive, "weapon", tolerance);
        return edits;
    }

    private static BonePose[][] Stream(IReadOnlyDictionary<string, BonePose[][]> file, string name)
    {
        return file.TryGetValue(name, out var stream) ? stream : throw new InvalidDataException($"The sample has no '{name}' stream.");
    }

    /// <summary>Edited bones per stream: "arms" is the clip's primary stream (the body in third person), "weapon" its weapon.</summary>
    private static Dictionary<string, IReadOnlyList<string>> ChangedBones(ClipWriteResult result, RigSkeleton primary, RigSkeleton? weapon)
    {
        var changed = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var stream in result.Streams.Where(stream => stream.ChangedTracks.Count > 0))
        {
            var isArms = stream.Skeleton == primary.Path;
            var bones = isArms ? primary.Bones : weapon?.Bones;
            changed[isArms ? "arms" : "weapon"] = stream.ChangedTracks
                .Select(track => bones is not null && track < bones.Count ? bones[track].Name : $"track {track}")
                .ToList();
        }

        return changed;
    }
}

/// <summary>An export refused because it would replace an edited export with an unedited one (see <see cref="ClipExporter.Export"/>).</summary>
public sealed class ExportGuardException(string message) : Exception(message);
