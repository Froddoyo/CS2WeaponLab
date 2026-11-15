using System.Numerics;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Viewport;

/// <summary>An event mark on the viewport's timeline.</summary>
public sealed record ViewportEvent(double Frame, bool IsSound, string Name);

/// <summary>
/// A clip ready to play in the viewport: for every frame, each skeleton
/// bone's model-space transform (same bone order as the scene).
/// </summary>
/// <param name="Source">Which version plays: "exported" (your Blender export), "addon" (the addon's own, what the game plays), "sandbox" (the sandbox template, on a custom skeleton when set) or "CS2".</param>
public sealed record ViewportClip(string Name, string Source, double FramesPerSecond, Matrix4x4[][] Frames, IReadOnlyList<ViewportEvent> Events)
{
    public int FrameCount => Frames.Length;
}

/// <summary>Reads a weapon clip's weapon stream for the viewport.</summary>
public static class ViewportClips
{
    /// <param name="clipPath">The game resource path, e.g. <c>animation/anims/viewmodel/rifle/rifle_ak/reload_ak.vnmclip_c</c>.</param>
    /// <param name="exported">Play the Blender export when there is one; false plays what the addon has.</param>
    /// <param name="addonClipFile">The addon's own version of the clip (see <see cref="Addons.AddonInventory.ClipFile"/>), or null.</param>
    /// <returns>The clip, or null when it has no stream for the scene's skeleton.</returns>
    public static ViewportClip? Load(GameConnection connection, SandboxInfo? sandbox, string clipPath, ViewportScene scene, bool exported, string? addonClipFile = null)
    {
        var (bytes, source) = Bytes(connection, sandbox, clipPath, exported, addonClipFile);
        var info = ClipReader.Read(bytes, clipPath);
        using var template = ClipTemplate.Open(bytes, clipPath);
        // The arms play the primary stream (the viewmodel rig); the weapon its paired stream, hanging off the arms' wpn bone.
        var armCount = scene.ArmBoneCount;
        var armPoses = armCount > 0 && template.Primary.Skeleton == ClipInfo.ViewmodelSkeleton ? template.Primary.ReadPoses() : null;
        var weaponPoses = template.Secondary.FirstOrDefault(candidate => candidate.Skeleton == scene.Skeleton)?.ReadPoses();
        if (source == "addon" && weaponPoses is null && scene.Bones.Count > armCount)
        {
            // The addon's version drives another skeleton (a custom one not pushed yet): play the sandbox's.
            return Load(connection, sandbox, clipPath, scene, exported, addonClipFile: null);
        }
        if (armPoses is null && weaponPoses is null)
        {
            return null;
        }

        var bones = scene.Bones;
        var frameCount = Math.Max(armPoses?.Length ?? 0, weaponPoses?.Length ?? 0);
        var frames = new Matrix4x4[frameCount][];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var model = new Matrix4x4[bones.Count];
            for (var bone = 0; bone < bones.Count; bone++)
            {
                // One track per skeleton bone, in skeleton order; a bone without a track keeps its rest pose.
                var (poses, track) = bone < armCount ? (armPoses, bone) : (weaponPoses, bone - armCount);
                var local = poses is not null && frame < poses.Length && track < poses[frame].Length ? ViewportScenes.Matrix(poses[frame][track]) : Local(scene, bone);
                model[bone] = bones[bone].Parent >= 0 ? local * model[bones[bone].Parent] : local;
            }

            frames[frame] = model;
        }

        var fps = info.FramesPerSecond > 0 ? info.FramesPerSecond : 30;
        var events = info.Events
            .Where(clipEvent => clipEvent.Kind is ClipEventKind.Sound or ClipEventKind.Gameplay)
            .Select(clipEvent => new ViewportEvent(clipEvent.StartFrame(info), clipEvent.Kind == ClipEventKind.Sound, clipEvent.Name)) // Event times run 0..1 over the clip.
            .ToList();
        return new ViewportClip(info.Name, source, fps, frames, events);
    }

    /// <summary>
    /// The bytes to play: the export (when asked and present), else what the addon plays (its own version, the
    /// live state, user decision 2026-11-07: "inherit the custom stuff when it's green"), else the sandbox
    /// template, else CS2's clip.
    /// </summary>
    private static (byte[] Bytes, string Source) Bytes(GameConnection connection, SandboxInfo? sandbox, string clipPath, bool exported, string? addonClipFile)
    {
        var clip = sandbox?.Manifest.Clips.FirstOrDefault(candidate => candidate.ResourcePath == clipPath);
        if (sandbox is not null && clip is not null && exported
            && ClipExporter.LastExport(sandbox, Blender.RigPackage.ClipId(clipPath)) is { IdenticalToTemplate: false, SandboxOutput: { } output } && File.Exists(output))
        {
            return (File.ReadAllBytes(output), "exported");
        }

        if (addonClipFile is not null && File.Exists(addonClipFile))
        {
            return (File.ReadAllBytes(addonClipFile), "addon");
        }

        if (sandbox is not null && clip is not null)
        {
            var (template, _) = Skeletons.CustomSkeletons.Template(sandbox, clip, Skeletons.CustomSkeletons.Load(sandbox));
            if (File.Exists(template))
            {
                return (File.ReadAllBytes(template), "sandbox");
            }
        }

        return (connection.Archive.ReadBytes(clipPath), "CS2");
    }

    /// <summary>A bone's parent-space rest transform, from the scene's model-space rest pose.</summary>
    private static Matrix4x4 Local(ViewportScene scene, int bone)
    {
        var parent = scene.Bones[bone].Parent;
        return parent >= 0 && Matrix4x4.Invert(scene.Bones[parent].Rest, out var inverse) ? scene.Bones[bone].Rest * inverse : scene.Bones[bone].Rest;
    }
}
