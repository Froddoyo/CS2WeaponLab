using System.Globalization;
using WeaponLab.Core.Delivery;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Profiles;

public enum WeaponState
{
    /// <summary>Nothing edited and nothing delivered.</summary>
    Untouched,

    /// <summary>The profile has edits the addon does not match yet (shown yellow).</summary>
    NeedsApply,

    /// <summary>The addon matches the profile's edits (shown green).</summary>
    Applied,

    /// <summary>Changed in the addon, but for another weapon or by the addon itself (shown blue); used for graphs.</summary>
    ChangedElsewhere,

    /// <summary>A shared graph the next push changes because of other weapons' pending work (shown teal, 0.24.1); used for graphs.</summary>
    PendingElsewhere,
}

/// <summary>Which part of a weapon a status is about; each tool tab colours the outliner by its own part.</summary>
public enum StatusAspect
{
    /// <summary>Everything together (the Deliver tab).</summary>
    All,

    /// <summary>Edited clips.</summary>
    Animations,

    /// <summary>Clips whose sound events were changed.</summary>
    Sounds,

    /// <summary>vdata values and class changes.</summary>
    VData,

    /// <summary>Graph edits.</summary>
    Graph,
}

/// <param name="HasEdits">The profile wants something here.</param>
/// <param name="Reasons">Why it is not in the addon yet.</param>
public sealed record StatusPart(bool HasEdits, IReadOnlyList<string> Reasons)
{
    public WeaponState State => Reasons.Count > 0 ? WeaponState.NeedsApply : HasEdits ? WeaponState.Applied : WeaponState.Untouched;
}

/// <param name="Reasons">Why the weapon needs applying, e.g. "idle_ak is not delivered".</param>
/// <param name="Parts">The same status split per aspect; missing aspects are untouched.</param>
public sealed record WeaponStatus(WeaponState State, IReadOnlyList<string> Reasons, IReadOnlyList<string> VDataBlocks, IReadOnlyDictionary<StatusAspect, StatusPart>? Parts = null)
{
    /// <summary>The status of one aspect; <see cref="StatusAspect.All"/> is the whole weapon.</summary>
    public StatusPart Part(StatusAspect aspect) =>
        aspect == StatusAspect.All ? new StatusPart(State != WeaponState.Untouched, Reasons)
        : Parts?.GetValueOrDefault(aspect) ?? new StatusPart(false, []);
}

/// <summary>
/// Compares what a profile wants for each weapon with what the target addon
/// actually holds: delivered clips (by hash) and compiled vdata values.
/// </summary>
public sealed class ProfileStatus
{
    private readonly Profile _profile;
    private readonly AddonTarget? _target;
    private readonly AddonManifest? _manifest;
    private readonly VDataFile _stock;
    /// <summary>The addon's vdata before Weapon Lab first changed it; null when it had none.</summary>
    private VDataFile? Original { get; init; }
    private readonly VDataFile? _addonVData;
    private readonly Dictionary<string, SandboxInfo> _sandboxes;
    private readonly Dictionary<string, Graphs.GraphEditState> _graphs;
    private GameConnection? Connection { get; init; }

    private ProfileStatus(Profile profile, AddonTarget? target, AddonManifest? manifest, VDataFile stock, VDataFile? addonVData, Dictionary<string, SandboxInfo> sandboxes,
        Dictionary<string, Graphs.GraphEditState> graphs)
    {
        _profile = profile;
        _target = target;
        _manifest = manifest;
        _stock = stock;
        _addonVData = addonVData;
        _sandboxes = sandboxes;
        _graphs = graphs;
    }

    public static ProfileStatus Load(Profile profile, SandboxStore sandboxes, GameConnection connection, AddonTarget? target)
    {
        AddonManifest? manifest = null;
        VDataFile? addonVData = null;
        if (target is not null)
        {
            try
            {
                manifest = AddonManifest.Read(target);
                addonVData = AddonVData.Read(target); // The source is the addon's truth (user decision 2026-11-07).
            }
            catch (InvalidDataException)
            {
                // An unreadable manifest or vdata counts as "does not match".
            }
        }

        var byWeapon = sandboxes.List().ToDictionary(sandbox => sandbox.Manifest.WeaponId, StringComparer.Ordinal);
        var original = target is null ? null : DeliveryPlanner.OriginalVData(AddonOriginals.Load(sandboxes.WorkspacePath, target.Name), target, manifest);
        return new ProfileStatus(profile, target, manifest, connection.ReadVData(), addonVData, byWeapon, Graphs.GraphStatus.Load(profile, connection, target)) { Original = original, Connection = connection };
    }

    /// <summary>A block as the addon had it before Weapon Lab changed it (CS2's when the addon had no vdata).</summary>
    private VDataBlock? Before(string block) => Original?[block] ?? _stock[block];

    /// <param name="vdataBlocks">The weapon's vdata blocks (from its skeleton).</param>
    public WeaponStatus For(string weaponId, IReadOnlyList<string> vdataBlocks)
    {
        var parts = new Dictionary<StatusAspect, StatusPart>
        {
            [StatusAspect.Animations] = ClipPart(weaponId, soundsOnly: false),
            [StatusAspect.Sounds] = ClipPart(weaponId, soundsOnly: true),
            [StatusAspect.Graph] = GraphPart(weaponId),
            [StatusAspect.VData] = VDataPart(vdataBlocks, weaponId),
        };

        // Sounds are part of the clips, so the whole weapon counts each reason once. VData is saved from its own
        // tab, not by applying the profile, so it is not part of the whole weapon's state.
        var reasons = parts.Where(part => part.Key is not StatusAspect.Sounds and not StatusAspect.VData).SelectMany(part => part.Value.Reasons).ToList();
        var hasEdits = parts.Where(part => part.Key != StatusAspect.VData).Any(part => part.Value.HasEdits);
        var state = reasons.Count > 0 ? WeaponState.NeedsApply : hasEdits ? WeaponState.Applied : WeaponState.Untouched;
        return new WeaponStatus(state, reasons, vdataBlocks, parts);
    }

    private static readonly IReadOnlyList<string> NoTarget = ["No target addon chosen."];

    /// <summary>
    /// The weapon's clip work in this profile against the live addon: yellow
    /// while some of it is not pushed yet, green once the addon has it (only
    /// clips with sound changes for the Sounds tab). A weapon left out of the
    /// push keeps its work in the profile without showing it as pending.
    /// </summary>
    private StatusPart ClipPart(string weaponId, bool soundsOnly)
    {
        var weapon = _profile.Weapon(weaponId);
        if (weapon.ResetToStock)
        {
            return soundsOnly || _target is null ? new StatusPart(false, []) : StockResetPart(weaponId);
        }

        if (!_sandboxes.TryGetValue(weaponId, out var sandbox))
        {
            return new StatusPart(false, []);
        }

        if (_target is null)
        {
            var any = EditedClips(weaponId).Count > 0;
            return new StatusPart(any, any ? NoTarget : []);
        }

        var clips = PendingWork.Clips(_profile, sandbox, _target).Where(clip => !soundsOnly || clip.HasSoundChanges).ToList();
        var committed = weapon.Animations == AnimationMode.Custom;
        var reasons = new List<string>();
        if (committed)
        {
            reasons.AddRange(clips.Where(clip => clip.State == PushState.Pending)
                .Select(clip => clip.Changes.StartsWith("re-targeted", StringComparison.Ordinal)
                    ? $"{clip.ClipId} is not in the addon on the custom skeleton yet."
                    : $"{clip.ClipId} is not in the addon in its latest version."));
            if (!soundsOnly && weapon.CustomSkeleton is not null && Skeletons.CustomSkeletons.Load(sandbox) is { FromAddon: false } skeleton
                && PendingWork.StateOf(_profile, _target, skeleton.Path + "_c", skeleton.SkeletonSha256) == PushState.Pending)
            {
                reasons.Add($"The custom skeleton {Path.GetFileName(skeleton.Path)} is not in the addon yet.");
            }

            // Clips set to CS2 default one at a time: yellow while the addon still has its copy (on a custom
            // skeleton the re-targeted CS2 clip is pending above instead).
            if (!soundsOnly && weapon.CustomSkeleton is null)
            {
                foreach (var clipId in weapon.StockClips.Where(clipId => ClipExporter.LastExport(sandbox, clipId) is not { IdenticalToTemplate: false }))
                {
                    var key = $"{GraphTree.CustomClipRoot}{weaponId}/{clipId}{Weapons.WeaponCatalog.ClipExtension}";
                    if (File.Exists(AddonManifest.Resolve(_target, key)))
                    {
                        reasons.Add($"{clipId} goes back to CS2's on the next push (set to CS2 default).");
                    }
                }
            }
        }

        return new StatusPart(clips.Count > 0 || weapon.StockClips.Count > 0, reasons);
    }

    /// <summary>A weapon set to CS2 default: yellow while the addon still has files for it.</summary>
    private StatusPart StockResetPart(string weaponId)
    {
        var files = Connection is null ? null : StockReset.For(Connection, _target!, weaponId);
        var delivered = _manifest?.Clips.GetValueOrDefault(weaponId)?.Count ?? 0;
        return files is { Count: > 0 } || delivered > 0
            ? new StatusPart(false, [$"Set to CS2 default: the addon's {Math.Max(files?.Count ?? 0, delivered)} file(s) for it are removed on the next apply."])
            : new StatusPart(false, []);
    }

    private StatusPart GraphPart(string weaponId)
    {
        var graphStates = _graphs.Values.Where(state => state.Owners.Contains(weaponId)).ToList();
        var wants = _profile.Weapon(weaponId) is { Animations: AnimationMode.Custom, ResetToStock: false } && graphStates.Count > 0;
        if (_target is null)
        {
            return new StatusPart(wants, wants ? NoTarget : []);
        }

        var reasons = new List<string>();
        if (wants)
        {
            foreach (var pending in graphStates.Where(item => !item.IsApplied))
            {
                reasons.Add($"{pending.Pending.Count} graph edit(s) in {Path.GetFileName(pending.Graph)} are not in the addon yet.");
            }
        }

        return new StatusPart(wants, reasons);
    }

    private StatusPart VDataPart(IReadOnlyList<string> vdataBlocks, string weaponId)
    {
        var vdataEdits = vdataBlocks.SelectMany(block => _profile.VData.TryGetValue(block, out var values) ? values.Select(value => (block, value.Key, value.Value)) : []).ToList();
        var classSwaps = vdataBlocks.Where(_profile.VDataClasses.ContainsKey).Select(block => (Block: block, Swap: _profile.VDataClasses[block])).ToList();
        var wants = vdataEdits.Count > 0 || classSwaps.Count > 0;
        if (_target is null)
        {
            return new StatusPart(wants, wants ? NoTarget : []);
        }

        var reasons = new List<string>();
        foreach (var (block, path, value) in vdataEdits)
        {
            var actual = _addonVData?[block]?.Value(path);
            if (actual is null || !Same(actual, value))
            {
                reasons.Add($"{block}.{path} is {actual ?? "not set"} in the addon, profile says {value}.");
            }
        }

        foreach (var (block, swap) in classSwaps)
        {
            if (_stock[block] is not { } stockBlock || _stock[swap.From] is not { } donor)
            {
                continue;
            }

            var keys = swap.RootKeys(stockBlock, donor);
            var set = vdataEdits.Where(edit => edit.block == block).Select(edit => edit.Key).ToHashSet(StringComparer.Ordinal);
            if (_addonVData?[block] is not { } actual || !VDataClassSwap.Matches(VDataClassSwap.Without(actual, set), VDataClassSwap.Without(donor, set), keys))
            {
                reasons.Add($"{block} is not yet {swap.Summary()} in the addon.");
            }
        }

        // Keys an earlier class change replaced that the profile dropped must be back at CS2's.
        foreach (var block in vdataBlocks)
        {
            var still = _profile.VDataClasses.TryGetValue(block, out var swap) && _stock[block] is { } own && _stock[swap.From] is { } from
                ? swap.RootKeys(own, from) : [];
            var dropped = (_manifest?.VDataClasses.GetValueOrDefault(block) ?? []).Where(key => !still.Contains(key)).ToList();
            if (dropped.Count > 0 && Before(block) is { } stockBlock
                && (_addonVData?[block] is not { } actual || !VDataClassSwap.Matches(actual, stockBlock, dropped)))
            {
                reasons.Add($"{block}: the earlier class change should be undone ({string.Join(", ", dropped.Take(4))}{(dropped.Count > 4 ? "…" : "")}).");
            }
        }

        // Values an earlier delivery set that the profile dropped must be back at CS2's.
        foreach (var block in vdataBlocks)
        {
            foreach (var path in _manifest?.VData.GetValueOrDefault(block)?.Keys ?? Enumerable.Empty<string>())
            {
                if (vdataEdits.Any(edit => edit.block == block && edit.Key == path))
                {
                    continue;
                }

                var stockValue = Before(block)?.Value(path);
                var actual = _addonVData?[block]?.Value(path);
                if (stockValue is not null && (actual is null || !Same(actual, stockValue)))
                {
                    reasons.Add($"{block}.{path} should go back to {stockValue}.");
                }
            }
        }

        return new StatusPart(wants, reasons);
    }

    private Dictionary<string, ClipExportReport> EditedClips(string weaponId)
    {
        var result = new Dictionary<string, ClipExportReport>(StringComparer.Ordinal);
        if (!_sandboxes.TryGetValue(weaponId, out var sandbox))
        {
            return result;
        }

        foreach (var clip in sandbox.Manifest.AllClips())
        {
            var clipId = Blender.RigPackage.ClipId(clip.ResourcePath);
            try
            {
                if (ClipExporter.LastExport(sandbox, clipId) is { IdenticalToTemplate: false } report)
                {
                    result[clipId] = report;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // A broken report is treated as not exported.
            }
        }

        return result;
    }

    private static bool Same(string actual, string expected)
    {
        if (actual == expected)
        {
            return true;
        }

        return double.TryParse(actual, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
            && double.TryParse(expected, NumberStyles.Float, CultureInfo.InvariantCulture, out var b)
            && Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(b) * 1e-6);
    }
}
