using System.Text.Json;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Skeletons;

/// <summary>
/// A sandbox's custom weapon skeleton, kept in <c>skeleton/skeleton.json</c>:
/// the skeleton built from a model's bones, the model it came from, where the
/// model copy that targets it goes in the addon, and the clip templates
/// re-targeted to it.
/// </summary>
/// <param name="Path">The skeleton's resource path without <c>_c</c>.</param>
/// <param name="SourceModel">A <see cref="PreviewModels"/> key: <c>game:...</c> or <c>addon:&lt;addon&gt;:...</c>.</param>
/// <param name="ModelCopy">Where versions before 0.23 delivered a model copy that targets the skeleton, without <c>_c</c> (0.23 ships none; its file name is the source model's).</param>
/// <param name="FromSkeleton">The weapon's own skeleton the clips were made for.</param>
/// <param name="ExtraBones">Bones the weapon's own skeleton does not have.</param>
/// <param name="SkeletonOnlyBones">Bones of the weapon's own skeleton the model has no mesh on. A skeleton Weapon Lab builds keeps them (still animated); an addon's own skeleton may lack them.</param>
/// <param name="Corrected">Extra bones placed relative to the weapon body because their parent rests differently in the model.</param>
/// <param name="Templates">Re-targeted clip resource path → SHA-256 of its template.</param>
/// <param name="FromAddon">The skeleton and model are the target addon's own files, used as they are (nothing is built or copied).</param>
/// <param name="ModelHierarchy">Built with the model's own hierarchy (<see cref="SkeletonBuilder.FromModel"/>) instead of CS2's.</param>
/// <param name="UnweightedBones">The model's bones no mesh is weighted to (structural ones left out); null when unknown.</param>
/// <param name="SkippedBones">Blender FBX leaf bones the skeleton leaves out.</param>
public sealed record CustomSkeletonSetup(
    string Path,
    string SourceModel,
    string ModelCopy,
    string FromSkeleton,
    IReadOnlyList<string> Bones,
    IReadOnlyList<string> ExtraBones,
    IReadOnlyList<string> SkeletonOnlyBones,
    IReadOnlyList<string> Corrected,
    IReadOnlyDictionary<string, string> Templates,
    string SkeletonSha256,
    DateTimeOffset CreatedUtc,
    bool FromAddon = false,
    bool ModelHierarchy = false,
    IReadOnlyList<string>? UnweightedBones = null,
    IReadOnlyList<string>? SkippedBones = null)
{
    /// <summary>Bones the rig hides: CS2 bones the model has no mesh on, and model bones no mesh is weighted to.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public IReadOnlyList<string> HiddenBones => [.. SkeletonOnlyBones.Concat(UnweightedBones ?? []).Distinct(StringComparer.Ordinal)];
}

/// <summary>How a model's bones compare with the weapon's own skeleton, shown before a skeleton is made.</summary>
public sealed record CustomSkeletonPreview(
    string FromSkeleton,
    IReadOnlyList<string> SharedBones,
    IReadOnlyList<string> ExtraBones,
    IReadOnlyList<string> SkeletonOnlyBones,
    IReadOnlyList<string> Corrected,
    IReadOnlyList<string> ModelSkeletonRefs)
{
    /// <summary>Blender FBX leaf bones (<c>*_end</c>) the skeleton leaves out.</summary>
    public IReadOnlyList<string> SkippedBones { get; init; } = [];

    /// <summary>Shared bones the model hangs under another parent than CS2's skeleton, e.g. "bolt: slide → weapon_offset".</summary>
    public IReadOnlyList<string> Reparented { get; init; } = [];
}

/// <summary>
/// Custom weapon skeletons, built on what the skeleton test proved in game
/// (2026-10-24): a skeleton made from a model's bones, a copy of the model
/// that targets it, and every clip of the weapon re-targeted to it and
/// delivered at custom paths. Clips left on the old skeleton leave the
/// weapon's parts still, so a weapon with a custom skeleton delivers all of
/// its clips. Inside the sandbox, the re-targeted clips become the
/// templates Blender builds from, so the extra bones are keyed and exported
/// like any other bone.
/// </summary>
public static class CustomSkeletons
{
    public const string Folder = "skeleton";
    private const string SetupFile = Folder + "/skeleton.json";
    private const string SkeletonFile = Folder + "/skeleton.vnmskel_c";
    private const string TemplatesFolder = Folder + "/templates";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static string DefaultPath(string weaponId) => $"animation/skeletons/weapons/weaponlab_{weaponId}.vnmskel";

    /// <summary>The model copy's addon path: <c>models/weapons/weaponlab/&lt;weapon&gt;/&lt;model&gt;.vmdl</c>.</summary>
    public static string ModelCopyPath(string weaponId, string sourceModel)
    {
        var name = System.IO.Path.GetFileName(ModelPath(sourceModel));
        return $"models/weapons/weaponlab/{weaponId}/{(name.EndsWith("_c", StringComparison.Ordinal) ? name[..^2] : name)}";
    }

    public static CustomSkeletonSetup? Load(SandboxInfo sandbox)
    {
        var path = System.IO.Path.Combine(sandbox.FolderPath, SetupFile);
        return File.Exists(path) ? JsonSerializer.Deserialize<CustomSkeletonSetup>(File.ReadAllText(path), Json) : null;
    }

    /// <summary>The compiled skeleton built for the sandbox.</summary>
    public static byte[] SkeletonBytes(SandboxInfo sandbox, CustomSkeletonSetup setup)
    {
        var bytes = File.ReadAllBytes(System.IO.Path.Combine(sandbox.FolderPath, SkeletonFile));
        if (GameArchive.Sha256(bytes) != setup.SkeletonSha256)
        {
            throw new InvalidDataException($"{sandbox.Manifest.WeaponId}'s custom skeleton file changed since it was made. Make the custom skeleton again.");
        }

        return bytes;
    }

    public static SkeletonInfo ReadSkeleton(SandboxInfo sandbox, CustomSkeletonSetup setup) =>
        SkeletonInfo.Read(SkeletonBytes(sandbox, setup), setup.Path);

    /// <summary>The template Blender builds from and exports onto: the re-targeted one when the clip has one.</summary>
    public static (string Path, string Sha256) Template(SandboxInfo sandbox, SandboxClip clip, CustomSkeletonSetup? setup)
    {
        if (setup is not null && setup.Templates.TryGetValue(clip.ResourcePath, out var sha))
        {
            return (AtomicFile.SafeJoin(sandbox.FolderPath, $"{TemplatesFolder}/{clip.ResourcePath}"), sha);
        }

        return (AtomicFile.SafeJoin(sandbox.FolderPath, clip.Template), clip.Sha256);
    }

    /// <summary>
    /// The skeleton most of the weapon's game clips animate the weapon with, e.g.
    /// <c>animation/skeletons/weapons/galil.vnmskel</c>. Read from the game, since
    /// templates taken from an addon may already be on a custom skeleton.
    /// </summary>
    public static string? WeaponSkeleton(SandboxInfo sandbox, GameConnection connection)
    {
        return sandbox.Manifest.Clips
            .Where(clip => connection.Archive.Contains(clip.ResourcePath))
            .SelectMany(clip => connection.ReadClip(clip.ResourcePath).SecondarySkeletons)
            .GroupBy(skeleton => skeleton)
            .OrderByDescending(group => group.Count())
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    /// <summary>Compares a model's bones with the weapon's own skeleton.</summary>
    public static CustomSkeletonPreview Preview(SandboxInfo sandbox, GameConnection connection, string sourceModel, string? targetAddon)
    {
        var from = WeaponSkeleton(sandbox, connection) ?? throw new InvalidDataException($"{sandbox.Manifest.WeaponId}'s clips have no weapon skeleton.");
        var skeleton = connection.ReadSkeleton(from);
        var animated = skeleton.Bones.Select(bone => bone.Name).ToList();
        var model = ReadModel(connection, sourceModel, targetAddon);
        var leaves = SkeletonBuilder.LeafBones(model);
        var names = model.Bones.Select(bone => bone.Name).Where(name => !leaves.Contains(name)).ToList();
        string? ParentName(IReadOnlyList<SkeletonBone> bones, string name) =>
            bones.FirstOrDefault(bone => bone.Name == name) is { ParentIndex: >= 0 } bone ? bones[bone.ParentIndex].Name : null;
        return new CustomSkeletonPreview(
            from,
            names.Where(animated.Contains).ToList(),
            names.Where(name => !animated.Contains(name)).ToList(),
            animated.Where(name => !names.Contains(name)).ToList(),
            SkeletonBuilder.AlignTo(model, skeleton).Corrected,
            model.SkeletonRefs)
        {
            SkippedBones = leaves,
            Reparented = names.Where(animated.Contains)
                .Select(name => (Name: name, Stock: ParentName(skeleton.Bones, name), Own: ParentName(model.Bones, name)))
                .Where(item => item.Stock != item.Own)
                .Select(item => $"{item.Name}: {item.Stock ?? "root"} → {item.Own ?? "root"}")
                .ToList(),
        };
    }

    /// <summary>
    /// Builds the skeleton from the model's bones, re-targets every clip
    /// template to it, and moves exported clips onto it so their edits are
    /// kept. Replaces an earlier custom skeleton of the sandbox. The .blend
    /// has to be built again afterwards.
    /// </summary>
    /// <param name="sourceModel">A game model, or a model of the target addon (the game cannot load another addon's files).</param>
    /// <param name="modelHierarchy">Follow the model's hierarchy for shared bones (<see cref="SkeletonBuilder.FromModel"/>); clips keep their motion relative to each bone's rest.</param>
    public static CustomSkeletonSetup Create(SandboxInfo sandbox, GameConnection connection, string sourceModel, string? targetAddon, string? path = null, IProgress<string>? progress = null, bool modelHierarchy = false)
    {
        var weaponId = sandbox.Manifest.WeaponId;
        path ??= DefaultPath(weaponId);
        if (!path.StartsWith("animation/skeletons/", StringComparison.Ordinal) || !path.EndsWith(".vnmskel", StringComparison.Ordinal))
        {
            throw new ArgumentException("A skeleton path looks like animation/skeletons/weapons/<name>.vnmskel.");
        }

        if (connection.Archive.Contains(path + "_c"))
        {
            throw new ArgumentException($"{path} is one of CS2's own skeletons; Workshop addons cannot replace it. Pick a new name.");
        }

        var from = WeaponSkeleton(sandbox, connection) ?? throw new InvalidDataException($"{weaponId}'s clips have no weapon skeleton.");
        var animated = connection.ReadSkeleton(from);
        var model = ReadModel(connection, sourceModel, targetAddon);
        if (!model.Bones.Any(bone => animated.Bones.Any(known => known.Name == bone.Name)))
        {
            throw new InvalidDataException($"{sourceModel} shares no bones with {from}, so none of the weapon's animation would reach it.");
        }

        progress?.Report($"Building {path}");
        var aligned = modelHierarchy ? SkeletonBuilder.FromModel(model, animated) : SkeletonBuilder.AlignTo(model, animated);
        var bones = aligned.Bones;
        var skeletonBytes = SkeletonBuilder.Build(connection.Archive.ReadBytes(from + "_c"), path, bones);
        var skeletonOnly = animated.Bones.Select(bone => bone.Name).Where(name => model.Bones.All(bone => bone.Name != name)).ToList();

        progress?.Report("Checking which bones carry mesh");
        var weighted = ModelWeights.WeightedBones(connection, sourceModel);
        var leaves = SkeletonBuilder.LeafBones(model);
        var unweighted = weighted is null ? null : bones
            .Select(bone => bone.Name)
            .Where(name => model.Bones.Any(bone => bone.Name == name) && !weighted.Contains(name) && !ModelWeights.Structural.Contains(name))
            .ToList();
        var setup = Install(sandbox, animated, path, sourceModel, ModelCopyPath(weaponId, sourceModel), bones, skeletonBytes, skeletonOnly, aligned.Corrected, fromAddon: false, progress);
        setup = setup with { ModelHierarchy = modelHierarchy, UnweightedBones = unweighted, SkippedBones = leaves };
        Save(sandbox, setup);
        return setup;
    }

    private static void Save(SandboxInfo sandbox, CustomSkeletonSetup setup) =>
        AtomicFile.WriteAllText(System.IO.Path.Combine(sandbox.FolderPath, SetupFile), JsonSerializer.Serialize(setup, Json) + "\n");

    /// <summary>
    /// The custom skeleton the target addon already gives this weapon: the
    /// addon's own clips for it (<c>animation/anims/custom/&lt;weapon&gt;/</c>)
    /// play on a skeleton CS2 does not ship and the addon has (made by hand, by
    /// another tool, or delivered earlier). The model is one of the addon's
    /// models that targets it, or null. Paths are without <c>_c</c>. Vdata is
    /// not read: it belongs to the VData tab alone (user decision 2026-10-26).
    /// </summary>
    public static (string Skeleton, string? Model)? FindInAddon(GameConnection connection, string? targetAddon, string weaponId)
    {
        var addon = connection.Addons.FirstOrDefault(item => string.Equals(item.Name, targetAddon, StringComparison.OrdinalIgnoreCase));
        if (addon is null)
        {
            return null;
        }

        var target = Delivery.AddonTarget.For(connection.Install, addon);
        var skeleton = Delivery.StockReset.For(connection, target, weaponId).Skeletons.FirstOrDefault();

        // After Set to CS2 default the clips are gone but Weapon Lab's skeleton stays while a model uses it (bug 2026-10-31).
        if (skeleton is null && File.Exists(Delivery.AddonManifest.Resolve(target, DefaultPath(weaponId) + "_c")))
        {
            skeleton = DefaultPath(weaponId) + "_c";
        }

        if (skeleton is null)
        {
            return null;
        }

        var path = skeleton[..^2];
        var model = Addons.AddonChecks.FilesUsing(target, path, new HashSet<string>())
            .Where(key => key.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        return (path, model?[..^2]);
    }

    /// <summary>
    /// Uses the target addon's own custom skeleton and model (see
    /// <see cref="FindInAddon"/>) as the sandbox's skeleton: clip templates
    /// and exports move onto it, and delivery leaves both files as they are.
    /// </summary>
    public static CustomSkeletonSetup Adopt(SandboxInfo sandbox, GameConnection connection, string targetAddon, string skeletonPath, string? modelPath, IProgress<string>? progress = null)
    {
        if (modelPath is null)
        {
            // No model of the addon targets it: the skeleton file alone, used in place.
            var addonFolder = connection.Addons.First(item => string.Equals(item.Name, targetAddon, StringComparison.OrdinalIgnoreCase)).GamePath;
            return UseFile(sandbox, connection, targetAddon, AtomicFile.SafeJoin(addonFolder, skeletonPath + "_c"), progress);
        }

        var addon = connection.Addons.First(item => string.Equals(item.Name, targetAddon, StringComparison.OrdinalIgnoreCase));
        var skeletonBytes = File.ReadAllBytes(AtomicFile.SafeJoin(addon.GamePath, skeletonPath + "_c"));
        var bones = SkeletonInfo.Read(skeletonBytes, skeletonPath).Bones;
        var from = WeaponSkeleton(sandbox, connection) ?? throw new InvalidDataException($"{sandbox.Manifest.WeaponId}'s clips have no weapon skeleton.");
        var animated = connection.ReadSkeleton(from);
        if (!bones.Any(bone => animated.Bones.Any(known => known.Name == bone.Name)))
        {
            throw new InvalidDataException($"{skeletonPath} shares no bones with {from}, so none of the weapon's animation would reach it.");
        }

        var refs = ModelSkeleton.Read(File.ReadAllBytes(AtomicFile.SafeJoin(addon.GamePath, modelPath + "_c")), modelPath + "_c").SkeletonRefs;
        if (!refs.Contains(skeletonPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{modelPath} does not target {skeletonPath} (it targets {(refs.Count == 0 ? "no skeleton" : string.Join(", ", refs))}).");
        }

        var skeletonOnly = animated.Bones.Select(bone => bone.Name).Where(name => bones.All(bone => bone.Name != name)).ToList();
        return Install(sandbox, animated, skeletonPath, $"{PreviewModels.AddonPrefix}{addon.Name}:{modelPath}_c", modelPath, bones, skeletonBytes, skeletonOnly, [], fromAddon: true, progress);
    }

    /// <summary>Marks a profile choice that names a skeleton file instead of a model (see <see cref="UseFile"/>).</summary>
    public const string FilePrefix = "skeleton:";

    /// <summary>
    /// Uses a compiled skeleton file (<c>.vnmskel_c</c>) as the weapon's
    /// skeleton, e.g. one made in the Workshop Tools or another program. A file
    /// inside the target addon is used where it is and never written; any other
    /// file is copied into the addon at its own resource path (its m_ID) on
    /// delivery. No model comes with it: point the slot's m_szWorldModel at a
    /// model that targets it in the VData tab.
    /// </summary>
    public static CustomSkeletonSetup UseFile(SandboxInfo sandbox, GameConnection connection, string? targetAddon, string file, IProgress<string>? progress = null)
    {
        var full = System.IO.Path.GetFullPath(file);
        if (!full.EndsWith(".vnmskel_c", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Pick a compiled skeleton (.vnmskel_c).");
        }

        var bytes = File.ReadAllBytes(full);
        var addon = connection.Addons.FirstOrDefault(item => string.Equals(item.Name, targetAddon, StringComparison.OrdinalIgnoreCase));
        var addonRoot = addon is null ? null : System.IO.Path.GetFullPath(addon.GamePath) + System.IO.Path.DirectorySeparatorChar;
        var inAddon = addonRoot is not null && full.StartsWith(addonRoot, StringComparison.OrdinalIgnoreCase);
        var path = inAddon ? full[addonRoot!.Length..^2].Replace('\\', '/') : SkeletonId(bytes);
        if (!path.StartsWith("animation/skeletons/", StringComparison.Ordinal) || !path.EndsWith(".vnmskel", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{System.IO.Path.GetFileName(full)} names itself {path}; a skeleton belongs under animation/skeletons/.");
        }

        if (!inAddon && connection.Archive.Contains(path + "_c"))
        {
            throw new ArgumentException($"{path} is one of CS2's own skeletons; Workshop addons cannot replace it. Use CS2 default for this slot instead.");
        }

        var bones = SkeletonInfo.Read(bytes, path).Bones;
        var from = WeaponSkeleton(sandbox, connection) ?? throw new InvalidDataException($"{sandbox.Manifest.WeaponId}'s clips have no weapon skeleton.");
        var animated = connection.ReadSkeleton(from);
        if (!bones.Any(bone => animated.Bones.Any(known => known.Name == bone.Name)))
        {
            throw new InvalidDataException($"{path} shares no bones with {from}, so none of the weapon's animation would reach it.");
        }

        var skeletonOnly = animated.Bones.Select(bone => bone.Name).Where(name => bones.All(bone => bone.Name != name)).ToList();
        return Install(sandbox, animated, path, FilePrefix + full, "", bones, bytes, skeletonOnly, [], fromAddon: inAddon, progress);
    }

    /// <summary>A compiled skeleton's own resource path (m_ID).</summary>
    public static string SkeletonId(byte[] bytes)
    {
        using var resource = new ValveResourceFormat.Resource { FileName = "skeleton.vnmskel_c" };
        resource.Read(new MemoryStream(bytes));
        return resource.DataBlock is ValveResourceFormat.ResourceTypes.BinaryKV3 data && data.Data.Root.TryGetValue("m_ID", out var id)
            ? id.ToString() ?? ""
            : throw new InvalidDataException("Not a compiled skeleton.");
    }

    /// <summary>Compares a skeleton file's bones with the weapon's own skeleton.</summary>
    public static CustomSkeletonPreview PreviewFile(SandboxInfo sandbox, GameConnection connection, string file)
    {
        var bytes = File.ReadAllBytes(file);
        var names = SkeletonInfo.Read(bytes, SkeletonId(bytes)).Bones.Select(bone => bone.Name).ToList();
        var from = WeaponSkeleton(sandbox, connection) ?? throw new InvalidDataException($"{sandbox.Manifest.WeaponId}'s clips have no weapon skeleton.");
        var animated = connection.ReadSkeleton(from).Bones.Select(bone => bone.Name).ToList();
        return new CustomSkeletonPreview(from, names.Where(animated.Contains).ToList(), names.Where(name => !animated.Contains(name)).ToList(),
            animated.Where(name => !names.Contains(name)).ToList(), [], []);
    }

    /// <summary>Re-targets the clip templates and exports to the skeleton and writes the sandbox's skeleton folder.</summary>
    private static CustomSkeletonSetup Install(
        SandboxInfo sandbox, SkeletonInfo animated, string path, string sourceModel, string modelCopy, IReadOnlyList<SkeletonBone> bones,
        byte[] skeletonBytes, IReadOnlyList<string> skeletonOnly, IReadOnlyList<string> corrected, bool fromAddon, IProgress<string>? progress)
    {
        var from = animated.ResourcePath;
        var animatedNames = animated.Bones.Select(bone => bone.Name).ToList();
        var templates = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var clip in sandbox.Manifest.Clips)
        {
            var bytes = File.ReadAllBytes(AtomicFile.SafeJoin(sandbox.FolderPath, clip.Template));
            if (ClipReader.Read(bytes, clip.ResourcePath).SecondarySkeletons.Contains(from))
            {
                progress?.Report($"Re-targeting {RigPackage.ClipId(clip.ResourcePath)}");
                templates[clip.ResourcePath] = ClipRetarget.Retarget(bytes, clip.ResourcePath, from, animatedNames, path, bones, RestTransfer(animated.Bones, bones));
            }
        }

        // Exports are on the sandbox's current weapon skeleton (an earlier custom one, or the weapon's own).
        var previous = Load(sandbox);
        var current = previous is null ? animated : ReadSkeleton(sandbox, previous);
        var exports = MoveExports(sandbox, current, path, bones, progress);

        var setup = new CustomSkeletonSetup(
            path,
            sourceModel,
            modelCopy,
            from,
            bones.Select(bone => bone.Name).ToList(),
            bones.Select(bone => bone.Name).Where(name => !animatedNames.Contains(name)).ToList(),
            skeletonOnly,
            corrected,
            templates.ToDictionary(item => item.Key, item => GameArchive.Sha256(item.Value), StringComparer.Ordinal),
            GameArchive.Sha256(skeletonBytes),
            DateTimeOffset.UtcNow,
            fromAddon);

        // Everything is built and verified in memory; now replace the sandbox's skeleton folder.
        var folder = System.IO.Path.Combine(sandbox.FolderPath, Folder);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        AtomicFile.WriteAllBytes(System.IO.Path.Combine(sandbox.FolderPath, SkeletonFile), skeletonBytes);
        foreach (var (resourcePath, bytes) in templates)
        {
            AtomicFile.WriteAllBytes(AtomicFile.SafeJoin(sandbox.FolderPath, $"{TemplatesFolder}/{resourcePath}"), bytes);
        }

        exports();
        AtomicFile.WriteAllText(System.IO.Path.Combine(sandbox.FolderPath, SetupFile), JsonSerializer.Serialize(setup, Json) + "\n");
        return setup;
    }

    /// <summary>
    /// Takes the custom skeleton out of the sandbox: templates go back to the
    /// game's clips and exported clips move back to the weapon's own skeleton
    /// (motion of the extra bones is dropped). The .blend has to be built again.
    /// </summary>
    public static bool Remove(SandboxInfo sandbox, GameConnection connection, IProgress<string>? progress = null)
    {
        var setup = Load(sandbox);
        if (setup is null)
        {
            return false;
        }

        var animated = connection.ReadSkeleton(setup.FromSkeleton);
        var exports = MoveExports(sandbox, ReadSkeleton(sandbox, setup), setup.FromSkeleton, animated.Bones, progress);
        exports();
        Directory.Delete(System.IO.Path.Combine(sandbox.FolderPath, Folder), recursive: true);
        return true;
    }

    /// <summary>
    /// The skeleton's source model (an addon model) was compiled again after the skeleton was made, e.g. with
    /// bones added in ModelDoc (0.24): Build .blend then makes the skeleton again from it.
    /// </summary>
    public static bool ModelChangedSince(GameConnection connection, CustomSkeletonSetup setup)
    {
        if (setup.FromAddon || !setup.SourceModel.StartsWith(PreviewModels.AddonPrefix, StringComparison.Ordinal)
            || PreviewModels.ResolveAddon(connection, setup.SourceModel) is not { } resolved || !File.Exists(resolved.File))
        {
            return false;
        }

        return File.GetLastWriteTimeUtc(resolved.File) > setup.CreatedUtc.UtcDateTime;
    }

    public static ModelSkeleton ReadModel(GameConnection connection, string sourceModel, string? targetAddon) =>
        ModelSkeleton.Read(ReadModelBytes(connection, sourceModel, targetAddon), ModelPath(sourceModel));

    private static byte[] ReadModelBytes(GameConnection connection, string sourceModel, string? targetAddon)
    {
        if (sourceModel.StartsWith(PreviewModels.GamePrefix, StringComparison.Ordinal))
        {
            return connection.Archive.ReadBytes(ModelPath(sourceModel));
        }

        var (addon, file) = PreviewModels.ResolveAddon(connection, sourceModel)
            ?? throw new ArgumentException($"Not a model key: {sourceModel}");
        if (!string.Equals(addon.Name, targetAddon, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{sourceModel} belongs to {addon.Name}, not the target addon {targetAddon ?? "(none)"}; the game only loads an addon's own files. Pick a CS2 model or one of the target addon's.");
        }

        return File.ReadAllBytes(file);
    }

    /// <summary>The model's path inside the game or its addon.</summary>
    private static string ModelPath(string sourceModel)
    {
        if (sourceModel.StartsWith(PreviewModels.GamePrefix, StringComparison.Ordinal))
        {
            return sourceModel[PreviewModels.GamePrefix.Length..];
        }

        var rest = sourceModel.StartsWith(PreviewModels.AddonPrefix, StringComparison.Ordinal) ? sourceModel[PreviewModels.AddonPrefix.Length..] : sourceModel;
        return rest[(rest.IndexOf(':') + 1)..];
    }

    /// <summary>
    /// Keeps a bone's motion relative to its rest when the new skeleton rests
    /// it elsewhere (a model-hierarchy skeleton puts a shared bone where the
    /// model has it): the clip's offset from the old rest is applied to the new
    /// rest. Null when no shared bone's rest changed, so clips move unchanged.
    /// </summary>
    private static Func<int, string, BonePose, BonePose>? RestTransfer(IReadOnlyList<SkeletonBone> from, IReadOnlyList<SkeletonBone> to)
    {
        var old = from.ToDictionary(bone => bone.Name, bone => bone.Rest, StringComparer.Ordinal);
        var moved = to
            .Where(bone => old.TryGetValue(bone.Name, out var rest) && BonePose.Differ(rest, bone.Rest, 1e-4f))
            .ToDictionary(bone => bone.Name, bone => (Old: old[bone.Name], New: bone.Rest), StringComparer.Ordinal);
        if (moved.Count == 0)
        {
            return null;
        }

        return (_, name, pose) => moved.TryGetValue(name, out var rest)
            ? BonePose.Compose(rest.New, BonePose.Compose(BonePose.Inverse(rest.Old), pose))
            : pose;
    }

    /// <summary>
    /// Re-targets every sandbox export to another skeleton in memory; the
    /// returned action writes them and their updated reports.
    /// </summary>
    private static Action MoveExports(SandboxInfo sandbox, SkeletonInfo current, string toSkeleton, IReadOnlyList<SkeletonBone> toBones, IProgress<string>? progress)
    {
        var writes = new List<(string Output, byte[] Bytes, ClipExportReport Report)>();
        var names = current.Bones.Select(bone => bone.Name).ToList();
        foreach (var clip in sandbox.Manifest.Clips)
        {
            var clipId = RigPackage.ClipId(clip.ResourcePath);
            if (ClipExporter.LastExport(sandbox, clipId) is not { SandboxOutput: { } output } report || !File.Exists(output))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(output);
            if (!ClipReader.Read(bytes, clip.ResourcePath).SecondarySkeletons.Contains(current.ResourcePath))
            {
                continue;
            }

            progress?.Report($"Moving export {clipId} to {System.IO.Path.GetFileName(toSkeleton)}");
            var moved = ClipRetarget.Retarget(bytes, clip.ResourcePath, current.ResourcePath, names, toSkeleton, toBones, RestTransfer(current.Bones, toBones));
            writes.Add((output, moved, report with { OutputSha256 = GameArchive.Sha256(moved) }));
        }

        return () =>
        {
            foreach (var (output, bytes, report) in writes)
            {
                AtomicFile.WriteAllBytes(output, bytes);
                ClipExporter.SaveReport(sandbox, report);
            }
        };
    }
}
