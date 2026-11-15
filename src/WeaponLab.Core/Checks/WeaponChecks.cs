using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Skeletons;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Core.Checks;

/// <summary>What a check is about; the app groups and colours by it.</summary>
public enum CheckArea
{
    Model,
    Hookup,
    VData,
    Timing,
    Blender,
}

/// <summary>A one-click fix or jump the app offers beside a check (0.24).</summary>
public enum CheckFix
{
    None,

    /// <summary>Build the .blend again (the clips or the skeleton changed since it was built).</summary>
    BuildBlend,

    /// <summary>"Update for export": an older .blend gets what export needs, untouched otherwise.</summary>
    UpdateBlend,

    /// <summary>Copy the skeleton's asset path (for ModelDoc's NmSkeletonReference).</summary>
    CopySkeletonPath,

    /// <summary>Open the clip in Anim Events.</summary>
    OpenClip,

    OpenVData,
    OpenDeliver,
    ShowSkeleton,
}

/// <summary>One known problem with a weapon, in plain words, with what the app can do about it.</summary>
/// <param name="ClipPath">The clip it is about (Fix/Show open it), or null.</param>
public sealed record WeaponCheck(CheckArea Area, string Message, CheckFix Fix = CheckFix.None, string? ClipPath = null);

/// <summary>
/// The per-weapon Checks list (0.24): every known problem in plain words with a Show or Fix. The model check,
/// hookup and vdata checks come from <see cref="ModelCheck"/> and <see cref="Addons.AddonChecks"/> (the app
/// adds them); this class adds what it can tell from the sandbox and the game's timing.
/// </summary>
public static class WeaponChecks
{
    /// <summary>The .blend against the sandbox: built by an older Weapon Lab, or built before the clips or the skeleton changed.</summary>
    public static IReadOnlyList<WeaponCheck> Blender(SandboxInfo sandbox)
    {
        if (!BlendBuilder.HasBlend(sandbox))
        {
            return [];
        }

        if (BlendBuilder.NeedsUpdate(sandbox))
        {
            return [new WeaponCheck(CheckArea.Blender, "The .blend was built by an older Weapon Lab and cannot export yet. Update for export adds what it needs; your keys are kept.", CheckFix.UpdateBlend)];
        }

        if (RigPackage.ReadRig(sandbox) is not { } rig)
        {
            return [];
        }

        var setup = CustomSkeletons.Load(sandbox);
        var changed = new List<string>();
        foreach (var clip in rig.Clips)
        {
            var sandboxClip = sandbox.Manifest.Clips.FirstOrDefault(candidate => candidate.ResourcePath == clip.ResourcePath);
            if (sandboxClip is not null && CustomSkeletons.Template(sandbox, sandboxClip, setup).Sha256 != clip.Sha256)
            {
                changed.Add(clip.Id);
            }
        }

        return changed.Count == 0
            ? []
            : [new WeaponCheck(CheckArea.Blender,
                $"The skeleton or the clips changed since the .blend was built ({Names(changed)}), so those clips cannot export or take event edits until "
                + "the .blend is built again. Your exports are kept (moved onto the new skeleton); the new .blend starts from the clips without them.",
                CheckFix.BuildBlend)];
    }

    /// <summary>
    /// The game's timing against each reload as it will play (the sandbox's export, else the addon's clip, else
    /// CS2's): <see cref="GameTimings.Warnings"/> per clip, plus what the vdata costs (<see cref="TimingGun.Warnings"/>).
    /// </summary>
    /// <param name="addonClip">The addon's own file for a game clip path, or null (see AddonInventory.ClipFile).</param>
    public static IReadOnlyList<WeaponCheck> Timing(GameConnection connection, WeaponEntry weapon, SandboxInfo? sandbox, TimingGun? gun, Func<string, string?> addonClip)
    {
        if (gun is null)
        {
            return [];
        }

        var checks = gun.Warnings().Select(warning => new WeaponCheck(CheckArea.Timing, warning)).ToList();
        foreach (var path in weapon.ClipPaths.Where(path => Path.GetFileName(path).Contains("reload", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var clip = AsPlayed(connection, sandbox, path, addonClip);
                if (GameTimings.For(connection, gun.Gun, weapon, clip) is { } timing)
                {
                    checks.AddRange(GameTimings.Warnings(timing, clip).Select(warning => new WeaponCheck(CheckArea.Timing, warning, CheckFix.OpenClip, path)));
                }
            }
            catch (Exception error) when (error is InvalidDataException or IOException)
            {
                // An unreadable clip is reported where it is used; it has no timing to compare.
            }
        }

        return checks;
    }

    /// <summary>The clip as the next push would leave it: the sandbox's export, else the addon's own clip, else CS2's.</summary>
    private static ClipInfo AsPlayed(GameConnection connection, SandboxInfo? sandbox, string path, Func<string, string?> addonClip)
    {
        if (sandbox is not null && ClipExporter.LastExport(sandbox, RigPackage.ClipId(path)) is { SandboxOutput: { } output } && File.Exists(output))
        {
            return ClipReader.Read(File.ReadAllBytes(output), path);
        }

        return addonClip(path) is { } file && File.Exists(file) ? ClipReader.Read(File.ReadAllBytes(file), path) : connection.ReadClip(path);
    }

    private static string Names(IReadOnlyList<string> clips) =>
        clips.Count <= 3 ? string.Join(", ", clips) : string.Join(", ", clips.Take(3)) + $" and {clips.Count - 3} more";
}
