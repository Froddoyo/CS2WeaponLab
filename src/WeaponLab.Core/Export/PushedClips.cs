using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Export;

/// <summary>
/// A copy of each clip's sandbox work as the addon has it, in
/// <c>pushed/&lt;clip&gt;/</c>: the exported clip, its report, the Blender
/// sample and the event edits. A copy is kept whenever the addon is found
/// holding a clip's current export (after a push, or when the app checks the
/// addon), so "Revert to addon default" can put the sandbox back exactly after
/// later changes that were not pushed.
/// </summary>
public static class PushedClips
{
    public const string Folder = "pushed";

    private const string ClipFile = "clip.vnmclip_c";
    private const string ReportFile = "export.json";
    private const string SampleFile = "edits.json.gz";
    private const string EventsFile = "events.json";

    public static string FolderFor(SandboxInfo sandbox, string clipId) => AtomicFile.SafeJoin(Path.Combine(sandbox.FolderPath, Folder), clipId);

    /// <summary>The Blender sample a clip's exports are made from.</summary>
    public static string SamplePath(SandboxInfo sandbox, string clipId) => Path.Combine(sandbox.FolderPath, ClipExporter.EditsFolder, clipId + ".json.gz");

    /// <summary>The SHA-256 of the kept clip, or null when no copy is kept.</summary>
    public static string? KeptSha(SandboxInfo sandbox, string clipId)
    {
        var path = Path.Combine(FolderFor(sandbox, clipId), ClipFile);
        return File.Exists(path) ? GameArchive.Sha256(File.ReadAllBytes(path)) : null;
    }

    /// <summary>
    /// Keeps the clip's current sandbox work as the addon's version. Call it only
    /// when the addon holds the clip's current export. Returns true when a new copy
    /// was made (false when there is no export or the same copy is already kept).
    /// </summary>
    public static bool Keep(SandboxInfo sandbox, string clipId)
    {
        var report = ClipExporter.LastExport(sandbox, clipId);
        if (report?.SandboxOutput is not { } output || !File.Exists(output))
        {
            return false;
        }

        var bytes = File.ReadAllBytes(output);
        if (KeptSha(sandbox, clipId) == GameArchive.Sha256(bytes))
        {
            return false;
        }

        // Build the copy beside the old one, then swap, so a failure never leaves half a copy.
        var folder = FolderFor(sandbox, clipId);
        var staging = folder + ".new";
        var old = folder + ".old";
        DeleteFolder(staging);
        DeleteFolder(old);
        Directory.CreateDirectory(staging);
        File.WriteAllBytes(Path.Combine(staging, ClipFile), bytes);
        File.Copy(ClipExporter.ReportPath(sandbox, clipId), Path.Combine(staging, ReportFile));
        CopyIfExists(SamplePath(sandbox, clipId), Path.Combine(staging, SampleFile));
        CopyIfExists(EventEdits.PathFor(sandbox, clipId), Path.Combine(staging, EventsFile));
        if (Directory.Exists(folder))
        {
            Directory.Move(folder, old);
        }

        Directory.Move(staging, folder);
        DeleteFolder(old);
        return true;
    }

    /// <summary>
    /// Moves a clip's sandbox work (its export and report, Blender sample and
    /// event edits) into <paramref name="backupFolder"/>, keeping their paths in
    /// the sandbox. The clip counts as unedited afterwards. Returns the moved
    /// files, relative to the sandbox.
    /// </summary>
    public static IReadOnlyList<string> SetAside(SandboxInfo sandbox, string clipId, string backupFolder)
    {
        var files = new List<string>();
        var exports = Path.GetFullPath(Path.Combine(sandbox.FolderPath, ClipExporter.ExportsFolder)) + Path.DirectorySeparatorChar;
        if (ClipExporter.LastExport(sandbox, clipId)?.SandboxOutput is { } output && Path.GetFullPath(output).StartsWith(exports, StringComparison.OrdinalIgnoreCase))
        {
            files.Add(output);
        }

        files.Add(ClipExporter.ReportPath(sandbox, clipId));
        files.Add(SamplePath(sandbox, clipId));
        files.Add(EventEdits.PathFor(sandbox, clipId));

        var moved = new List<string>();
        var root = Path.GetFullPath(sandbox.FolderPath);
        foreach (var file in files.Where(File.Exists))
        {
            var relative = Path.GetRelativePath(root, Path.GetFullPath(file));
            var destination = Path.Combine(backupFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(file, destination, overwrite: true);
            moved.Add(relative);
        }

        return moved;
    }

    /// <summary>
    /// Puts the kept copy back as the clip's sandbox work, after moving the
    /// current work into <paramref name="backupFolder"/>.
    /// </summary>
    public static void Restore(SandboxInfo sandbox, string clipId, string backupFolder)
    {
        var folder = FolderFor(sandbox, clipId);
        var clipPath = Path.Combine(folder, ClipFile);
        if (!File.Exists(clipPath))
        {
            throw new InvalidDataException($"No copy of {clipId} as the addon has it is kept.");
        }

        var bytes = File.ReadAllBytes(clipPath);
        SetAside(sandbox, clipId, backupFolder);

        var reportPath = ClipExporter.ReportPath(sandbox, clipId);
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.Copy(Path.Combine(folder, ReportFile), reportPath, overwrite: true);
        var report = ClipExporter.LastExport(sandbox, clipId) ?? throw new InvalidDataException($"The kept report of {clipId} is empty.");
        var output = AtomicFile.SafeJoin(Path.Combine(sandbox.FolderPath, ClipExporter.ExportsFolder), report.ResourcePath);
        AtomicFile.WriteAllBytes(output, bytes);
        ClipExporter.SaveReport(sandbox, report with { SandboxOutput = output, OutputSha256 = GameArchive.Sha256(bytes) });

        RestoreIfKept(Path.Combine(folder, SampleFile), SamplePath(sandbox, clipId));
        RestoreIfKept(Path.Combine(folder, EventsFile), EventEdits.PathFor(sandbox, clipId));
    }

    private static void RestoreIfKept(string kept, string destination)
    {
        if (File.Exists(kept))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(kept, destination, overwrite: true);
        }
    }

    private static void CopyIfExists(string source, string destination)
    {
        if (File.Exists(source))
        {
            File.Copy(source, destination);
        }
    }

    private static void DeleteFolder(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
