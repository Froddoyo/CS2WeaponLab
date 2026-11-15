using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Sandbox;

namespace WeaponLab.Core.Delivery;

/// <summary>Where one of a profile's files stands against the live addon.</summary>
public enum PushState
{
    /// <summary>Not in the addon yet: the next apply pushes it.</summary>
    Pending,

    /// <summary>The addon has exactly these bytes.</summary>
    Live,

    /// <summary>This profile pushed it, but the addon now has another version (another profile's, or a hand edit). Not pushed again until exported again.</summary>
    ReplacedInAddon,
}

/// <param name="Key">The file's key in the addon, e.g. <c>animation/anims/custom/rifle_ak/idle_ak.vnmclip_c</c>.</param>
/// <param name="SourceFile">The sandbox file holding the bytes (an export, or a re-targeted template).</param>
/// <param name="Changes">What changed, for the review.</param>
/// <param name="HasSoundChanges">The export retimed or swapped sound events.</param>
public sealed record ProfileClip(string WeaponId, string ClipId, string StockPath, string Key, string Sha, string SourceFile, string Changes, bool HasSoundChanges, PushState State);

/// <summary>
/// A profile's work on clips and what of it the live addon does not have yet
/// (user decision 2026-10-26: the profile holds changes not pushed yet; the
/// addon is the live state). Delivery and the outliner colours share this.
/// </summary>
public static class PendingWork
{
    /// <summary>
    /// The clips a sandbox offers the addon: every export with edits, and on a
    /// custom skeleton every other clip re-targeted to it. Clips left out in the
    /// Deliver tree are skipped.
    /// </summary>
    public static IReadOnlyList<ProfileClip> Clips(Profile profile, SandboxInfo sandbox, AddonTarget target)
    {
        var weaponId = sandbox.Manifest.WeaponId;
        var settings = profile.Weapon(weaponId);
        var skeleton = settings.CustomSkeleton is not null ? Skeletons.CustomSkeletons.Load(sandbox) : null;
        var result = new List<ProfileClip>();
        foreach (var clip in sandbox.Manifest.AllClips())
        {
            var clipId = Blender.RigPackage.ClipId(clip.ResourcePath);
            if (settings.ExcludedClips.Contains(clipId))
            {
                continue;
            }

            ClipExportReport? report = null;
            try
            {
                report = ClipExporter.LastExport(sandbox, clipId);
            }
            catch (System.Text.Json.JsonException)
            {
                // A broken report counts as not exported.
            }

            string source;
            string sha;
            string changes;
            var sounds = false;
            // An export whose clip file is gone counts as not exported (bug 2026-10-31: delivery crashed on it); export it again.
            if (report is { IdenticalToTemplate: false, SandboxOutput: { } output } && File.Exists(output))
            {
                (source, sha, changes) = (output, report.OutputSha256, Summary(report));
                sounds = report.ChangedEvents is { Count: > 0 };
            }
            else if (skeleton is not null && skeleton.Templates.ContainsKey(clip.ResourcePath))
            {
                (source, sha) = Skeletons.CustomSkeletons.Template(sandbox, clip, skeleton);
                changes = "re-targeted to " + Path.GetFileName(skeleton.Path);
            }
            else
            {
                continue;
            }

            var stock = clip.ResourcePath[..^2];
            var key = GraphTree.CustomClipPath(weaponId, stock) + "_c";
            result.Add(new ProfileClip(weaponId, clipId, stock, key, sha, source, changes, sounds, StateOf(profile, target, key, sha)));
        }

        return result;
    }

    /// <summary>
    /// Keeps a copy of the sandbox work behind every clip the addon holds in its
    /// current version (<see cref="PushedClips"/>), so a later revert can put it
    /// back. Returns how many new copies were made.
    /// </summary>
    public static int KeepLive(Profile profile, SandboxStore sandboxes, AddonTarget target)
    {
        var kept = 0;
        foreach (var sandbox in sandboxes.List())
        {
            foreach (var clip in Clips(profile, sandbox, target).Where(clip => clip.State == PushState.Live && !clip.Changes.StartsWith("re-targeted", StringComparison.Ordinal)))
            {
                if (PushedClips.Keep(sandbox, clip.ClipId))
                {
                    kept++;
                }
            }
        }

        return kept;
    }

    /// <summary>A file of this profile against the live addon: live when the addon has these bytes, pending unless pushed before and replaced since.</summary>
    public static PushState StateOf(Profile profile, AddonTarget target, string key, string sha)
    {
        var path = AddonManifest.Resolve(target, key);
        if (!File.Exists(path))
        {
            return PushState.Pending;
        }

        if (GameArchive.Sha256(File.ReadAllBytes(path)) == sha)
        {
            return PushState.Live;
        }

        return profile.WasPushed(target.Name, key, sha) ? PushState.ReplacedInAddon : PushState.Pending;
    }

    private static string Summary(ClipExportReport report)
    {
        var bones = report.ChangedBones.Values.Sum(list => list.Count);
        var events = report.ChangedEvents?.Count ?? 0;
        return $"{bones} bone(s), {events} event change(s)";
    }
}
