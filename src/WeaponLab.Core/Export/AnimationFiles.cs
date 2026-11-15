using System.IO.Compression;
using System.Text.Json;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Skeletons;

namespace WeaponLab.Core.Export;

/// <summary>One stream of an animation: the skeleton it was made on, its bones (names and parent names) and its poses per frame.</summary>
public sealed record AnimationStream(string Skeleton, IReadOnlyList<string> Bones, IReadOnlyList<string?> Parents, BonePose[][] Poses)
{
    public int Frames => Poses.Length;
}

/// <summary>An animation read from a file: its arms stream (CS2's viewmodel skeleton) and its weapon stream, whichever it has.</summary>
public sealed record AnimationSource(string Name, AnimationStream? Arms, AnimationStream? Weapon, bool Additive)
{
    public int Frames => Math.Max(Arms?.Frames ?? 0, Weapon?.Frames ?? 0);
}

/// <summary>What an import took: per stream ("arms", "weapon"), the bones that got the file's motion and those that kept the clip's.</summary>
public sealed record AnimationMatch(int Frames, IReadOnlyDictionary<string, IReadOnlyList<string>> Matched, IReadOnlyDictionary<string, IReadOnlyList<string>> Kept)
{
    public int MatchedCount => Matched.Values.Sum(names => names.Count);
}

/// <summary>
/// Animation files in and out of Weapon Lab (0.25): compiled clips (.vnmclip_c) and baked pose files (.json.gz, the
/// format Blender's export writes, with bone names since 0.25) are read here and mapped onto one of a weapon's clips
/// by bone name. A bone takes the file's motion when the file has a bone of that name with the same parent (poses are
/// parent-local); every other bone keeps the clip's own motion (its build reference). The result is a pose sample
/// like Blender's, so <see cref="ClipExporter.Export"/> turns it into the clip's export and Blender can load it back.
/// </summary>
public static class AnimationFiles
{
    public const string ArmsStream = "arms";
    public const string WeaponStream = "weapon";

    /// <summary>Skeletons by resource path: the sandbox's custom one, CS2's, or the target addon's file; null when unknown.</summary>
    public static Func<string, SkeletonInfo?> Skeletons(GameConnection connection, SandboxInfo? sandbox, AddonTarget? target)
    {
        var custom = sandbox is null ? null : CustomSkeletons.Load(sandbox);
        return path =>
        {
            var plain = path.EndsWith("_c", StringComparison.Ordinal) ? path[..^2] : path;
            try
            {
                if (custom is not null && sandbox is not null && custom.Path == plain)
                {
                    return CustomSkeletons.ReadSkeleton(sandbox, custom);
                }

                if (connection.Archive.Contains(plain + "_c"))
                {
                    return connection.ReadSkeleton(plain);
                }

                var file = target is null ? null : Path.Combine(target.GamePath, (plain + "_c").Replace('/', Path.DirectorySeparatorChar));
                return file is not null && File.Exists(file) ? SkeletonInfo.Read(File.ReadAllBytes(file), plain) : null;
            }
            catch (Exception error) when (error is InvalidDataException or IOException)
            {
                return null;
            }
        };
    }

    /// <summary>A compiled clip: its primary stream (arms) and the secondary on <paramref name="weaponSkeleton"/> (else its first).</summary>
    public static AnimationSource FromClip(byte[] bytes, string name, Func<string, SkeletonInfo?> skeletons, string? weaponSkeleton)
    {
        var resourcePath = name.EndsWith("_c", StringComparison.Ordinal) ? name : name + "_c";
        var info = ClipReader.Read(bytes, resourcePath);
        using var template = ClipTemplate.Open(bytes, resourcePath);
        AnimationStream? Stream(PoseStream? stream)
        {
            if (stream is null)
            {
                return null;
            }

            var skeleton = skeletons(stream.Skeleton)
                ?? throw new InvalidDataException($"{Path.GetFileName(name)}: its skeleton {stream.Skeleton} is in neither CS2, the addon nor the sandbox, so its bones have no names.");
            return new AnimationStream(stream.Skeleton, skeleton.Bones.Select(bone => bone.Name).ToList(),
                skeleton.Bones.Select(bone => bone.ParentIndex >= 0 ? skeleton.Bones[bone.ParentIndex].Name : null).ToList(), stream.ReadPoses());
        }

        var secondary = template.Secondary.FirstOrDefault(stream => weaponSkeleton is not null && SamePath(stream.Skeleton, weaponSkeleton)) ?? template.Secondary.FirstOrDefault();
        return new AnimationSource(Path.GetFileNameWithoutExtension(name.Replace(".vnmclip_c", ".vnmclip", StringComparison.Ordinal)), Stream(template.Primary), Stream(secondary), info.IsAdditive);
    }

    /// <summary>
    /// A pose file: Blender's sample ("arms", "weapon"), Weapon Lab's export of one, or `weaponlab dump-poses`
    /// ("primary", "secondary0"). Bone names come from the file (0.25 samples) or from its skeleton.
    /// </summary>
    public static AnimationSource FromPoseFile(string path, Func<string, SkeletonInfo?> skeletons)
    {
        var poses = PoseFile.Read(path);
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var document = JsonDocument.Parse(gzip);
        AnimationStream? Stream(params string[] keys)
        {
            foreach (var key in keys)
            {
                if (!document.RootElement.TryGetProperty(key, out var element) || element.ValueKind != JsonValueKind.Object || !poses.TryGetValue(key, out var frames))
                {
                    continue;
                }

                var skeletonPath = element.TryGetProperty("skeleton", out var skeletonValue) ? skeletonValue.GetString() ?? "" : "";
                if (element.TryGetProperty("bones", out var bones) && bones.ValueKind == JsonValueKind.Array)
                {
                    var names = bones.EnumerateArray().Select(value => value.GetString() ?? "").ToList();
                    IReadOnlyList<string?> parents = element.TryGetProperty("parents", out var parentValues) && parentValues.ValueKind == JsonValueKind.Array
                        ? parentValues.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null).ToList()
                        : skeletons(skeletonPath) is { } known ? ParentsByName(names, known) : names.Select(_ => (string?)null).ToList();
                    return new AnimationStream(skeletonPath, names, parents, frames);
                }

                var skeleton = skeletons(skeletonPath)
                    ?? throw new InvalidDataException($"{Path.GetFileName(path)}: stream {key} has no bone names and its skeleton {skeletonPath} is unknown.");
                return new AnimationStream(skeletonPath, skeleton.Bones.Select(bone => bone.Name).ToList(),
                    skeleton.Bones.Select(bone => bone.ParentIndex >= 0 ? skeleton.Bones[bone.ParentIndex].Name : null).ToList(), frames);
            }

            return null;
        }

        return new AnimationSource(Path.GetFileName(path).Replace(".json.gz", "", StringComparison.Ordinal), Stream(ArmsStream, "primary"), Stream(WeaponStream, "secondary0"), Additive: false);
    }

    /// <summary>Reads a .vnmclip_c or a .json.gz pose file.</summary>
    public static AnimationSource Read(string path, Func<string, SkeletonInfo?> skeletons, string? weaponSkeleton) =>
        path.EndsWith(".vnmclip_c", StringComparison.OrdinalIgnoreCase)
            ? FromClip(File.ReadAllBytes(path), Path.GetFileName(path), skeletons, weaponSkeleton)
            : path.EndsWith(".json.gz", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase)
                ? FromPoseFile(path, skeletons)
                : throw new ArgumentException($"{Path.GetFileName(path)}: Weapon Lab reads .vnmclip_c and .json.gz here; .blend and .fbx go through Blender.");

    /// <summary>
    /// Maps an animation onto one of the sandbox's clips by bone name and writes the result as a pose sample
    /// (<paramref name="output"/>) on the clip's skeletons, <see cref="AnimationSource.Frames"/> long. Needs the .blend's
    /// rig package (rig.json and the build reference). Additive clips are refused on either side.
    /// </summary>
    public static AnimationMatch ToSample(SandboxInfo sandbox, string clipId, AnimationSource source, string output)
    {
        var rig = RigPackage.ReadRig(sandbox) ?? throw new InvalidOperationException($"{sandbox.Manifest.WeaponId} has no .blend build yet (no rig.json). Build .blend first.");
        var clip = rig.Clips.FirstOrDefault(item => item.Id == clipId) ?? throw new ArgumentException($"{clipId} is not part of {sandbox.Manifest.WeaponId}'s .blend build.");
        if (clip.Additive || source.Additive)
        {
            throw new ArgumentException($"{(clip.Additive ? clipId : source.Name)} is an additive clip (it adds onto another pose); additive clips cannot be imported yet.");
        }

        var referencePath = Path.Combine(RigPackage.InputPath(sandbox), "reference", clipId + ".json.gz");
        if (!File.Exists(referencePath))
        {
            throw new InvalidOperationException($"{clipId} has no build reference. In Weapon Lab click Update for export (or Build .blend), then import again.");
        }

        var reference = PoseFile.Read(referencePath);
        var frames = Math.Max(1, source.Frames);
        var streams = new Dictionary<string, (string Skeleton, BonePose[][] Poses)>(StringComparer.Ordinal);
        var bones = new Dictionary<string, (IReadOnlyList<string> Names, IReadOnlyList<string?> Parents)>(StringComparer.Ordinal);
        var matched = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var kept = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var (primary, weapon) = rig.Skeletons.For(clip); // The body's for a third-person clip (0.26).
        foreach (var (name, ours, theirs) in new[] { (ArmsStream, primary, source.Arms), (WeaponStream, weapon, source.Weapon) })
        {
            if (ours is null || !reference.TryGetValue(name, out var basePoses))
            {
                continue;
            }

            var names = ours.Bones.Select(bone => bone.Name).ToList();
            var parents = ours.Bones.Select(bone => bone.Parent >= 0 ? ours.Bones[bone.Parent].Name : null).ToList();
            var poses = Fit(basePoses, frames);
            var took = new List<string>();
            if (theirs is not null)
            {
                var index = theirs.Bones.Select((bone, position) => (bone, position)).GroupBy(item => item.bone, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().position, StringComparer.Ordinal);
                var source_ = Fit(theirs.Poses, frames);
                for (var track = 0; track < names.Count; track++)
                {
                    if (!index.TryGetValue(names[track], out var from) || theirs.Parents[from] != parents[track])
                    {
                        continue;
                    }

                    for (var frame = 0; frame < frames; frame++)
                    {
                        poses[frame][track] = source_[frame][from];
                    }

                    took.Add(names[track]);
                }
            }

            streams[name] = (ours.Path, poses);
            bones[name] = (names, parents);
            matched[name] = took;
            kept[name] = names.Where(bone => !took.Contains(bone)).ToList();
        }

        if (matched.Values.All(names => names.Count == 0))
        {
            throw new ArgumentException($"None of {source.Name}'s bones match {sandbox.Manifest.WeaponId}'s by name and parent, so nothing would change.");
        }

        PoseFile.Write(output, streams, bones);
        return new AnimationMatch(frames, matched, kept);
    }

    /// <summary>The clip as it plays now (its sandbox export, else its template), decoded with bone names, as a pose file.</summary>
    public static AnimationSource Current(SandboxInfo sandbox, string clipId, Func<string, SkeletonInfo?> skeletons)
    {
        var clip = sandbox.Manifest.FindClip(clipId)
            ?? throw new ArgumentException($"{clipId} is not a clip of {sandbox.Manifest.WeaponId}.");
        var export = ClipExporter.LastExport(sandbox, clipId) is { SandboxOutput: { } output } && File.Exists(output) ? output : null;
        var bytes = File.ReadAllBytes(export ?? CustomSkeletons.Template(sandbox, clip, CustomSkeletons.Load(sandbox)).Path);
        var weaponSkeleton = RigPackage.IsWorldId(clipId) ? WeaponSkeletonFor(sandbox, clipId) : CustomSkeletons.Load(sandbox)?.Path ?? WeaponSkeletonFor(sandbox, clipId);
        return FromClip(bytes, clip.ResourcePath, skeletons, weaponSkeleton);
    }

    /// <summary>
    /// The weapon skeleton of a clip in the .blend build: the Weapon rig's, or for a third-person clip (0.26) the
    /// third-person weapon rig's (CS2's own: a custom skeleton is first person only). Null without a build.
    /// </summary>
    public static string? WeaponSkeletonFor(SandboxInfo sandbox, string clipId)
    {
        var skeletons = RigPackage.ReadRig(sandbox)?.Skeletons;
        return (RigPackage.IsWorldId(clipId) ? skeletons?.WorldWeapon : skeletons?.Weapon)?.Path;
    }

    /// <summary>Writes an animation as a pose file with bone names and parents (what Blender and other weapons map by).</summary>
    public static void Write(string path, AnimationSource source)
    {
        var streams = new Dictionary<string, (string Skeleton, BonePose[][] Poses)>(StringComparer.Ordinal);
        var bones = new Dictionary<string, (IReadOnlyList<string> Names, IReadOnlyList<string?> Parents)>(StringComparer.Ordinal);
        foreach (var (name, stream) in new[] { (ArmsStream, source.Arms), (WeaponStream, source.Weapon) })
        {
            if (stream is not null)
            {
                streams[name] = (stream.Skeleton, stream.Poses);
                bones[name] = (stream.Bones, stream.Parents);
            }
        }

        var temporary = path + ".tmp";
        PoseFile.Write(temporary, streams, bones);
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>The poses at another length: cut, or the last frame held.</summary>
    private static BonePose[][] Fit(BonePose[][] poses, int frames) =>
        Enumerable.Range(0, frames).Select(frame => poses.Length == 0 ? [] : (BonePose[])poses[Math.Min(frame, poses.Length - 1)].Clone()).ToArray();

    private static IReadOnlyList<string?> ParentsByName(IReadOnlyList<string> names, SkeletonInfo skeleton)
    {
        var parents = skeleton.Bones.ToDictionary(bone => bone.Name, bone => bone.ParentIndex >= 0 ? skeleton.Bones[bone.ParentIndex].Name : null, StringComparer.Ordinal);
        return names.Select(name => parents.GetValueOrDefault(name)).ToList();
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.EndsWith("_c", StringComparison.Ordinal) ? a[..^2] : a, b.EndsWith("_c", StringComparison.Ordinal) ? b[..^2] : b, StringComparison.OrdinalIgnoreCase);
}
