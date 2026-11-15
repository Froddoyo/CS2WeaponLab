using System.Text.Json;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Export;

/// <summary>
/// Event edits made in the app's Anim Events tab, kept per clip in the
/// sandbox (<c>events/&lt;clip&gt;.json</c>) and applied to every export of
/// the clip, so a Blender re-export keeps them (added loop markers too). A
/// Blender export merges them with its sound markers: a marker moved or
/// renamed in Blender wins for that event, a marker left alone keeps the
/// app's edit.
/// </summary>
public static class EventEdits
{
    public const string Folder = "events";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>The file's shape. Files from 0.18 hold only the list of changes; <c>frames</c> (0.20.1) is the clip's length when the user changed it.</summary>
    private sealed record FileFormat(
        List<EventChange>? Changes,
        List<int>? Removed,
        List<AddedEvent>? Added,
        List<SyncSection>? Sync,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? Frames = null);

    public static string PathFor(SandboxInfo sandbox, string clipId) => Path.Combine(sandbox.FolderPath, Folder, clipId + ".json");

    /// <summary>The changes to the clip's own events.</summary>
    public static IReadOnlyList<EventChange> Load(SandboxInfo sandbox, string clipId) => Read(sandbox, clipId).Changes;

    public static StoredEventEdits Read(SandboxInfo sandbox, string clipId)
    {
        var path = PathFor(sandbox, clipId);
        if (!File.Exists(path))
        {
            return StoredEventEdits.Empty;
        }

        var text = File.ReadAllText(path);
        if (text.TrimStart().StartsWith('['))
        {
            return new StoredEventEdits(JsonSerializer.Deserialize<List<EventChange>>(text, Json) ?? [], EventListEdit.None);
        }

        var file = JsonSerializer.Deserialize<FileFormat>(text, Json) ?? throw new InvalidDataException(path + " is empty.");
        return new StoredEventEdits(file.Changes ?? [], new EventListEdit { Removed = file.Removed ?? [], Added = file.Added ?? [], Sync = file.Sync }) { Frames = file.Frames };
    }

    /// <summary>Stores the changes to the clip's own events, keeping its added and removed events.</summary>
    public static void Save(SandboxInfo sandbox, string clipId, IReadOnlyList<EventChange> changes) =>
        Save(sandbox, clipId, Read(sandbox, clipId) with { Changes = changes });

    /// <summary>Stores the clip's edits; nothing left removes the file.</summary>
    public static void Save(SandboxInfo sandbox, string clipId, StoredEventEdits edits)
    {
        var path = PathFor(sandbox, clipId);
        if (edits.IsEmpty)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            return;
        }

        var file = new FileFormat(
            edits.Changes.OrderBy(change => change.Index).ToList(),
            edits.List.Removed.Order().ToList(),
            edits.List.Added.OrderBy(added => added.StartTime).ToList(),
            edits.List.Sync?.ToList(),
            edits.Frames);
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(file, Json) + "\n");
    }

    /// <summary>
    /// The app's edits with Blender's markers on top: a marker that differs
    /// from the template's event (moved or renamed in Blender) replaces the
    /// app's start time or name for that event; untouched markers change
    /// nothing, and markers of events removed in the app are ignored.
    /// </summary>
    public static IReadOnlyList<EventChange> Merge(IReadOnlyList<ClipEvent> template, IReadOnlyList<EventChange> stored, IReadOnlyList<EventChange> fromBlender, IReadOnlyCollection<int>? removed = null)
    {
        var result = stored.ToDictionary(change => change.Index);
        foreach (var marker in fromBlender.Where(marker => removed?.Contains(marker.Index) != true))
        {
            if (marker.Index < 0 || marker.Index >= template.Count)
            {
                result[marker.Index] = marker; // Let the writer report the bad index.
                continue;
            }

            var original = template[marker.Index];
            var renamed = marker.Name is not null && marker.Name != original.Name;
            var moved = marker.StartTime is { } start && Math.Abs(start - original.StartTime) > 1e-6;
            if (!renamed && !moved)
            {
                continue;
            }

            result[marker.Index] = result.TryGetValue(marker.Index, out var app)
                ? app with { Name = renamed ? marker.Name : app.Name, StartTime = moved ? marker.StartTime : app.StartTime }
                : marker with { Name = renamed ? marker.Name : null, StartTime = moved ? marker.StartTime : null };
        }

        return result.Values.OrderBy(change => change.Index).ToList();
    }

    /// <summary>
    /// Writes the clip's sandbox export with its stored edits: the last
    /// Blender export is run again when there is one (so its pose edits are
    /// kept), otherwise the template is patched with the events alone. Returns
    /// null when the clip ends up unedited (its export is removed).
    /// </summary>
    public static ClipExportReport? Apply(SandboxInfo sandbox, string clipId)
    {
        var clip = sandbox.Manifest.Clips.FirstOrDefault(candidate => RigPackage.ClipId(candidate.ResourcePath) == clipId)
            ?? throw new ArgumentException($"{clipId} is not a clip of {sandbox.Manifest.WeaponId}.");
        var sample = Path.Combine(sandbox.FolderPath, ClipExporter.EditsFolder, clipId + ".json.gz");
        if (File.Exists(sample) && File.Exists(Path.Combine(RigPackage.InputPath(sandbox), RigPackage.RigFileName)))
        {
            return ClipExporter.Export(sandbox, clipId, sample, toSandbox: true, extraOutputPath: null);
        }

        if (ClipExporter.LastExport(sandbox, clipId) is { ChangedBones.Count: > 0 })
        {
            throw new InvalidDataException($"{clipId} has pose edits from Blender but their sample is gone. Export it from Blender again, then edit its events.");
        }

        var (templatePath, _) = Skeletons.CustomSkeletons.Template(sandbox, clip, Skeletons.CustomSkeletons.Load(sandbox));
        var templateBytes = File.ReadAllBytes(templatePath);
        var stored = Read(sandbox, clipId);
        var result = ClipWriter.Write(templateBytes, clip.ResourcePath, new ClipEdit { Events = stored.Changes, EventList = stored.List, FrameCount = stored.Frames });
        if (result.IdenticalToTemplate)
        {
            ClipExporter.DeleteExport(sandbox, clipId);
            return null;
        }

        var output = AtomicFile.SafeJoin(Path.Combine(sandbox.FolderPath, ClipExporter.ExportsFolder), clip.ResourcePath);
        AtomicFile.WriteAllBytes(output, result.Bytes);
        var report = new ClipExportReport(
            clipId,
            clip.ResourcePath,
            false,
            new Dictionary<string, IReadOnlyList<string>>(),
            ClipExporter.DescribeEvents(result, clip.ResourcePath),
            0,
            0,
            0,
            GameArchive.Sha256(result.Bytes),
            output,
            null,
            DateTimeOffset.UtcNow);
        ClipExporter.SaveReport(sandbox, report);
        return report;
    }
}
