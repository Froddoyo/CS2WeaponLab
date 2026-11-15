using System.Globalization;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Export;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.Weapons;

namespace WeaponLab.Cli;

/// <summary>
/// Headless commands for tests and automation.
/// </summary>
public static class Program
{
    private const string Usage = """
        Usage: weaponlab <command> [options]

        Commands:
          discover                  Find CS2 and summarize the build, addons and weapons.
          weapons                   List viewmodel weapons and their clip counts.
          clips <weapon>            List the clips of one weapon.
          clip <weapon> <clip>      Show a clip's timing, skeletons and events.
          poses <weapon> <clip> [--frame <n>]
                                    Print each bone's decoded pose on one frame.
          verify-encoder [<weapon>] Check the clip writer against game clips (all weapons if none given).
          sandbox create <weapon> [--addon <name>] [--from-addon true]
                                    Import a weapon's clips into a new sandbox.
          sandbox list              List sandboxes in the workspace.
          sandbox check <weapon>    Report clips that changed in the game since import.
          sandbox add-third-person <weapon>
                                    Add the weapon's third-person clips to a sandbox made before 0.26.
          blender locate            Show which Blender Weapon Lab will use.
          blender addon status|install|remove
                                    Check, install or remove the Weapon Lab Bridge in your Blender, so the
                                    panel is there when you open a .blend directly.
          blender build <weapon> [--preview game:<vmdl_c>|addon:<addon>:<vmdl_c>] [--keep true]
                                    Build the sandbox's .blend (rigs, one Action per clip, previews; third-person
                                    clips on a Body rig). --keep true carries your keys from the current .blend.
                                    An existing .blend is backed up to blender/backups first.
          blender set-model <weapon> --model <file or key> [--skeleton true|false] [--hierarchy true|false] [--keep true|false]
                                    Change the gun model (a compiled .vmdl_c from CS2 or the target addon):
                                    with --skeleton true its bones become the custom skeleton (clips and exports
                                    move onto it). Rebuilds the .blend, carrying your keys (--keep false: from
                                    the clips only). The Blender add-on's "Change gun model" runs this.
          blender update <weapon>   Add what export needs to an older .blend without touching it.
          blender open <weapon>     Open the sandbox's .blend in Blender.
          events fit <weapon> <clip> [--profile <name>] [--from <start,end,shell>] [--dry-run true]
                                    Put a shell reload's events on the game timing (the gun picked in
                                    Anim Events, else the vdata's; other events move with their part).
                                    --from says where the loop is now in a clip that has none yet.
          events length <weapon> <clip> <frames> [--profile <name>] [--dry-run true]
                                    Set a clip's length: shorter cuts the end, longer holds the last
                                    pose; events keep their frames. Export from Blender again after.
          animation export <weapon> <clip> <file> [--profile <name>]
                                    Write one clip's animation: .vnmclip_c (its export or template), .json.gz
                                    (baked poses with bone names), .blend (its keys) or .fbx (both through
                                    Blender, from the saved .blend).
          animation import <weapon> <clip> <file> [--profile <name>]
                                    Put an animation file on one clip, matched by bone name (and parent);
                                    becomes the clip's export (its events stay). .blend and .fbx through Blender.
          animation convert <sandbox folder> <clip> <file> <out.json.gz>
                                    For the Blender add-on: a .vnmclip_c or .json.gz mapped onto the clip as a
                                    pose sample (prints the bones that matched).
          export-clip <sandbox folder> <clip> <edited.json.gz> [--out <file>] [--force true] [--from <clip>]
                                    Write an edited clip from a Blender sample: into the sandbox's
                                    exports folder, or to --out instead. Used by the Blender add-on.
          profile list|show|new <name>|use <name>|rename <name> <new>|delete <name>|target <addon>|set <weapon> default|custom|reset <weapon>
                                    Manage profiles: local workspaces of Blender work and changes not pushed yet.
          deliver <addon> [--profile <name>] [--dry-run true] [--refresh-vdata true] [--confirm true] [--cs2-root-copy true|false]
                                    Make the addon match the profile: edited clips (custom paths +
                                    cloned graphs), vdata, removal of what the profile no longer wants.
                                    --cs2-root-copy true also writes viewmodel_custom over CS2's own root
                                    graph path (local play only; needed for shell-by-shell reloads), false
                                    removes that copy; later deliveries keep it in sync until then.
          deliver rollback <addon>  Undo the last delivery to that addon.
          skeleton show|create <weapon> --model game:<vmdl_c>|addon:<addon>:<vmdl_c> [--path <vnmskel>] [--model-hierarchy true] [--profile <name>]
                                    Compare a model's bones with the weapon skeleton, or make a custom
                                    skeleton from them (all clips move onto it; delivered with the weapon).
          skeleton adopt <weapon> [--profile <name>]
                                    Use the skeleton the target addon's vdata already gives the weapon.
          skeleton remove <weapon> [--profile <name>]
                                    Go back to the weapon's own skeleton.
          vdata list                List the blocks of the game's weapons.vdata.
          vdata show <block> [--addon <name>]
                                    Show a block's values, inheritance and the addon's overrides.
          dump-poses <clip file> <out.json.gz>
                                    Decode a compiled clip's poses to a pose file (for checks).

        Options:
          --game <path>             CS2 install folder, if Steam does not know it.
          --workspace <path>        Workspace folder (default %LOCALAPPDATA%\CS2WeaponLab\workspace).
        """;

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // Callers such as the Blender add-on read our output as UTF-8.
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.WriteLine(Usage);
            if (args.Length == 0 && OwnsItsConsole())
            {
                // Double-clicked in Explorer: the console would close before anyone reads it.
                Console.WriteLine("This is Weapon Lab's command-line tool. To open the app, run CS2WeaponLab.exe.");
                Console.WriteLine("Press any key to close.");
                Console.ReadKey(intercept: true);
            }

            return args.Length == 0 ? 1 : 0;
        }

        try
        {
            var options = CommandLine.Parse(args);
            return options.Positional switch
            {
                ["discover"] => Discover(options),
                ["weapons"] => Weapons(options),
                ["clips", var weapon] => Clips(options, weapon),
                ["clip", var weapon, var clip] => Clip(options, weapon, clip),
                ["poses", var weapon, var clip] => Poses(options, weapon, clip),
                ["verify-encoder"] => VerifyEncoder(options, weaponId: null),
                ["verify-encoder", var weapon] => VerifyEncoder(options, weapon),
                ["sandbox", "create", var weapon] => SandboxCreate(options, weapon),
                ["sandbox", "list"] => SandboxList(options),
                ["sandbox", "check", var weapon] => SandboxCheck(options, weapon),
                ["sandbox", "add-third-person", var weapon] => SandboxAddThirdPerson(options, weapon),
                ["blender", "locate"] => BlenderLocate(),
                ["blender", "addon", var action] => BlenderAddonCommand(action),
                ["blender", "build", var weapon] => BlenderBuild(options, weapon, referencesOnly: false),
                ["blender", "update", var weapon] => BlenderBuild(options, weapon, referencesOnly: true),
                ["blender", "set-model", var weapon] => BlenderSetModel(options, weapon),
                ["blender", "open", var weapon] => BlenderOpen(options, weapon),
                ["events", "fit", var weapon, var clip] => EventsFit(options, weapon, clip),
                ["events", "length", var weapon, var clip, var frames] => EventsLength(options, weapon, clip, frames),
                ["export-clip", var sandbox, var clip, var poses] => ExportClip(options, sandbox, clip, poses),
                ["animation", "export", var weapon, var clip, var file] => AnimationExport(options, weapon, clip, file),
                ["animation", "import", var weapon, var clip, var file] => AnimationImport(options, weapon, clip, file),
                ["animation", "convert", var sandbox, var clip, var file, var output] => AnimationConvert(options, sandbox, clip, file, output),
                ["dump-poses", var clipFile, var output] => DumpPoses(clipFile, output),
                ["profile", .. var rest] => Profile(options, rest),
                ["deliver", "rollback", var addon] => DeliverRollback(options, addon),
                ["deliver", var addon] => Deliver(options, addon),
                ["skeleton", "show", var weapon] => SkeletonCommand(options, weapon, "show"),
                ["skeleton", "create", var weapon] => SkeletonCommand(options, weapon, "create"),
                ["skeleton", "remove", var weapon] => SkeletonCommand(options, weapon, "remove"),
                ["skeleton", "adopt", var weapon] => SkeletonCommand(options, weapon, "adopt"),
                ["vdata", "list"] => VDataList(options),
                ["vdata", "show", var block] => VDataShow(options, block),
                _ => UnknownCommand(string.Join(' ', options.Positional)),
            };
        }
        catch (BlendBuildException error)
        {
            Console.Error.WriteLine(error.Log);
            Console.Error.WriteLine("Blender build failed: " + error.Message);
            return 4;
        }
        catch (Exception error) when (error is GameNotFoundException or IOException or InvalidDataException or ArgumentException or ClipWriteException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(error.Message);
            return 2;
        }
    }

    private static BlenderInstall RequireBlender()
    {
        return BlenderLocator.Locate()
            ?? throw new ArgumentException($"No Blender {BlenderLocator.Minimum} or newer found. Install Blender or set WEAPONLAB_BLENDER to blender.exe.");
    }

    private static int BlenderLocate()
    {
        var install = RequireBlender();
        Console.WriteLine($"{install} at {install.ExecutablePath}");
        return 0;
    }

    private static int BlenderAddonCommand(string action)
    {
        var blender = RequireBlender();
        var state = action switch
        {
            "install" => BlenderAddon.InstallAsync(blender).GetAwaiter().GetResult(),
            "remove" => BlenderAddon.RemoveAsync(blender).GetAwaiter().GetResult(),
            "status" => BlenderAddon.StatusAsync(blender).GetAwaiter().GetResult(),
            _ => throw new ArgumentException("Use blender addon status, install or remove."),
        };
        Console.WriteLine($"Weapon Lab Bridge {(state.Installed ? "is installed" : "is not installed")} in {blender}: {state.Path}");
        return 0;
    }

    private static int BlenderBuild(CommandLine options, string weaponId, bool referencesOnly)
    {
        var blender = RequireBlender();
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);

        var progress = new SynchronousProgress(message => Console.WriteLine("  " + message));
        var profile = LoadProfile(options, store.WorkspacePath);
        var weapon = RequireWeapon(connection, weaponId);
        var preview = options.Value("--preview") ?? profile.Weapon(weaponId).PreviewModel;
        if (preview is not null)
        {
            Console.WriteLine($"  preview model: {preview}");
        }

        // --keep true: a rebuild carries your keys from the current .blend (as the app's "Keep my animation" does).
        var keep = !referencesOnly && options.Value("--keep") == "true" && BlendBuilder.HasBlend(sandbox);
        var result = BlendBuilder.BuildAsync(sandbox, connection, blender, progress, referencesOnly: referencesOnly, previewModel: preview, propBones: profile.Weapon(weaponId).PropBones, carryKeys: keep).GetAwaiter().GetResult();
        WriteGameTiming(options, sandbox, connection, store.WorkspacePath);
        Console.WriteLine(referencesOnly
            ? $"Updated {weaponId} for export; {result.Blend} was not touched."
            : $"Built {result.Blend}");
        Console.WriteLine($"  {result.Actions} actions ({result.ThirdPersonClips} third person), {result.ArmsBones} arm bones, {result.WeaponBones} weapon bones");
        if (result.Carried is { } carried)
        {
            Console.WriteLine($"  kept your keys: {carried.Bones} bone(s) in {carried.Clips} clip(s)");
        }
        Console.WriteLine($"  previews: {(result.Previews.Count == 0 ? "none" : string.Join(", ", result.Previews))} ({result.Blender})");
        foreach (var warning in result.Warnings)
        {
            Console.WriteLine("  warning: " + warning);
        }

        return 0;
    }

    /// <summary>
    /// Change gun model (0.24, from the Blender add-on): the model becomes the weapon's viewport model and, with
    /// --skeleton true, its bones the custom skeleton (templates re-targeted, exports moved; see CustomSkeletons.Create).
    /// Then the .blend is rebuilt carrying the user's keys from the current one (backed up first), so the add-on can
    /// reload it with the new bones in place. Prints WEAPONLAB_RESULT {json} for the add-on.
    /// </summary>
    private static int BlenderSetModel(CommandLine options, string weaponId)
    {
        var blender = RequireBlender();
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);
        var progress = new SynchronousProgress(message => Console.WriteLine("  " + message));
        var profiles = new WeaponLab.Core.Profiles.ProfileStore(store.WorkspacePath);
        var profile = LoadProfile(options, store.WorkspacePath);
        var settings = profile.EnsureWeapon(weaponId);
        var model = options.Value("--model") ?? throw new ArgumentException("Give the model with --model <.vmdl_c file> or a key (game:… / addon:<addon>:…).");
        var key = PreviewModels.FromFile(connection, model);
        var before = WeaponLab.Core.Skeletons.CustomSkeletons.Load(sandbox);
        string? skeletonText = null;
        if (options.Value("--skeleton") is "true")
        {
            var hierarchy = options.Value("--hierarchy") is { } given ? given == "true" : before?.ModelHierarchy ?? false;
            var path = before is { FromAddon: false } ? before.Path : WeaponLab.Core.Skeletons.CustomSkeletons.DefaultPath(weaponId);
            var setup = WeaponLab.Core.Skeletons.CustomSkeletons.Create(sandbox, connection, key, profile.TargetAddon, path, progress, hierarchy);
            settings.CustomSkeleton = new WeaponLab.Core.Profiles.CustomSkeletonChoice(key, setup.Path, hierarchy);
            var added = setup.Bones.Except(before?.Bones ?? []).ToList();
            skeletonText = $"{setup.Path}: {setup.Bones.Count} bones" + (added.Count == 0 ? "" : $", new: {string.Join(", ", added)}");
        }

        settings.PreviewModel = key;
        profiles.Save(profile);

        var keep = options.Value("--keep") is not "false";
        var result = BlendBuilder.BuildAsync(sandbox, connection, blender, progress, previewModel: key, propBones: settings.PropBones, carryKeys: keep && BlendBuilder.HasBlend(sandbox))
            .GetAwaiter().GetResult();
        WriteGameTiming(options, sandbox, connection, store.WorkspacePath);
        var summary = new
        {
            model = key,
            skeleton = skeletonText,
            blend = result.Blend,
            carriedClips = result.Carried?.Clips ?? 0,
            carriedBones = result.Carried?.Bones ?? 0,
            notCarried = result.Carried?.NotCarried ?? [],
            warnings = result.Warnings,
        };
        Console.WriteLine("WEAPONLAB_RESULT " + System.Text.Json.JsonSerializer.Serialize(summary));
        return 0;
    }

    private static int BlenderOpen(CommandLine options, string weaponId)
    {
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        if (!BlendBuilder.HasBlend(sandbox))
        {
            throw new ArgumentException($"No .blend yet. Run 'weaponlab blender build {weaponId}' first.");
        }

        using (var connection = GameConnection.Open(options.Game))
        {
            WriteGameTiming(options, sandbox, connection, store.WorkspacePath);
        }

        BlendBuilder.Open(sandbox, RequireBlender());
        return 0;
    }

    /// <summary>"42,61,50" → loop start, loop end, shell frame; null when not given.</summary>
    private static ShellLoop? ParseLoop(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var parts = text.Split(',').Select(part => int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var frame) ? frame : -1).ToArray();
        return parts.Length == 3 && parts.All(frame => frame >= 0)
            ? new ShellLoop(parts[0], parts[1], parts[2])
            : throw new ArgumentException("--from takes the clip's current loop as start,end,shell frames, e.g. 42,61,50.");
    }

    /// <summary>The profile's target addon and its compiled vdata, when it has both.</summary>
    private static (string? Name, WeaponLab.Core.VData.VDataFile? VData) TargetVData(CommandLine options, GameConnection connection, string workspace)
    {
        var target = LoadProfile(options, workspace).TargetAddon;
        var addon = target is null ? null : connection.Addons.FirstOrDefault(item => string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
        return addon is null ? (null, null) : (addon.Name, GameConnection.ReadAddonVData(addon));
    }

    /// <summary>The game's timing marks and the clips' lengths for the Blender add-on (blender/input/game_timing.json, clip_lengths.json).</summary>
    private static void WriteGameTiming(CommandLine options, SandboxInfo sandbox, GameConnection connection, string workspace)
    {
        var (addon, vdata) = TargetVData(options, connection, workspace);
        GameTimings.Write(sandbox, connection, vdata, addon, LoadProfile(options, workspace).Weapon(sandbox.Manifest.WeaponId).TimingGun);
        ClipLengths.Write(sandbox);
    }

    /// <summary>
    /// Puts a shell reload's events on the timing of the gun the weapon plays
    /// like (the loop, the shell, every other event moved with its part), like
    /// Anim Events' "Use the …'s timing"; writes the sandbox's edits and export.
    /// </summary>
    private static int EventsFit(CommandLine options, string weaponId, string clipId)
    {
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);
        var weapon = RequireWeapon(connection, weaponId);
        var (addon, vdata) = TargetVData(options, connection, store.WorkspacePath);
        var gun = GameTimings.Resolve(connection, weapon, vdata, addon, LoadProfile(options, store.WorkspacePath).Weapon(weaponId).TimingGun)
            ?? throw new ArgumentException($"No gun's timing is known for {weaponId}: no vdata block plays its slot and none is picked in Anim Events.");
        Console.WriteLine($"Game timing: {gun.Describe()}.");
        foreach (var warning in gun.Warnings())
        {
            Console.WriteLine("  warning: " + warning);
        }

        var (sandboxClip, templateBytes, stock) = ClipTemplateOf(sandbox, weaponId, clipId);
        var current = EventEdits.Read(sandbox, clipId);
        var template = ClipLength.At(stock, current.Frames ?? stock.FrameCount); // The clip as long as the user set it.
        var timing = GameTimings.For(connection, gun.Gun, weapon, template);
        if (timing?.Loop is not { } target)
        {
            throw new ArgumentException($"{clipId}: the {gun.Gun.Name}'s clip in this role is not a shell-by-shell reload, so there is no loop to fit to.");
        }

        var from = ShellReload.Read(template, current) ?? ParseLoop(options.Value("--from"));
        var fitted = ShellReload.Fit(template, current, from, target, timing.OutroEnd, clipId.Contains("silent", StringComparison.OrdinalIgnoreCase));
        ClipWriter.Write(templateBytes, sandboxClip.ResourcePath, new ClipEdit { Events = fitted.Changes, EventList = fitted.List, FrameCount = fitted.Frames });
        Console.WriteLine($"{clipId}: loop {from?.LoopStart.ToString(CultureInfo.InvariantCulture) ?? "none"}-{from?.LoopEnd.ToString(CultureInfo.InvariantCulture) ?? "none"} (shell {from?.AddAmmo.ToString(CultureInfo.InvariantCulture) ?? "-"}) -> "
            + $"{target.LoopStart}-{target.LoopEnd} (shell {target.AddAmmo}), outro ends at {timing.OutroEnd:0} (the {timing.Gun}'s {timing.Reference.Name}); other events moved with their part.");
        if (options.Value("--dry-run") is "true")
        {
            Console.WriteLine("Dry run: nothing written.");
            return 0;
        }

        EventEdits.Save(sandbox, clipId, fitted);
        WriteGameTiming(options, sandbox, connection, store.WorkspacePath); // So Blender shows the same frames.
        var report = EventEdits.Apply(sandbox, clipId);
        Console.WriteLine(report is null
            ? $"Saved {EventEdits.PathFor(sandbox, clipId)}; the clip has no export from Blender yet, so the events go in with its next export."
            : $"Saved the events and rewrote the sandbox export ({report.SandboxOutput}). Push it from the Deliver tab.");
        return 0;
    }

    /// <summary>
    /// Sets a clip's length in frames, like Anim Events' Length: shorter cuts
    /// the end, longer holds the last pose; events keep their frames. Writes
    /// the sandbox's edits, Blender's clip lengths and the export.
    /// </summary>
    private static int EventsLength(CommandLine options, string weaponId, string clipId, string framesText)
    {
        if (!int.TryParse(framesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
        {
            throw new ArgumentException("The length is a whole number of frames, e.g. 50.");
        }

        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        var (sandboxClip, templateBytes, stock) = ClipTemplateOf(sandbox, weaponId, clipId);
        var current = EventEdits.Read(sandbox, clipId);
        var next = ClipLength.SetFrames(stock, current, frames);
        ClipWriter.Write(templateBytes, sandboxClip.ResourcePath, new ClipEdit { Events = next.Changes, EventList = next.List, FrameCount = next.Frames });
        var fps = stock.FramesPerSecond > 0 ? stock.FramesPerSecond : 30;
        Console.WriteLine($"{clipId}: {current.Frames ?? stock.FrameCount} -> {frames} frames ({(frames - 1) / fps:0.00} s; CS2's is {stock.FrameCount}). "
            + (frames < (current.Frames ?? stock.FrameCount) ? "The end is cut" : "The last pose holds") + "; events keep their frames.");
        if (options.Value("--dry-run") is "true")
        {
            Console.WriteLine("Dry run: nothing written.");
            return 0;
        }

        EventEdits.Save(sandbox, clipId, next);
        ClipLengths.Write(sandbox);
        var report = EventEdits.Apply(sandbox, clipId);
        Console.WriteLine(report is null
            ? $"Saved {EventEdits.PathFor(sandbox, clipId)}; it applies with the clip's next export."
            : $"Saved and rewrote the sandbox export ({report.SandboxOutput}). Export from Blender again for poses at the new length, then push.");
        return 0;
    }

    /// <summary>A sandbox clip, the template it exports onto (re-targeted on a custom skeleton) and that template read.</summary>
    private static (SandboxClip Clip, byte[] Bytes, ClipInfo Info) ClipTemplateOf(SandboxInfo sandbox, string weaponId, string clipId)
    {
        var sandboxClip = sandbox.Manifest.Clips.FirstOrDefault(clip => RigPackage.ClipId(clip.ResourcePath) == clipId)
            ?? throw new ArgumentException($"{weaponId} has no clip {clipId}.");
        var (templatePath, _) = WeaponLab.Core.Skeletons.CustomSkeletons.Template(sandbox, sandboxClip, WeaponLab.Core.Skeletons.CustomSkeletons.Load(sandbox));
        var bytes = File.ReadAllBytes(templatePath);
        return (sandboxClip, bytes, ClipReader.Read(bytes, sandboxClip.ResourcePath));
    }

    /// <summary>
    /// Prints a human summary, then one <c>WEAPONLAB_EXPORT {json}</c> line
    /// for the Blender add-on to parse.
    /// </summary>
    /// <summary>The profile's target addon, when the connection has it.</summary>
    private static WeaponLab.Core.Delivery.AddonTarget? ProfileTarget(CommandLine options, GameConnection connection, string workspace)
    {
        var name = LoadProfile(options, workspace).TargetAddon;
        var addon = name is null ? null : connection.Addons.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        return addon is null ? null : WeaponLab.Core.Delivery.AddonTarget.For(connection.Install, addon);
    }

    /// <summary>Blender when it is found; only keys (.blend) and FBX need it.</summary>
    private static BlenderInstall? FindBlender(string file) =>
        AnimationTransfer.FormatOf(file) is "keys" or "fbx" ? RequireBlender() : null;

    private static int AnimationExport(CommandLine options, string weaponId, string clipId, string file)
    {
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);
        var message = AnimationTransfer.ExportAsync(sandbox, connection, ProfileTarget(options, connection, store.WorkspacePath), FindBlender(file), clipId, file,
            new SynchronousProgress(line => Console.WriteLine("  " + line))).GetAwaiter().GetResult();
        Console.WriteLine(message + " " + Path.GetFullPath(file));
        return 0;
    }

    private static int AnimationImport(CommandLine options, string weaponId, string clipId, string file)
    {
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);
        var result = AnimationTransfer.ImportAsync(sandbox, connection, ProfileTarget(options, connection, store.WorkspacePath), FindBlender(file), clipId, file,
            new SynchronousProgress(line => Console.WriteLine("  " + line))).GetAwaiter().GetResult();
        Console.WriteLine($"{clipId}: {result.Taken.Count} bone(s) took {Path.GetFileName(file)}'s motion over {result.Frames} frames; kept: {(result.Skipped.Count == 0 ? "none" : string.Join(", ", result.Skipped.Take(12)))}.");
        Console.WriteLine($"Wrote {result.Export.SandboxOutput} (its events stay). In Blender: Load Weapon Lab's version of {clipId}.");
        return 0;
    }

    /// <summary>For the Blender add-on's import: a .vnmclip_c or .json.gz as a pose sample on the clip (matched bones printed).</summary>
    private static int AnimationConvert(CommandLine options, string sandboxFolder, string clipId, string file, string output)
    {
        var sandbox = SandboxStore.OpenFolder(sandboxFolder);
        using var connection = GameConnection.Open(options.Game);
        var target = options.Value("--profile") is not null ? ProfileTarget(options, connection, options.Workspace ?? SandboxStore.DefaultWorkspacePath()) : null;
        var source = AnimationFiles.Read(file, AnimationFiles.Skeletons(connection, sandbox, target), AnimationFiles.WeaponSkeletonFor(sandbox, clipId));
        var match = AnimationFiles.ToSample(sandbox, clipId, source, output);
        Console.WriteLine("WEAPONLAB_RESULT " + System.Text.Json.JsonSerializer.Serialize(new { frames = match.Frames, matched = match.Matched, kept = match.Kept }));
        return 0;
    }

    private static int ExportClip(CommandLine options, string sandboxFolder, string clipId, string posesPath)
    {
        var sandbox = SandboxStore.OpenFolder(sandboxFolder);
        var outPath = options.Value("--out");
        ClipExportReport report;
        try
        {
            report = ClipExporter.Export(sandbox, clipId, posesPath, toSandbox: outPath is null, extraOutputPath: outPath, keepEditedExport: options.Value("--force") is not "true",
                sourceClip: options.Value("--from"));
        }
        catch (ExportGuardException guard)
        {
            Console.WriteLine("WEAPONLAB_GUARD " + guard.Message);
            Console.Error.WriteLine(guard.Message);
            return 6;
        }


        if (report.IdenticalToTemplate)
        {
            Console.WriteLine($"{clipId}: no edits found; the output is the original clip, byte for byte.");
        }
        else
        {
            foreach (var (stream, bones) in report.ChangedBones)
            {
                Console.WriteLine($"{clipId}: {stream} edited: {string.Join(", ", bones)}");
            }

            foreach (var change in report.ChangedEvents)
            {
                Console.WriteLine($"{clipId}: event {change}");
            }

            Console.WriteLine($"Encoding error: {report.MaxTranslationError:0.######} units, {report.MaxRotationErrorDegrees:0.####}°, scale {report.MaxScaleError:0.######}.");
        }

        Console.WriteLine($"Wrote {report.SandboxOutput ?? report.ExtraOutput}");
        Console.WriteLine("WEAPONLAB_EXPORT " + System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        return 0;
    }

    private static int Deliver(CommandLine options, string addonName)
    {
        using var connection = GameConnection.Open(options.Game);
        var addon = connection.Addons.FirstOrDefault(item => item.Name.Equals(addonName, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"No addon named '{addonName}' in {connection.Install.GameAddonsPath}.");
        var store = ProfileSandboxes(options);
        var profile = LoadProfile(options, store.WorkspacePath);
        var rootCopy = options.Value("--cs2-root-copy") switch
        {
            null => (bool?)null,
            "true" => true,
            "false" => false,
            var other => throw new ArgumentException($"--cs2-root-copy takes true or false, not '{other}'."),
        };
        var plan = WeaponLab.Core.Delivery.DeliveryPlanner.Plan(
            store, connection, WeaponLab.Core.Delivery.AddonTarget.For(connection.Install, addon), profile,
            rebaseVData: options.Value("--refresh-vdata") == "true", cs2RootCopy: rootCopy);

        Console.WriteLine($"Profile '{profile.Name}' → {addon.Name}: {plan.Clips.Count} clip(s), {plan.Graphs.Count} graph(s), {plan.VDataEdits} vdata edit(s), {plan.VDataResets} vdata reset(s)");
        foreach (var clip in plan.Clips)
        {
            Console.WriteLine($"  clip   {clip.CustomPath}  ({clip.Changes})");
        }

        foreach (var graph in plan.Graphs)
        {
            Console.WriteLine($"  graph  {graph}");
        }

        foreach (var file in plan.Replaced)
        {
            Console.WriteLine($"  replaces {file}");
        }

        foreach (var file in plan.Removals)
        {
            Console.WriteLine($"  removes  {file}");
        }

        foreach (var note in plan.Notes)
        {
            Console.WriteLine($"  note: {note}");
        }

        foreach (var warning in plan.Warnings)
        {
            Console.WriteLine($"  warning: {warning.Subject}: {warning.Message}");
        }

        if (plan.IsEmpty)
        {
            Console.WriteLine("Nothing pending: the addon already has all of this profile's work.");
            return 0;
        }

        if (options.Value("--dry-run") == "true")
        {
            return 0;
        }

        if ((plan.Overwrites.Count > 0 || plan.BrokenReferences.Count > 0) && options.Value("--confirm") != "true")
        {
            Console.WriteLine("Not applied: see the warnings above. Run again with --confirm true to apply anyway (everything touched is backed up).");
            return 4;
        }

        var progress = new SynchronousProgress(message => Console.WriteLine("  " + message));
        try
        {
            var result = WeaponLab.Core.Delivery.AddonDelivery.DeliverAsync(plan, connection.Install, store.WorkspacePath, progress).GetAwaiter().GetResult();
            Console.WriteLine($"Delivered: {result.FilesWritten} file(s) written, {result.FilesRemoved} removed. Backup: {result.BackupFolder}");
            plan.RecordIn(profile); // Pushed work leaves the profile's pending list.
            new WeaponLab.Core.Profiles.ProfileStore(store.WorkspacePath).Save(profile);
            // A copy of what the addon now has, so the app's Revert to addon default can put it back later.
            WeaponLab.Core.Delivery.PendingWork.KeepLive(profile, store, plan.Target);
            return 0;
        }
        catch (WeaponLab.Core.Delivery.DeliveryException error)
        {
            if (error.Log is { Length: > 0 } log)
            {
                Console.Error.WriteLine(log);
            }

            Console.Error.WriteLine(error.Message);
            return 5;
        }
    }

    private static int SkeletonCommand(CommandLine options, string weaponId, string action)
    {
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);
        var profiles = new WeaponLab.Core.Profiles.ProfileStore(store.WorkspacePath);
        var profile = LoadProfile(options, store.WorkspacePath);
        if (action == "remove")
        {
            var removed = WeaponLab.Core.Skeletons.CustomSkeletons.Remove(sandbox, connection);
            profile.EnsureWeapon(weaponId).CustomSkeleton = null;
            profiles.Save(profile);
            Console.WriteLine(removed ? $"{weaponId} is back on its own skeleton. Build the .blend again." : $"{weaponId} had no custom skeleton.");
            return 0;
        }

        if (action == "adopt")
        {
            var weapon = connection.FindWeapon(weaponId) ?? throw new ArgumentException($"No weapon named {weaponId}.");
            var found = WeaponLab.Core.Skeletons.CustomSkeletons.FindInAddon(connection, profile.TargetAddon, weapon.Id)
                ?? throw new ArgumentException($"{profile.TargetAddon ?? "The target addon"} gives {weaponId} no skeleton of its own.");
            var adopted = WeaponLab.Core.Skeletons.CustomSkeletons.Adopt(sandbox, connection, profile.TargetAddon!, found.Skeleton, found.Model);
            profile.EnsureWeapon(weaponId).CustomSkeleton = new WeaponLab.Core.Profiles.CustomSkeletonChoice(adopted.SourceModel, adopted.Path);
            profiles.Save(profile);
            Console.WriteLine($"{weaponId} now uses the addon's skeleton {adopted.Path} ({adopted.Bones.Count} bones, {adopted.ExtraBones.Count} beyond CS2's) {(adopted.ModelCopy.Length > 0 ? " and model " + adopted.ModelCopy : "")}; {adopted.Templates.Count} clips re-targeted. Build the .blend again.");
            return 0;
        }

        var model = options.Value("--model") ?? throw new ArgumentException("Give the model with --model game:<vmdl_c> or addon:<addon>:<vmdl_c>.");
        var preview = WeaponLab.Core.Skeletons.CustomSkeletons.Preview(sandbox, connection, model, profile.TargetAddon);
        Console.WriteLine($"{preview.SharedBones.Count} bone(s) shared with {preview.FromSkeleton}");
        Console.WriteLine($"new bones: {(preview.ExtraBones.Count == 0 ? "none" : string.Join(", ", preview.ExtraBones))}");
        if (preview.SkeletonOnlyBones.Count > 0)
        {
            Console.WriteLine($"kept from the weapon skeleton (no mesh in this model): {string.Join(", ", preview.SkeletonOnlyBones)}");
        }

        if (preview.Corrected.Count > 0)
        {
            Console.WriteLine($"placed relative to the weapon body (parent rests differently in the model): {string.Join(", ", preview.Corrected)}");
        }

        if (preview.Reparented.Count > 0)
        {
            Console.WriteLine($"hung under another parent than CS2 (kept as CS2 unless --model-hierarchy true): {string.Join("; ", preview.Reparented)}");
        }

        if (preview.SkippedBones.Count > 0)
        {
            Console.WriteLine($"left out (Blender FBX leaf bones): {string.Join(", ", preview.SkippedBones)}");
        }

        Console.WriteLine($"model targets: {(preview.ModelSkeletonRefs.Count == 0 ? "no skeleton" : string.Join(", ", preview.ModelSkeletonRefs))}");
        if (action == "show")
        {
            return 0;
        }

        var path = options.Value("--path") ?? WeaponLab.Core.Skeletons.CustomSkeletons.DefaultPath(weaponId);
        var hierarchy = options.Value("--model-hierarchy") == "true";
        var setup = WeaponLab.Core.Skeletons.CustomSkeletons.Create(sandbox, connection, model, profile.TargetAddon, path, new Progress<string>(Console.WriteLine), hierarchy);
        profile.EnsureWeapon(weaponId).CustomSkeleton = new WeaponLab.Core.Profiles.CustomSkeletonChoice(model, setup.Path, hierarchy);
        if (setup.UnweightedBones is { Count: > 0 } empty)
        {
            Console.WriteLine($"no mesh is weighted to: {string.Join(", ", empty)} (often bones from animation files the .vmdl lists)");
        }

        profiles.Save(profile);
        Console.WriteLine($"Made {setup.Path}: {setup.Bones.Count} bones, {setup.ExtraBones.Count} new, {setup.Templates.Count} clips re-targeted; model copy {setup.ModelCopy}. Profile '{profile.Name}' updated. Build the .blend again.");
        return 0;
    }

    private static WeaponLab.Core.Profiles.Profile LoadProfile(CommandLine options, string workspace)
    {
        var profiles = new WeaponLab.Core.Profiles.ProfileStore(workspace);
        var profile = options.Value("--profile") is { } name ? profiles.Load(name) : profiles.LoadActive();
        // As the app does on load (0.21): weapons that showed their skeleton's model by default keep it as their pick.
        WeaponLab.Core.Profiles.ViewportModels.KeepOldDefaults(profile, profiles.SandboxesFor(profile.Name));
        return profile;
    }

    /// <summary>The sandboxes of the profile named by --profile, else the active one (each profile keeps its own).</summary>
    private static SandboxStore ProfileSandboxes(CommandLine options)
    {
        var profiles = new WeaponLab.Core.Profiles.ProfileStore(options.Workspace ?? SandboxStore.DefaultWorkspacePath());
        return profiles.SandboxesFor(LoadProfile(options, profiles.WorkspacePath).Name);
    }

    private static int Profile(CommandLine options, string[] arguments)
    {
        var workspace = options.Workspace ?? SandboxStore.DefaultWorkspacePath();
        var profiles = new WeaponLab.Core.Profiles.ProfileStore(workspace);
        var active = LoadProfile(options, workspace);
        switch (arguments)
        {
            case ["list"]:
                foreach (var name in profiles.Names())
                {
                    Console.WriteLine((name == active.Name ? "* " : "  ") + name);
                }

                return 0;
            case ["show"]:
                Console.Write(WeaponLab.Core.Profiles.ProfileStore.ToJson(active));
                return 0;
            case ["new", var name]:
                profiles.Create(name, active.TargetAddon);
                Console.WriteLine($"Created and switched to profile '{name}' ({profiles.FolderFor(name)}).");
                return 0;
            case ["use", var name]:
                profiles.Load(name);
                profiles.SetActive(name);
                Console.WriteLine($"Switched to profile '{name}'.");
                return 0;
            case ["rename", var name, var newName]:
                profiles.Rename(name, newName);
                Console.WriteLine($"Renamed profile '{name}' to '{newName}'.");
                return 0;
            case ["delete", var name]:
                Console.WriteLine($"Profile '{name}' moved to {profiles.Delete(name)}.");
                return 0;
            case ["target", var addon]:
                // The addon the profile pushes to by default; the profile itself is not tied to it.
                active.TargetAddon = addon;
                profiles.Save(active);
                Console.WriteLine($"'{active.Name}' targets {addon}.");
                return 0;
            case ["set", var weapon, var mode]:
                active.EnsureWeapon(weapon).Animations = Enum.Parse<WeaponLab.Core.Profiles.AnimationMode>(mode, ignoreCase: true);
                profiles.Save(active);
                Console.WriteLine($"'{active.Name}': {weapon} animations = {mode}.");
                return 0;
            case ["reset", var weapon]:
                // Set to CS2 default: the next deliver deletes every file the addon has for the weapon.
                active.EnsureWeapon(weapon).ResetToStock = true;
                profiles.Save(active);
                Console.WriteLine($"'{active.Name}': {weapon} set to CS2 default; the next deliver removes its files from the addon.");
                return 0;
            default:
                return UnknownCommand("profile " + string.Join(' ', arguments));
        }
    }

    private static int DeliverRollback(CommandLine options, string addonName)
    {
        var workspace = options.Workspace ?? SandboxStore.DefaultWorkspacePath();
        var last = WeaponLab.Core.Delivery.AddonDelivery.LastDelivery(workspace, addonName)
            ?? throw new ArgumentException($"No delivery to {addonName} to roll back.");
        WeaponLab.Core.Delivery.AddonDelivery.Rollback(last);
        Console.WriteLine($"Rolled back the delivery from {Path.GetFileName(last)}; {addonName} is back to how it was before.");
        return 0;
    }

    private static int VDataList(CommandLine options)
    {
        using var connection = GameConnection.Open(options.Game);
        var vdata = connection.ReadVData();
        foreach (var block in vdata.Blocks)
        {
            var skeleton = block.Value(WeaponLab.Core.VData.VDataBlock.SkeletonKey);
            Console.WriteLine($"{block.Name,-32} base {block.Base ?? "-",-16} {block.Properties.Count,4} values  {skeleton ?? ""}");
        }

        return 0;
    }

    private static int VDataShow(CommandLine options, string blockName)
    {
        using var connection = GameConnection.Open(options.Game);
        var vdata = connection.ReadVData();
        var block = vdata[blockName] ?? throw new ArgumentException($"No vdata block named '{blockName}'. Run 'weaponlab vdata list'.");
        var addonName = options.Value("--addon");
        var addon = addonName is null ? null
            : GameConnection.ReadAddonVData(connection.Addons.FirstOrDefault(item => item.Name == addonName) ?? throw new ArgumentException($"No addon named '{addonName}'."));

        Console.WriteLine($"{block.Name}: class {block.Class ?? "-"}, base chain {string.Join(" -> ", vdata.BaseChain(block).Select(item => item.Name).Prepend(block.Name))}");
        foreach (var row in WeaponLab.Core.VData.VDataView.Rows(vdata, block, addon, overrides: null))
        {
            var source = row.Source == WeaponLab.Core.VData.VDataSource.Inherited ? $"from {row.InheritedFrom}" : "set here";
            var addonNote = row.AddonDiffers ? $"   addon: {row.AddonValue}" : "";
            Console.WriteLine($"  {row.Path,-48} {row.StockValue,-28} {source,-18}{addonNote}");
        }

        return 0;
    }

    private static int DumpPoses(string clipFile, string output)
    {
        using var template = ClipTemplate.Open(File.ReadAllBytes(clipFile), Path.GetFileName(clipFile));
        PoseFile.Write(output, template.Streams.ToDictionary(
            stream => stream.IsPrimary ? "primary" : $"secondary{stream.SecondaryIndex}",
            stream => (stream.Skeleton, stream.ReadPoses())));
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    /// <summary>Reports on the calling thread, so console lines keep their order.</summary>
    private sealed class SynchronousProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    private static int Discover(CommandLine options)
    {
        using var connection = GameConnection.Open(options.Game);
        var install = connection.Install;

        Console.WriteLine($"Game:           {install.RootPath}");
        Console.WriteLine($"Build:          {connection.Version?.ToString() ?? "unknown (no steam.inf)"}");
        Console.WriteLine($"Workshop Tools: {(install.HasWorkshopTools ? "found" : "not found")}");
        Console.WriteLine($"Core VPK:       {(File.Exists(install.CoreVpkPath) ? "found" : "missing")}");
        Console.WriteLine($"Addons ({connection.Addons.Count}):");
        foreach (var addon in connection.Addons)
        {
            var notes = new List<string>();
            if (addon.ContentPath is null)
            {
                notes.Add("no content folder");
            }

            if (addon.HasWeaponsVData)
            {
                notes.Add("has weapons.vdata_c");
            }

            Console.WriteLine($"  {addon.Name}{(notes.Count > 0 ? "  (" + string.Join(", ", notes) + ")" : "")}");
        }

        var clipCount = connection.Weapons.Sum(weapon => weapon.ClipPaths.Count);
        Console.WriteLine($"Weapons:        {connection.Weapons.Count} folders, {clipCount} clips");
        return 0;
    }

    private static int Weapons(CommandLine options)
    {
        using var connection = GameConnection.Open(options.Game);
        foreach (var weapon in connection.Weapons)
        {
            Console.WriteLine($"{weapon.Category,-24} {weapon.Id,-28} {weapon.ClipPaths.Count,4} clips");
        }

        return 0;
    }

    private static int Clips(CommandLine options, string weaponId)
    {
        using var connection = GameConnection.Open(options.Game);
        var weapon = RequireWeapon(connection, weaponId);
        foreach (var clip in weapon.ClipPaths)
        {
            Console.WriteLine(clip);
        }

        return 0;
    }

    private static int Clip(CommandLine options, string weaponId, string clipName)
    {
        using var connection = GameConnection.Open(options.Game);
        var path = RequireClip(RequireWeapon(connection, weaponId), clipName);

        var clip = connection.ReadClip(path);
        Console.WriteLine($"Clip:       {clip.ResourcePath}");
        Console.WriteLine($"Frames:     {clip.FrameCount} ({clip.DurationSeconds:0.####} s, {clip.FramesPerSecond:0.##} fps)");
        Console.WriteLine($"Additive:   {(clip.IsAdditive ? "yes" : "no")}");
        Console.WriteLine($"Skeleton:   {clip.Skeleton} ({clip.TrackCount} tracks)");
        foreach (var secondary in clip.SecondarySkeletons)
        {
            Console.WriteLine($"Secondary:  {secondary}");
        }

        Console.WriteLine($"Editable:   {(clip.IsEditable ? "yes" : "no (not a paired viewmodel hand rig)")}");
        Console.WriteLine($"SHA-256:    {clip.Sha256}");
        Console.WriteLine($"Events ({clip.Events.Count}):");
        foreach (var clipEvent in clip.Events)
        {
            var secondary = clipEvent.SecondaryId.Length > 0 ? $" [{clipEvent.SecondaryId}]" : "";
            Console.WriteLine($"  #{clipEvent.Index,-3} frame {clipEvent.StartFrame(clip),6:0.##}  {clipEvent.StartSeconds(clip),7:0.000}s  {clipEvent.Kind,-8} {clipEvent.Name}{secondary}");
        }

        return 0;
    }

    private static int Poses(CommandLine options, string weaponId, string clipName)
    {
        using var connection = GameConnection.Open(options.Game);
        var path = RequireClip(RequireWeapon(connection, weaponId), clipName);
        using var template = connection.OpenClipTemplate(path);

        var frameText = options.Value("--frame") ?? "0";
        if (!int.TryParse(frameText, out var frame) || frame < 0 || frame >= template.Primary.FrameCount)
        {
            throw new ArgumentException($"--frame must be between 0 and {template.Primary.FrameCount - 1}.");
        }

        foreach (var stream in template.Streams)
        {
            var skeleton = connection.ReadSkeleton(stream.Skeleton);
            var channels = stream.ReadChannels();
            var poses = stream.ReadPoses()[frame];
            Console.WriteLine($"{stream.Label}: {stream.Skeleton}, frame {frame} of {stream.FrameCount}");
            for (var track = 0; track < poses.Length; track++)
            {
                var pose = poses[track];
                var t = pose.Translation;
                var q = pose.Rotation;
                Console.WriteLine(
                    $"  {track,3} {skeleton.BoneName(track),-28} t=({t.X,9:0.###} {t.Y,9:0.###} {t.Z,9:0.###})  " +
                    $"q=({q.X,7:0.####} {q.Y,7:0.####} {q.Z,7:0.####} {q.W,7:0.####})  s={pose.Scale:0.###}  {channels[track]}");
            }
        }

        return 0;
    }

    private static int VerifyEncoder(CommandLine options, string? weaponId)
    {
        using var connection = GameConnection.Open(options.Game);
        var weapons = weaponId is null ? connection.Weapons : [RequireWeapon(connection, weaponId)];

        int passed = 0, failed = 0;
        double worstTranslation = 0, worstRotation = 0, worstScale = 0;
        foreach (var path in weapons.SelectMany(weapon => weapon.ClipPaths))
        {
            var result = EncoderSelfTest.Run(connection.Archive.ReadBytes(path), path);
            if (!result.Passed)
            {
                failed++;
                Console.WriteLine($"FAIL [{result.Check}] {path}: {result.Message}");
                continue;
            }

            passed++;
            worstTranslation = Math.Max(worstTranslation, result.MaxTranslationError);
            worstRotation = Math.Max(worstRotation, result.MaxRotationErrorDegrees);
            worstScale = Math.Max(worstScale, result.MaxScaleError);
        }

        Console.WriteLine($"{passed} clips passed, {failed} failed.");
        Console.WriteLine($"Worst full re-encode error: translation {worstTranslation:0.######}, rotation {worstRotation:0.######}°, scale {worstScale:0.######}.");
        return failed == 0 ? 0 : 3;
    }

    private static int SandboxCreate(CommandLine options, string weaponId)
    {
        using var connection = GameConnection.Open(options.Game);
        var weapon = RequireWeapon(connection, weaponId);
        var store = ProfileSandboxes(options);

        // --from-addon true: start from the target addon's own clips where it has them.
        var addon = options.Value("--addon");
        var source = options.Value("--from-addon") == "true" && addon is not null
            ? WeaponLab.Core.Delivery.AddonTarget.For(connection.Install, connection.Addons.First(item => item.Name == addon))
            : null;
        var sandbox = store.Create(connection, weapon, addon, sourceAddon: source);
        var editable = sandbox.Manifest.Clips.Count(clip => clip.Editable);
        var fromAddon = sandbox.Manifest.Clips.Count(clip => clip.AddonClip is not null);
        Console.WriteLine($"Created sandbox for {weapon.Id}: {sandbox.FolderPath}");
        Console.WriteLine($"  {sandbox.Manifest.Clips.Count} clips ({editable} editable), target addon: {sandbox.Manifest.TargetAddon ?? "not chosen"}");
        Console.WriteLine($"  {WorldSummary(sandbox.Manifest)}");
        if (fromAddon > 0)
        {
            Console.WriteLine($"  {fromAddon} clip(s) taken from {sandbox.Manifest.SourceAddon}");
        }
        return 0;
    }

    private static string WorldSummary(SandboxManifest manifest) => manifest.WorldClips.Count == 0
        ? "no third-person clips"
        : $"{manifest.WorldClips.Count} third-person clips ({manifest.WorldClips.Count(clip => clip.Editable)} editable; idle and shoot add onto other poses and stay read-only)";

    /// <summary>Adds the weapon's third-person clips to a sandbox made before 0.26 (0.26).</summary>
    private static int SandboxAddThirdPerson(CommandLine options, string weaponId)
    {
        using var connection = GameConnection.Open(options.Game);
        var weapon = RequireWeapon(connection, weaponId);
        var store = ProfileSandboxes(options);
        var before = store.Open(weaponId);
        if (weapon.WorldClipPaths.Count == 0)
        {
            Console.WriteLine($"CS2 has no third-person clips in animation/anims/world/{weapon.Category}/{weapon.Id}/.");
            return 0;
        }

        if (before.Manifest.WorldClips.Count > 0)
        {
            Console.WriteLine($"{weaponId} already has its {WorldSummary(before.Manifest)}.");
            return 0;
        }

        var sandbox = SandboxStore.AddWorldClips(before, connection, weapon);
        Console.WriteLine($"Added {WorldSummary(sandbox.Manifest)} to {weaponId}.");
        Console.WriteLine("Build the .blend again with your keys kept to edit them in Blender: weaponlab blender build " + weaponId + " --keep true");
        return 0;
    }

    private static int SandboxList(CommandLine options)
    {
        var store = ProfileSandboxes(options);
        var sandboxes = store.List();
        Console.WriteLine($"Workspace: {store.WorkspacePath}");
        if (sandboxes.Count == 0)
        {
            Console.WriteLine("No sandboxes yet.");
        }

        foreach (var sandbox in sandboxes)
        {
            var manifest = sandbox.Manifest;
            Console.WriteLine($"  {manifest.WeaponId,-28} {manifest.Clips.Count,4} clips  game {manifest.Game.PatchVersion}  addon {manifest.TargetAddon ?? "-"}");
        }

        return 0;
    }

    private static int SandboxCheck(CommandLine options, string weaponId)
    {
        var store = ProfileSandboxes(options);
        var sandbox = store.Open(weaponId);
        using var connection = GameConnection.Open(options.Game);

        var changed = SandboxStore.ChangedSinceImport(sandbox, connection.Archive);
        if (changed.Count == 0)
        {
            Console.WriteLine($"All {sandbox.Manifest.Clips.Count} template clips match the current game.");
            return 0;
        }

        Console.WriteLine($"{changed.Count} clips changed or were removed since import:");
        foreach (var path in changed)
        {
            Console.WriteLine($"  {path}");
        }

        return 3;
    }

    private static WeaponLab.Core.Weapons.WeaponEntry RequireWeapon(GameConnection connection, string weaponId)
    {
        return connection.FindWeapon(weaponId)
            ?? throw new ArgumentException($"No weapon folder named '{weaponId}'. Run 'weaponlab weapons' to list them.");
    }

    private static string RequireClip(WeaponLab.Core.Weapons.WeaponEntry weapon, string clipName)
    {
        return weapon.ClipPaths.FirstOrDefault(candidate =>
                candidate.EndsWith("/" + clipName, StringComparison.OrdinalIgnoreCase)
                || candidate.EndsWith("/" + clipName + WeaponLab.Core.Weapons.WeaponCatalog.ClipExtension, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"'{weapon.Id}' has no clip named '{clipName}'. Run 'weaponlab clips {weapon.Id}' to list them.");
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"Unknown command or missing argument: {command}");
        Console.Error.WriteLine(Usage);
        return 1;
    }

    /// <summary>True when no shell started us (Explorer opened a console just for this process).</summary>
    private static bool OwnsItsConsole()
    {
        if (!OperatingSystem.IsWindows() || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return false;
        }

        var processes = new uint[2];
        return GetConsoleProcessList(processes, (uint)processes.Length) == 1;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);
}
