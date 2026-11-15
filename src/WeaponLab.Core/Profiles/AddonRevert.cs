using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Graphs;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Profiles;

public enum RevertKind
{
    Clip,
    Graph,
    VData,
    Skeleton,
}

/// <summary>Which of a weapon's pending work a revert looks at; the outliner passes the open tab's part.</summary>
[Flags]
public enum RevertParts
{
    Clips = 1,
    Graphs = 2,
    VData = 4,

    /// <summary>A custom skeleton the addon does not have (0.22.5); its re-targeted clips go with it.</summary>
    Skeleton = 8,
    All = Clips | Graphs | VData | Skeleton,
}

/// <summary>The skeleton the target addon gives a weapon itself (<see cref="Skeletons.CustomSkeletons.FindInAddon"/>), which a skeleton revert adopts.</summary>
public sealed record AddonSkeleton(string Addon, string Skeleton, string? Model);

/// <summary>
/// One pending change (yellow in the outliner) that "Revert to addon default"
/// can undo, or, with <see cref="CanRevert"/> false, why it cannot.
/// </summary>
public sealed record RevertItem(RevertKind Kind, string Label, string Detail, bool CanRevert)
{
    public string? ClipId { get; init; }

    /// <summary>The clip's kept copy (<see cref="PushedClips"/>) is the addon's version and goes back into the sandbox.</summary>
    public bool RestoresKeptCopy { get; init; }

    public string? Graph { get; init; }

    /// <summary>Graph edit keys to drop: delivery builds on the addon's graph, so without them it keeps the addon's values.</summary>
    public IReadOnlyList<string> GraphKeys { get; init; } = [];

    public string? Block { get; init; }
    public string? ValuePath { get; init; }

    /// <summary>The value the profile takes (the addon's); null drops the profile's edit.</summary>
    public string? Value { get; init; }

    public bool RemovesClassSwap { get; init; }

    /// <summary>Skeleton item: the addon's own skeleton to adopt, or null to go back to CS2's default for the slot.</summary>
    public AddonSkeleton? AdoptsAddonSkeleton { get; init; }
}

/// <summary>
/// "Revert to addon default": throws away a weapon's pending work so it
/// matches what the target addon has again (yellow → green). Nothing in the
/// addon or the .blend changes; sandbox files are moved to a backup folder (a
/// custom skeleton's are copied there before it goes), never lost.
/// </summary>
public static class AddonRevert
{
    /// <param name="onlyClip">Only this clip (a clip row in the outliner).</param>
    /// <param name="onlyGraph">Only this graph, by its CS2 path (a graph row).</param>
    public static IReadOnlyList<RevertItem> Plan(Profile profile, SandboxStore sandboxes, GameConnection connection, AddonTarget target,
        string weaponId, IReadOnlyList<string> vdataBlocks, RevertParts parts, string? onlyClip = null, string? onlyGraph = null)
    {
        var items = new List<RevertItem>();
        var skeleton = parts.HasFlag(RevertParts.Skeleton) && onlyClip is null && onlyGraph is null
            ? SkeletonItem(profile, sandboxes, connection, target, weaponId)
            : null;
        if (skeleton is not null)
        {
            items.Add(skeleton);
        }

        if (parts.HasFlag(RevertParts.Clips) && onlyGraph is null)
        {
            items.AddRange(ClipItems(profile, sandboxes, target, weaponId, onlyClip, skeletonGoes: skeleton is not null));
        }

        if (parts.HasFlag(RevertParts.Graphs) && onlyClip is null)
        {
            items.AddRange(GraphItems(profile, connection, target, weaponId, onlyGraph));
        }

        if (parts.HasFlag(RevertParts.VData) && onlyClip is null && onlyGraph is null)
        {
            items.AddRange(VDataItems(profile, sandboxes, connection, target, vdataBlocks));
        }

        return items;
    }

    /// <summary>
    /// The weapon's custom skeleton when the addon does not have it: reverting drops it (or adopts the
    /// skeleton the addon gives the weapon), and every clip only re-targeted to it goes back with it.
    /// </summary>
    private static RevertItem? SkeletonItem(Profile profile, SandboxStore sandboxes, GameConnection connection, AddonTarget target, string weaponId)
    {
        var settings = profile.Weapon(weaponId);
        if (settings.ResetToStock || settings.CustomSkeleton is null || !sandboxes.Exists(weaponId))
        {
            return null;
        }

        var sandbox = sandboxes.Open(weaponId);
        if (Skeletons.CustomSkeletons.Load(sandbox) is not { FromAddon: false } setup
            || PendingWork.StateOf(profile, target, setup.Path + "_c", setup.SkeletonSha256) != PushState.Pending)
        {
            return null;
        }

        var retargeted = PendingWork.Clips(profile, sandbox, target)
            .Count(clip => clip.State == PushState.Pending && clip.Changes.StartsWith("re-targeted", StringComparison.Ordinal));
        var found = Skeletons.CustomSkeletons.FindInAddon(connection, target.Name, weaponId);
        var back = found is { } addon
            ? $"the skeleton {target.Name} gives it ({Path.GetFileName(addon.Skeleton)})"
            : $"CS2's default skeleton for its slot ({target.Name} gives it none)";
        var clips = retargeted > 0 ? $" Its {retargeted} clip(s) only re-targeted to it go back with it." : "";
        return new RevertItem(RevertKind.Skeleton, $"Custom skeleton {Path.GetFileName(setup.Path)}",
            $"Not in the addon. It is dropped, so {weaponId} goes back to {back}.{clips} Exports you keep move onto that skeleton. "
            + "Build the .blend again before exporting from Blender.", true)
            { AdoptsAddonSkeleton = found is { } skeleton ? new AddonSkeleton(target.Name, skeleton.Skeleton, skeleton.Model) : null };
    }

    /// <param name="skeletonGoes">The plan also reverts the custom skeleton, which takes the clips only re-targeted to it along.</param>
    private static IEnumerable<RevertItem> ClipItems(Profile profile, SandboxStore sandboxes, AddonTarget target, string weaponId, string? onlyClip, bool skeletonGoes)
    {
        var settings = profile.Weapon(weaponId);
        if (settings.ResetToStock || !sandboxes.Exists(weaponId))
        {
            yield break;
        }

        var sandbox = sandboxes.Open(weaponId);
        var retargets = settings.CustomSkeleton is not null ? " On the custom skeleton, CS2's clip re-targeted to it goes in instead." : "";
        foreach (var clip in PendingWork.Clips(profile, sandbox, target).Where(clip => clip.State == PushState.Pending && (onlyClip is null || clip.ClipId == onlyClip)))
        {
            if (clip.Changes.StartsWith("re-targeted", StringComparison.Ordinal))
            {
                if (!skeletonGoes)
                {
                    yield return new RevertItem(RevertKind.Clip, clip.ClipId, RetargetedOnly(profile, sandbox, target), false) { ClipId = clip.ClipId };
                }

                continue;
            }

            var addonFile = AddonManifest.Resolve(target, clip.Key);
            var addonSha = File.Exists(addonFile) ? GameArchive.Sha256(File.ReadAllBytes(addonFile)) : null;
            if (addonSha is null)
            {
                yield return new RevertItem(RevertKind.Clip, clip.ClipId,
                    "The addon has no version of it. Your export, Blender sample and event edits are set aside (kept in the backup), so it plays CS2's again." + retargets, true)
                    { ClipId = clip.ClipId };
            }
            else if (PushedClips.KeptSha(sandbox, clip.ClipId) == addonSha)
            {
                yield return new RevertItem(RevertKind.Clip, clip.ClipId,
                    "Puts the addon's version back in the sandbox: its export, Blender sample, event edits and length. Your current work goes to the backup.", true)
                    { ClipId = clip.ClipId, RestoresKeptCopy = true };
            }
            else if (profile.WasPushed(target.Name, clip.Key, addonSha))
            {
                yield return new RevertItem(RevertKind.Clip, clip.ClipId,
                    "The addon has a version this profile pushed before Weapon Lab kept copies of pushes (0.21), so the sandbox work behind it cannot be put back. Pushes from now on can be reverted.", false)
                    { ClipId = clip.ClipId };
            }
            else
            {
                yield return new RevertItem(RevertKind.Clip, clip.ClipId,
                    "The addon has its own version (not this profile's). Your work is set aside (kept in the backup) and the addon keeps its version.", true)
                    { ClipId = clip.ClipId };
            }
        }
    }

    /// <summary>Why a clip only re-targeted to the custom skeleton has nothing of its own to revert, and what does.</summary>
    private static string RetargetedOnly(Profile profile, SandboxInfo sandbox, AddonTarget target)
    {
        var skeleton = Skeletons.CustomSkeletons.Load(sandbox);
        var skeletonPending = skeleton is { FromAddon: false }
            && PendingWork.StateOf(profile, target, skeleton.Path + "_c", skeleton.SkeletonSha256) == PushState.Pending;
        return skeletonPending
            ? "Not edited, only re-targeted to the custom skeleton, which the addon does not have yet. Revert the weapon's row or its Skeleton row to drop the skeleton; this clip goes back with it."
            : "Not edited, only re-targeted to the custom skeleton the addon has. Untick it in the Deliver tree to leave it out of the push.";
    }

    private static IEnumerable<RevertItem> GraphItems(Profile profile, GameConnection connection, AddonTarget target, string weaponId, string? onlyGraph)
    {
        foreach (var state in GraphStatus.Load(profile, connection, target).Values
                     .Where(state => state.Owners.Contains(weaponId) && !state.IsApplied && (onlyGraph is null || state.Graph == onlyGraph)))
        {
            var others = state.Owners.Where(owner => owner != weaponId).ToList();
            var shared = others.Count > 0 ? $" The graph is shared with {string.Join(", ", others.Take(3))}{(others.Count > 3 ? "…" : "")}." : "";
            yield return new RevertItem(RevertKind.Graph, Path.GetFileName(state.Graph),
                $"{state.Pending.Count} edit(s) the addon does not have are dropped; the graph keeps what the addon has.{shared}", true)
                { Graph = state.Graph, GraphKeys = state.Pending.Order(StringComparer.Ordinal).ToList() };
        }
    }

    private static IEnumerable<RevertItem> VDataItems(Profile profile, SandboxStore sandboxes, GameConnection connection, AddonTarget target, IReadOnlyList<string> blocks)
    {
        VDataFile? addon = null;
        AddonManifest? manifest = null;
        try
        {
            addon = AddonVData.Read(target);
            manifest = AddonManifest.Read(target);
        }
        catch (InvalidDataException)
        {
            // Without them every edit counts as not in the addon, as the status does.
        }

        var stock = connection.ReadVData();
        var original = DeliveryPlanner.OriginalVData(AddonOriginals.Load(sandboxes.WorkspacePath, target.Name), target, manifest);
        foreach (var block in blocks)
        {
            var setByLab = manifest?.VData.GetValueOrDefault(block);
            var edits = profile.VData.GetValueOrDefault(block);
            foreach (var (path, value) in edits ?? [])
            {
                var actual = addon?[block]?.Value(path);
                if (actual is not null && VDataValues.Same(actual, value))
                {
                    continue;
                }

                // A value an earlier push set stays pinned to what the addon has; otherwise the edit just goes.
                var keep = actual is not null && setByLab?.ContainsKey(path) == true;
                yield return new RevertItem(RevertKind.VData, $"{block}.{path}",
                    keep ? $"{value} → the addon's {actual}" : actual is null ? $"{value} → not set (as in the addon)" : $"{value} → the addon's {actual} (edit dropped)", true)
                    { Block = block, ValuePath = path, Value = keep ? actual : null };
            }

            // Values an earlier push set that the profile dropped: the addon still has them, so the profile takes them back.
            foreach (var path in setByLab?.Keys ?? Enumerable.Empty<string>())
            {
                if (edits?.ContainsKey(path) == true || addon?[block]?.Value(path) is not { } actual)
                {
                    continue;
                }

                var before = (original?[block] ?? stock[block])?.Value(path);
                if (before is not null && !VDataValues.Same(actual, before))
                {
                    yield return new RevertItem(RevertKind.VData, $"{block}.{path}", $"back to {before} → kept at the addon's {actual}", true)
                        { Block = block, ValuePath = path, Value = actual };
                }
            }

            var swapped = profile.VDataClasses.TryGetValue(block, out var swap);
            var inAddon = manifest?.VDataClasses.ContainsKey(block) == true;
            if (swapped && !inAddon)
            {
                yield return new RevertItem(RevertKind.VData, $"{block} class", $"the change to {swap!.Summary()} is dropped", true) { Block = block, RemovesClassSwap = true };
            }
            else if (!swapped && inAddon)
            {
                yield return new RevertItem(RevertKind.VData, $"{block} class",
                    "the addon has an earlier class change the profile dropped; change the class back in the VData tab", false) { Block = block };
            }
        }
    }

    /// <summary>
    /// Applies the items that can be reverted. Sandbox files go into
    /// <paramref name="backupFolder"/>; the caller saves the profile. Returns
    /// what was done, one line per item. A skeleton item needs
    /// <paramref name="connection"/> and goes last, so clips set aside first
    /// are not moved onto the other skeleton.
    /// </summary>
    public static IReadOnlyList<string> Apply(Profile profile, SandboxStore sandboxes, string weaponId, IEnumerable<RevertItem> items, string backupFolder,
        GameConnection? connection = null)
    {
        var done = new List<string>();
        SandboxInfo? sandbox = null;
        foreach (var item in items.Where(item => item.CanRevert).OrderBy(item => item.Kind == RevertKind.Skeleton))
        {
            switch (item.Kind)
            {
                case RevertKind.Clip:
                    sandbox ??= sandboxes.Open(weaponId);
                    if (item.RestoresKeptCopy)
                    {
                        PushedClips.Restore(sandbox, item.ClipId!, backupFolder);
                        done.Add($"{item.ClipId}: the addon's version is back in the sandbox");
                    }
                    else
                    {
                        var moved = PushedClips.SetAside(sandbox, item.ClipId!, backupFolder);
                        done.Add($"{item.ClipId}: {moved.Count} file(s) set aside");
                    }

                    break;

                case RevertKind.Graph:
                    if (profile.Graphs.TryGetValue(item.Graph!, out var edits))
                    {
                        foreach (var key in item.GraphKeys)
                        {
                            edits.Remove(key);
                        }

                        if (edits.Count == 0)
                        {
                            profile.Graphs.Remove(item.Graph!);
                        }
                    }

                    done.Add($"{item.Label}: {item.GraphKeys.Count} graph edit(s) dropped");
                    break;

                case RevertKind.VData:
                    RevertVData(profile, item);
                    done.Add($"{item.Label}: {item.Detail}");
                    break;

                case RevertKind.Skeleton:
                    sandbox ??= sandboxes.Open(weaponId);
                    done.Add(RevertSkeleton(profile, sandbox, connection ?? throw new ArgumentNullException(nameof(connection), "A skeleton revert reads the game."), item, backupFolder));
                    break;
            }
        }

        if (sandbox is not null)
        {
            Blender.ClipLengths.Write(sandbox);
        }

        return done;
    }

    /// <summary>
    /// Takes the custom skeleton out of the sandbox and the profile, or swaps it for the addon's own. The
    /// sandbox's skeleton folder and exports are copied to the backup first: removing the skeleton deletes
    /// its folder and moves exports onto the other skeleton.
    /// </summary>
    private static string RevertSkeleton(Profile profile, SandboxInfo sandbox, GameConnection connection, RevertItem item, string backupFolder)
    {
        var old = Skeletons.CustomSkeletons.Load(sandbox);
        CopyFolder(Path.Combine(sandbox.FolderPath, Skeletons.CustomSkeletons.Folder), Path.Combine(backupFolder, Skeletons.CustomSkeletons.Folder));
        CopyFolder(Path.Combine(sandbox.FolderPath, "exports"), Path.Combine(backupFolder, "exports on the custom skeleton"));

        var settings = profile.EnsureWeapon(sandbox.Manifest.WeaponId);
        var wasItsModel = settings.PreviewModel is not null && settings.PreviewModel == ViewportModels.ModelOf(old);
        string result;
        if (item.AdoptsAddonSkeleton is { } addon)
        {
            var adopted = Skeletons.CustomSkeletons.Adopt(sandbox, connection, addon.Addon, addon.Skeleton, addon.Model);
            settings.CustomSkeleton = new CustomSkeletonChoice(adopted.SourceModel, adopted.Path);
            if (wasItsModel)
            {
                settings.PreviewModel = ViewportModels.ModelOf(adopted);
            }

            result = $"now on {addon.Addon}'s skeleton {Path.GetFileName(adopted.Path)}";
        }
        else
        {
            Skeletons.CustomSkeletons.Remove(sandbox, connection);
            settings.CustomSkeleton = null;
            if (wasItsModel)
            {
                settings.PreviewModel = null; // The skeleton's model goes with it; the viewport shows CS2's again.
            }

            result = "back on CS2's default skeleton";
        }

        return $"{item.Label}: {result}; build the .blend again before exporting from Blender";
    }

    private static void CopyFolder(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, overwrite: false);
        }
    }

    private static void RevertVData(Profile profile, RevertItem item)
    {
        var block = item.Block!;
        if (item.RemovesClassSwap)
        {
            profile.VDataClasses.Remove(block);
            return;
        }

        if (item.Value is null)
        {
            if (profile.VData.TryGetValue(block, out var values))
            {
                values.Remove(item.ValuePath!);
                if (values.Count == 0)
                {
                    profile.VData.Remove(block);
                }
            }

            return;
        }

        if (!profile.VData.TryGetValue(block, out var set))
        {
            profile.VData[block] = set = new SortedDictionary<string, string>(StringComparer.Ordinal);
        }

        set[item.ValuePath!] = item.Value;
    }
}
