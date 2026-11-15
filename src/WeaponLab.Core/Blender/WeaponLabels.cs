namespace WeaponLab.Core.Blender;

/// <summary>
/// Display names for weapon folders whose ids are not self-explanatory.
/// The <c>_default_*</c> folders hold the default weapons' clips.
/// </summary>
public static class WeaponLabels
{
    private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rifle_ak"] = "AK-47",
        ["rifle_m4a4"] = "M4A4",
        ["rifle_galilar"] = "Galil AR",
        ["rifle_sg556"] = "SG 553",
        ["rifle_scar20"] = "SCAR-20",
        ["rifle_g3sg1"] = "G3SG1",
        ["rifle_ssg08"] = "SSG 08",
        ["rifle_bizon"] = "PP-Bizon",
        ["rifle_mac10"] = "MAC-10",
        ["rifle_mag7"] = "MAG-7",
        ["rifle_mp5sd"] = "MP5-SD",
        ["rifle_ump45"] = "UMP-45",
        ["rifle_sawedoff"] = "Sawed-Off",
        ["pistol_hkp2000"] = "P2000",
        ["pistol_elite"] = "Dual Berettas",
        ["pistol_taser"] = "Zeus x27",
        ["pistol_fiveseven"] = "Five-SeveN",
        ["pistol_cz75a"] = "CZ75-Auto",
        ["pistol_deagle"] = "Desert Eagle",
        ["pistol_revolver"] = "R8 Revolver",
        ["pistol_glock18"] = "Glock-18",
        ["pistol_tec9"] = "Tec-9",
        ["_default_rifle"] = "M4A1-S (default rifle)",
        ["_default_pistol"] = "USP-S (default pistol)",
        ["_default_knife"] = "CT Knife (default knife)",
        ["_default_grenade"] = "Decoy (default grenade)",
        ["knife_canis"] = "Survival Knife",
        ["knife_cord"] = "Paracord Knife",
        ["knife_css"] = "Classic Knife",
        ["knife_outdoor"] = "Nomad Knife",
        ["knife_push"] = "Shadow Daggers",
        ["knife_tactical"] = "Huntsman Knife",
        ["knife_default_t"] = "T Knife",
        ["knife_m9"] = "M9 Bayonet",
        ["grenade_hegrenade"] = "HE Grenade",
        ["grenade_smokegrenade"] = "Smoke Grenade",
        ["c4"] = "C4",
        ["healthshot"] = "Healthshot",
    };

    public static string For(string weaponId)
    {
        if (Labels.TryGetValue(weaponId, out var label))
        {
            return label;
        }

        // rifle_awp -> AWP, knife_karambit -> KARAMBIT
        var underscore = weaponId.IndexOf('_');
        var name = underscore > 0 && !weaponId.StartsWith('_') ? weaponId[(underscore + 1)..] : weaponId;
        return name.Replace('_', ' ').ToUpperInvariant();
    }
}
