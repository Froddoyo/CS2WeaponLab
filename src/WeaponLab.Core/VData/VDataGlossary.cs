namespace WeaponLab.Core.VData;

/// <summary>A readable name, group and explanation for a vdata property.</summary>
public sealed record VDataTerm(string Group, string Name, string Description);

/// <summary>
/// Friendly names and descriptions for weapons.vdata properties. Array
/// properties like <c>m_flSpread[1]</c> use the term of <c>m_flSpread</c>; the
/// index usually means [0] = primary fire / unscoped, [1] = alternate / scoped.
/// Unknown keys fall back to their raw name.
/// </summary>
public static class VDataGlossary
{
    private static readonly Dictionary<string, VDataTerm> Terms = new(StringComparer.Ordinal)
    {
        // Damage
        ["m_nDamage"] = new("Damage", "Damage per bullet", "Base damage of one bullet before armor, range falloff and hit location."),
        ["m_flHeadshotMultiplier"] = new("Damage", "Headshot multiplier", "Damage is multiplied by this on a headshot."),
        ["m_flArmorRatio"] = new("Damage", "Armor penetration", "How well bullets ignore armor; higher means more damage goes through armor (the armor ratio)."),
        ["m_flPenetration"] = new("Damage", "Wall penetration power", "How much material a bullet can pass through."),
        ["m_flRange"] = new("Damage", "Max range", "Distance in game units after which bullets stop dealing damage."),
        ["m_flRangeModifier"] = new("Damage", "Range falloff", "Damage is multiplied by this for every 500 units travelled."),
        ["m_nNumBullets"] = new("Damage", "Pellets per shot", "Bullets fired per shot; shotguns fire several."),
        ["m_flFlinchVelocityModifierLarge"] = new("Damage", "Tagging (large hit)", "Victim speed multiplier after a big hit; lower slows them more."),
        ["m_flFlinchVelocityModifierSmall"] = new("Damage", "Tagging (small hit)", "Victim speed multiplier after a small hit; lower slows them more."),
        ["m_nKillAward"] = new("Economy", "Kill reward", "Money awarded for a kill with this weapon."),
        ["m_nPrice"] = new("Economy", "Price", "Buy menu cost."),

        // Firing
        ["m_flCycleTime"] = new("Firing", "Time between shots", "Seconds between shots; 0.1 means 600 rounds per minute. [0] normal, [1] alternate mode."),
        ["m_bIsFullAuto"] = new("Firing", "Full auto", "Keeps firing while the trigger is held."),
        ["m_bHasBurstMode"] = new("Firing", "Burst mode", "Weapon has a burst-fire mode on secondary attack."),
        ["m_flCycleTimeWhenInBurstMode"] = new("Firing", "Burst cycle time", "Seconds between bursts."),
        ["m_flTimeBetweenBurstShots"] = new("Firing", "Time between burst shots", "Seconds between the bullets of one burst."),
        ["m_bIsRevolver"] = new("Firing", "Revolver", "Uses revolver hammer behaviour (R8)."),
        ["m_bCannotShootUnderwater"] = new("Firing", "Cannot shoot underwater", "Weapon will not fire underwater."),
        ["m_nTracerFrequency"] = new("Firing", "Tracer frequency", "Every Nth bullet draws a tracer; 0 means none."),
        ["m_flDeployDuration"] = new("Firing", "Draw time", "Seconds after drawing before the weapon can fire."),
        ["m_flDisallowAttackAfterReloadStartDuration"] = new("Ammo", "Reload lockout", "Seconds after a reload starts during which you cannot fire; should match the reload animation."),

        // Accuracy
        ["m_flSpread"] = new("Accuracy", "Base spread", "Random spread of every shot, before movement and stance penalties. [0] unscoped, [1] scoped/alternate."),
        ["m_flInaccuracyStand"] = new("Accuracy", "Inaccuracy standing", "Extra inaccuracy while standing still."),
        ["m_flInaccuracyCrouch"] = new("Accuracy", "Inaccuracy crouching", "Extra inaccuracy while crouched."),
        ["m_flInaccuracyMove"] = new("Accuracy", "Inaccuracy moving", "Extra inaccuracy at full running speed."),
        ["m_flInaccuracyJump"] = new("Accuracy", "Inaccuracy jumping", "Extra inaccuracy in the air."),
        ["m_flInaccuracyJumpInitial"] = new("Accuracy", "Jump inaccuracy (start)", "Inaccuracy spike when leaving the ground."),
        ["m_flInaccuracyJumpApex"] = new("Accuracy", "Jump inaccuracy (apex)", "Inaccuracy at the top of a jump."),
        ["m_flInaccuracyLand"] = new("Accuracy", "Inaccuracy landing", "Inaccuracy spike when landing."),
        ["m_flInaccuracyLadder"] = new("Accuracy", "Inaccuracy on ladders", "Extra inaccuracy while on a ladder."),
        ["m_flInaccuracyFire"] = new("Accuracy", "Inaccuracy per shot", "Inaccuracy added by each shot (spray bloom)."),
        ["m_flInaccuracyReload"] = new("Accuracy", "Inaccuracy reloading", "Extra inaccuracy while reloading."),
        ["m_flInaccuracyPitchShift"] = new("Accuracy", "Inaccuracy pitch shift", "Pitch shift of the shot sound as inaccuracy grows."),
        ["m_flInaccuracyAltSoundThreshold"] = new("Accuracy", "Alt sound threshold", "Inaccuracy above which the alternate shot sound plays."),
        ["m_flRecoveryTimeStand"] = new("Accuracy", "Recovery standing", "Seconds to recover accuracy after firing, standing."),
        ["m_flRecoveryTimeCrouch"] = new("Accuracy", "Recovery crouching", "Seconds to recover accuracy after firing, crouched."),
        ["m_flRecoveryTimeStandFinal"] = new("Accuracy", "Recovery standing (late spray)", "Standing recovery time used later in a spray."),
        ["m_flRecoveryTimeCrouchFinal"] = new("Accuracy", "Recovery crouching (late spray)", "Crouched recovery time used later in a spray."),
        ["m_nRecoveryTransitionStartBullet"] = new("Accuracy", "Recovery blend start bullet", "Bullet where recovery starts blending to the late-spray value."),
        ["m_nRecoveryTransitionEndBullet"] = new("Accuracy", "Recovery blend end bullet", "Bullet where recovery reaches the late-spray value."),

        // Recoil
        ["m_flRecoilAngle"] = new("Recoil", "Recoil angle", "Direction of the recoil kick in degrees (0 = straight up)."),
        ["m_flRecoilAngleVariance"] = new("Recoil", "Recoil angle variance", "Random variation of the recoil direction."),
        ["m_flRecoilMagnitude"] = new("Recoil", "Recoil strength", "How far each shot kicks the view."),
        ["m_flRecoilMagnitudeVariance"] = new("Recoil", "Recoil strength variance", "Random variation of the kick."),
        ["m_nRecoilSeed"] = new("Recoil", "Recoil pattern seed", "Seed of the fixed spray pattern."),
        ["m_nSpreadSeed"] = new("Recoil", "Spread seed", "Seed for random spread."),

        // Ammo
        ["m_iMaxClip1"] = new("Ammo", "Magazine size", "Rounds per magazine."),
        ["m_iMaxClip2"] = new("Ammo", "Secondary magazine size", "Rounds for a secondary fire mode; usually 0."),
        ["m_iDefaultClip1"] = new("Ammo", "Starting magazine", "Rounds in the magazine when spawned; -1 means full."),
        ["m_iDefaultClip2"] = new("Ammo", "Starting secondary magazine", "-1 means full."),
        ["m_nPrimaryReserveAmmoMax"] = new("Ammo", "Reserve ammo", "Spare ammo carried (in rounds, or in magazines when reserve is counted as clips)."),
        ["m_nSecondaryReserveAmmoMax"] = new("Ammo", "Secondary reserve ammo", "Spare ammo for a secondary mode."),
        ["m_bReserveAmmoAsClips"] = new("Ammo", "Reserve counted in magazines", "Reserve ammo is a number of magazines instead of rounds."),
        ["m_nPrimaryAmmoType"] = new("Ammo", "Ammo type", "Which ammo pool the weapon uses."),

        // Movement
        ["m_flMaxSpeed"] = new("Movement", "Run speed", "Movement speed with this weapon out, in units per second. [0] normal, [1] scoped/alternate."),
        ["m_flAttackMovespeedFactor"] = new("Movement", "Speed while firing", "Movement speed multiplier while attacking."),
        ["m_iWeight"] = new("Movement", "Weight", "Used to rank weapons for auto-switching."),

        // Zoom
        ["m_nZoomLevels"] = new("Zoom", "Zoom levels", "How many scope zoom steps."),
        ["m_nZoomFOV1"] = new("Zoom", "Zoom FOV 1", "Field of view at the first zoom step."),
        ["m_nZoomFOV2"] = new("Zoom", "Zoom FOV 2", "Field of view at the second zoom step."),
        ["m_flZoomTime0"] = new("Zoom", "Unzoom time", "Seconds to return to no zoom."),
        ["m_flZoomTime1"] = new("Zoom", "Zoom time 1", "Seconds to reach the first zoom step."),
        ["m_flZoomTime2"] = new("Zoom", "Zoom time 2", "Seconds to reach the second zoom step."),
        ["m_bUnzoomsAfterShot"] = new("Zoom", "Unzoom after shot", "Scope drops after each shot (AWP style)."),
        ["m_bHideViewModelWhenZoomed"] = new("Zoom", "Hide view model when scoped", "Hides the arms and weapon while scoped."),

        // Identity and presentation
        ["_class"] = new("Identity", "Entity class", "The weapon entity class this block creates."),
        ["_base"] = new("Identity", "Inherits from", "The block this one takes its unset values from."),
        ["m_szName"] = new("Identity", "Weapon name", "Internal weapon name."),
        ["m_szWorldModel"] = new("Presentation", "World model", "Third-person model shown in other players' hands and on the ground."),
        ["m_szAnimClass"] = new("Presentation", "Animation class", "Which weapon's animation set and graph routing the viewmodel uses (e.g. weapon_bizon)."),
        ["m_szAnimSkeleton"] = new("Presentation", "Animation skeleton", "Weapon skeleton its viewmodel animations use; links this block to a weapon's clips."),
        ["m_szTracerParticle"] = new("Presentation", "Tracer effect", "Particle used for bullet tracers."),
        ["m_vecMuzzlePos0"] = new("Presentation", "Muzzle position", "Muzzle offset used for effects."),
        ["m_aShootSounds"] = new("Sound", "Shot sounds", "Soundevents played when firing, per sound slot."),
        ["m_eSilencerType"] = new("Firing", "Silencer", "Whether a silencer is built in, detachable or absent."),
        ["m_bAllowFlipping"] = new("Presentation", "Allow left-handed", "Viewmodel can be flipped for left-handed players."),
        ["m_bBuiltRightHanded"] = new("Presentation", "Built right-handed", "The viewmodel was authored for the right hand."),
        ["m_bMeleeWeapon"] = new("Identity", "Melee weapon", "Knife-type weapon."),
        ["m_iRumbleEffect"] = new("Presentation", "Controller rumble", "Rumble effect used when firing."),
        ["m_GearSlot"] = new("Inventory", "Gear slot", "Which inventory slot it occupies."),
        ["m_GearSlotPosition"] = new("Inventory", "Gear slot position", "Order within its slot."),
        ["m_DefaultLoadoutPosition"] = new("Inventory", "Loadout position", "Default loadout slot."),
        ["m_WeaponType"] = new("Identity", "Weapon type", "Category such as rifle, pistol or SMG."),
        ["m_bAutoSwitchFrom"] = new("Inventory", "Auto-switch away", "Allows auto-switching away from this weapon."),
        ["m_bAutoSwitchTo"] = new("Inventory", "Auto-switch to", "Allows auto-switching to this weapon when picked up."),
        ["taxonomy"] = new("Identity", "Taxonomy", "Category tags used by game rules."),
    };

    /// <summary>The term for a flattened path such as <c>m_flSpread[1]</c> or <c>taxonomy.rifle</c>.</summary>
    public static VDataTerm Describe(string path)
    {
        var key = RootKey(path);
        var suffix = path[key.Length..];
        if (Terms.TryGetValue(key, out var term))
        {
            return suffix.Length == 0 ? term : term with { Name = term.Name + " " + Readable(suffix) };
        }

        return new VDataTerm("Other", path, "No description yet.");
    }

    public static string RootKey(string path)
    {
        var end = path.IndexOfAny(['.', '[']);
        return end < 0 ? path : path[..end];
    }

    // [1] → (alt), .rifle → · rifle
    private static string Readable(string suffix)
    {
        return suffix switch
        {
            "[0]" => "(normal)",
            "[1]" => "(alt/scoped)",
            _ when suffix.StartsWith('.') => "· " + suffix[1..].Replace('_', ' '),
            _ => suffix,
        };
    }

    /// <summary>Display order of groups in the editor.</summary>
    public static readonly IReadOnlyList<string> GroupOrder =
        ["Damage", "Firing", "Accuracy", "Recoil", "Ammo", "Movement", "Zoom", "Economy", "Sound", "Presentation", "Inventory", "Identity", "Other"];
}
