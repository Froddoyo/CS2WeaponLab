using System.Text.Json;
using System.Text.Json.Serialization;
using WeaponLab.Core.Blender;
using WeaponLab.Core.Clips;
using WeaponLab.Core.Game;
using WeaponLab.Core.Sandbox;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Weapons;

/// <summary>A moment of the game's timing, in frames of the weapon's own clip.</summary>
public sealed record TimingMark(double Frame, string Label);

/// <summary>A stretch of the game's timing the clip cannot change (the intro, the shell loop…), in frames of the weapon's own clip.</summary>
/// <param name="Kind">intro, loop, outro, before or after (the refill), done (the game finished the reload).</param>
public sealed record TimingBand(double Start, double End, string Label, string Kind);

/// <summary>
/// The game's timing for one of a weapon's clips: the clip in the same role of
/// the gun whose timing the game runs (reload_ssg08 → reload_nova), whose events
/// decide when the game counts ammo even though the viewmodel plays the weapon's own.
/// </summary>
/// <param name="Loop">The reference's shell loop at the weapon clip's frame rate, for shell-by-shell reloads.</param>
/// <param name="OutroEnd">Where the reference's outro ends, at the weapon clip's frame rate.</param>
/// <param name="Refill">The frame the game refills a magazine at (the reference's WPN_RELOAD_ADD_AMMO), for magazine reloads.</param>
/// <param name="Bands">The reference's parts on the weapon clip's frames, for the timeline bands.</param>
public sealed record GameTiming(string Gun, ClipInfo Reference, ShellLoop? Loop, double OutroEnd, double? Refill, IReadOnlyList<TimingMark> Marks)
{
    public IReadOnlyList<TimingBand> Bands { get; init; } = [];
}

/// <summary>
/// The gun whose timing the game runs a weapon with (0.24, "expected class"): picked by the user for display,
/// or what the vdata makes CS2 run (<see cref="PlaysLike"/>, which reads CS2's root graph).
/// </summary>
/// <param name="Chosen">Picked in Weapon Lab (<see cref="Profiles.WeaponProfile.TimingGun"/>); false = from the vdata.</param>
/// <param name="FromVData">What the vdata gives, also when another gun is chosen; null when no vdata block plays the slot.</param>
public sealed record TimingGun(GunState Gun, bool Chosen, PlaysLikeResult? FromVData)
{
    /// <summary>The <c>Group/Name</c> key stored in the profile.</summary>
    public string Key => KeyOf(Gun);

    public static string KeyOf(GunState gun) => gun.Group + "/" + gun.Name;

    /// <summary>"the Nova (from the vdata)", or "the Nova (picked; the vdata gives the SSG 08)".</summary>
    public string Describe()
    {
        if (!Chosen)
        {
            return FromVData is { Listed: false, Group: { } group }
                ? $"the {Gun.Name} (from the vdata: the {PlaysLike.Noun(group)} held last, the {Gun.Name} until another is used)"
                : $"the {Gun.Name} (from the vdata)";
        }

        return FromVData?.Gun is { } given && given.Name != Gun.Name
            ? $"the {Gun.Name} (picked here; the vdata makes the game run the {given.Name})"
            : $"the {Gun.Name} (picked here)";
    }

    /// <summary>Plain sentences about what the vdata costs (an unlisted class, a scope that will not show).</summary>
    public IReadOnlyList<string> Warnings() => FromVData?.Warnings() ?? [];
}

public static class GameTimings
{
    public const string FileName = "game_timing.json";
    public const string Schema = "weaponlab.gametiming/2";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // Plain apostrophes for people reading it.
    };

    /// <summary>Every gun CS2's root graph knows, grouped, for the expected-class picker.</summary>
    public static IReadOnlyList<GunState> Guns(GameConnection connection) =>
        GunSelection.Of(connection).Groups.SelectMany(group => group.Guns).ToList();

    /// <summary>
    /// The gun whose timing to show for a weapon: <paramref name="choice"/> (a <see cref="TimingGun.Key"/>) when it
    /// names a gun CS2's graph has, else the one the vdata gives. Null when neither gives one.
    /// </summary>
    public static TimingGun? Resolve(GameConnection connection, WeaponEntry weapon, VDataFile? addonVData, string? addonName, string? choice)
    {
        var fromVData = PlaysLike.ForSlot(connection, weapon, addonVData, addonName).FirstOrDefault();
        var chosen = choice is null ? null : Guns(connection).FirstOrDefault(gun => TimingGun.KeyOf(gun) == choice);
        if (chosen is not null)
        {
            return new TimingGun(chosen, true, fromVData);
        }

        return fromVData?.Gun is { } gun ? new TimingGun(gun, false, fromVData) : null;
    }

    public static GameTiming? For(GameConnection connection, PlaysLikeResult playsLike, WeaponEntry weapon, ClipInfo clip) =>
        playsLike.Gun is null ? null : For(connection, playsLike.Gun, weapon, clip);

    /// <summary>The game's timing for a clip, or null when the gun has no clip in that role or the clip counts no ammo.</summary>
    public static GameTiming? For(GameConnection connection, GunState gun, WeaponEntry weapon, ClipInfo clip)
    {
        if (PlaysLike.WeaponOf(connection, gun) is not { } gunWeapon
            || PlaysLike.MatchingClip(weapon, clip.ResourcePath, gunWeapon) is not { } path)
        {
            return null;
        }

        var reference = connection.ReadClip(path);
        var scale = reference.FramesPerSecond > 0 && clip.FramesPerSecond > 0 ? clip.FramesPerSecond / reference.FramesPerSecond : 1;
        var outroEnd = reference.LastFrame * scale;
        if (ShellReload.Read(reference.SyncTrack, reference.Events, reference.LastFrame) is { } loop)
        {
            var scaled = ShellReload.Scale(loop, scale);
            return new GameTiming(gun.Name, reference, scaled, outroEnd, null,
            [
                new(scaled.LoopStart, "intro ends"),
                new(scaled.AddAmmo, "shell counted"),
                new(scaled.LoopEnd, "loop ends"),
                new(outroEnd, "outro ends"),
            ])
            {
                Bands = WithDone(
                [
                    new(0, scaled.LoopStart, "intro", "intro"),
                    new(scaled.LoopStart, scaled.LoopEnd, "loop: once per shell", "loop"),
                    new(scaled.LoopEnd, outroEnd, "outro", "outro"),
                ], outroEnd, clip.LastFrame),
            };
        }

        var addAmmo = reference.Events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == ShellReload.AddAmmo);
        if (addAmmo is null)
        {
            return null;
        }

        var refill = addAmmo.StartFrame(reference) * scale;
        return new GameTiming(gun.Name, reference, null, outroEnd, refill, [new(refill, "ammo refills"), new(outroEnd, "reload ends")])
        {
            Bands = WithDone(
            [
                new(0, refill, "before the refill", "before"),
                new(refill, outroEnd, "refilled", "after"),
            ], outroEnd, clip.LastFrame),
        };
    }

    /// <summary>Adds the stretch after the game is done, when the weapon's clip runs past it.</summary>
    private static IReadOnlyList<TimingBand> WithDone(List<TimingBand> bands, double outroEnd, int lastFrame)
    {
        if (lastFrame > outroEnd + 0.5)
        {
            bands.Add(new TimingBand(outroEnd, lastFrame, "game done: firing cuts this", "done"));
        }

        return bands;
    }

    /// <summary>
    /// Plain sentences comparing a clip as it will play (its own sync track and ADD_AMMO mark) with the game's
    /// timing: when the counter goes up against what the player sees. Empty when they agree (or cannot be compared).
    /// </summary>
    /// <param name="clip">The weapon's clip with its edits (an export, or the template at its set length).</param>
    public static IReadOnlyList<string> Warnings(GameTiming timing, ClipInfo clip)
    {
        var warnings = new List<string>();
        var label = timing.Gun;
        if (timing.Loop is not null)
        {
            var theirs = ShellReload.Timing(ShellReload.Read(timing.Reference.SyncTrack, timing.Reference.Events, timing.Reference.LastFrame), timing.Reference);
            var mine = ShellReload.Timing(ShellReload.Read(clip.SyncTrack, clip.Events, clip.LastFrame), clip);
            if (theirs is null)
            {
                return warnings;
            }

            if (mine is null)
            {
                warnings.Add($"The game reloads it shell by shell like the {label}, but {clip.Name} has no shell loop, so it plays once while the counter adds every shell.");
                return warnings;
            }

            if (mine.Intro > theirs.Intro + 0.05)
            {
                warnings.Add($"{clip.Name}: the intro is {mine.Intro - theirs.Intro:0.00} s longer than the {label}'s, so the game may load every shell before your loop starts, and the loop does not repeat on screen.");
            }

            var firstShell = mine.Intro + mine.AddAmmoIntoLoop - (theirs.Intro + theirs.AddAmmoIntoLoop);
            var perShell = mine.Loop - theirs.Loop;
            if (Math.Abs(firstShell) > 0.05 || Math.Abs(perShell) > 0.05)
            {
                var drift = Math.Abs(perShell) > 0.05 ? $", and {Math.Abs(perShell):0.00} s {(perShell > 0 ? "more" : "less")} with each shell after that" : "";
                warnings.Add($"{clip.Name}: the ammo counter adds the first shell {Math.Abs(firstShell):0.00} s {(firstShell > 0 ? "before" : "after")} your animation shows it{drift} (the game runs the {label}'s timing).");
            }

            return warnings;
        }

        if (timing.Refill is { } refill && clip.FramesPerSecond > 0)
        {
            var mark = clip.Events.FirstOrDefault(clipEvent => clipEvent.Kind == ClipEventKind.Gameplay && clipEvent.Name == ShellReload.AddAmmo);
            if (mark is not null)
            {
                var markSeconds = mark.StartSeconds(clip);
                var refillSeconds = refill / clip.FramesPerSecond;
                if (Math.Abs(markSeconds - refillSeconds) > 0.1)
                {
                    warnings.Add($"{clip.Name}: the magazine refills at {refillSeconds:0.00} s (the {label}'s timing), {Math.Abs(markSeconds - refillSeconds):0.00} s {(markSeconds > refillSeconds ? "before" : "after")} the moment this clip marks.");
                }
            }
        }

        return warnings;
    }

    /// <summary>
    /// Writes <c>blender/input/game_timing.json</c> for the Blender add-on: the gun whose timing the game runs,
    /// its marks and bands per clip. Removes a stale file when there is no such gun. Called after a build (which
    /// renews the input folder), before Blender opens and when the expected class changes.
    /// </summary>
    /// <param name="choice">The profile's <see cref="Profiles.WeaponProfile.TimingGun"/>, or null for the vdata's.</param>
    public static void Write(SandboxInfo sandbox, GameConnection connection, VDataFile? addonVData, string? addonName, string? choice = null)
    {
        var path = Path.Combine(RigPackage.InputPath(sandbox), FileName);
        var weapon = connection.Weapons.FirstOrDefault(item => item.Id == sandbox.Manifest.WeaponId);
        var gun = weapon is null ? null : Resolve(connection, weapon, addonVData, addonName, choice);
        if (weapon is null || gun is null || !Directory.Exists(RigPackage.InputPath(sandbox)))
        {
            File.Delete(path);
            return;
        }

        var clips = new SortedDictionary<string, ClipTimingFile>(StringComparer.Ordinal);
        foreach (var clip in sandbox.Manifest.Clips)
        {
            if (For(connection, gun.Gun, weapon, connection.ReadClip(clip.ResourcePath)) is { } timing)
            {
                clips[RigPackage.ClipId(clip.ResourcePath)] = new ClipTimingFile(timing.Reference.Name,
                    timing.Marks.Select(mark => new MarkFile(Math.Round(mark.Frame, 2), mark.Label)).ToList(),
                    timing.Bands.Select(band => new BandFile(Math.Round(band.Start, 2), Math.Round(band.End, 2), band.Label, band.Kind)).ToList());
            }
        }

        var document = new TimingFile(Schema, gun.Gun.Name, gun.Describe(), gun.Warnings(), clips);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(document, Json));
        File.Move(temporary, path, overwrite: true);
    }

    /// <param name="Source">Where the gun comes from, e.g. "the Nova (from the vdata)".</param>
    private sealed record TimingFile(string Schema, string? Gun, string Source, IReadOnlyList<string> Warnings, IReadOnlyDictionary<string, ClipTimingFile> Clips);

    private sealed record ClipTimingFile(string Reference, IReadOnlyList<MarkFile> Marks, IReadOnlyList<BandFile> Bands);

    private sealed record MarkFile(double Frame, string Label);

    private sealed record BandFile(double Start, double End, string Label, string Kind);
}
