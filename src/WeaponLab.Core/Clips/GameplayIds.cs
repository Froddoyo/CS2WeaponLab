namespace WeaponLab.Core.Clips;

/// <summary>A gameplay ID (<c>CNmIDEvent.m_ID</c>) CS2's viewmodel clips use, and what it does.</summary>
/// <param name="Meaning">One line; "not confirmed" where it is read from the clips rather than tested in game.</param>
public sealed record GameplayId(string Id, string Meaning, string Secondary = "");

/// <summary>
/// The gameplay IDs found in CS2's viewmodel clips (scan of 1.41.9.3,
/// 2026-10-31), most used first, with a one-line meaning for the picker.
/// </summary>
public static class GameplayIds
{
    public static IReadOnlyList<GameplayId> All { get; } =
    [
        new("WPN_RELOAD_ADD_AMMO", "The game adds ammo here: the magazine, or one shell per pass on a shell-by-shell reload (verified)."),
        new("WPN_DROP_MAG", "Drops the magazine; the secondary ID names the part (mag_drop, clip).", "mag_drop"),
        new("WPN_BLOCK_INSPECT", "While it runs, an inspect cannot start (draws, reloads, actions)."),
        new("WPN_RELOAD_SILENT", "On normal reloads (the Nova's runs 0-92%); purpose not confirmed."),
        new("WPN_RELOAD_INTRO", "Shell reload: the opening section, before the loop (verified)."),
        new("WPN_RELOAD_LOOP", "Shell reload: one shell going in; the graph jumps back here while the game wants more shells (verified)."),
        new("WPN_RELOAD_OUTRO", "Shell reload: the closing section; reached when the game says the last shell is in (verified)."),
        new("WPN_INSPECT_INTRO", "Inspect: the opening section."),
        new("WPN_INSPECT_LOOP", "Inspect: the middle section."),
        new("WPN_INSPECT_OUTRO", "Inspect: the closing section."),
        new("WPN_INSPECT_USE_DRAW_VERSION", "Knife draws: lets the inspect start from the draw; not confirmed."),
        new("WPN_INSPECT_USE_FIXUP_VERSION", "Inspect fix-up variant; not confirmed."),
        new("WPN_INSPECTCANCEL_USE_FIXUP_EXIT", "Inspect cancel fix-up exit; not confirmed."),
        new("WPN_SHOW_BULLETS", "Shows the belt's bullets (M249 and Negev reloads)."),
        new("WPN_SHOW_SILENCER", "Shows the silencer model (silencer attach clips)."),
        new("WPN_SILENCER_ATTACH", "The silencer counts as attached here."),
        new("WPN_SILENCER_DETACH", "The silencer counts as detached here."),
        new("WPN_DISABLE_LEFT_HAND_IK", "Turns off left-hand IK while it runs."),
        new("WPN_BOMB_STAGE", "C4 plant: the stage of the plant (secondary ID 0, 1…).", "0"),
        new("WPN_HEALTHSHOT_INJECT", "Healthshot: the injection happens here."),
        new("WPN_RELOAD_STAGE_END", "XM1014: the game picks how many shells to load here; not confirmed."),
        new("WPN_RELOAD_1SHELL", "XM1014: entry point when one shell is needed; not confirmed."),
        new("WPN_RELOAD_2SHELLS", "XM1014: entry point when two shells are needed; not confirmed."),
        new("WPN_RELOAD_3SHELLS", "XM1014: entry point when three shells are needed; not confirmed."),
        new("WPN_RELOAD_1SHELL_LOOP", "XM1014 silent reload: one-shell loop marker; not confirmed."),
        new("WPN_RELOAD_2SHELL_LOOP", "XM1014 silent reload: two-shell loop marker; not confirmed."),
        new("WPN_RELOAD_3SHELL_LOOP", "XM1014 silent reload: three-shell loop marker; not confirmed."),
        new("WPN_RELOAD_MAX_LOOP", "XM1014 silent reload: loop marker for a full load; not confirmed."),
        new("WPN_IK_ACTION_ENDING", "Third person: the hand IK blends back as the action ends."),
    ];

    public static GameplayId? Find(string id) => All.FirstOrDefault(item => item.Id == id);
}
