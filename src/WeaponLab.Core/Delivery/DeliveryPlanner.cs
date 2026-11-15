using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.IO;
using WeaponLab.Core.Profiles;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Delivery;

/// <summary>Where a delivery goes: an addon's game folder and its content (source) folder.</summary>
public sealed record AddonTarget(string Name, string GamePath, string ContentPath)
{
    public string ManifestPath => Path.Combine(GamePath, AddonManifest.FileName);
    public string VDataSourcePath => Path.Combine(ContentPath, VDataCompiler.ContentRelativePath.Replace('/', Path.DirectorySeparatorChar));
    public string VDataCompiledPath => Path.Combine(GamePath, "scripts", "weapons.vdata_c");

    /// <summary>The addon must be one of the install's addons: game/csgo_addons/&lt;name&gt;.</summary>
    public static AddonTarget For(GameInstall install, AddonInfo addon)
    {
        var game = Path.GetFullPath(addon.GamePath);
        if (!string.Equals(Path.GetDirectoryName(game), Path.GetFullPath(install.GameAddonsPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"{addon.Name} is not an addon of this CS2 install.");
        }

        return new AddonTarget(addon.Name, game, Path.Combine(install.ContentAddonsPath, addon.Name));
    }
}

public enum DeliveryScope
{
    /// <summary>Clips, graphs and vdata: make the whole addon match the profile.</summary>
    Everything,

    /// <summary>Only vdata (the VData tab's Save); delivered clips and graphs are left as they are.</summary>
    VDataOnly,
}

/// <param name="Destination">Absolute path in the addon.</param>
/// <param name="ManifestKey">Manifest entry: relative to the game folder, or "content:" + path.</param>
/// <param name="Restores">Puts the addon's own original back (or rewrites one of the addon's own files); the file is the addon's afterwards, not in the manifest.</param>
public sealed record DeliveryFile(string Destination, string ManifestKey, byte[] Bytes, bool Restores = false);

public sealed record DeliveredClip(string WeaponId, string ClipId, string StockPath, string CustomPath, string Changes);

/// <summary>Everything a delivery will write, replace and remove, for review before it runs.</summary>
public sealed record DeliveryPlan(
    AddonTarget Target,
    IReadOnlyList<DeliveryFile> Files,
    IReadOnlyList<string> Removals,
    IReadOnlyList<DeliveredClip> Clips,
    IReadOnlyList<string> Graphs,
    IReadOnlyList<(string Block, string Path, string Value)> VDataValues,
    int VDataResets,
    bool CompilesVData,
    IReadOnlyList<string> Notes,
    AddonManifest Manifest,
    IReadOnlyList<VDataKeySwap>? VDataSwaps = null,
    VDataFile? StockVData = null,
    AddonManifest? Previous = null,
    VDataFile? OriginalVData = null,
    IReadOnlyList<Addons.AddonWarning>? AddonWarnings = null)
{
    /// <summary>Problems that would stop the delivered work from showing in game (no hooked-up player model, model not targeting its skeleton).</summary>
    public IReadOnlyList<Addons.AddonWarning> Warnings => AddonWarnings ?? [];

    /// <summary>Player models of the addon whose first-person graph is viewmodel_custom.</summary>
    public IReadOnlyList<Addons.PlayerModel> HookedPlayerModels { get; init; } = [];

    /// <summary>Clips or skeleton files another profile pushed that this push replaces with this profile's version.</summary>
    public IReadOnlyList<Addons.AddonWarning> Overwrites { get; init; } = [];

    /// <summary>The profile's files this push writes (key, SHA-256): recorded in the profile as pushed once applied.</summary>
    public IReadOnlyList<(string Key, string Sha)> Pushes { get; init; } = [];

    /// <summary>Graphs whose profile edits this push writes: they leave the profile once applied.</summary>
    public IReadOnlyList<string> PushedGraphs { get; init; } = [];

    /// <summary>Applies a successful push to the profile: its files count as pushed, and its graph edits are live now.</summary>
    public void RecordIn(Profile profile)
    {
        foreach (var (key, sha) in Pushes)
        {
            profile.RecordPushed(Target.Name, key, sha);
        }

        foreach (var graph in PushedGraphs)
        {
            profile.Graphs.Remove(graph);
        }

        foreach (var graph in StockGraphs)
        {
            profile.StockGraphs.Remove(graph);
        }

        foreach (var (weaponId, clipId) in StockClips)
        {
            profile.EnsureWeapon(weaponId).StockClips.Remove(clipId);
            if (profile.Pushed.TryGetValue(Target.Name, out var pushedFiles))
            {
                pushedFiles.Remove($"{GraphTree.CustomClipRoot}{weaponId}/{clipId}{Weapons.WeaponCatalog.ClipExtension}");
            }
        }

        foreach (var weaponId in StockResets)
        {
            // The addon is CS2's for this weapon now. The profile keeps its Blender work, left out of pushes until
            // the weapon is ticked again in the Deliver tree; then all of it is pushed again.
            var settings = profile.EnsureWeapon(weaponId);
            settings.ResetToStock = false;
            settings.Animations = AnimationMode.Default;
            if (profile.Pushed.TryGetValue(Target.Name, out var pushed))
            {
                foreach (var key in pushed.Keys.Where(key => key.StartsWith(GraphTree.CustomClipRoot + weaponId + "/", StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    pushed.Remove(key);
                }
            }
        }
    }

    /// <summary>Addon files outside the manifest that would point at something this delivery removes.</summary>
    public IReadOnlyList<Addons.AddonWarning> BrokenReferences { get; init; } = [];

    /// <summary>Weapons set to CS2 default whose addon files this delivery removes; their flag is cleared once applied.</summary>
    public IReadOnlyList<string> StockResets { get; init; } = [];

    /// <summary>Clips set to CS2 default one at a time that this delivery handles; they leave the profile's list once applied.</summary>
    public IReadOnlyList<(string Weapon, string Clip)> StockClips { get; init; } = [];

    /// <summary>Graphs this push rebuilds from CS2's (set to CS2 default, 0.24).</summary>
    public IReadOnlyList<string> StockGraphs { get; init; } = [];

    /// <summary>Keys whose recorded originals are dropped after applying: what this delivery leaves is the addon's own.</summary>
    public IReadOnlyList<string> ForgetOriginals { get; init; } = [];

    public int VDataEdits => VDataValues.Count - VDataResets;

    public IReadOnlyList<VDataKeySwap> Swaps => VDataSwaps ?? [];

    public IEnumerable<string> Replaced => Files.Select(file => file.Destination).Where(File.Exists);

    /// <summary>Manifest keys of every file the delivery writes, restores or removes.</summary>
    public IEnumerable<string> TouchedKeys =>
        Files.Where(file => file.ManifestKey != AddonManifest.FileName).Select(file => file.ManifestKey)
            .Concat(Removals.Select(path => AddonManifest.KeyFor(Target, path)))
            .Concat(CompilesVData ? [AddonManifest.CompiledVDataPath] : []);

    /// <summary>
    /// True when applying would change nothing in the addon: no removals, no
    /// vdata compile, and every file already on disk with the same bytes.
    /// </summary>
    public bool IsEmpty =>
        Removals.Count == 0
        && !CompilesVData
        && Files.Where(file => file.ManifestKey != AddonManifest.FileName)
            .All(file => File.Exists(file.Destination) && File.ReadAllBytes(file.Destination).AsSpan().SequenceEqual(file.Bytes));
}

/// <summary>
/// Works out what an addon needs to match a profile, CS2AL-style: edited clips
/// of Custom weapons at <c>animation/anims/custom/&lt;weapon&gt;/</c>, the graphs
/// that play them cloned up to <c>viewmodel_custom.vnmgraph</c>, and the
/// profile's vdata written over the addon's own vdata for compiling.
/// The addon is the starting point: graphs the addon already has are patched
/// rather than rebuilt from CS2's, and anything the profile no longer wants
/// goes back to the addon's own original (see <see cref="AddonOriginals"/>).
/// </summary>
public static class DeliveryPlanner
{
    /// <summary>Everything the planning steps share.</summary>
    private sealed class Context(GameConnection connection, AddonTarget target, Profile profile, AddonManifest? previous, AddonOriginals originals)
    {
        public GameConnection Connection { get; } = connection;
        public AddonTarget Target { get; } = target;
        public Profile Profile { get; } = profile;
        public AddonManifest? Previous { get; } = previous;
        public AddonOriginals Originals { get; } = originals;
        public List<DeliveryFile> Files { get; } = [];
        public List<string> Notes { get; } = [];
        public AddonManifest Manifest { get; } = new() { Profile = profile.Name, DeliveredUtc = DateTimeOffset.UtcNow };

        /// <summary>Problems found while planning (a custom skeleton the vdata does not point at).</summary>
        public List<Addons.AddonWarning> Warnings { get; } = [];

        /// <summary>The profile's files this push writes (key, SHA-256), recorded in the profile once applied.</summary>
        public List<(string Key, string Sha)> Pushes { get; } = [];

        /// <summary>Graphs whose profile edits this push writes; the edits leave the profile once applied (they are live then).</summary>
        public List<string> PushedGraphs { get; } = [];

        /// <summary>Graphs rebuilt from CS2's this push (<see cref="Profiles.Profile.StockGraphs"/>), cleared from the profile once delivered.</summary>
        public List<string> StockGraphs { get; } = [];

        /// <summary>The addon's own version of a file, or null (see <see cref="AddonOriginals.Before"/>).</summary>
        public byte[]? AddonOwn(string key) => Originals.Before(key, Target, Previous);
    }

    /// <param name="rebaseVData">Start the addon's vdata from the current CS2 file instead of its own.</param>
    /// <param name="compileVData">Compile the vdata source after writing it; false writes only the content source.</param>
    /// <param name="cs2RootCopy">
    /// True adds a copy of viewmodel_custom at CS2's own root graph path (local play only), false removes it,
    /// null keeps what the addon has (see <see cref="PlanCs2RootCopy"/>).
    /// </param>
    public static DeliveryPlan Plan(
        SandboxStore sandboxes,
        GameConnection connection,
        AddonTarget target,
        Profile profile,
        DeliveryScope scope = DeliveryScope.Everything,
        bool rebaseVData = false,
        bool compileVData = true,
        bool? cs2RootCopy = null)
    {
        var context = new Context(connection, target, profile, AddonManifest.Read(target), AddonOriginals.Load(sandboxes.WorkspacePath, target.Name));
        context.Manifest.Workspace = Path.GetFullPath(sandboxes.WorkspacePath);
        var previous = context.Previous;
        var manifest = context.Manifest;

        // The addon is the live state (user decision 2026-10-26): everything earlier pushes put in stays listed,
        // and this push adds to it. Only Set to CS2 default removes files. VData is the VData tab's alone.
        if (previous is not null)
        {
            foreach (var (key, sha) in previous.Files.Where(entry => scope == DeliveryScope.Everything || !AddonManifest.IsVData(entry.Key)))
            {
                manifest.Files[key] = sha;
            }

            foreach (var (key, owner) in previous.FileProfiles)
            {
                manifest.FileProfiles[key] = owner;
            }

            foreach (var (weaponId, list) in previous.Clips)
            {
                manifest.Clips[weaponId] = [.. list];
            }

            foreach (var (graph, edits) in previous.Graphs)
            {
                manifest.Graphs[graph] = new SortedDictionary<string, string>(edits, StringComparer.Ordinal);
            }

            manifest.PlayerModelGraph = previous.PlayerModelGraph;
            if (scope == DeliveryScope.Everything)
            {
                manifest.VData = previous.VData;
                manifest.VDataClasses = previous.VDataClasses;
            }
        }

        var clips = new List<DeliveredClip>();
        var graphs = new List<string>();
        if (scope == DeliveryScope.Everything)
        {
            PlanClips(context, sandboxes, clips, graphs);
        }

        var vdata = scope == DeliveryScope.VDataOnly
            ? PlanVData(context, rebaseVData, compileVData)
            : new VDataPart([], [], 0, false, null);

        var removals = scope == DeliveryScope.VDataOnly ? Removals(context, scope, vdata.Compiles) : [];
        var stockClips = scope == DeliveryScope.Everything ? PlanStockClips(context, sandboxes, removals) : ([], []);
        var reset = scope == DeliveryScope.Everything ? PlanStockResets(context, removals) : new StockResetPart([], []);
        if (scope == DeliveryScope.Everything)
        {
            PlanCs2RootCopy(context, removals, cs2RootCopy);
            PlanStaleModelCopies(context, removals);
        }

        foreach (var path in removals)
        {
            var key = AddonManifest.KeyFor(target, path);
            manifest.Files.Remove(key);
            manifest.FileProfiles.Remove(key);
        }

        foreach (var weaponId in reset.Weapons)
        {
            manifest.Clips.Remove(weaponId);
        }

        // Clips and skeleton files another profile pushed are replaced by this profile's: say so before it happens.
        var overwrites = new List<Addons.AddonWarning>();
        foreach (var file in context.Files.Where(file => !file.Restores && !file.ManifestKey.EndsWith(".vnmgraph_c", StringComparison.OrdinalIgnoreCase) && !AddonManifest.IsVData(file.ManifestKey)))
        {
            if (previous?.FileProfiles.TryGetValue(file.ManifestKey, out var owner) == true && owner != profile.Name
                && File.Exists(file.Destination) && GameArchive.Sha256(File.ReadAllBytes(file.Destination)) != GameArchive.Sha256(file.Bytes))
            {
                overwrites.Add(new Addons.AddonWarning(file.ManifestKey, $"was pushed by profile '{owner}'; this push replaces it with the version from profile '{profile.Name}'."));
            }
        }

        foreach (var file in context.Files.Where(file => !file.Restores))
        {
            manifest.Files[file.ManifestKey] = GameArchive.Sha256(file.Bytes);
            manifest.FileProfiles[file.ManifestKey] = profile.Name;
        }

        // A delivered file this push rewrites in place (Set to CS2 default re-points Weapon Lab's own graphs) keeps its entry with
        // the new bytes, so the next push does not call it changed outside Weapon Lab (bug 2026-10-31).
        foreach (var file in context.Files.Where(file => file.Restores && manifest.Files.ContainsKey(file.ManifestKey)))
        {
            manifest.Files[file.ManifestKey] = GameArchive.Sha256(file.Bytes);
        }

        // The compiled vdata is hashed after compiling (see AddonDelivery).
        if (vdata.Compiles)
        {
            manifest.Files.Remove(AddonManifest.CompiledVDataPath);
        }

        foreach (var file in context.Files.Where(file => !file.Restores && File.Exists(file.Destination)))
        {
            if (previous is not null && previous.Files.TryGetValue(file.ManifestKey, out var sha)
                && GameArchive.Sha256(File.ReadAllBytes(file.Destination)) != sha)
            {
                context.Notes.Add($"{file.ManifestKey} was changed outside Weapon Lab since it was pushed; this push replaces it (backed up first).");
            }
        }

        context.Files.Add(new DeliveryFile(target.ManifestPath, AddonManifest.FileName, manifest.ToBytes()));

        // Workshop addons cannot replace CS2's own files: check the addon can reach what it delivers.
        var inventory = Addons.AddonInventory.Load(connection, target, previous);
        var warnings = Addons.AddonChecks.Hookup(inventory, graphs.Count > 0, graphs.Contains(GraphTree.WorldCustomParent)).Concat(context.Warnings).Concat(overwrites).ToList();

        // Safeguard: addon files left pointing at removed ones. CS2 still has its own root graph when the copy goes.
        var removedKeys = removals.Select(path => AddonManifest.KeyFor(target, path))
            .Where(key => !key.Equals(Cs2RootCopyKey, StringComparison.OrdinalIgnoreCase)).ToList();
        var writtenKeys = context.Files.Select(file => file.ManifestKey).ToList();
        var broken = Addons.AddonChecks.BrokenReferences(target, removedKeys, writtenKeys);
        warnings.AddRange(broken);
        return new DeliveryPlan(target, context.Files, removals, clips, graphs, vdata.Values, vdata.Resets, vdata.Compiles, context.Notes, manifest,
            vdata.Swaps, vdata.Swaps.Count > 0 ? connection.ReadVData() : null, previous, vdata.Original, warnings)
        {
            HookedPlayerModels = Addons.AddonChecks.HookedPlayerModels(inventory),
            Overwrites = overwrites,
            BrokenReferences = broken,
            StockResets = reset.Weapons,
            StockClips = stockClips.Done,
            StockGraphs = context.StockGraphs,
            ForgetOriginals = [.. reset.Forget, .. stockClips.Forget],
            Pushes = context.Pushes,
            PushedGraphs = context.PushedGraphs,
        };
    }

    // ---- Clips and graphs ----

    /// <summary>
    /// Pushes the profile's pending clips (see <see cref="PendingWork"/>):
    /// edited exports and custom-skeleton clips the live addon does not have
    /// yet go to <c>animation/anims/custom/&lt;weapon&gt;/</c>, and the graphs that
    /// play them are cloned on top of the addon's current custom graphs, so
    /// whatever the addon already routes (other weapons, other profiles' pushes)
    /// stays. Weapons left out of the push and clips already live are skipped.
    /// </summary>
    private static void PlanClips(Context context, SandboxStore sandboxes, List<DeliveredClip> clips, List<string> graphNames)
    {
        var (connection, target, profile, manifest) = (context.Connection, context.Target, context.Profile, context.Manifest);
        var clipMap = new Dictionary<string, string>(StringComparer.Ordinal);

        // CS2's path → the addon's copy, for every clip of this profile the addon has or gets now: a graph
        // edit may name either (the Graphs tab offers both), and must play the slot that already holds it.
        var clipAliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sandbox in sandboxes.List())
        {
            var weaponId = sandbox.Manifest.WeaponId;
            if (profile.Weapon(weaponId) is { Animations: AnimationMode.Default } or { ResetToStock: true })
            {
                continue;
            }

            var skeleton = PlanSkeleton(context, sandbox);
            var offered = PendingWork.Clips(profile, sandbox, target);
            foreach (var replaced in offered.Where(clip => clip.State == PushState.ReplacedInAddon))
            {
                context.Notes.Add($"{weaponId}/{replaced.ClipId}: the addon has another version now (another profile's or a hand edit); this profile's is pushed again only after a new export.");
            }

            foreach (var clip in offered.Where(clip => clip.State == PushState.Pending || File.Exists(AddonManifest.Resolve(target, clip.Key))))
            {
                clipAliases[clip.StockPath] = clip.Key[..^2];
            }

            var pending = offered.Where(clip => clip.State == PushState.Pending).ToList();
            if (pending.Count > 0 && sandbox.Manifest.SourceAddon is { } source && !string.Equals(source, target.Name, StringComparison.OrdinalIgnoreCase))
            {
                context.Warnings.Add(new Addons.AddonWarning(weaponId,
                    $"its Blender work started from {source}'s clips, so {target.Name} gets {source}'s animations with your edits on top."));
            }

            foreach (var clip in pending)
            {
                var bytes = File.ReadAllBytes(clip.SourceFile);
                if (GameArchive.Sha256(bytes) != clip.Sha)
                {
                    throw new DeliveryException(skeleton is not null && clip.Changes.StartsWith("re-targeted", StringComparison.Ordinal)
                        ? $"{weaponId}/{clip.ClipId}: its re-targeted template changed. Make the custom skeleton again."
                        : $"{weaponId}/{clip.ClipId}: the exported clip changed after export. Export it again from Blender.");
                }

                var custom = clip.Key[..^2];
                if (File.Exists(AddonManifest.Resolve(target, clip.Key)) && context.Previous?.Files.ContainsKey(clip.Key) != true)
                {
                    context.Notes.Add($"Replaces {clip.Key}, which the addon had from outside Weapon Lab (backed up first).");
                }

                clipMap[clip.StockPath] = custom;
                context.Files.Add(new DeliveryFile(AtomicFile.SafeJoin(target.GamePath, clip.Key), clip.Key, bytes));
                context.Pushes.Add((clip.Key, clip.Sha));
                clips.Add(new DeliveredClip(weaponId, clip.ClipId, clip.StockPath, custom, clip.Changes));
                if (!manifest.Clips.TryGetValue(weaponId, out var list))
                {
                    manifest.Clips[weaponId] = list = [];
                }

                if (!list.Contains(clip.ClipId))
                {
                    list.Add(clip.ClipId);
                }
            }

            // Third-person clips keep the weapon's own skeleton (0.26): a custom skeleton is first person only.
            var world = pending.Where(clip => Weapons.WeaponCatalog.IsWorldClip(clip.StockPath)).ToList();
            if (world.Count > 0 && skeleton is not null)
            {
                context.Notes.Add($"{weaponId}: its {world.Count} third-person clip(s) keep CS2's weapon skeleton; the custom skeleton {Path.GetFileName(skeleton.Path)} is first person only. Untested how CS2 plays them on your model.");
            }

            WarnMixedSkeletons(context, weaponId, pending.Except(world).ToList());
            WarnTiming(context, sandbox, pending);
        }

        // The third-person graphs only when a third-person clip goes out (0.26): they route it like the viewmodel's.
        var anyWorld = clipMap.Keys.Any(Weapons.WeaponCatalog.IsWorldClip);
        var allGraphs = profile.Graphs.Count == 0 && clipMap.Count == 0 && profile.StockGraphs.Count == 0 ? null : GraphTree.Load(connection.Archive, includeWorld: anyWorld);
        var graphEdits = allGraphs is null ? [] : DeliverableGraphEdits(connection, profile, allGraphs, context.Notes);
        if (clipMap.Count == 0 && graphEdits.Count == 0 && profile.StockGraphs.Count == 0)
        {
            return;
        }

        var plan = GraphTree.Plan(allGraphs!, clipMap, graphEdits.Keys.Concat(profile.StockGraphs));
        foreach (var graph in plan.Graphs)
        {
            var custom = plan.ResourceMap[graph.Path];
            var key = custom + "_c";
            var edits = graphEdits.GetValueOrDefault(graph.Path);
            var stockBytes = connection.Archive.ReadBytes(graph.Path + "_c");

            // Build on the addon's current version (the live state: other weapons' and other profiles' routing) so it survives.
            var livePath = AddonManifest.Resolve(target, key);
            var liveBytes = File.Exists(livePath) ? File.ReadAllBytes(livePath) : null;
            var reset = profile.StockGraphs.Contains(graph.Path);
            var baseBytes = reset ? stockBytes : liveBytes ?? stockBytes;
            var map = plan.ResourceMap;
            if (reset)
            {
                // Set to CS2 default (0.24): CS2's graph again, with this profile's clips (pushed before or now) routed in.
                var merged = new Dictionary<string, string>(plan.ResourceMap, StringComparer.Ordinal);
                foreach (var (stock, customClip) in clipAliases)
                {
                    merged.TryAdd(stock, customClip);
                }

                map = merged;
                context.StockGraphs.Add(graph.Path);
                context.Notes.Add($"Rebuilds {key} from CS2's graph (set to CS2 default): edits the addon's version had are dropped; this profile's clips stay routed.");
            }
            else if (liveBytes is not null)
            {
                context.Notes.Add($"Builds on the addon's current {key}, keeping what it already routes.");
                if (edits is not null && !SameNodeCount(liveBytes, stockBytes, key))
                {
                    context.Notes.Add($"{key}: the addon's graph has a different node layout from CS2's, so {edits.Count} graph edit(s) are skipped.");
                    edits = null;
                }
            }

            if (edits is not null && liveBytes is not null && !reset)
            {
                // An edit over a value the addon already changed (another profile's push, a hand edit): say what it replaces.
                var (live, stock) = (Graphs.GraphDocument.Read(liveBytes, key), Graphs.GraphDocument.Read(stockBytes, key));
                foreach (var (editKey, value) in edits)
                {
                    var (node, valuePath) = Graphs.GraphEdits.Parse(editKey);
                    if (node < 0 || node >= live.Nodes.Count || valuePath == Graphs.GraphDocument.ClipEditKey)
                    {
                        continue;
                    }

                    var (now, original) = (live[node].Value(valuePath), stock[node].Value(valuePath));
                    if (now is not null && now != original && now != value)
                    {
                        context.Notes.Add($"{Path.GetFileName(key)}: {live[node].Name} {valuePath} goes from the addon's {now} (CS2: {original}) to {value}.");
                    }
                }
            }

            var (bytes, _) = GraphPatcher.Patch(baseBytes, key, map, edits, clipAliases);
            context.Files.Add(new DeliveryFile(AtomicFile.SafeJoin(target.GamePath, key), key, bytes));
            graphNames.Add(custom);
            if (edits is not null)
            {
                if (!manifest.Graphs.TryGetValue(graph.Path, out var held))
                {
                    manifest.Graphs[graph.Path] = held = new SortedDictionary<string, string>(StringComparer.Ordinal);
                }

                foreach (var (editKey, value) in edits)
                {
                    held[editKey] = value;
                }

                context.PushedGraphs.Add(graph.Path);
            }
        }

        var clipsPlayedByEdits = graphEdits.Values
            .SelectMany(edits => edits.Where(edit => edit.Key.EndsWith(":" + Graphs.GraphDocument.ClipEditKey, StringComparison.Ordinal)).Select(edit => edit.Value))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var clip in plan.UnreferencedClips.Where(clip => !clipsPlayedByEdits.Contains(clip)))
        {
            context.Notes.Add($"{clip} is not played by any {(Weapons.WeaponCatalog.IsWorldClip(clip) ? "third-person" : "viewmodel")} graph, so the game will not use it.");
        }

        if (plan.Graphs.Any(graph => graph.Path == GraphTree.WorldParent))
        {
            context.Notes.Add($"Third person: {Path.GetFileName(GraphTree.WorldCustomParent)} plays the third-person clips. Player models need it in their AnimGraph2List "
                + "(the \"\" and \"worldmodel\" entries); untested in game which entry CS2 uses, and whether other players see it.");
        }

        manifest.PlayerModelGraph = GraphTree.CustomParent;
    }

    /// <summary>
    /// Plain-words timing warnings for the reloads this push delivers (0.24): when the game, running the expected
    /// class's reload (the profile's pick, else the vdata's gun), counts ammo against what the clip shows.
    /// </summary>
    private static void WarnTiming(Context context, SandboxInfo sandbox, IReadOnlyList<ProfileClip> pushed)
    {
        var weaponId = sandbox.Manifest.WeaponId;
        var reloads = pushed.Where(clip => clip.ClipId.Contains("reload", StringComparison.OrdinalIgnoreCase)).Select(clip => clip.StockPath + "_c").ToHashSet(StringComparer.Ordinal);
        if (reloads.Count == 0 || context.Connection.FindWeapon(weaponId) is not { } weapon)
        {
            return;
        }

        try
        {
            var vdata = File.Exists(context.Target.VDataCompiledPath) ? VData.VDataFile.Read(File.ReadAllBytes(context.Target.VDataCompiledPath), context.Target.VDataCompiledPath) : null;
            var gun = Weapons.GameTimings.Resolve(context.Connection, weapon, vdata, context.Target.Name, context.Profile.Weapon(weaponId).TimingGun);
            foreach (var check in Checks.WeaponChecks.Timing(context.Connection, weapon, sandbox, gun, _ => null).Where(check => check.ClipPath is { } path && reloads.Contains(path)))
            {
                context.Warnings.Add(new Addons.AddonWarning(weaponId, check.Message));
            }
        }
        catch (Exception error) when (error is InvalidDataException or IOException)
        {
            // No timing to compare with: nothing to warn about.
        }
    }

    /// <summary>
    /// Pushing some of a weapon's clips while the addon keeps others on another
    /// skeleton leaves the weapon half on one skeleton and half on the other:
    /// the clips that do not match its model stand still in game.
    /// </summary>
    private static void WarnMixedSkeletons(Context context, string weaponId, IReadOnlyList<ProfileClip> pushed)
    {
        if (pushed.Count == 0)
        {
            return;
        }

        static string? SkeletonOf(string file) => StockReset.WeaponSkeletonOf(File.ReadAllBytes(file), file);

        var pushedSkeletons = pushed.Select(clip => SkeletonOf(clip.SourceFile)).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var folder = AddonManifest.Resolve(context.Target, GraphTree.CustomClipRoot + weaponId);
        if (pushedSkeletons.Count != 1 || !Directory.Exists(folder))
        {
            return;
        }

        var keys = pushed.Select(clip => clip.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var others = Directory.EnumerateFiles(folder, "*.vnmclip_c")
            .Where(file => !keys.Contains(AddonManifest.KeyFor(context.Target, file)))
            .Select(file => (File: file, Skeleton: SkeletonOf(file)))
            .Where(item => item.Skeleton is not null && !pushedSkeletons.Contains(item.Skeleton))
            .Select(item => Path.GetFileNameWithoutExtension(item.File))
            .ToList();
        if (others.Count > 0)
        {
            context.Warnings.Add(new Addons.AddonWarning(weaponId,
                $"the pushed clips play on {Path.GetFileName(pushedSkeletons.First())}, but the addon keeps {string.Join(", ", others.Take(5))}{(others.Count > 5 ? "…" : "")} on another skeleton. " +
                "Clips that do not match the weapon's model stand still in game: push them on the same skeleton too, or use Set to CS2 default first."));
        }
    }

    /// <summary>
    /// A Custom weapon's custom skeleton: the skeleton file goes into the addon
    /// (the user points their own model at it in ModelDoc), and the model the
    /// weapon shows is checked against it (warnings). Returns the sandbox's setup, or null when the weapon
    /// keeps its own skeleton. The profile and the sandbox must agree.
    /// </summary>
    private static Skeletons.CustomSkeletonSetup? PlanSkeleton(Context context, SandboxInfo sandbox)
    {
        var weaponId = sandbox.Manifest.WeaponId;
        var choice = context.Profile.Weapon(weaponId).CustomSkeleton;
        var setup = Skeletons.CustomSkeletons.Load(sandbox);
        if (choice is null && setup is null)
        {
            return null;
        }

        if (choice is null)
        {
            throw new DeliveryException($"{weaponId}'s sandbox has a custom skeleton ({setup!.Path}) the profile does not use. Use it in the profile or remove it (Animations tab, Custom skeleton).");
        }

        if (setup is null || setup.Path != choice.Path || setup.SourceModel != choice.Model)
        {
            throw new DeliveryException($"{weaponId}: the profile's custom skeleton ({choice.Path} from {choice.Model}) is not made in its sandbox. Make it in the Animations tab (Custom skeleton).");
        }

        var skeletonBytes = Skeletons.CustomSkeletons.SkeletonBytes(sandbox, setup);
        if (setup.FromAddon)
        {
            // The addon's own skeleton and model stay as they are; only the clips come from Weapon Lab.
            CheckModel(context, sandbox, setup);
            var addonSkeleton = AtomicFile.SafeJoin(context.Target.GamePath, setup.Path + "_c");
            if (!File.Exists(addonSkeleton))
            {
                // A sandbox that started from another addon's skeleton, pushed here: the skeleton comes along, its model does not.
                context.Files.Add(new DeliveryFile(addonSkeleton, setup.Path + "_c", skeletonBytes));
                context.Pushes.Add((setup.Path + "_c", GameArchive.Sha256(skeletonBytes)));
                context.Warnings.Add(new Addons.AddonWarning(weaponId,
                    $"its clips play on {setup.Path}, which came from another addon; the skeleton is copied in, but its model ({setup.ModelCopy}) is not. Give the weapon a model that targets it."));
                return setup;
            }

            if (GameArchive.Sha256(File.ReadAllBytes(addonSkeleton)) != setup.SkeletonSha256)
            {
                throw new DeliveryException($"{weaponId}: the addon's skeleton {setup.Path} is not the one the sandbox uses (changed since, or another addon's). Build the .blend again to pick it up.");
            }

            context.Notes.Add($"{weaponId} uses the addon's own skeleton {setup.Path} ({setup.Bones.Count} bones) and model {setup.ModelCopy}; its edited clips are pushed on it.");
            return setup;
        }

        // Only the skeleton ships (0.23): the user points their own model at it in ModelDoc (NmSkeletonReference). The patched
        // model copy earlier versions shipped went stale with every recompile of theirs; PlanStaleModelCopies removes it.
        var files = new List<(string Key, byte[] Bytes)> { (setup.Path + "_c", skeletonBytes) };
        CheckModel(context, sandbox, setup);

        var pushed = false;
        foreach (var (key, bytes) in files)
        {
            var sha = GameArchive.Sha256(bytes);
            if (PendingWork.StateOf(context.Profile, context.Target, key, sha) != PushState.Pending)
            {
                continue; // Already live, or replaced in the addon since this profile pushed it.
            }

            if (File.Exists(AddonManifest.Resolve(context.Target, key)) && context.Previous?.Files.ContainsKey(key) != true)
            {
                context.Notes.Add($"Replaces {key}, which the addon had from outside Weapon Lab (backed up first).");
            }

            context.Files.Add(new DeliveryFile(AtomicFile.SafeJoin(context.Target.GamePath, key), key, bytes));
            context.Pushes.Add((key, sha));
            pushed = true;
        }

        if (pushed)
        {
            context.Notes.Add($"{weaponId} uses the custom skeleton {setup.Path} ({setup.Bones.Count} bones, {setup.ExtraBones.Count} extra); all of its clips are pushed on it. "
                + $"Its model must use the skeleton: in ModelDoc, NmSkeletonReference Source File {setup.Path}.");
        }

        return setup;
    }

    /// <summary>
    /// Warns when the model the weapon shows in game does not use its custom skeleton, or gives a shared bone
    /// another parent (CS2 applies tracks on the model's hierarchy; see <see cref="Skeletons.ModelCheck"/>).
    /// </summary>
    private static void CheckModel(Context context, SandboxInfo sandbox, Skeletons.CustomSkeletonSetup setup)
    {
        var weaponId = sandbox.Manifest.WeaponId;
        IReadOnlyList<string> blocks = [];
        try
        {
            blocks = context.Connection.FindWeapon(weaponId) is { } weapon ? context.Connection.VDataBlocksFor(weapon) : [];
        }
        catch (InvalidDataException)
        {
            // No vdata link: the skeleton's source model stands in.
        }

        var check = Skeletons.ModelCheck.For(context.Connection, context.Target, context.Profile, blocks,
            Skeletons.CustomSkeletons.ReadSkeleton(sandbox, setup), setup.SourceModel);
        foreach (var problem in check.Problems())
        {
            context.Warnings.Add(new Addons.AddonWarning(weaponId, problem));
        }
    }

    /// <summary>
    /// Removes the model copies earlier versions shipped with a custom skeleton (models/weapons/weaponlab/, the
    /// source model with its skeleton reference swapped; 0.12 to 0.22.5). Only copies Weapon Lab wrote (the
    /// manifest lists them) and no vdata m_szWorldModel names, compiled or in the source; backed up like every removal.
    /// </summary>
    private static void PlanStaleModelCopies(Context context, List<string> removals)
    {
        if (context.Previous is null)
        {
            return;
        }

        var copies = context.Previous.Files.Keys
            .Where(key => key.StartsWith(ModelCopyRoot, StringComparison.OrdinalIgnoreCase) && key.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (copies.Count == 0)
        {
            return;
        }

        var compiled = File.Exists(context.Target.VDataCompiledPath) ? TryReadVData(context.Target.VDataCompiledPath) : null;
        var source = File.Exists(context.Target.VDataSourcePath) ? File.ReadAllText(context.Target.VDataSourcePath) : "";
        var named = (compiled?.Blocks ?? [])
            .Select(block => block.Value(VDataBlock.WorldModelKey))
            .Concat(context.Profile.VData.Values.Select(values => values.GetValueOrDefault(VDataBlock.WorldModelKey)))
            .OfType<string>()
            .Select(model => model.Trim().Trim('"').Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var key in copies)
        {
            var model = key[..^2];
            if (named.Contains(model) || source.Contains(model, StringComparison.OrdinalIgnoreCase))
            {
                context.Notes.Add($"{key} stays: a weapon's vdata names it as its model (Weapon Lab no longer updates it).");
                continue;
            }

            var path = AddonManifest.Resolve(context.Target, key);
            if (File.Exists(path) && !removals.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                removals.Add(path);
                context.Notes.Add($"Removes {key}: a model copy earlier versions shipped with a custom skeleton; nothing names it now (backed up first).");
            }
        }
    }

    /// <summary>Where earlier versions put a custom skeleton's model copy.</summary>
    private const string ModelCopyRoot = "models/weapons/weaponlab/";

    private static VDataFile? TryReadVData(string path)
    {
        try
        {
            return VDataFile.Read(File.ReadAllBytes(path), path);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool SameNodeCount(byte[] addon, byte[] stock, string key) =>
        Graphs.GraphDocument.Read(addon, key).Nodes.Count == Graphs.GraphDocument.Read(stock, key).Nodes.Count;

    /// <summary>
    /// The profile's graph edits for graphs that exist and whose weapons are
    /// not all set to Default (a graph no weapon's clips are in, such as the
    /// root, is always delivered).
    /// </summary>
    public static Dictionary<string, SortedDictionary<string, string>> DeliverableGraphEdits(
        GameConnection connection, Profile profile, IReadOnlyDictionary<string, GraphInfo> graphs, List<string>? notes = null)
    {
        var result = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (graph, edits) in profile.Graphs.Where(item => item.Value.Count > 0))
        {
            if (!graphs.TryGetValue(graph, out var info))
            {
                notes?.Add($"{graph} has edits but is no longer in the game; they are skipped.");
                continue;
            }

            var owners = GraphOwners(connection, info);
            if (owners.Count > 0 && owners.All(weapon => profile.Weapon(weapon) is { Animations: AnimationMode.Default } or { ResetToStock: true }))
            {
                notes?.Add($"{graph}: {edits.Count} graph edit(s) skipped because {string.Join(", ", owners)} {(owners.Count == 1 ? "is" : "are")} set to Default.");
                continue;
            }

            result[graph] = edits;
        }

        return result;
    }

    /// <summary>
    /// The weapon(s) a graph belongs to: those with the most clips in it. A
    /// variation borrowing one clip from another weapon (the AK's graph plays
    /// an M249 clip) still belongs to its own weapon.
    /// </summary>
    public static IReadOnlyList<string> GraphOwners(GameConnection connection, GraphInfo graph)
    {
        if (graph.Path == GraphTree.Parent)
        {
            return []; // The root routes every weapon; the few clips it plays itself (C4, Healthshot) do not make it theirs.
        }

        var resources = graph.Resources.ToHashSet(StringComparer.Ordinal);
        var counts = connection.Weapons
            .Select(weapon => (weapon.Id, Count: weapon.ClipPaths.Count(path => resources.Contains(path[..^2]))))
            .Where(item => item.Count > 0)
            .ToList();
        var most = counts.Select(item => item.Count).DefaultIfEmpty(0).Max();
        return counts.Where(item => item.Count == most).Select(item => item.Id).ToList();
    }

    // ---- VData ----

    private sealed record VDataPart(
        List<(string Block, string Path, string Value)> Values,
        List<VDataKeySwap> Swaps,
        int Resets,
        bool Compiles,
        VDataFile? Original);

    /// <summary>
    /// The addon's vdata before Weapon Lab first changed it, or null when it
    /// had none (then CS2's values are the addon's values).
    /// </summary>
    public static VDataFile? OriginalVData(AddonOriginals originals, AddonTarget target, AddonManifest? manifest)
    {
        try
        {
            return originals.Before(AddonManifest.CompiledVDataPath, target, manifest) is { } bytes
                ? VDataFile.Read(bytes, "original weapons.vdata_c")
                : null;
        }
        catch (InvalidDataException)
        {
            return null; // An unreadable original falls back to CS2's values.
        }
    }

    private static VDataPart PlanVData(Context context, bool rebase, bool compile)
    {
        var (connection, target, profile, previous, manifest, notes) = (context.Connection, context.Target, context.Profile, context.Previous, context.Manifest, context.Notes);
        var stock = connection.ReadVData();
        var originalBytes = context.AddonOwn(AddonManifest.CompiledVDataPath);
        var original = OriginalVData(context.Originals, target, previous);
        var baseline = original ?? stock;
        var baselineName = original is null ? "CS2's" : "the addon's own";

        var swaps = new List<VDataKeySwap>();
        foreach (var (block, swap) in profile.VDataClasses)
        {
            if (stock[block] is not { } targetBlock || stock[swap.From] is not { } donor)
            {
                notes.Add($"{block} cannot be based on {swap.From}: CS2 has no such block any more.");
                continue;
            }

            var keys = swap.RootKeys(targetBlock, donor);
            swaps.Add(new VDataKeySwap(block, swap.From, keys));
            manifest.VDataClasses[block] = [.. keys];
            notes.Add($"{block} is {swap.Summary()}.");
        }

        // Keys an earlier class change replaced that the profile no longer replaces go back to the addon's own.
        var classResets = 0;
        foreach (var (block, keys) in previous?.VDataClasses ?? [])
        {
            var still = swaps.FirstOrDefault(swap => swap.Block == block)?.Keys ?? [];
            var dropped = keys.Where(key => !still.Contains(key)).ToList();
            if (dropped.Count > 0 && baseline[block] is not null)
            {
                swaps.Add(new VDataKeySwap(block, block, dropped, FromOriginal: original is not null));
                classResets++;
                notes.Add($"{block}: {dropped.Count} key(s) an earlier class change replaced go back to {baselineName} values.");
            }
        }

        var values = new List<(string Block, string Path, string Value)>();
        foreach (var (block, paths) in profile.VData)
        {
            foreach (var (path, value) in paths)
            {
                values.Add((block, path, value));
            }

            manifest.VData[block] = new SortedDictionary<string, string>(paths, StringComparer.Ordinal);
        }

        // Values an earlier delivery set that the profile no longer has go back to the addon's own (or CS2's).
        var resets = 0;
        foreach (var (block, paths) in previous?.VData ?? [])
        {
            foreach (var path in paths.Keys)
            {
                if (profile.VData.TryGetValue(block, out var current) && current.ContainsKey(path))
                {
                    continue;
                }

                // A key a class change replaces is reset by the swap itself.
                if (swaps.Any(swap => swap.Block == block && swap.Keys.Contains(VDataGlossary.RootKey(path))))
                {
                    continue;
                }

                if ((baseline[block] ?? stock[block])?.Value(path) is { } before)
                {
                    values.Add((block, path, before));
                    resets++;
                }
            }
        }

        if (values.Count == 0 && swaps.Count == 0 && !rebase)
        {
            return new VDataPart(values, swaps, 0, false, original);
        }

        var addonHasVData = File.Exists(target.VDataCompiledPath);
        var stockBytes = connection.Archive.ReadBytes(VDataFile.GamePath);
        var baseBytes = addonHasVData && !rebase ? File.ReadAllBytes(target.VDataCompiledPath) : stockBytes;
        string text;
        if (!rebase && File.Exists(target.VDataSourcePath))
        {
            // Edit the addon's own source in place: its formatting, comments and uncompiled hand edits stay.
            text = VDataSourcePatcher.Patch(File.ReadAllText(target.VDataSourcePath), values, swaps, baseBytes, stockBytes, originalBytes);
        }
        else
        {
            (text, _) = VDataCompiler.BuildText(baseBytes, values, swaps, stockBytes, originalBytes);
        }

        if (!compile)
        {
            notes.Add("Only the vdata source is written; compile it (Save and compile, or the Workshop Tools) before the game sees the change.");
        }
        resets += classResets;
        context.Files.Add(new DeliveryFile(target.VDataSourcePath, AddonManifest.VDataSourceKey, System.Text.Encoding.UTF8.GetBytes(text)));

        if (!addonHasVData)
        {
            notes.Add($"{target.Name} has no weapons.vdata yet; a new one is created from the current CS2 data.");
        }
        else if (rebase)
        {
            notes.Add($"{target.Name}'s weapons.vdata is replaced by the current CS2 data plus this profile's values (backed up first).");
        }
        else if (resets > 0)
        {
            notes.Add($"{resets} vdata value(s) this profile no longer sets go back to {baselineName} values.");
        }

        return new VDataPart(values, swaps, resets, compile, original);
    }

    // ---- Removing and restoring ----

    /// <summary>
    /// Files an earlier delivery wrote that the profile no longer wants: the
    /// addon's own original is restored when there was one, else the file is
    /// removed. Returns the paths to remove; restores are added to the files.
    /// </summary>
    private static List<string> Removals(Context context, DeliveryScope scope, bool compilesVData)
    {
        var removals = new List<string>();
        if (context.Previous is null)
        {
            return removals;
        }

        var keep = context.Files.Select(file => file.ManifestKey).Concat(context.Manifest.Files.Keys).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (compilesVData)
        {
            keep.Add(AddonManifest.CompiledVDataPath);
        }

        foreach (var (key, sha) in context.Previous.Files)
        {
            if (keep.Contains(key) || (scope == DeliveryScope.VDataOnly && !AddonManifest.IsVData(key)))
            {
                continue;
            }

            var path = AddonManifest.Resolve(context.Target, key);
            if (context.Originals.IsRecorded(key) && context.AddonOwn(key) is { } own)
            {
                context.Files.Add(new DeliveryFile(path, key, own, Restores: true));
                context.Notes.Add($"{key} goes back to the addon's own version.");
                continue;
            }

            if (!File.Exists(path))
            {
                continue;
            }

            if (GameArchive.Sha256(File.ReadAllBytes(path)) != sha)
            {
                context.Notes.Add($"Repair: {key} was changed outside Weapon Lab; it is removed anyway (backed up first).");
            }

            removals.Add(path);
        }

        return removals;
    }

    // ---- Set to CS2 default ----

    private sealed record StockResetPart(IReadOnlyList<string> Weapons, IReadOnlyList<string> Forget);

    /// <summary>
    /// Weapons set to CS2 default: every addon file for them is removed, made
    /// by Weapon Lab or not (no restoring the addon's own originals), and every
    /// graph that stays is pointed back at CS2's graphs and clips for them. A
    /// skeleton stays while another addon file (a model) still uses it.
    /// </summary>
    private static StockResetPart PlanStockResets(Context context, List<string> removals)
    {
        var (connection, target) = (context.Connection, context.Target);
        var weapons = context.Profile.Weapons.Where(item => item.Value.ResetToStock).Select(item => item.Key).ToList();
        if (weapons.Count == 0)
        {
            return new StockResetPart([], []);
        }

        var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
        var skeletons = new List<(string Weapon, string Key)>();
        foreach (var weaponId in weapons)
        {
            var files = StockReset.For(connection, target, weaponId);
            removedKeys.UnionWith(files.Files);
            skeletons.AddRange(files.Skeletons.Select(key => (weaponId, key)));
            foreach (var (custom, stock) in files.Redirects)
            {
                redirects[custom] = stock;
            }

            context.Notes.Add(files.Files.Count == 0
                ? $"{weaponId} is set to CS2 default; the addon has no clips or graphs of its own for it."
                : $"{weaponId} is set to CS2 default: its {files.Files.Count} file(s) in the addon are removed, whoever made them (backed up first).");
        }

        // A skeleton goes too, unless something that stays (such as a model the vdata uses) still needs it.
        var skip = removedKeys.Concat(removals.Select(path => AddonManifest.KeyFor(target, path))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (weaponId, key) in skeletons.DistinctBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var users = Addons.AddonChecks.FilesUsing(target, key[..^2], skip);
            if (users.Count > 0)
            {
                context.Notes.Add($"{weaponId}: the skeleton {key} stays because {string.Join(", ", users)} still use{(users.Count == 1 ? "s" : "")} it.");
                continue;
            }

            removedKeys.Add(key);
        }

        // Nothing is written or restored at a removed path.
        context.Files.RemoveAll(file => removedKeys.Contains(file.ManifestKey));
        foreach (var key in removedKeys)
        {
            var path = AddonManifest.Resolve(target, key);
            if (File.Exists(path) && !removals.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                removals.Add(path);
            }
        }

        var forget = new List<string>(removedKeys);
        forget.AddRange(RedirectGraphs(context, redirects, skip.Concat(removedKeys), $"CS2's graphs for {string.Join(", ", weapons)}"));
        return new StockResetPart(weapons, forget);
    }

    /// <summary>
    /// Points the graphs that stay back at CS2's files for removed ones: the graphs this delivery writes,
    /// then the addon's own on disk. Returns the addon's own graphs it rewrote (their originals are forgotten).
    /// </summary>
    /// <param name="skip">Keys not to rewrite from disk (written or removed by this delivery).</param>
    private static List<string> RedirectGraphs(Context context, Dictionary<string, string> redirects, IEnumerable<string> skip, string what)
    {
        var forget = new List<string>();
        if (redirects.Count == 0)
        {
            return forget;
        }

        for (var index = 0; index < context.Files.Count; index++)
        {
            var file = context.Files[index];
            if (file.ManifestKey.EndsWith(".vnmgraph_c", StringComparison.OrdinalIgnoreCase) && Redirect(file.Bytes, file.ManifestKey, redirects) is { } bytes)
            {
                context.Files[index] = file with { Bytes = bytes };
                if (file.Restores)
                {
                    forget.Add(file.ManifestKey);
                }
            }
        }

        var target = context.Target;
        var written = context.Files.Select(file => file.ManifestKey).Concat(skip).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.Exists(target.GamePath) ? Directory.EnumerateFiles(target.GamePath, "*.vnmgraph_c", SearchOption.AllDirectories) : [])
        {
            var key = AddonManifest.KeyFor(target, path);
            if (written.Contains(key) || Redirect(File.ReadAllBytes(path), key, redirects) is not { } bytes)
            {
                continue;
            }

            context.Files.Add(new DeliveryFile(path, key, bytes, Restores: true));
            context.Notes.Add($"{key}: the addon's own graph is pointed back at {what}; the rest of its routing stays.");
            forget.Add(key);
        }

        return forget;
    }

    /// <summary>
    /// Clips set to CS2 default one at a time (0.22). On CS2's skeleton the addon's copy is removed and every
    /// graph that plays it goes back to CS2's clip; on a custom skeleton nothing is removed, since CS2's clip
    /// re-targeted to it is pushed as the clip (PendingWork offers it once the export is set aside). A clip
    /// exported again since is pushed as that export instead.
    /// </summary>
    private static (List<(string Weapon, string Clip)> Done, List<string> Forget) PlanStockClips(Context context, SandboxStore sandboxes, List<string> removals)
    {
        var (connection, target) = (context.Connection, context.Target);
        var done = new List<(string Weapon, string Clip)>();
        var redirects = new Dictionary<string, string>(StringComparer.Ordinal);
        var removedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (weaponId, settings) in context.Profile.Weapons.Where(item => item.Value is { StockClips.Count: > 0, ResetToStock: false, Animations: AnimationMode.Custom }))
        {
            var weapon = connection.FindWeapon(weaponId);
            var sandbox = sandboxes.Exists(weaponId) ? sandboxes.Open(weaponId) : null;
            foreach (var clipId in settings.StockClips)
            {
                var resource = weapon?.ClipPaths.FirstOrDefault(path => Blender.RigPackage.ClipId(path) == clipId);
                if (resource is null)
                {
                    continue;
                }

                done.Add((weaponId, clipId)); // Leaves the list once pushed.
                if (sandbox is not null && ClipExporter.LastExport(sandbox, clipId) is { IdenticalToTemplate: false })
                {
                    continue; // Exported again since: that export is pushed instead.
                }

                if (settings.CustomSkeleton is not null)
                {
                    context.Notes.Add($"{weaponId}/{clipId} is set to CS2 default: CS2's clip re-targeted to the custom skeleton replaces yours in the addon.");
                    continue;
                }

                var stock = resource[..^2];
                var custom = GraphTree.CustomClipPath(weaponId, stock);
                var key = custom + "_c";
                if (!File.Exists(AddonManifest.Resolve(target, key)))
                {
                    continue; // The addon plays CS2's already.
                }

                removedKeys.Add(key);
                redirects[custom] = stock;
                context.Notes.Add($"{weaponId}/{clipId} is set to CS2 default: the addon's {key} is removed and its graphs play CS2's clip again (backed up first).");
                if (context.Manifest.Clips.TryGetValue(weaponId, out var list))
                {
                    list.Remove(clipId);
                }
            }
        }

        context.Files.RemoveAll(file => removedKeys.Contains(file.ManifestKey));
        foreach (var path in removedKeys.Select(key => AddonManifest.Resolve(target, key)))
        {
            if (!removals.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                removals.Add(path);
            }
        }

        var forget = new List<string>(removedKeys);
        forget.AddRange(RedirectGraphs(context, redirects, removedKeys, "CS2's clips set to default"));
        return (done, forget);
    }

    // ---- CS2's own root graph (local play only) ----

    /// <summary>The manifest key of the copy of viewmodel_custom at CS2's own root graph path.</summary>
    public const string Cs2RootCopyKey = GraphTree.Parent + "_c";

    /// <summary>
    /// Keeps a copy of viewmodel_custom at CS2's own root graph path when asked.
    /// CS2's gameplay side (reload ammo, the shell-by-shell reload stages) runs
    /// that graph, not the player model's hudmodel graph, so a reload whose
    /// events drive gameplay only works with the copy (verified in game on PC2,
    /// 2026-10-31). Workshop players do not get it: only local play does. Once
    /// delivered, the copy follows viewmodel_custom on every push until it is
    /// switched off.
    /// </summary>
    /// <param name="wanted">True adds the copy, false removes it, null keeps what the addon has.</param>
    private static void PlanCs2RootCopy(Context context, List<string> removals, bool? wanted)
    {
        var target = context.Target;
        var path = AddonManifest.Resolve(target, Cs2RootCopyKey);
        var delivered = context.Previous?.Files.ContainsKey(Cs2RootCopyKey) == true;
        bool IsCopy(DeliveryFile file) => file.ManifestKey.Equals(Cs2RootCopyKey, StringComparison.OrdinalIgnoreCase);

        // viewmodel_custom as this delivery leaves it.
        var rootKey = GraphTree.CustomParent + "_c";
        var rootPath = AddonManifest.Resolve(target, rootKey);
        var root = context.Files.LastOrDefault(file => file.ManifestKey.Equals(rootKey, StringComparison.OrdinalIgnoreCase))?.Bytes
            ?? (File.Exists(rootPath) && !removals.Contains(rootPath, StringComparer.OrdinalIgnoreCase) ? File.ReadAllBytes(rootPath) : null);

        if ((wanted ?? delivered) && root is not null)
        {
            context.Files.RemoveAll(IsCopy);
            context.Files.Add(new DeliveryFile(path, Cs2RootCopyKey, root));
            context.Notes.Add($"{Cs2RootCopyKey} is a copy of {GraphTree.CustomParent}, so CS2's gameplay side runs the custom graphs too " +
                "(needed for shell-by-shell reloads). Local play only: Workshop players get neither this copy nor its fixes.");
            return;
        }

        if (wanted == true)
        {
            context.Notes.Add($"No copy of CS2's root graph: the addon has no {GraphTree.CustomParent} to copy.");
        }

        if (!delivered)
        {
            return;
        }

        // Switched off, or nothing left to copy: the addon's own version comes back, or CS2's is used again.
        context.Files.RemoveAll(IsCopy);
        context.Manifest.Files.Remove(Cs2RootCopyKey);
        context.Manifest.FileProfiles.Remove(Cs2RootCopyKey);
        if (context.Originals.IsRecorded(Cs2RootCopyKey) && context.AddonOwn(Cs2RootCopyKey) is { } own)
        {
            context.Files.Add(new DeliveryFile(path, Cs2RootCopyKey, own, Restores: true));
            context.Notes.Add($"{Cs2RootCopyKey} goes back to the addon's own version.");
        }
        else if (File.Exists(path) && !removals.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            removals.Add(path);
            context.Notes.Add($"{Cs2RootCopyKey} is removed: CS2's own root graph is used again.");
        }
    }

    /// <summary>The graph with its references to removed files pointed back at CS2's, or null when it has none.</summary>
    private static byte[]? Redirect(byte[] bytes, string key, IReadOnlyDictionary<string, string> redirects)
    {
        if (!redirects.Keys.Any(path => bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(path)) >= 0))
        {
            return null;
        }

        var (patched, changes) = GraphPatcher.Patch(bytes, key, redirects);
        return changes.Count > 0 ? patched : null;
    }
}
