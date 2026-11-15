# CS2 Weapon Lab: PC2 memory

Last updated: 2026-11-01 (America/Edmonton).

PC2's own notes. Only PC2 edits this file; see "Two PCs" in
`.claude/CLAUDE.md`. PC1's memory (`.claude/MEMORY.md`) holds the project
history and is read-only from here.

## Setup (checked 2026-10-28)

- GTX 1080 (user, 2026-10-29). PC1, the main PC, has the RTX 3090.

- Repo clone at `C:\CS2WeaponLab` (moved out of OneDrive Documents
  2026-10-28, hash-verified; user hates OneDrive: never put the repo or
  workspace under it), at 6f643d7 = origin/main. No `git` on PATH; GitHub
  Desktop's bundled git works:
  `%LOCALAPPDATA%\GitHubDesktop\app-3.6.10\resources\app\git\cmd\git.exe`.
- CS2 at `C:\Program Files (x86)\Steam\steamapps\common\Counter-Strike Global
  Offensive` (only Steam library), build 1.41.9.3 / client 2000938, Oct 28 2026.
  Workshop Tools present (resourcecompiler.exe). Addons: addon_template,
  cs_script_demo, logictest (no weaponlab_skeltest, no cs_hl_template).
  2026-11-01: addon `orb_battles` (the user's Orb Battles game mode) is its own repo,
  `github.com/Froddoyo/orb_battles`, cloned at `content/csgo_addons/orb_battles`; its
  plan and PC1's references live there now, not in this repo.
  cs_script API reference: `content/csgo_addons/cs_script_demo/maps/scripts/point_script.d.ts`.
  Command-line git here has no stored GitHub credential (fetch/push fail with "Cannot
  prompt"): commit with the CLI, fetch/pull/push in GitHub Desktop.
- Installed 2026-10-28 via winget (verified): .NET SDK 10.0.403
  (`C:\Program Files\dotnet`; only SDK) and Blender 5.2.3 at
  `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe` (BlenderLocator
  scans that folder). No Python (not needed).
- Computer use works on PC2 (verified 2026-10-28; tools only appeared after the
  user restarted the Claude app). GitHub Desktop's window belongs to
  `app-3.6.10\githubdesktop.exe`, which needs its own grant besides "GitHub
  Desktop". GitHub Desktop now tracks `C:\CS2WeaponLab` (old OneDrive entry removed).
- 2026-10-28: 93/97 tests pass on PC2; 3 FAIL because CS2 1.41.9.3 (Oct 28) changed
  game data: AK now has 12 clips (BlenderTests expects 11) and reload_ak's event
  order changed (EventAndVDataTests GameplayEventsAreReadOnly /
  SoundEventsCanBeRetimedAndRenamed hard-code event #5). Not fixed yet.
- PC2 VDATA LAB (user-approved, standing test addon, free to write): addon
  `weaponlab_vdatalab` (game + content), map `wl_vdlab` (user-built in Hammer; no
  baked lighting: GTX 1080 cannot bake, resourcecompiler map builds fail at vrad3;
  compile maps in Hammer with lighting off), cs_script probe
  `content/.../maps/scripts/wl_probe.js` (WL| log lines, wl_* cheat commands),
  `cfg/wl_setup.cfg`. Harness in `.claude/scratch/vdtool` (vdtool.cs patch/build,
  deploy.ps1, vcon.cs VConsole bridge `vcon_bin\vcon.exe serve <log> <inbox>`,
  wl.ps1, shot.ps1, speed.ps1, spread.ps1); notes `.claude/scratch/vdlab/FINDINGS.md`.
  Keep these when cleaning scratch. Addon vdata left = stock.
  Workflow: vdata NOT hot-reloaded; reload with `map wl_vdlab nomapvalidation=true`
  (plain map/changelevel = "invalid map name"); cs_script .vjs IS hot-reloaded;
  RegisterCheatCommand needs sv_cheats on when the script runs. VConsole port 29000
  serves ONE client. Console buy/drop via VConsole have no player context.
- Harness additions (2026-10-29/31, scratch, keep): `vdtool/reloadtest.ps1` (reload N
  times, name the clip by Clipout->F100 gap), `vdtool/shelltest.ps1 -Weapon -Clip
  -Seconds` (clip/reserve trace + weapon sounds after a reload; sounds only log while
  CS2 has focus), `anim/graphprobe.cs` (filters `class:`/`uses:`), `anim/hookup.cs`
  (rebuild the test-only stock-path hookup from viewmodel_custom + extra resource
  redirects via GraphPatcher), `anim/empty_reload.py` (headless clip variant + Save
  Clip As), `anim/shellclip.cs` (loop seam search + shell-reload clip build),
  `anim/clipdump.cs` (clip sync track + events). `mp_restartgame` brings freeze time back: set `mp_freezetime 0` before
  testing fire. The CLI used is still the 10-28 Debug build.
  More (2026-10-31): `vdtool/cs2start.ps1 [-Weapon] [-NoAgents]` (start/attach,
  wl_vdlab, agents, respawn, give), `inspecttest.ps1`, `variationtest.ps1`;
  `anim/agentpatch.cs` (agent copy with another hudmodel graph), `anim/fxclip.cs`
  (particle swap + add sound event), `anim/eventscan.cs`, `anim/modelprobe.cs`,
  `anim/graphrefs.cs`. vdtool patch strings: `res:` resource, `snd:` soundevent.
  wl_probe.js commands added: wl_agents on|off, wl_model [path], wl_precache,
  wl_fx off|explosive|launcher|lifesteal. NEVER edit wl_probe.js while CS2 runs:
  the hot reload re-runs its top-level Instance.Precache, which crashed CS2.
  Real input for attack2: `bind p +attack2` + computer-use key press (mouse
  right-click via computer use did not register as attack2).
- Lab state 2026-10-31 (end, set up for the user's ADS + heat test): NO stock-path
  root graph (moved to `scratch/vdlab/removed_root_hookup/`). Agents
  `agents/models/weaponlab/{ctm_sas_wl,tm_phoenix_wl}.vmdl_c` -> viewmodel_custom.
  AK = stock clips (profile rifle_ak Custom with all 12 sandbox clips excluded, the
  10-28 test edits and 10-29 reload probes removed) + graph edits 19/205/34/44 (ADS)
  and 29/35/39/45 (heat shot) -> hand-placed `custom/rifle_ak/{ironsight_ak,
  ironsight_shoot_ak_heat,shoot1_ak_heat}.vnmclip_c`; Deagle custom unchanged. vdata
  = p_aug_as_ak (weapon_aug wears the AK). AK material override + textures in
  `weapons/models/ak47/materials`, `weaponlab/ak47`. wl_probe.js: agents ON by
  default, spawn kit weapon_aug (`wl_kit <weapon>|off`). Maps: wl_vdlab (unlit) and
  wl_script (script only); `cfg/wl_dust2.cfg`. Sounds `sounds/weaponlab/testshot` +
  `soundevents/soundevents_addon.vsndevts`. Deliveries 20261031-131018 (AK reset)
  and -131054 (ADS/heat graph) have backups.

## Test rules (user)

- 2026-10-31 (user): from now on test ONLY with custom character models (agents) whose
  hudmodel graph is a custom graph, on a custom map whose vscript sets those characters
  (here: `wl_vdlab` + `wl_probe.js`, agents on by default). No copy of the custom graph
  at CS2's root graph path (`animation/graphs/viewmodel/viewmodel.vnmgraph_c`) in the
  lab, and no `deliver --cs2-root-copy true` for tests.
  Exception (user, 2026-10-31): gameplay anim-event ID tests may use a local-only root
  copy in the lab (the server hard-codes the root path). Label those results "root copy,
  local only"; remove the copy afterwards.

## Findings (PC2, in game)

- 2026-10-29 AK reloads (VERIFIED IN GAME): a separate empty reload works via graph
  edits `66:clip`/`76:clip` (GraphPatcher adds the slot). A third reload works by
  link-rewiring the unreachable XM1014 reload selectors (#80/#86, comparisons
  #83/#89). Threshold tests: deploy_/idle_variation always >= 0.5; inspect_variation
  < 0.5 until the first inspect, then >= 0.5; attack_variation is not a gun-graph
  parameter. SUPERSEDED 2026-10-31: random reloads ARE possible through a weighted
  selector (see WEIGHTED SELECTORS).
- 2026-10-31 SSG08 SHELL-BY-SHELL RELOAD (VERIFIED IN GAME with stock assets; rig:
  SSG08 variation reload nodes 63/66/73/76 + silent 166/170 -> reload_nova /
  reload_silent_nova, Shotguns' Nova state redirected to the SSG08 variation):
  * The ENDLESS LOOP is caused by `m_bReserveAmmoAsClips = true` (SSG08 inherits it
    from sniper_rifle), NOT by the weapon type. Reserve in magazines: the first
    ADD_AMMO fills the whole magazine and takes one reserve magazine, then the shell
    loop never ends (sniper AND shotgun type, 8+ s, until fired). Reserve in rounds
    (`m_bReserveAmmoAsClips false`, reserve max in rounds): one round per loop, then
    nova_pump, fires after - with WEAPONTYPE_SNIPER_RIFLE and with SHOTGUN alike;
    also stops correctly when reserve runs out (3 -> 7 of 10 with 4 in reserve) and
    when reloading while walking (+speed; whether the silent states ran is unknown).
    Keys present in all passing runs: m_bReloadsSingleShells true, taxonomy +
    reloads_per_shell + shotgun, disallow 0.466667 (which of these are needed is
    untested). Every stock shell reloader (nova/xm1014/sawedoff) has all four:
    single shells, reloads_per_shell, reserve in rounds, SHOTGUN type.
  * HAND-MADE LOOP EVENTS ON AN SSG08 CLIP WORK (VERIFIED IN GAME 2026-10-31, stock
    reload_ssg08 + reload_silent_ssg08 rebuilt by `.claude/scratch/anim/shellclip.cs`,
    played through the real route: sniper type, SniperRifles -> SSG08 slot, vdata as
    above). Partial 3->10: 7 loop passes / 7 rounds, then the bolt outro, fires after;
    empty 0->10: 10/10; firing mid-loop stops the reload (2 rounds in, shot fired).
    Recipe (= reload_nova): m_syncTrack.m_syncEvents = INTRO [0,loop start), LOOP
    [loop start, loop end), OUTRO [loop end, 1] (normalized = frame / (frames-1)); a
    CNmIDEvent per section with m_ID = m_syncID = WPN_RELOAD_INTRO/LOOP/OUTRO and the
    same span; WPN_RELOAD_ADD_AMMO inside LOOP; WPN_RELOAD_SILENT 0..~85% on the normal
    reload only; WPN_DROP_MAG removed; events sorted by start time.
    ADD_AMMO TIMING MATTERS: at 1 frame before the loop end (SSG08's own frame 60, loop
    52-61) the game played 8 passes for 7 rounds (one empty extra pass); moved to frame
    56 (5 frames before the end; reload_nova uses 4) it is exact. Loop seams used:
    52->61 (normal, 112 f) and 106->121 (silent, 223 f; the silent clip is the normal
    one at half speed, same normalized event times), from a pose search
    (`shellclip.cs seam`). Walking (+speed) does NOT trigger the silent reload (loop
    timing showed the normal clip), so the silent clip's loop is untested.
  * WHY SHOTGUN TYPE FORCES THE NOVA (verified from data): root viewmodel graph `WPN`
    SM picks a group by `weapon_category`, which follows m_WeaponType
    (weapon_category_shotgun/sniper/...; vdata m_WeaponCategory is a different, buy-menu
    enum WEAPONCATEGORY_*). Shotguns SM (#273) has states Nova/XM1014/SawedOff/MAG7
    keyed on weapon_type, default state 0 = Nova, so any other anim class lands on
    the Nova's gun variation. The SSG08 state lives in SniperRifles (#342, check #351).
- 2026-10-31 ANNABELLE LOOP = HOOKUP, NOT ASSETS OR VDATA (VERIFIED IN GAME, PC1's
  handoff `.claude/handoff/annabelle-loop-20261031`, all 24 sha256 OK, files copied
  into the lab except viewmodel_custom + the HL weapons.vdata_c; the user's exact
  weapon_ssg08 block imported into the lab vdata; agents on). Same files, two hookups:
  * A, CS2's own root graph overridden in the addon (stock path
    animation/graphs/viewmodel/viewmodel.vnmgraph_c = copy of the lab viewmodel_custom,
    SSG08 -> the handoff's viewmodel_gun_custom.vnmgraph+ssg08): WORKS. 2/3 -> 1 pass,
    3/3 reserve 31, bolt outro (+0.7 s), fires; 0/3 -> passes at 0.44/0.74/1.05 s,
    0->2->3, reserve 29, outro at 1.84 s, fires.
  * B, root override removed, viewmodel_custom reached ONLY through the agent's
    hudmodel ref: 2/3 still ends (one round needed); 0/3 FAILS like the user's: 0 -> 1,
    reserve 31, then a loop pass (ClipHit + AddAmmo) every ~0.30 s for 5+ s, clip
    stuck at 1. Earlier the same with my stock-SSG08 loop clips (V0 and user-like V4
    vdata): agent-only = 1 round then endless loop; root override = correct.
  Reading (inferred): the reload's gameplay side does not run the agent's hudmodel
  graph; it behaves like CS2's stock SSG08 reload (one ADD_AMMO -> one round), so the
  client's loop never gets stage_outro. vdata differences (no _base, BUCKSHOT, clip 3,
  reserve 32) are NOT the cause. Unexplained: the user's 2/3 reload looped in
  cs_hl_template while PC2's agent-only 2/3 ended.
- 2026-10-31 WORKSHOP-LEGIT HOOKUP (VERIFIED IN GAME, no stock-path graph override in
  the addon): player agents live in `agents/models/<agent>/<agent>.vmdl_c` (NOT
  characters/models, those are mesh parts); DATA `m_animGraph2Refs` = ""/worldmodel ->
  worldmodel.vnmgraph, uimodel -> ui/uimodel.vnmgraph, hudmodel ->
  viewmodel/viewmodel.vnmgraph. Copies made with Weapon Lab's ReferencePatcher (map
  {stock model path -> new path, viewmodel.vnmgraph -> viewmodel_custom.vnmgraph}, all
  else verified): addon `agents/models/weaponlab/ctm_sas_wl.vmdl_c`, `tm_phoenix_wl`.
  Map script (wl_probe.js): `Instance.Precache({resources:[...]})` at script load, then
  `pawn.SetModel(...)` in OnPlayerReset (+0.2 s) / `wl_agents on` / `wl_model <path>`.
  Result: custom agent -> custom AK clips (reload gap 16 = edited reload_ak), stock agent
  -> stock clips (gap 10); switching mid-life works both ways once precached. WITHOUT
  precache: "RESOURCE_TYPE_MODEL ... requested but is not in the system" and the
  viewmodel freezes in its reference pose (no events). Calling Precache during a .js
  hot reload CRASHED CS2 once; at map load it is fine. Weapon Lab's delivery hookup
  warning disappears once the addon has these agents. Limits: needs a map script (an
  addon on official maps has none); third person/bots not checked.
- 2026-10-31 WEIGHTED SELECTORS (VERIFIED IN GAME): a ParameterizedClipSelector with
  m_bHasWeightsSet picks a weighted option as a FIXED FUNCTION of its parameter value
  (AK reload test: reserve 3 -> option B 6/6, reserve 2 -> option A 3/3), not a fresh
  dice roll. Draws/inspects look random because the game re-rolls deploy_variation per
  draw and inspect_variation per inspect: AK draw weights 1:8 with a clip in draw1 ->
  2/28 of 30; inspect weights 1:1:1 -> 12/9/9 of 30. Weights are per-variation node
  values (AK inspects: lookat01 5, lookat03 1 = a stock rare inspect, slot 3 weight 0);
  a clip in an empty option + weight > 0 adds a rare draw/inspect to any gun. RANDOM
  RELOAD (verified): repurpose the AK's Deploy2 selector #214 (Deploy2 state #213
  m_nChildNodeIdx -> Deploy0's #210), options -> two reload clips, weights 1:1, reload
  selector #62 option 0 -> #214. Parameter deploy_variation = new pick per DRAW (7/8 of
  15 with redraws; 20/20 same without); parameter weapon_ammo_reserve (#0) = changes
  every reload (deterministic sequence; looks random with reserve in rounds). AK sound
  events WeaponMove1/2/3 pick movement1/2/3.vsnd at random: identify clips by timing.
- 2026-10-31 CLIP EVENTS (VERIFIED IN GAME, AK, `.claude/scratch/anim/fxclip.cs`):
  muzzle flash and shell eject are CNmParticleEvents in the SHOOT CLIPS (shoot1_ak:
  `uweapon_muzflsh_ak47_fps.vpcf` tag muzzleflash, config game; shell
  `weapon_shell_casing_rifle.vpcf` config shell_eject) - why vdata
  m_szMuzzleFlashParticle does nothing. Swapping m_hParticleSystem (+ RERL entry) to
  explosions_fx/explosion_basic.vpcf -> explosion at the muzzle every shot. A
  CNmSoundEvent added at frame 0 plays every shot alongside the vdata gunshot (a
  per-animation gunshot; for dupes the vdata gunshot is the parent's). Weapon Lab's
  Anim Events can already edit the particle path; adding events it cannot.
- 2026-10-31 CUSTOM SOUNDS (VERIFIED IN GAME): a generated .wav in
  `content/.../sounds/weaponlab/testshot.wav` -> testshot.vsnd_c (tools mode compiles it
  on save), event in `soundevents/soundevents_addon.vsndevts` (name must stay; KV3;
  `base = "Weapon_AK47.Single"` + `vsnd_files_track_01`) -> plays from a clip sound
  event AND as vdata WEAPON_SOUND_SINGLE on a real block (M4A4).
- 2026-10-31 MORE VDATA (in game): WEAPONSILENCER_DETACHABLE on the AK = spawns
  silenced, shots use m_aShootSounds WEAPON_SOUND_SPECIAL1 (set Weapon_M4A1.Silenced);
  toggling could NOT be tested (attack2 by console, mouse click and bound key all
  failed to detach even the stock M4A1-S; the same bound key zooms the AWP).
  Zoom: AK and P90 with m_nZoomLevels 1 or 2 (+FOV/times) did NOT zoom while the AWP
  did with the same key -> the 10-28 "P90 2-level scope works" note looks wrong;
  scopes seem class-gated. Iron sights: graph check #19 weapon_is_using_ironsights =
  weapon_type in {sg556, aug} (renamable per variation), IronsightPose #205,
  ShootIronsight #34/#44; untestable without zoom. HE m_flThrowVelocity 2500 = no
  effect (still 750). Taser m_iMaxClip1 5 = 5 shots (removed when empty even with
  m_iFlags deleted). Dupe weapon_ar2 (subclass_change): own clipmax 50, HUD name/icon
  "AK-47" and AK gunshot from the parent. cl_prefer_lefthanded true: no flip, even
  after respawn. Kill feed of a dupe not seen (wl_bot sets 100000 HP).
- 2026-10-31 CS_SCRIPT (VERIFIED IN GAME): only ONE callback per event ("Callback
  OnBulletImpact already registered" for a second one). `wl_fx explosive` (OnBulletImpact
  -> SpawnGrenadeProjectile HE + Detonate: 79 dmg at the hit, splash), `launcher`
  (OnGunFire -> HE with thrower = me, eye + forward*1600: bounces, detonates, damages),
  `lifesteal` (OnPlayerDamage -> SetHealth: 40 -> 72 after 32 dmg) all work.
- 2026-10-31 AK AIM DOWN SIGHTS (VERIFIED IN GAME): the AUG/SG553 skeletons have no
  ADS bones; ADS = the gun graph's Idle Blend1D (#203) blending idle -> IronsightPose
  (#205, AnimationPoseNode, frame 0 of its clip) by weapon_ironsight_amount, only when
  weapon_is_using_ironsights (#18 = #19 weapon_type in {sg556, aug} AND #20 amount
  >= 0.1). AUG's ironsight_fidget_aug = idle with wpn moved (16.06,-3.81,-1.9) ->
  (17.42,-0.17,-1.89), square to the view: the camera is the viewmodel origin looking
  +X, so the scope line goes through (0,0,0). Zoom/ironsight is class-gated (AK block
  with zoom keys did not zoom), so: AUG block with m_szWorldModel = AK model,
  m_szAnimSkeleton ak47, m_szAnimClass weapon_ak47 (AUG zoom/ironsight keys kept) +
  AK variation edits `19:m_comparisionIDs[1]` = weapon_ak47, `205:clip`/`34:clip`/
  `44:clip` = an AK ironsight pose clip -> attack2 zooms, the AK centres with rear
  notch + front post on the crosshair (about 2 deg low; tune with WL_ADS_DROP), fires
  in ADS, attack2 again returns. Pose clip: `.claude/scratch/anim/ads_pose.py` (AK
  front sight top z 1.54 / centre y 0.21 in weapon space from the preview mesh; wpn
  target (x+1.4, -0.21, -1.54)). BLENDER AXES: root_motion is CS2 space, its children
  carry pose_io.CONVERSION (CS2 X->Blender Z, Y->X, Z->Y); CS2 wpn = CONVERSION^-1 @
  wpn.matrix when root is identity. HUD/stats are the AUG's (its own block).
- 2026-10-31 OVERHEATED BARREL, NO SCRIPT (VERIFIED IN GAME on de_dust2): CS2's own
  CNmMaterialAttributeEvent (draw_c4: m_target Weapon, m_attributeName c4_light,
  m_x spline over the event) drives material DynamicParams (weapon_c4.vmat:
  g_flSelfIllumBrightness = `return exists(c4_light) ? c4_light.x*c4_light.x*12-6 :
  0;`, Scale = `c4_light.x`; m_renderAttributesUsed). Copied: AK material override
  (csgo_weapon.vfx, F_SELF_ILLUM 1, albedo factor 0, tint [1 .32 .05], a barrel
  TextureSelfIllumMask painted in UV space for triangles at weapon x > 12, DynamicParams
  reading `wl_heat`) + a CNmMaterialAttributeEvent `wl_heat` 1 -> 0 over shoot1_ak ->
  the front sight glows orange right after full-auto fire and cools in ~2 s. Tools:
  `anim/matextract.cs` (expr = VfxEval decompile; extract = VRF MaterialExtract incl.
  textures as PNG), `anim/heat_mask.py`, `fxclip.cs material:<attr>=<v0>,<v1>`, kept
  files `anim/keep_ads_heat/`. vmat source: "Compiled Textures" blocks and .vtex
  texture paths do NOT compile (default textures / error); textures must be sources.
  Untested: accumulating heat (each event sets an absolute value), the wpn barrel off
  screen (only the front sight was in view).
- 2026-10-31 ADS + HEAT THROUGH THE AGENT HOOKUP (VERIFIED IN GAME, no stock-path root
  graph, custom agents set by the map script, on lit de_dust2): ADS zoom + AK sight
  picture as before; new `ironsight_shoot_ak` = stock shoot1_ak with idle's sight
  correction on root_motion (recoil ~3.7 units back, settles on the sights, keeps the
  muzzle flash + shell events; built by `anim/ads_clips.py`, which resets idle/shoot
  to stock first via bpy.ops.weaponlab.reset_clip). Heat: front sight + barrel tip
  solid orange right after a burst, gone ~0.3 s game time later (x*x*10-4 is > 0 only
  for the first ~0.28 s of the 0.77 s clip; the glow can never outlast the shoot
  clip). In ADS the glow is gone before the recoil settles, so not seen there.
- 2026-10-31 ANIM EVENTS THROUGH THE AGENT HOOKUP (VERIFIED IN GAME, real weapon_ak47, lit
  dust2 + wl_script): server.dll hard-codes `animation/graphs/viewmodel/viewmodel.vnmgraph`,
  and the gameplay side (server AND client prediction) runs that root graph, never the
  agent's hudmodel graph. So gameplay IDs in agent-graph clips do nothing: ADD_AMMO moved
  to frame 5 -> server refill still 1.109-1.234 s and the HUD stayed at the old count
  until then; WPN_HEALTHSHOT_INJECT per shot -> HP stayed 50; legacy
  AE_WEAPON_PERFORM_ATTACK (the revolver's fire event) in draw -> no shot. What works from
  the agent graph = what the visible viewmodel shows: sound events, particle events, material
  attribute events. A particle event lives only while its clip plays (molotov flame on
  muzzle_flash during draw lit the gun, gone at idle). WPN_DROP_MAG in shoot / moved in
  reload: no first-person mag seen either way (inconclusive). Catalogue (`anim/eventcatalog.cs`,
  710 viewmodel clips): CNmIDEvent 904, CNmSoundEvent 2119, CNmParticleEvent 109,
  CNmMaterialAttributeEvent 15 (c4_light, c4_digits), CNmLegacyEvent 4
  (AE_WEAPON_PERFORM_ATTACK revolver, AE_CL_BODYGROUP_SET_TO_CLIP m249). Gameplay-ish IDs:
  WPN_RELOAD_ADD_AMMO, WPN_DROP_MAG, WPN_RELOAD_SILENT, shell-reload INTRO/LOOP/OUTRO/
  STAGE_END/1-3SHELLS/MAX_LOOP, WPN_SILENCER_ATTACH/DETACH, WPN_SHOW_SILENCER/BULLETS,
  WPN_HEALTHSHOT_INJECT, WPN_BOMB_STAGE (m_secondaryID 0..n), WPN_BLOCK_INSPECT,
  WPN_DISABLE_LEFT_HAND_IK, inspect INTRO/LOOP/OUTRO + USE_DRAW/FIXUP_VERSION. Particle
  events: m_relevance ClientOnly/ClientAndServer, attachment + PATTACH_POINT_FOLLOW, tags
  `silenced`/`unsilenced` filter muzzle flashes. Every gun graph has empty SilencerAttach
  (#223) / SilencerDetach (#54) clip nodes. `fxclip.cs` now also does id:/legacy:/move:/
  del:/pfx:. Gameplay-ID tests need the root-path graph (local only, against the test rule)
  - asked the user.
- 2026-11-07 PROP BONES (VERIFIED IN GAME, details in "For PC1" 2026-11-07): tracks apply
  on the MODEL's hierarchy; a model-root bone is placed from the viewmodel/arms origin
  (camera space), not the gun. Knife viewmodels come from the econ item (cannot show a
  custom ursus model offline here). Model DMX by hand works: `skeltest_model.ps1`.
- 2026-11-03 "PLAYS LIKE" (VERIFIED IN GAME, details in "For PC1" 2026-11-03): a class a
  root-graph group does not list plays like the group's LAST-HELD gun (sticky), not its
  default; listed classes are deterministic; the scope view needs WEAPONTYPE_SNIPER_RIFLE
  (server zoom state toggles on any type); routing in viewmodel_custom moves visuals only.
- 2026-10-31 GAMEPLAY ANIM-EVENT IDs, ROOT COPY, LOCAL ONLY (VERIFIED IN GAME, dust2 +
  wl_script, Weapon Lab 0.18.2 `deliver --cs2-root-copy true`, rebuilt on PC2; the copy
  stayed byte-equal to viewmodel_custom through later pushes; rollback removed it):
  * Root copy confirmed: AK reload ADD_AMMO at frame 5 -> server refill 0.172 s.
  * ADD_AMMO always takes ONE reserve "unit" and fills the clip: in the AK shoot clip
    with reserve as magazines -> the first 3 shots each ate a whole reserve mag (clip
    stayed full, reserve 3->0); with `m_bReserveAmmoAsClips false` + reserve 90 ->
    BELT-FED: clip stays 30, reserve drains 1 per shot. In inspect clips -> inspect
    reloads the gun (and burns a mag even when the clip is already full).
  * Silencer: CS2 client setting `cl_silencer_mode` (0 = cannot detach, the default
    here) blocked every detach so far, incl. the stock M4A1-S ("Removal of the silencer
    is disabled" HUD hint). With 1: AK + vdata WEAPONSILENCER_DETACHABLE + clips in its
    empty SilencerDetach #54 / SilencerAttach #223 nodes (draw_ak + WPN_SILENCER_DETACH /
    ATTACH at frame 15) -> attack2 toggles an AK silencer; WPN_SILENCER_DETACH in the
    reload took it off, WPN_SILENCER_ATTACH in the inspect put it back. Silencer IDs
    work from ANY clip. Setting put back to 0 after.
  * WPN_HEALTHSHOT_INJECT x3 in inject_healthshot (root graph #386) -> one syringe
    healed 10 -> 100 (stock +50), heals stack over time. In the AK shoot clip: nothing
    (healthshot class only).
  * AE_WEAPON_PERFORM_ATTACK: AK draw -> no shot; revolver inspect -> no shot (only in
    the revolver's hammer charge).
  * C4 plant (root #412) with all 15 WPN_BOMB_STAGE removed: INCONCLUSIVE, the harness
    could not plant even with the stock clip (VConsole +attack on A site drops the C4).
- 2026-10-31 ADS BLUR = CS2's AIMSIGHT SCOPE EFFECT (VERIFIED IN GAME, user: "blurry, DOF
  when I ADS"): the AUG/SG553 taxonomy tag `aimsight` turns on the client iron-sight
  system (FOV zoom, weapon_ironsight_amount pose, blur). Without the tag the server
  still toggles scoped=true but nothing happens client-side. `r_dof_override` does not
  touch the blur. Mechanism (client.dll "aimsight lens mask", "IronSight: Blur X/Y"):
  geometry whose material has the attribute `CSGO_Material_Flag_Stencil_Mask 1` (the
  AUG's shared_scope_lens.vmat; opacity `pow(1-$ent_ironsight,.5)`, so invisible in
  ADS) is the lens. Everywhere outside a lens is blurred; inside a lens CS2 draws a
  FIXED screen-centred scope picture (black square ~47% of screen width with a
  circular window, the scope dot, a tint/edge glow) and leaves the rest sharp. The
  square does not follow lens size/shape/UVs, cl_ironsight_dot_scale (dot only),
  m_flIronSightPivotForward or m_flIronSightFOV. User's idea tested: an AK model copy
  with an invisible 40x40 lens quad (`content/.../weaponlab/ak47_ads/`: VRF
  ModelExtract vmdl + DMX, `ads_lens.dmx` skinned to weapon_offset, `ads_lens.vmat`
  translucent opacity 0 + the attribute; compile `resourcecompiler -nop4 -f -i
  <vmdl>`, -f needed when only a DMX changed) -> periphery sharp, centre = scope
  picture. A hidden tiny lens -> blur everywhere again. So no clean blur-free iron
  sights through the AUG class yet. Lab put back to p_aug_as_ak (stock AK model).
  Model-space note: AK mesh = weapon-bone space + (2.97, -0.19, 2.98); ADS camera at
  model (-13.2, 0.0, 4.5) looking +X.
- 2026-10-31 LIT TEST MAP WITHOUT BAKING (VERIFIED): magenta = a map with no baked
  lighting (wl_vdlab: lighting off on the GTX 1080, plus 2 unbaked
  env_combined_light_probe_volume + cubemap). `mat_fullbright 1` shows albedo but
  hides self-illum entirely (forced-on glow invisible), so useless for material tests.
  User's tip: `spawn_group_load <map>` (cheat) loads a map's entities on top of the
  running one. Loading wl_vdlab onto de_dust2 runs its script but turns dust2 magenta
  too (its probe volumes take over). Fix: `maps/wl_script.vmap` = only root + world +
  the point_script (made with `game/bin/win64/dmxconvert.exe -oe keyvalues2`, then
  keeping those blocks, emptying visibility nodes / nodeInstanceData); compiled with
  `resourcecompiler -fshallow -nop4 -nompi -i <vmap> -world -phys -vis` (no bake, 13 KB
  vpk). `map de_dust2` + `exec wl_dust2` -> dust2 lit, script loaded, OnPlayerReset
  sets agents + kit after the cfg's mp_restartgame. A `;` inside `echo` in a cfg
  splits the command. The loading card can stay on screen until the window is focused.
- 2026-10-31 SUBCLASS WITH ITS OWN ANIM CLASS (VERIFIED IN GAME, wl_probe clip watch =
  tick-accurate refill time): weapon_ak47_b = copy of weapon_ak47 with m_szAnimClass
  weapon_famas. subclass_change back and forth on one AK: AK -> refill 1.109 s + AK
  sounds; _b -> 1.672 s + FAMAS sounds (FAMAS ADD_AMMO 1.67 s); back -> 1.109 s;
  survives redraws. Both the client animation and the gameplay reload follow the anim
  class (unlike the agent swap). It does not change which root graph the server runs.
- 2026-10-31 WEAPON LAB 0.18.x on PC2: core tests 106 pass, 1 skipped, 0 fail
  (`dotnet test tests\WeaponLab.Core.Tests`; MTP rejects `--logger`). Quirk: a graph
  `clip` edit naming the STOCK path of a clip Weapon Lab already delivered as custom
  stays stock when that delivery has no pending clip changes (no redirect).

## For PC1

Results and requests for the main PC. PC1 copies what it uses into its own
memory or plan; it does not edit this file.

- 2026-11-07 ANSWERS: CUSTOM SKELETON / PROP TESTS (VERIFIED IN GAME on PC2: lab addon,
  de_dust2 + wl_script, custom agents, no root copy; Weapon Lab 0.22.5 built from source).
  Setup: test models written as keyvalues2 DMX (`scratch/anim/skeltest_model.ps1`): gun box
  on `weapon` (bind 0,0,0.5), orange cube on `prop` 6 units to +Y, optional green cube on
  `extra`; M1 = prop under weapon, M2 = prop a model root, M1x = M1 + extra (not in the
  skeleton); all with NmSkeletonReference to the custom skeleton. Skeleton made once from
  M1 (--model-hierarchy); tracks written as samples (`scratch/anim/proptrack.cs`) and
  exported with `export-clip` (only prop changed, error <= 0.006 deg).
  SLOT: knife_ursus could not be shown on PC2. A knife's first-person model and anims come
  from the ECON ITEM: GiveNamedItem and a bound `give weapon_knife_ursus` both give the
  default knife; ChangeSubclass makes the server say weapon_knife_ursus with the vdata
  model, but the viewmodel stays the default knife. So the tests ran on rifle_famas
  (untouched slot; a gun's first-person model follows vdata m_szWorldModel).
  | model | track | clip | where the prop is | follows the gun |
  | M1 | R rest | idle, lookat01 | beside the gun, 6 to its left | yes |
  | M1 | F (20,0,0) | idle | at the barrel tip, 20 along weapon +X | yes |
  | M1 | S | lookat01 | holds its idle place on screen while the gun turns | no (as meant) |
  | M2 | R rest | idle, lookat01 | out of view; a 60-unit cube encloses the CAMERA | no |
  | M2 | F (20,0,0) | idle | screen centre, 20 straight ahead of the camera (~1 low) | no |
  | M2 | S | lookat01 | out of view (its values stay near the camera) | - |
  | M1x extra | none | idle, lookat01 | at its model bind, 6 right of the gun | yes |
  | M2 + vdata m_szAnimSkeleton = custom | F | idle | same as with the stock skeleton | - |
  1. Hypothesis CONFIRMED: CS2 applies each track parent-local on the MODEL's hierarchy.
     M1 behaves as Weapon Lab intends (R on the gun, S still on screen); M2 does not.
  2. M2's frame is the viewmodel/arms origin (camera space: +X forward, +Y left, +Z up),
     not the model origin at wpn. root_motion never moves in these clips, so arms origin vs
     viewmodel origin is not separable. It explains the user's lighter: 0.22.4's S track
     applied from the camera = inv(wpn(t)) x H -> near the camera, moving against the gun.
  3. Off-origin `weapon` bind: no large shift seen; 0.5 units is too small to judge by eye
     (M2's F cube sat ~1 unit under the crosshair, likely the viewmodel offset). UNTESTED
     precisely.
  4. A model bone the skeleton lacks gets no track: it sits at its model bind under its
     model parent and follows the gun.
  5. Two-root skeleton: NOT TESTED. 6. m_szAnimSkeleton stock vs custom: no difference.
     "Do Not Discard Skeleton Bones": NOT TESTED (ModelDoc dropped the unweighted
     weapon_offset/econ/... from the compiled models; skeleton create kept them from the
     weapon skeleton). K (+10 Y) replaced by F (a fixed 20,0,0): in M2 K would sit off-screen
     beside the camera, F shows the frame on screen.
  For Weapon Lab: (a) warn when a model bone's parent differs from the skeleton's (root in
  the model, under weapon in the skeleton = track lands in camera space); (b) two working
  ways to hold a prop still: keep it under `weapon` in the model and write S (0.22.4), or
  keep it a model root and write its place in camera space as a CONSTANT track (verified
  with F: fixed on screen, nothing to compensate); (c) the user's bong: parent
  lighter_offset under weapon in the .vmdl/FBX and re-export, or keep it root and export
  its camera-space place. Also: delivery needs the source model to target a skeleton
  ("targets no animation skeleton" otherwise), so compile it with the NmSkeletonReference.

- 2026-11-03 ANSWERS: "PLAYS LIKE" TESTS (VERIFIED IN GAME on PC2: lab addon, de_dust2 +
  wl_script, custom agents, NO root copy; gameplay read on the server = refill time from
  the wl_probe clip watch, plus the reload sounds; harness `scratch/vdtool/playslike.ps1`).
  0. KEY FINDING: a class the group does not list does NOT get the group default. It
     plays like the gun of that group the player held LAST (gameplay and visuals); the
     default only holds until another gun of the group is used. Glock with a made-up
     class: after a USP -> 0.906 s + USP sounds, after a Deagle -> 0.781 s + Deagle
     sounds (flipped 3 times). Your user's Annabelle block (SHOTGUN + weapon_annabelle):
     after a Nova -> Nova shells; after a MAG-7 -> MAG-7 timing (1.078 s) and ONE shell
     per reload (single shells + the MAG-7's single ADD_AMMO); after a Sawed-Off ->
     Sawed-Off sounds. A LISTED class is deterministic (SSG08 with weapon_deagle stays
     Deagle after a USP). So "plays like" via an unlisted class is only "plays like the
     last <group> gun"; worth a warning, or prefer a listed class (and replace that
     gun's visuals).
  1. Pistol-group fallback: P250 + anim weapon_ak47 fires and reloads normally (4/4),
     refill 0.906 s = USP's (stock P250 clip: 0.933 s), USP reload sounds. Own visuals:
     YES via viewmodel_custom only ("59:" and "79:m_comparisionIDs[1]" = weapon_ak47,
     the Deagle state): the P250 showed the lab Deagle's sideways idle and played Deagle
     reload sounds while the real USP kept USP sounds and visuals. Gameplay stayed
     USP-like (0.906 s) while the visuals run on Deagle timing (0.767 s): a timing
     mismatch to surface.
  2. PISTOL type on a scoped gun: SSG08 + WEAPONTYPE_PISTOL + weapon_deagle plays like the
     Deagle (refill 0.781 s, Deagle sounds, 3/3 shots, game reports type 1). Zoom: the
     server still toggles scoped=true, but the CLIENT shows nothing (no FOV change, no
     scope overlay, viewmodel not hidden). Same for the AWP with WEAPONTYPE_SHOTGUN;
     control SCAR-20 (sniper type) zooms with the overlay. So the scope view needs
     WEAPONTYPE_SNIPER_RIFLE (the Annabelle on SHOTGUN type has no scope view). Untested:
     buy-menu placement, scoped accuracy with the invisible zoom, movement (max speed is
     per block in vdata).
  3. Made-up class breaking fire: NOT REPRODUCED on PC2. weapon_wlmadeup on Glock,
     Galil, MP9, XM1014 and AWP(SHOTGUN + weapon_annabelle) all fire and reload. The
     closest copy of your test B also works: the user's weapon_ssg08 block imported from
     the handoff vdata_c, SHOTGUN + weapon_annabelle, the Annabelle model + its
     weaponlab_rifle_ssg08 skeleton (handoff files), your routing edits #294/#442 (shotgun
     classes), #366/#448 (sniper classes + annabelle), #351/#359 (+annabelle) on
     viewmodel_custom -> 3/3 shots, empty reload shell by shell on Nova timing, 3/3 again.
     Not covered here: cs_hl_template itself (its map scripts, its custom SSG08 variation
     and loop clips, the rest of its vdata) and session history (class changed without a
     full restart?). Suggest re-running B on PC1 after a full CS2 restart, holding a Nova
     first. Graph facts: the root WPN SM picks the group by weapon_category (#294 etc.);
     Pistols SM default state index 1 = USP (Revolver is 0).

- PC2 results from before the split (2026-10-28/29: animation pipeline test,
  VData limits, more vdata keys) are already in `.claude/MEMORY.md`. The
  2026-10-29 AK empty/third-reload and random-reload results are under Findings.
- PLAN FOR PC1, ANNABELLE SHELL-BY-SHELL RELOAD (written 2026-10-31 on PC2; the
  user will start it on PC1 "when ready"; do not start unasked). Addon
  cs_hl_template, weapon_ssg08 block, sandbox rifle_ssg08 with the Annabelle
  custom skeleton. Why each step: see Findings above.
  1. VData (VData tab, weapon_ssg08): keep m_WeaponType WEAPONTYPE_SNIPER_RIFLE
     (SHOTGUN routes to the Nova slot); set m_bReserveAmmoAsClips false (the
     endless-loop cause); set m_nPrimaryReserveAmmoMax in ROUNDS (ask the user; the
     inherited 2 would mean 2 rounds); keep m_bReloadsSingleShells true, taxonomy
     reloads_per_shell (+ shotgun), m_iMaxClip1 and disallow 0.466667 as the user
     has them. Check m_szWorldModel points at the delivered Annabelle copy (the
     2026-10-31 static-bones issue) unless the user already fixed it. Save + compile.
  2. Animation (user, Blender, rifle_ssg08 sandbox): reload_ssg08 keeps its template
     length (112 frames, 3.7 s; reload_silent_ssg08 223 frames at half speed).
     Animate intro (open action) -> ONE clean shell insert meant to repeat -> outro
     (close action). Note the loop's start and end frames: the pose at the end must
     match the start (offer a pose-seam check), and the shell must count (ADD_AMMO)
     at least 4 frames before the loop end. Same for reload_silent_ssg08 (frames
     doubled) if the user wants it; _lgcy clips only if they use the legacy model.
  3. Export from Blender (Export All). Any later export or Anim Events save of a
     reload clip DROPS the loop events, so step 4 always comes last.
  4. Add the loop structure to the exported reload clips (recipe under Findings:
     INTRO/LOOP/OUTRO sync sections + ID events, ADD_AMMO moved inside the loop
     >= 4 frames before its end, WPN_RELOAD_SILENT on the normal reload, WPN_DROP_MAG
     removed); verify poses, sounds and all other data unchanged. Weapon Lab cannot
     add events yet (EventPatcher edits existing ones only): use a scratch step like
     PC1's earlier probe / PC2's shellclip.cs, or build the feature first if the user
     prefers. The hand-made exports get the "ReplacedInAddon" note handling PC1
     noted 2026-10-31.
  5. Deliver to cs_hl_template (backup kept); the user restarts CS2 fully.
  6. User test in game (`snd_showstart 1` shows each pass): partial reload from 1-2
     rounds, empty reload, fire mid-reload (must stop the reload), reserve running out
     mid-reload, no extra empty pass at the end, smooth loop seam. PC2 can repeat
     the logic tests in the VDATA LAB with the user's clips if they are copied over.
- 2026-10-31 ANSWER to "Annabelle shell reload still loops" (details under Findings,
  "ANNABELLE LOOP = HOOKUP"): with your exact handoff files and the user's exact
  weapon_ssg08 block, the reload WORKS in PC2's lab when CS2's own root graph is
  overridden by the custom one, and LOOPS FOREVER (0/3 -> 1 round, then a pass every
  0.30 s) when the custom graph is reached only through a player model's hudmodel ref.
  So the first change that makes it end is the hookup, not the model, skeleton, clips
  or vdata. Please check how cs_hl_template reaches viewmodel_custom (player models'
  AnimGraph2 hudmodel / script SetModel vs a stock-path root override). Fix options:
  the stock-path root override (works locally; whether it reaches Workshop players is
  untested and Weapon Lab's delivery warns it cannot), or find a way to make the
  gameplay side use the agent's graph (untested: setting the agent before spawn or
  through the loadout). Still unexplained: the user's 2/3 loop (PC2's agent-only 2/3
  ended). The handoff files were removed from the lab afterwards; the handoff folder
  in git was not touched.
- 2026-10-31 more results that touch Weapon Lab (details under Findings): AK ADS via an
  AUG-class block + an AK ironsight pose clip; barrel heat via a material attribute
  event + a DynamicParams material; subclass anim classes switch animation sets with
  the gameplay side following. Idea for the Annabelle hookup (untested): instead of
  overriding the root graph, override the SSG08's STOCK clip paths in the addon so the
  stock root/variation (which the server runs) plays the custom clips; whether stock-
  path overrides reach Workshop players is the same open question for both.
- 2026-10-31 REQUEST (user: "ask pc1 to compile it"): please bake lighting for PC2's
  lab map `wl_vdlab` on PC1's RTX 3090 (the GTX 1080 cannot run vrad3; expected).
  Unbaked, it renders magenta and hides material effects such as the barrel glow.
  PC2 will hand over the source `wl_vdlab.vmap` (user-built in Hammer, uses dev
  materials + `maps/scripts/wl_probe.vjs`) once the user approves the transfer. On
  PC1: put it in an addon's `content/.../maps/`, build in Hammer with lighting on
  (any quality), and send back `game/.../maps/wl_vdlab.vpk` (+ any `wl_vdlab/` folder
  it writes). Until then PC2 tests on lit de_dust2 + the script-only wl_script map.
- 2026-11-01 ORB BATTLES MOVED OUT (user: new repo, then clean up Weapon Lab): thanks
  for the references. PC2 copied `orb-battles-from-pc1-20261101/` into the new repo
  `github.com/Froddoyo/orb_battles` at `reference/pc1-20261101/` (all 115 sha256 OK), then
  removed both orb handoff folders from this repo at the user's request. Your
  `.claude/MEMORY.md` entry "ORB BATTLES REFERENCES READY" now points at a removed folder;
  please drop or repoint it. Further Orb Battles work goes through the orb_battles repo.
- Feature ideas for Weapon Lab (user has not decided): (1) warn when single-shell
  reloads are on while m_bReserveAmmoAsClips is true; (2) a "shell reload" action in
  Anim Events that writes the loop structure from chosen loop frames (pose-seam hint,
  ADD_AMMO placement check) and survives later Blender re-exports. PC2's scratch
  `shellclip.cs` (seam + build, verified poses/sounds/other data unchanged) is a
  working reference; it is not in git.
