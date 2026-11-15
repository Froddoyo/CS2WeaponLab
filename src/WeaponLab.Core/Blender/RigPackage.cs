using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Blender;

/// <summary>
/// The Blender build's input: <c>rig.json</c> (skeletons, clips, events,
/// preview meshes) plus one gzip pose file per clip, written into the
/// sandbox's <c>blender/input</c> folder. Plain files, so the Blender
/// script needs nothing but the Python standard library.
/// </summary>
public static class RigPackage
{
    public const string Schema = "weaponlab.rig/1";
    public const string InputFolder = "blender/input";
    public const string RigFileName = "rig.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string InputPath(SandboxInfo sandbox) => Path.Combine(sandbox.FolderPath, InputFolder);

    /// <summary>The sandbox's rig.json (what its .blend was built from), or null when there is none or it cannot be read.</summary>
    public static RigDocument? ReadRig(SandboxInfo sandbox)
    {
        var path = Path.Combine(InputPath(sandbox), RigFileName);
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<RigDocument>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the package from the sandbox's template clips. Replaces any
    /// previous package; the sandbox templates are never modified.
    /// </summary>
    /// <param name="previewModel">A <see cref="PreviewModels"/> key to show instead of the stock weapon mesh; null = stock.</param>
    /// <param name="propBones">Weapon bones ticked "Keep apart from the gun" (<see cref="Profiles.WeaponProfile.PropBones"/>).</param>
    public static RigPackageResult Write(SandboxInfo sandbox, GameConnection connection, IProgress<string>? progress = null, string? previewModel = null, ArmsPreview? arms = null,
        IReadOnlyCollection<string>? propBones = null)
    {
        var manifest = sandbox.Manifest;
        var editable = manifest.Clips.Where(clip => clip.Editable).ToList();
        if (editable.Count == 0)
        {
            throw new InvalidDataException($"{manifest.WeaponId} has no clips on the viewmodel hand rig, so it cannot be built for Blender.");
        }

        var output = InputPath(sandbox);
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }

        Directory.CreateDirectory(output);
        var warnings = new List<string>();

        // A custom skeleton (see CustomSkeletons) gives the clips re-targeted templates and the rig its extra bones.
        var setup = Skeletons.CustomSkeletons.Load(sandbox);
        previewModel ??= setup?.SourceModel;
        SkeletonInfo ReadSkeleton(string path) => setup is not null && path == setup.Path ? Skeletons.CustomSkeletons.ReadSkeleton(sandbox, setup) : connection.ReadSkeleton(path);

        // The props the .blend poses apart from the gun (any weapon skeleton), and a custom skeleton's extra and hidden bones.
        RigSkeleton WeaponSkeleton(SkeletonInfo info)
        {
            var props = PropBones(info, propBones);
            var rig = Skeleton(info) with { FreeBones = props.Count > 0 ? props : null };
            return setup is null || info.ResourcePath != setup.Path ? rig : rig with { ExtraBones = setup.ExtraBones, HiddenBones = setup.HiddenBones };
        }

        // Open every template once to learn the skeletons. Third-person clips (0.26) always use their own templates:
        // a custom skeleton is first person only.
        var templates = editable
            .Select(clip => (Clip: clip, Active: Skeletons.CustomSkeletons.Template(sandbox, clip, setup)))
            .Select(item => (item.Clip, item.Active.Sha256, Template: ClipTemplate.Open(File.ReadAllBytes(item.Active.Path), item.Clip.ResourcePath)))
            .ToList();
        var worldTemplates = manifest.WorldClips.Where(clip => clip.Editable)
            .Select(clip => (Clip: clip, clip.Sha256, Template: ClipTemplate.Open(File.ReadAllBytes(AtomicFile.SafeJoin(sandbox.FolderPath, clip.Template)), clip.ResourcePath)))
            .ToList();
        try
        {
            var armsSkeleton = templates[0].Template.Primary.Skeleton;
            var weaponSkeleton = MostUsedSecondary(templates.Select(item => item.Template));
            var clips = templates
                .Select(item => WriteClip(output, item.Clip, item.Sha256, item.Template, weaponSkeleton, view: null, warnings, progress))
                .ToList();

            // Third person: the body rig plays the clip's primary stream, a second weapon rig (on the body's wpn) its weapon stream.
            var bodySkeleton = worldTemplates.Count > 0 ? worldTemplates[0].Template.Primary.Skeleton : null;
            var worldWeaponSkeleton = MostUsedSecondary(worldTemplates.Select(item => item.Template));
            clips.AddRange(worldTemplates.Select(item => WriteClip(output, item.Clip, item.Sha256, item.Template, worldWeaponSkeleton, WorldView, warnings, progress)));

            var weaponInfo = weaponSkeleton is null ? null : ReadSkeleton(weaponSkeleton);
            var previews = ExportPreviews(connection, output, weaponInfo, warnings, progress, previewModel, arms ?? ArmsPreview.Default, body: bodySkeleton is not null);
            var skeletons = new RigSkeletons(Skeleton(connection.ReadSkeleton(armsSkeleton)), weaponInfo is null ? null : WeaponSkeleton(weaponInfo))
            {
                Body = bodySkeleton is null ? null : Skeleton(connection.ReadSkeleton(bodySkeleton)),
                WorldWeapon = worldWeaponSkeleton is null ? null : Skeleton(connection.ReadSkeleton(worldWeaponSkeleton)),
            };
            var rig = new RigDocument(
                Schema,
                manifest.WeaponId,
                WeaponLabels.For(manifest.WeaponId),
                manifest.Category,
                DefaultClip(clips.Where(clip => clip.View is null).ToList()),
                skeletons,
                clips,
                previews,
                warnings);

            AtomicFile.WriteAllText(Path.Combine(output, RigFileName), JsonSerializer.Serialize(rig, Json) + "\n");
            return new RigPackageResult(output, clips.Count, previews.Count, warnings);
        }
        finally
        {
            foreach (var (_, _, template) in templates.Concat(worldTemplates))
            {
                template.Dispose();
            }
        }
    }

    /// <summary>The <see cref="RigClip.View"/> of third-person clips (0.26).</summary>
    public const string WorldView = "world";

    /// <summary>The paired-stream skeleton most of the templates use (the weapon), or null when none has one.</summary>
    private static string? MostUsedSecondary(IEnumerable<ClipTemplate> templates) => templates
        .SelectMany(template => template.Secondary.Select(stream => stream.Skeleton))
        .GroupBy(skeleton => skeleton)
        .OrderByDescending(group => group.Count())
        .Select(group => group.Key)
        .FirstOrDefault();

    /// <summary>Writes one clip's pose file and describes it for rig.json.</summary>
    private static RigClip WriteClip(string output, SandboxClip clip, string sha256, ClipTemplate template, string? weaponSkeleton, string? view, List<string> warnings, IProgress<string>? progress)
    {
        var id = ClipId(clip.ResourcePath);
        progress?.Report($"Writing poses for {id}");

        var weaponStream = template.Secondary.FirstOrDefault(stream => stream.Skeleton == weaponSkeleton);
        if (template.Secondary.Count > 0 && weaponStream is null)
        {
            warnings.Add($"{id}: paired stream uses another skeleton; only the {(view == WorldView ? "body" : "arms")} are imported.");
        }

        var posesFile = $"poses/{id}.json.gz";
        WritePoses(Path.Combine(output, posesFile), template.Primary, weaponStream);

        var info = ClipReader.Read(template.Bytes, clip.ResourcePath);
        return new RigClip(
            id,
            clip.ResourcePath,
            sha256,
            template.Primary.FrameCount,
            template.Primary.Clip.Duration,
            template.Primary.Clip.IsAdditive,
            weaponStream is not null,
            posesFile,
            info.Events.Select(clipEvent => new RigEvent(clipEvent.Kind.ToString(), clipEvent.Name, clipEvent.StartFrame(info))).ToList())
        {
            View = view,
        };
    }

    /// <summary>
    /// Third-person clip ids start with this (0.26): a weapon's world clips are named like its first-person ones
    /// (world <c>reload_ak</c> beside viewmodel <c>reload_ak</c>), and ids name files in the sandbox and Actions in the .blend.
    /// </summary>
    public const string WorldPrefix = "world_";

    /// <summary>The clip's id in the sandbox and the .blend: its file name, with <see cref="WorldPrefix"/> for a third-person clip.</summary>
    public static string ClipId(string resourcePath)
    {
        var name = Path.GetFileName(resourcePath).Replace(Weapons.WeaponCatalog.ClipExtension, "");
        return Weapons.WeaponCatalog.IsWorldClip(resourcePath) ? WorldPrefix + name : name;
    }

    /// <summary>A third-person clip id (<see cref="WorldPrefix"/>).</summary>
    public static bool IsWorldId(string clipId) => clipId.StartsWith(WorldPrefix, StringComparison.Ordinal);

    /// <summary>
    /// The ticked bones the .blend poses as props, in skeleton order: never the root (the whole gun), never a
    /// bone the skeleton lacks, and not a bone under another ticked one (it moves with that one).
    /// </summary>
    public static IReadOnlyList<string> PropBones(SkeletonInfo skeleton, IReadOnlyCollection<string>? ticked)
    {
        if (ticked is null || ticked.Count == 0)
        {
            return [];
        }

        bool IsProp(SkeletonBone bone) => bone.ParentIndex >= 0 && ticked.Contains(bone.Name);
        bool UnderProp(SkeletonBone bone)
        {
            for (var parent = bone.ParentIndex; parent >= 0; parent = skeleton.Bones[parent].ParentIndex)
            {
                if (IsProp(skeleton.Bones[parent]))
                {
                    return true;
                }
            }

            return false;
        }

        return [.. skeleton.Bones.Where(bone => IsProp(bone) && !UnderProp(bone)).Select(bone => bone.Name)];
    }

    // Idle (non-additive) first, as the scene's starting animation.
    private static string DefaultClip(List<RigClip> clips)
    {
        return clips.FirstOrDefault(clip => clip.Id.StartsWith("idle", StringComparison.Ordinal) && !clip.Additive)?.Id
            ?? clips.FirstOrDefault(clip => !clip.Additive)?.Id
            ?? clips[0].Id;
    }

    private static RigSkeleton Skeleton(SkeletonInfo skeleton)
    {
        return new RigSkeleton(
            skeleton.ResourcePath,
            skeleton.Bones.Select(bone => new RigBone(
                bone.Name,
                bone.ParentIndex,
                [bone.Rest.Translation.X, bone.Rest.Translation.Y, bone.Rest.Translation.Z],
                [bone.Rest.Rotation.X, bone.Rest.Rotation.Y, bone.Rest.Rotation.Z, bone.Rest.Rotation.W],
                bone.Rest.Scale)).ToList());
    }

    /// <summary>
    /// Pose file: <c>{"arms": [...], "weapon": [...]}</c>, each a flat list
    /// of frames × tracks × [tx, ty, tz, qx, qy, qz, qw, scale] in engine
    /// parent-local space, with frame and track counts alongside.
    /// </summary>
    private static void WritePoses(string path, PoseStream arms, PoseStream? weapon)
    {
        var document = new Dictionary<string, object>
        {
            ["arms"] = StreamPoses(arms),
        };
        if (weapon is not null)
        {
            document["weapon"] = StreamPoses(weapon);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        JsonSerializer.Serialize(gzip, document);
    }

    private static object StreamPoses(PoseStream stream)
    {
        var poses = stream.ReadPoses();
        var values = new List<float>(stream.FrameCount * stream.TrackCount * 8);
        foreach (var frame in poses)
        {
            foreach (var pose in frame)
            {
                values.AddRange([pose.Translation.X, pose.Translation.Y, pose.Translation.Z, pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W, pose.Scale]);
            }
        }

        return new { frames = stream.FrameCount, tracks = stream.TrackCount, skeleton = stream.Skeleton, values };
    }

    /// <summary>
    /// Exports a chosen game or addon model as the weapon preview. It only
    /// follows the rig if its bones carry the weapon skeleton's names, so
    /// mismatches are reported.
    /// </summary>
    private static bool TryExportCustom(GameConnection connection, PreviewExporter exporter, string output, SkeletonInfo weaponSkeleton, string key, List<string> warnings, IProgress<string>? progress)
    {
        try
        {
            progress?.Report($"Exporting custom preview model {key}");
            var addon = PreviewModels.ResolveAddon(connection, key);
            var gamePath = key.StartsWith(PreviewModels.GamePrefix, StringComparison.Ordinal) ? key[PreviewModels.GamePrefix.Length..] : null;
            var rig = weaponSkeleton.Bones.Select(bone => bone.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var bones = exporter.BoneNames(gamePath ?? "", addon?.File);
            var shared = bones.Count(rig.Contains);
            if (shared == 0)
            {
                warnings.Add($"{key} shares no bones with {weaponSkeleton.ResourcePath}; it would not follow the weapon rig, so the stock mesh is used.");
                return false;
            }

            if (shared < rig.Count / 2)
            {
                warnings.Add($"{key} matches only {shared} of {rig.Count} weapon rig bones; parts of it may not move with the animation.");
            }

            var file = Path.Combine(output, "previews", "weapon.glb");
            if (addon is { } found)
            {
                exporter.ExportAddonModel(found.File, found.Addon.GamePath, file, meshFilter: null);
            }
            else
            {
                exporter.Export(gamePath!, file, meshFilter: null);
            }

            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            warnings.Add($"Custom preview model {key} failed: {error.Message}; the stock mesh is used.");
            return false;
        }
    }

    /// <param name="body">The .blend gets a third-person body rig (0.26): preview the agent's third-person meshes on it.</param>
    private static Dictionary<string, string> ExportPreviews(GameConnection connection, string output, SkeletonInfo? weaponSkeleton, List<string> warnings, IProgress<string>? progress, string? previewModel, ArmsPreview arms, bool body)
    {
        var previews = new Dictionary<string, string>();
        using var exporter = new PreviewExporter(connection.Install);

        if (previewModel is not null && weaponSkeleton is not null)
        {
            if (TryExportCustom(connection, exporter, output, weaponSkeleton, previewModel, warnings, progress))
            {
                previews["weapon"] = "previews/weapon.glb";
            }

            weaponSkeleton = previews.ContainsKey("weapon") ? null : weaponSkeleton; // Skip the stock lookup when the custom model worked.
        }

        void TryExport(string kind, string? model, params string[] meshFilter)
        {
            if (model is null)
            {
                warnings.Add($"No stock {kind} model found; the {kind} rig works without a preview mesh.");
                return;
            }

            try
            {
                progress?.Report($"Exporting {kind} preview mesh");
                var file = $"previews/{kind}.glb";
                exporter.Export(model, Path.Combine(output, file), meshFilter);
                previews[kind] = file;
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                warnings.Add($"{kind} preview from {model} failed: {error.Message}");
            }
        }

        if (weaponSkeleton is not null)
        {
            TryExport("weapon", exporter.FindWeaponModel(weaponSkeleton.ResourcePath), "body_hd");
        }

        // An empty choice means no preview mesh for that part (hands only, or bare arms).
        if (arms.Sleeves is not "")
        {
            TryExport("arms", arms.Sleeves, "firstperson_sleeves");
        }

        if (arms.Gloves is not "")
        {
            TryExport("gloves", arms.Gloves, "viewmodel");
        }

        if (body)
        {
            // The agent the sleeves come from, as seen in third person (its body and gloves meshes); the default agent without sleeves.
            TryExport("body", arms.Sleeves is { Length: > 0 } agent ? agent : PreviewExporter.ArmsModel, PreviewExporter.BodyMeshes);
        }

        return previews;
    }
}

public sealed record RigPackageResult(string InputPath, int ClipCount, int PreviewCount, IReadOnlyList<string> Warnings);

public sealed record RigDocument(
    string Schema,
    string WeaponId,
    string Label,
    string Category,
    string DefaultClip,
    RigSkeletons Skeletons,
    IReadOnlyList<RigClip> Clips,
    IReadOnlyDictionary<string, string> Previews,
    IReadOnlyList<string> Warnings);

public sealed record RigSkeletons(RigSkeleton Arms, RigSkeleton? Weapon)
{
    /// <summary>The third-person body (worldmodel.vnmskel) when the sandbox has third-person clips (0.26), else null.</summary>
    public RigSkeleton? Body { get; init; }

    /// <summary>
    /// The weapon skeleton of the third-person clips (0.26): CS2's own, also when <see cref="Weapon"/> is a custom one.
    /// The .blend gives it its own rig, riding the body's wpn.
    /// </summary>
    public RigSkeleton? WorldWeapon { get; init; }

    /// <summary>The primary and weapon skeletons of a clip: the body's for a third-person clip, else the arms'.</summary>
    public (RigSkeleton? Primary, RigSkeleton? Weapon) For(RigClip clip) =>
        clip.View == RigPackage.WorldView ? (Body, WorldWeapon) : (Arms, Weapon);
}

/// <param name="ExtraBones">A custom skeleton's bones the weapon's own skeleton lacks; null otherwise.</param>
/// <param name="HiddenBones">A custom skeleton's bones without mesh (CS2 bones the model lacks, model bones nothing is weighted to); Blender hides them.</param>
/// <param name="FreeBones">
/// Props kept apart from the gun: bones ticked "Keep apart from the gun" (0.23; see <see cref="RigPackage.PropBones"/>).
/// The .blend gives each a hidden anchor parent that cancels the gun's motion, holds it still in every clip, and
/// export writes its place relative to its game parent, so the game shows what Blender shows.
/// </param>
public sealed record RigSkeleton(string Path, IReadOnlyList<RigBone> Bones, IReadOnlyList<string>? ExtraBones = null, IReadOnlyList<string>? HiddenBones = null, IReadOnlyList<string>? FreeBones = null);

/// <param name="Rotation">Quaternion as [x, y, z, w].</param>
public sealed record RigBone(string Name, int Parent, float[] Translation, float[] Rotation, float Scale);

public sealed record RigClip(
    string Id,
    string ResourcePath,
    string Sha256,
    int Frames,
    double Duration,
    bool Additive,
    bool HasWeapon,
    string Poses,
    IReadOnlyList<RigEvent> Events)
{
    /// <summary>
    /// <see cref="RigPackage.WorldView"/> for a third-person clip (0.26), null for first person. Pose files keep the
    /// stream names "arms" (the clip's primary stream: the body in third person) and "weapon".
    /// </summary>
    public string? View { get; init; }
}

public sealed record RigEvent(string Kind, string Name, double Frame);

/// <summary>
/// The models previewed on the arm rig: an agent model's first-person sleeves
/// and a glove model. Null keeps the default; an empty string shows none.
/// </summary>
public sealed record ArmsPreview(string? Sleeves, string? Gloves)
{
    public static ArmsPreview Default => new(PreviewExporter.ArmsModel, PreviewExporter.GlovesModel);

    /// <summary>Agent models (sleeves) and glove models in the game, as vpk paths.</summary>
    public static (IReadOnlyList<string> Sleeves, IReadOnlyList<string> Gloves) Choices(GameConnection connection)
    {
        var models = connection.Archive.AllPaths().Where(path => path.StartsWith("agents/models/", StringComparison.Ordinal) && path.EndsWith(".vmdl_c", StringComparison.Ordinal)).ToList();
        var gloves = models.Where(path => path.StartsWith("agents/models/shared/arms/glove", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var sleeves = models.Where(path => !path.StartsWith("agents/models/shared/", StringComparison.Ordinal) && path.Count(c => c == '/') == 3).Order(StringComparer.Ordinal).ToList();
        return (sleeves, gloves);
    }
}
