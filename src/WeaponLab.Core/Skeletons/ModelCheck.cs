using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Skeletons;

/// <summary>A bone whose parent in the compiled model differs from its parent in the skeleton the clips drive.</summary>
/// <param name="ModelParent">Null when the bone is a root bone in the model.</param>
/// <param name="ModelHasSkeletonParent">The model has the skeleton's parent bone at all (ModelDoc drops bones no mesh is weighted to).</param>
public sealed record ParentMismatch(string Bone, string? ModelParent, string? SkeletonParent, bool ModelHasSkeletonParent);

/// <summary>What <see cref="ModelCheck.For"/> found about the model a weapon shows in game.</summary>
/// <param name="Model">The model's resource path, e.g. <c>models/weapons/x/y.vmdl</c>.</param>
/// <param name="FromVData">The weapon's vdata names the model (m_szWorldModel); false = the skeleton's source model, as a guess.</param>
/// <param name="Found">The compiled model is in the target addon or the game.</param>
/// <param name="SkeletonPath">The skeleton the clips drive, e.g. <c>animation/skeletons/weapons/weaponlab_knife_ursus.vnmskel</c>.</param>
public sealed record ModelCheckResult(string Model, bool FromVData, bool Found, string SkeletonPath, bool TargetsSkeleton, IReadOnlyList<ParentMismatch> Mismatches)
{
    public bool IsClean => Found && TargetsSkeleton && Mismatches.Count == 0;

    /// <summary>Each problem as a sentence with its fix, for warnings and tips.</summary>
    public IReadOnlyList<string> Problems()
    {
        var name = Path.GetFileName(Model);
        var skeleton = Path.GetFileName(SkeletonPath);
        if (!Found)
        {
            return [$"{name} is in neither the addon nor the game (compiled), so it cannot be checked against {skeleton}."];
        }

        var problems = new List<string>();
        if (!TargetsSkeleton)
        {
            problems.Add($"{name}{(FromVData ? " (the weapon's m_szWorldModel)" : "")} does not use the skeleton {skeleton}, so the clips do not reach its bones in game. "
                + $"Once the skeleton is in the addon (push it), set the model's NmSkeletonReference Source File in ModelDoc to {SkeletonPath} "
                + "(Copy asset path on the Skeleton row) and compile it.");
        }

        foreach (var mismatch in Mismatches)
        {
            if (mismatch.ModelParent is null)
            {
                problems.Add($"{mismatch.Bone} is a root bone in {name} but hangs under {mismatch.SkeletonParent} in {skeleton}: CS2 reads its animation from the camera, "
                    + $"so it shows far from the gun. Parent it under {mismatch.SkeletonParent} in the model (Blender/FBX and ModelDoc's Skeleton) and compile it.");
            }
            else if (mismatch.SkeletonParent is null)
            {
                problems.Add($"{mismatch.Bone} hangs under {mismatch.ModelParent} in {name} but is the root of {skeleton}: make it the model's root bone too.");
            }
            else
            {
                problems.Add($"{mismatch.Bone} hangs under {mismatch.ModelParent} in {name} but under {mismatch.SkeletonParent} in {skeleton}: CS2 moves it from "
                    + $"{mismatch.ModelParent}, so it shows in the wrong place. Parent it under {mismatch.SkeletonParent} in the model"
                    + (mismatch.ModelHasSkeletonParent ? "." : $"; the compiled model has no {mismatch.SkeletonParent} (ModelDoc drops bones no mesh is weighted to): set do_not_discard on it."));
            }
        }

        return problems;
    }
}

/// <summary>
/// Checks the model a weapon shows in game against the custom skeleton its clips drive (0.23). CS2 applies each
/// track relative to the bone's parent in the COMPILED MODEL, and reads a model root's track from the camera
/// (verified in game on PC2, 2026-11-07), so the model must use the skeleton and give every shared bone the same
/// parent. Weapon Lab never edits the model: it only says what to change.
/// </summary>
public static class ModelCheck
{
    private static readonly Dictionary<(string File, DateTime Stamp), ModelSkeleton?> Cache = [];

    /// <param name="blocks">The weapon's vdata blocks (<see cref="GameConnection.VDataBlocksFor"/>).</param>
    /// <param name="fallbackModel">The skeleton's source model key, used when no vdata block names a model.</param>
    public static ModelCheckResult For(GameConnection connection, AddonTarget? target, Profile profile, IReadOnlyList<string> blocks,
        SkeletonInfo skeleton, string? fallbackModel)
    {
        var fromVData = WorldModel(connection, target, profile, blocks);
        var modelPath = fromVData ?? (fallbackModel is null ? null : KeyPath(fallbackModel)[..^2]);
        if (modelPath is null)
        {
            return new ModelCheckResult("(no model)", false, false, skeleton.ResourcePath, false, []);
        }

        var model = Read(connection, target, modelPath);
        if (model is null)
        {
            return new ModelCheckResult(modelPath, fromVData is not null, false, skeleton.ResourcePath, false, []);
        }

        var targets = model.SkeletonRefs.Any(reference => string.Equals(Normalize(reference), skeleton.ResourcePath, StringComparison.OrdinalIgnoreCase));
        return new ModelCheckResult(modelPath, fromVData is not null, true, skeleton.ResourcePath, targets, Mismatches(model, skeleton));
    }

    /// <summary>Shared bones whose parent differs between the compiled model and the skeleton, in skeleton order.</summary>
    public static IReadOnlyList<ParentMismatch> Mismatches(ModelSkeleton model, SkeletonInfo skeleton)
    {
        var modelBones = model.Bones.ToDictionary(bone => bone.Name, StringComparer.Ordinal);
        var result = new List<ParentMismatch>();
        foreach (var bone in skeleton.Bones)
        {
            if (!modelBones.TryGetValue(bone.Name, out var modelBone))
            {
                continue; // No mesh on it in the model: nothing to place wrong.
            }

            var modelParent = modelBone.ParentIndex < 0 ? null : model.Bones[modelBone.ParentIndex].Name;
            var skeletonParent = bone.ParentIndex < 0 ? null : skeleton.Bones[bone.ParentIndex].Name;
            if (modelParent != skeletonParent)
            {
                result.Add(new ParentMismatch(bone.Name, modelParent, skeletonParent, skeletonParent is null || modelBones.ContainsKey(skeletonParent)));
            }
        }

        return result;
    }

    /// <summary>
    /// The model the weapon shows in game (m_szWorldModel) for its first block that names one: the profile's
    /// unsaved VData edit, else the addon's vdata, else CS2's. Null when no block names one.
    /// </summary>
    public static string? WorldModel(GameConnection connection, AddonTarget? target, Profile profile, IReadOnlyList<string> blocks)
    {
        VDataFile? addon = null;
        try
        {
            addon = target is not null && File.Exists(target.VDataCompiledPath) ? VDataFile.Read(File.ReadAllBytes(target.VDataCompiledPath), target.VDataCompiledPath) : null;
        }
        catch (InvalidDataException)
        {
            // Unreadable addon vdata: CS2's values stand.
        }

        var stock = connection.ReadVData();
        foreach (var block in blocks)
        {
            var model = profile.VData.GetValueOrDefault(block)?.GetValueOrDefault(VDataBlock.WorldModelKey)
                ?? addon?[block]?.Value(VDataBlock.WorldModelKey)
                ?? stock[block]?.Value(VDataBlock.WorldModelKey);
            if (!string.IsNullOrWhiteSpace(model))
            {
                return Normalize(model);
            }
        }

        return null;
    }

    /// <summary>The compiled model from the target addon, else the game; null when neither has it or it cannot be read.</summary>
    private static ModelSkeleton? Read(GameConnection connection, AddonTarget? target, string modelPath)
    {
        var compiled = modelPath + "_c";
        var file = target is null ? null : Path.Combine(target.GamePath, compiled.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (file is not null && File.Exists(file))
            {
                var key = (file, File.GetLastWriteTimeUtc(file));
                lock (Cache)
                {
                    if (Cache.TryGetValue(key, out var cached))
                    {
                        return cached;
                    }
                }

                var read = ModelSkeleton.Read(File.ReadAllBytes(file), Path.GetFileName(file));
                lock (Cache)
                {
                    Cache[key] = read;
                }

                return read;
            }

            return connection.Archive.Contains(compiled) ? ModelSkeleton.Read(connection.Archive.ReadBytes(compiled), Path.GetFileName(compiled)) : null;
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            return null;
        }
    }

    /// <summary>A model key's compiled path (<c>game:x_c</c> or <c>addon:name:x_c</c> → <c>x_c</c>).</summary>
    private static string KeyPath(string key)
    {
        var path = key.StartsWith(PreviewModels.GamePrefix, StringComparison.Ordinal) ? key[PreviewModels.GamePrefix.Length..]
            : key.StartsWith(PreviewModels.AddonPrefix, StringComparison.Ordinal) ? key[PreviewModels.AddonPrefix.Length..][(key[PreviewModels.AddonPrefix.Length..].IndexOf(':') + 1)..]
            : key;
        return path.EndsWith("_c", StringComparison.Ordinal) ? path : path + "_c";
    }

    /// <summary>A resource path as vdata and models name it: forward slashes, no quotes, no <c>_c</c>.</summary>
    private static string Normalize(string path)
    {
        var clean = path.Trim().Trim('"').Replace('\\', '/');
        return clean.EndsWith("_c", StringComparison.Ordinal) ? clean[..^2] : clean;
    }
}
