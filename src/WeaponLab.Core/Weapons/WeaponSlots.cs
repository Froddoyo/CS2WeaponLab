using WeaponLab.Core.Game;
using WeaponLab.Core.VData;

namespace WeaponLab.Core.Weapons;

/// <summary>A vdata block whose weapon plays a slot, and where its class comes from.</summary>
/// <param name="Source">"CS2", or the addon whose vdata gives it this class.</param>
public sealed record SlotPlayer(string Block, string Source);

/// <param name="WeaponId">The weapon folder holding the slot's clips, e.g. rifle_aug.</param>
/// <param name="WeaponType">The <c>weapon_type</c> value the root viewmodel graph switches on, e.g. weapon_aug.</param>
/// <param name="PlayedBy">Every weapon that lands in this slot: its class (or anim class) is the slot's weapon_type.</param>
/// <param name="PerKnife">A knife: it plays its own animations, not a class's slot (see <see cref="WeaponSlots.PicksPerKnife"/>).</param>
public sealed record WeaponSlot(string WeaponId, string? WeaponType, IReadOnlyList<SlotPlayer> PlayedBy, bool PerKnife = false);

/// <summary>
/// Animation slots. The root viewmodel graph picks a weapon's animations by
/// the <c>weapon_type</c> control parameter (165 comparisons such as
/// weapon_cz75a, verified 2026-10-25), which the game sets from the weapon's
/// class: <c>m_szAnimClass</c> when the block has one, else <c>_class</c>.
/// An M249 given the AUG's class stays an M249 everywhere else but animates in
/// the AUG's slot. The anim class can also be a made-up name: CS2 passes it to
/// the graph as written (verified in game 2026-10-27), so an addon's own root
/// graph can give it a branch, which makes a new slot. Without a branch the
/// weapon falls into its state machine's default state (the USP for pistols).
/// Knives are the exception (<see cref="PicksPerKnife"/>): each plays its own.
/// </summary>
public static class WeaponSlots
{
    public const string AnimClassKey = "m_szAnimClass";

    /// <summary>The class every CS2 knife block has (weapon_knife, weapon_knife_t, weapon_knife_ursus, …).</summary>
    public const string KnifeClass = "weapon_knife";

    /// <summary>The slot a block's weapon animates in (a knife's is <see cref="KnifeClass"/>, but see <see cref="PicksPerKnife"/>).</summary>
    public static string? SlotOf(VDataBlock block) =>
        block.Value(AnimClassKey) is { Length: > 0 } animClass ? animClass : block.Class;

    /// <summary>
    /// Knives pick their animations per knife, not by class (0.23): every CS2 knife block has class weapon_knife and
    /// no anim class, and the root graph has no per-knife weapon_type; knives run through viewmodel_knife.vnmgraph,
    /// which has a variation per knife (+ursus, +karambit, +default_ct, …). In game a knife plays its own clips (the
    /// user's Ursus, 2026-11-07). HOW the game picks the variation is NOT verified (likely the knife's own item,
    /// model or skeleton). So a block with class weapon_knife and no anim class plays its own weapon's clips.
    /// </summary>
    public static bool PicksPerKnife(VDataBlock block) =>
        block.Value(AnimClassKey) is not { Length: > 0 } && block.Class == KnifeClass;

    /// <param name="addonVData">The target addon's compiled vdata, or null.</param>
    public static WeaponSlot For(GameConnection connection, WeaponEntry weapon, VDataFile? addonVData, string? addonName)
    {
        var stock = connection.ReadVData();
        var own = connection.VDataBlocksFor(weapon).Select(name => stock[name]).OfType<VDataBlock>().ToList();
        if (own.Count > 0 && own.All(PicksPerKnife))
        {
            // A knife's clips are its own: played by its own blocks while they still pick per knife.
            var knives = own.Where(block => PicksPerKnife(addonVData?[block.Name] ?? block))
                .Select(block => new SlotPlayer(block.Name, "CS2"))
                .ToList();
            return new WeaponSlot(weapon.Id, KnifeClass, knives, PerKnife: true);
        }

        var type = own.Select(SlotOf).FirstOrDefault(slot => slot is not null);
        if (type is null)
        {
            return new WeaponSlot(weapon.Id, null, []);
        }

        var players = new List<SlotPlayer>();
        foreach (var block in stock.Blocks.Where(block => block.IsWeapon))
        {
            var addon = addonVData?[block.Name];
            if (SlotOf(addon ?? block) == type)
            {
                var changed = addon is not null && SlotOf(addon) != SlotOf(block);
                players.Add(new SlotPlayer(block.Name, changed ? addonName ?? "the addon" : "CS2"));
            }
        }

        return new WeaponSlot(weapon.Id, type, players);
    }

    /// <summary>"weapon_aug slot, played by weapon_aug and weapon_m249 (my_addon)", or a knife's own animations.</summary>
    public static string Describe(WeaponSlot slot)
    {
        if (slot.WeaponType is null)
        {
            return "No vdata block uses this weapon's clips.";
        }

        var players = slot.PlayedBy.Select(player => player.Source == "CS2" ? player.Block : $"{player.Block} ({player.Source})").ToList();
        if (slot.PerKnife)
        {
            return players.Count == 0
                ? "A knife's own animations, used by no block (knives pick their animations per knife, not by class)"
                : $"{string.Join(", ", players)}'s own animations (knives pick their animations per knife, not by class {KnifeClass})";
        }

        return players.Count == 0
            ? $"{slot.WeaponType} slot, used by no weapon"
            : $"{slot.WeaponType} slot, played by {string.Join(", ", players)}";
    }
}
