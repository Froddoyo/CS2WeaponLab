namespace WeaponLab.Core.VData;

/// <summary>A group of top-level vdata keys a class change can copy from another weapon.</summary>
public sealed record VDataClassPart(string Id, string Label, string Description, bool OnByDefault, Func<string, bool> Owns);

/// <summary>
/// "Base this weapon on another one": a block takes the donor block's values
/// for the chosen parts, e.g. a CZ75 that becomes an SMG by taking the
/// Bizon's type, animations and handling. Stored in the profile per block;
/// the donor's values are CS2's own. Your single-value edits apply on top.
/// </summary>
public sealed class VDataClassSwap
{
    public string From { get; set; } = "";

    public List<string> Parts { get; set; } = [];

    public static readonly IReadOnlyList<VDataClassPart> AllParts =
    [
        new("type", "Weapon type", "Inherits from, category tags and weapon type (pistol, SMG, rifle…). Decides which game rules treat it as what.", true,
            key => key is VDataBlock.BaseKey or "taxonomy" or "m_WeaponType"),
        new("animations", "Animations", "Animation class and weapon skeleton: the viewmodel then plays the donor's clips and graph routing.", true,
            key => key is "m_szAnimClass" or VDataBlock.SkeletonKey),
        new("model", "Model and effects", "World model, muzzle position, tracer and handedness.", true,
            key => key is VDataBlock.WorldModelKey or "m_vecMuzzlePos0" or "m_szTracerParticle" or "m_bBuiltRightHanded" or "m_bAllowFlipping" or "m_iRumbleEffect"),
        new("sounds", "Shot sounds", "The firing soundevents.", true, key => key == "m_aShootSounds"),
        new("handling", "Handling", "Damage, firing, accuracy, recoil, ammo, movement and zoom values.", true,
            key => VDataGlossary.Describe(key).Group is "Damage" or "Firing" or "Accuracy" or "Recoil" or "Ammo" or "Movement" or "Zoom"),
        new("economy", "Price and kill reward", "Buy price and kill award.", false, key => VDataGlossary.Describe(key).Group == "Economy"),
        new("inventory", "Inventory slot", "Gear slot, slot position, loadout position and auto-switch. Changing these moves the weapon in the buy menu and loadout.", false,
            key => VDataGlossary.Describe(key).Group == "Inventory"),
        new("entity", "Entity class (_class)", "The game code that runs the weapon. Changing it makes the game create the donor's weapon entity; loadouts, skins and item names may stop matching. Usually left off.", false,
            key => key == VDataBlock.ClassKey),
    ];

    /// <summary>Keys the swap never touches: the block keeps its own name.</summary>
    private static bool Protected(string key) => key == "m_szName";

    public bool Covers(string rootKey) =>
        !Protected(rootKey) && AllParts.Any(part => Parts.Contains(part.Id) && part.Owns(rootKey));

    /// <summary>The top-level keys of either block this swap replaces.</summary>
    public IReadOnlyList<string> RootKeys(VDataBlock target, VDataBlock donor)
    {
        return target.Properties.Select(property => VDataGlossary.RootKey(property.Path))
            .Concat(donor.Properties.Select(property => VDataGlossary.RootKey(property.Path)))
            .Distinct(StringComparer.Ordinal)
            .Where(Covers)
            .ToList();
    }

    /// <summary>
    /// The block as the swap makes it: covered keys come from the donor (and
    /// disappear when the donor lacks them), everything else stays.
    /// </summary>
    public VDataBlock Apply(VDataBlock target, VDataBlock donor)
    {
        var keys = RootKeys(target, donor).ToHashSet(StringComparer.Ordinal);
        var properties = target.Properties.Where(property => !keys.Contains(VDataGlossary.RootKey(property.Path))).ToList();
        properties.AddRange(donor.Properties.Where(property => keys.Contains(VDataGlossary.RootKey(property.Path))));
        string? Text(string key) => properties.FirstOrDefault(property => property.Path == key)?.Value;
        return new VDataBlock(target.Name, Text(VDataBlock.BaseKey), Text(VDataBlock.ClassKey), properties);
    }

    /// <summary>True when <paramref name="actual"/> holds exactly the donor's values for the covered keys.</summary>
    public static bool Matches(VDataBlock actual, VDataBlock expected, IReadOnlyCollection<string> rootKeys)
    {
        static Dictionary<string, string> Leaves(VDataBlock block, IReadOnlyCollection<string> keys) =>
            block.Properties.Where(property => keys.Contains(VDataGlossary.RootKey(property.Path)))
                .ToDictionary(property => property.Path, property => property.Value, StringComparer.Ordinal);

        var a = Leaves(actual, rootKeys);
        var b = Leaves(expected, rootKeys);
        return a.Count == b.Count && b.All(item => a.TryGetValue(item.Key, out var value) && VDataValues.Same(value, item.Value));
    }

    /// <summary>The block without the given leaf paths (values set on top of a class change).</summary>
    public static VDataBlock Without(VDataBlock block, IReadOnlySet<string> paths) =>
        block with { Properties = block.Properties.Where(property => !paths.Contains(property.Path)).ToList() };

    public string Summary() =>
        $"based on {From}: " + string.Join(", ", AllParts.Where(part => Parts.Contains(part.Id)).Select(part => part.Label.ToLowerInvariant()));
}

/// <summary>Value comparison that tolerates float precision lost in compiling.</summary>
public static class VDataValues
{
    public static bool Same(string actual, string expected)
    {
        if (actual == expected)
        {
            return true;
        }

        return double.TryParse(actual, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)
            && double.TryParse(expected, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var b)
            && Math.Abs(a - b) <= Math.Max(1e-6, Math.Abs(b) * 1e-6);
    }
}
