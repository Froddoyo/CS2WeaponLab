using System.Globalization;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Weapons;

namespace WeaponLab.App;

/// <summary>Everything the window shows for one selected clip.</summary>
public sealed record ClipView(
    IReadOnlyList<PropertyGroup> Properties,
    IReadOnlyList<EventRow> Events,
    IReadOnlyList<TrackRow> Tracks);

/// <summary>
/// Turns game data into display rows. Runs off the UI thread.
/// </summary>
public static class ClipPresenter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <param name="addonClipFile">The addon's own version of the clip (what the game plays), shown instead of CS2's; null for CS2's.</param>
    /// <param name="addonGamePath">The addon's game folder, for skeletons only the addon has.</param>
    public static ClipView Load(GameConnection connection, string resourcePath, string? addonClipFile = null, string? addonGamePath = null)
    {
        var bytes = addonClipFile is not null && File.Exists(addonClipFile) ? File.ReadAllBytes(addonClipFile) : connection.Archive.ReadBytes(resourcePath);
        var clip = ClipReader.Read(bytes, resourcePath);
        using var template = ClipTemplate.Open(bytes, resourcePath);

        var tracks = new List<TrackRow>();
        foreach (var stream in template.Streams)
        {
            var skeleton = ReadSkeleton(connection, stream.Skeleton, addonGamePath);
            var streamName = stream.IsPrimary ? "arms" : SkeletonShortName(stream.Skeleton);
            var channels = stream.ReadChannels();
            for (var track = 0; track < stream.TrackCount; track++)
            {
                var parentIndex = track < skeleton.Bones.Count ? skeleton.Bones[track].ParentIndex : -1;
                tracks.Add(new TrackRow(
                    streamName,
                    track.ToString(Invariant),
                    skeleton.BoneName(track),
                    parentIndex < 0 ? "" : skeleton.BoneName(parentIndex),
                    channels[track].ToString(),
                    channels[track].Any));
            }
        }

        var animated = tracks.Count(track => track.Animated);
        var gameplay = clip.Events.Count(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay);
        var sounds = clip.Events.Count(clipEvent => clipEvent.Kind == ClipEventKind.Sound);
        var properties = new List<PropertyGroup>
        {
            new("General",
            [
                new("Name", clip.Name),
                new("Resource", clip.ResourcePath),
                new("Shown", addonClipFile is not null && File.Exists(addonClipFile) ? $"the addon's own version ({Path.GetFileName(addonClipFile)}), what the game plays" : "CS2's clip"),
                new("Editable", clip.IsEditable ? "yes" : "no (not a paired hand rig)"),
                new("Additive", clip.IsAdditive ? "yes" : "no"),
            ]),
            new("Timing",
            [
                new("Frames", clip.FrameCount.ToString(Invariant)),
                new("Duration", clip.DurationSeconds.ToString("0.#### s", Invariant)),
                new("Frame rate", clip.FramesPerSecond.ToString("0.## fps", Invariant)),
            ]),
            new("Skeleton",
            [
                new("Primary", clip.Skeleton),
                new("Paired", clip.SecondarySkeletons.Count == 0 ? "none" : string.Join(", ", clip.SecondarySkeletons)),
                new("Tracks", $"{tracks.Count} ({animated} animated)"),
            ]),
            new("Events",
            [
                new("Gameplay", gameplay.ToString(Invariant)),
                new("Sound", sounds.ToString(Invariant)),
                new("Other", (clip.Events.Count - gameplay - sounds).ToString(Invariant)),
            ]),
            new("Template",
            [
                new("SHA-256", clip.Sha256),
            ]),
        };

        return new ClipView(properties, clip.Events.Select(clipEvent => EventRow.From(clipEvent, clip)).ToList(), tracks);
    }

    /// <summary>A skeleton from the game, else from the addon (a custom skeleton only the addon has).</summary>
    private static SkeletonInfo ReadSkeleton(GameConnection connection, string path, string? addonGamePath)
    {
        if (connection.Archive.Contains(path + "_c") || addonGamePath is null)
        {
            return connection.ReadSkeleton(path);
        }

        var file = WeaponLab.Core.IO.AtomicFile.SafeJoin(addonGamePath, path + "_c");
        return File.Exists(file) ? SkeletonInfo.Read(File.ReadAllBytes(file), path) : connection.ReadSkeleton(path);
    }

    /// <summary>The clip's last export from Blender, shown under the clip's properties.</summary>
    public static PropertyGroup ExportProperties(ClipExportReport? report)
    {
        if (report is null)
        {
            return new PropertyGroup("Export", [new("Status", "not exported yet")]);
        }

        var bones = report.ChangedBones.SelectMany(pair => pair.Value.Select(bone => $"{pair.Key}: {bone}")).ToList();
        var events = report.ChangedEvents ?? [];
        return new PropertyGroup("Export",
        [
            new("Exported", report.ExportedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", Invariant)),
            new("Edited bones", report.IdenticalToTemplate ? "none (original clip)" : bones.Count == 0 ? "none" : $"{bones.Count}: {string.Join(", ", bones)}"),
            new("Sound events", events.Count == 0 ? "unchanged" : string.Join(", ", events)),
            new("Max error", $"{report.MaxTranslationError:0.######} units, {report.MaxRotationErrorDegrees:0.####}°"),
            new("File", report.SandboxOutput ?? ""),
        ]);
    }

    public static IReadOnlyList<PropertyGroup> WeaponProperties(WeaponEntry weapon, bool hasSandbox, string sandboxFolder, string? blendPath, int exportedClips)
    {
        return
        [
            new("Weapon",
            [
                new("Id", weapon.Id),
                new("Category", weapon.Category.Length == 0 ? "(none)" : weapon.Category),
                new("Folder", weapon.Folder),
                new("Clips", weapon.ClipPaths.Count.ToString(Invariant)),
            ]),
            new("Sandbox",
            [
                new("Status", hasSandbox ? "created" : "not created"),
                new("Folder", hasSandbox ? sandboxFolder : ""),
                new(".blend", blendPath ?? (hasSandbox ? "not built yet" : "")),
                new("Exported clips", hasSandbox ? exportedClips.ToString(Invariant) : ""),
            ]),
        ];
    }

    /// <summary>Keeps rows whose name or value contains <paramref name="filter"/>; drops empty groups.</summary>
    public static IReadOnlyList<PropertyGroup> Filter(IReadOnlyList<PropertyGroup> groups, string filter)
    {
        if (filter.Length == 0)
        {
            return groups;
        }

        return groups
            .Select(group => group with
            {
                Rows = group.Rows
                    .Where(row => row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || row.Value.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList(),
            })
            .Where(group => group.Rows.Count > 0)
            .ToList();
    }

    private static string SkeletonShortName(string skeletonPath)
    {
        var name = skeletonPath[(skeletonPath.LastIndexOf('/') + 1)..];
        return name.Replace(".vnmskel", "");
    }
}
