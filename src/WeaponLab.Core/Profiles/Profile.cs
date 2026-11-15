using System.Text.Json.Serialization;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Profiles;

/// <summary>How a weapon's animations are delivered.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AnimationMode>))]
public enum AnimationMode
{
    /// <summary>The game's own clips and graphs; Weapon Lab removes anything it delivered for this weapon.</summary>
    Default,

    /// <summary>Deliver every clip exported from Blender with edits.</summary>
    Custom,
}

/// <summary>What a profile wants for one weapon folder, e.g. rifle_ak.</summary>
public sealed class WeaponProfile
{
    public AnimationMode Animations { get; set; } = AnimationMode.Custom;

    /// <summary>
    /// Model to preview and edit with in Blender instead of the stock mesh:
    /// "game:weapons/models/..." or "addon:&lt;addon&gt;:models/...". Null = stock.
    /// </summary>
    public string? PreviewModel { get; set; }

    /// <summary>
    /// A custom weapon skeleton built from a model's bones (see
    /// <see cref="Skeletons.CustomSkeletons"/>); null = the weapon's own. Its
    /// clips and skeleton are delivered with a Custom weapon (the user points their model at it).
    /// </summary>
    public CustomSkeletonChoice? CustomSkeleton { get; set; }

    /// <summary>Build the .blend from the target addon's own clips and skeleton (true) or the game files (false); null = not chosen yet.</summary>
    public bool? BuildFromAddon { get; set; }

    /// <summary>Sleeve (agent) and glove models previewed on the arm rig; null = default, "" = none.</summary>
    public string? ArmsModel { get; set; }

    public string? GlovesModel { get; set; }

    /// <summary>Clips (by id, e.g. reload_ak) left out of delivery: their edits stay in the sandbox, unticked in the Deliver tree.</summary>
    public SortedSet<string> ExcludedClips { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Set to CS2 default: the next apply removes every file the addon has for
    /// this weapon, Weapon Lab's or not (see <see cref="Delivery.StockReset"/>),
    /// and delivers nothing for it. Cleared once applied, or when new work starts.
    /// </summary>
    public bool ResetToStock { get; set; }

    /// <summary>
    /// Clips (by id) set to CS2 default one at a time (0.22): their sandbox work is set aside, and the
    /// next push removes the addon's copy and points its graphs back at CS2's clip (on a custom skeleton,
    /// CS2's clip re-targeted to it goes in instead). Cleared once pushed; a new export of the clip wins.
    /// </summary>
    public SortedSet<string> StockClips { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Weapon bones ticked "Keep apart from the gun" (0.23): the .blend poses them as props (e.g. a spare magazine
    /// held in the other hand) that hold still while the gun moves, and export writes the motion that keeps them there. Off by
    /// default: every bone is a normal gun part. Takes effect on the next Build .blend.
    /// </summary>
    public SortedSet<string> PropBones { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// The gun whose game timing Anim Events and Blender show for this weapon (0.24, the "expected class"), as
    /// <c>Group/Name</c> of CS2's root graph, e.g. <c>Shotguns/Nova</c>. Null = the one the vdata makes CS2 run.
    /// Display only: it changes no vdata, so the game still runs what the vdata says.
    /// </summary>
    public string? TimingGun { get; set; }

    public bool IsDefault => Animations == AnimationMode.Default && PreviewModel is null && CustomSkeleton is null && PropBones.Count == 0 && TimingGun is null;
}

/// <param name="Model">The model whose bones make the skeleton, as a preview model key.</param>
/// <param name="Path">The skeleton's resource path, e.g. <c>animation/skeletons/weapons/weaponlab_rifle_galilar.vnmskel</c>.</param>
/// <param name="ModelHierarchy">Follow the model's own hierarchy for bones CS2's skeleton also has (a full custom animset), instead of CS2's.</param>
public sealed record CustomSkeletonChoice(string Model, string Path, bool ModelHierarchy = false);

/// <summary>
/// A profile: a local workspace of changes not pushed yet (user decision
/// 2026-10-26). It holds per-weapon choices, graph edits and unsaved vdata
/// edits; its folder holds the sandboxes (Blender files and exports). It says
/// nothing about what an addon already has: the addon is the live state, and
/// applying pushes the pending changes into it.
/// </summary>
public sealed class Profile
{
    public const string CurrentSchema = "weaponlab.profile/1";

    public string Schema { get; set; } = CurrentSchema;
    public string Name { get; set; } = "My profile";

    /// <summary>The addon last picked with this profile (picked again when switching to it).</summary>
    public string? TargetAddon { get; set; }

    /// <summary>
    /// What this profile already pushed: addon → file key (as in the delivery
    /// manifest) → SHA-256. An export or skeleton is pending until its bytes
    /// were pushed to that addon; a new export from Blender makes it pending again.
    /// </summary>
    public SortedDictionary<string, SortedDictionary<string, string>> Pushed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool WasPushed(string addon, string key, string sha) =>
        Pushed.TryGetValue(addon, out var files) && files.TryGetValue(key, out var pushed) && pushed == sha;

    public void RecordPushed(string addon, string key, string sha)
    {
        if (!Pushed.TryGetValue(addon, out var files))
        {
            Pushed[addon] = files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        files[key] = sha;
    }

    public SortedDictionary<string, WeaponProfile> Weapons { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// 0.21: a weapon's viewport model left at default is CS2's. Before, default
    /// meant a custom skeleton's model; profiles from then get that model stored
    /// as the weapon's pick once (<see cref="ViewportModels.KeepOldDefaults"/>).
    /// </summary>
    public bool ViewportDefaultIsCs2 { get; set; }

    public SortedDictionary<string, SortedDictionary<string, string>> VData { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Blocks based on another weapon (block → donor and parts), applied before <see cref="VData"/>.</summary>
    public SortedDictionary<string, VDataClassSwap> VDataClasses { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Graph edits: graph resource path (without <c>_c</c>) → <c>node:value path</c> → value
    /// (see <see cref="Graphs.GraphEdits"/>). Edited graphs are delivered as custom graphs.
    /// </summary>
    public SortedDictionary<string, SortedDictionary<string, string>> Graphs { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Graphs set to CS2 default (0.24, right-click a graph row): the next push rebuilds each from CS2's graph
    /// instead of the addon's current version, keeping only this profile's clip routing, so edits already in the
    /// addon are dropped. Graph resource paths without <c>_c</c>; cleared once pushed.
    /// </summary>
    public SortedSet<string> StockGraphs { get; set; } = new(StringComparer.Ordinal);

    public WeaponProfile Weapon(string weaponId) =>
        Weapons.TryGetValue(weaponId, out var weapon) ? weapon : new WeaponProfile();

    public WeaponProfile EnsureWeapon(string weaponId)
    {
        if (!Weapons.TryGetValue(weaponId, out var weapon))
        {
            Weapons[weaponId] = weapon = new WeaponProfile();
        }

        return weapon;
    }
}
