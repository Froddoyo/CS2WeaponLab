# CS2 Weapon Lab: memory

Last updated: 2026-11-07 (America/Edmonton).

## Status

- 2026-10-23: Revisions pushed by the user: `2488b3b` (0.1.0 skeleton,
  discovery, catalog), `814021c` (0.2.0 clip/event decoding, sandboxes),
  `5eaff4b` (workspace in repo). User tested 0.1.0 and confirmed it works.
- 2026-10-23: Revision 3 (0.3.0), uncommitted. Core: `Clips/ClipTemplate`
  (pose streams, `ReadPoses`), `BonePose`, `SkeletonInfo`, `ClipWriter`
  (template-preserving writer), `EncoderSelfTest`, `Clips/Encoding/`
  (`TrackEncoder`, `ResourceSplicer`, `KVFingerprint`, `KVValues`). CLI:
  `poses`, `verify-encoder`. App restyled after Hammer 5 Tools (see UI
  direction). 37 tests pass incl. game-backed golden tests. Verified:
  `verify-encoder` passes all 674 clips in ~5 s (worst re-encode error
  0.000064 units, 0.0034°); every stream has one track per skeleton bone;
  app driven via UI Automation + mouse: connect, tree, clip details,
  Tracks tab, Verify encoder all worked. Nothing has been tested in game.
- 2026-10-23: User feedback on 0.3.0: "good stuff so far"; asked for
  glowing next-step buttons and the Blender file creator.
- 2026-10-23: Revision 4 (0.4.0), uncommitted (0.3.0 also uncommitted).
  Glow: `Button.suggested` style in `App.axaml`; `UpdateSuggestion()` picks
  Connect → Create sandbox → Build .blend → Open in Blender. Blender builder:
  Core `Blender/` (`RigPackage` writes `sandbox/blender/input/rig.json` +
  `poses/*.json.gz` + `previews/*.glb`; `PreviewExporter`; `BlenderLocator`;
  `BlendBuilder` runs `blender/weaponlab_bridge/build_weapon.py` headless and
  parses `WEAPONLAB_PROGRESS/RESULT/ERROR` lines), CLI `blender
  locate|build|open`, app buttons. 49 tests pass. Verified: all 66 hand-rig
  weapons build (≈4 s each, ≈27 MB per sandbox incl. .blend and previews),
  every weapon with a weapon rig gets its mesh; rig poses match engine poses
  (AK/karambit/deagle/C4 ≤0.00001 units, 0°); workbench render of reload_ak
  frame 18 looked right; app flow driven by clicks on a scratch copy.
  Not done: export calibration/reference sampling (CS2AL baked it at build).
- 2026-10-23: 0.3.0 + 0.4.0 committed as `efa78f8` (not yet pushed when
  noted). User opened a built .blend successfully.
- 2026-10-23: Revision 5 (0.5.0), uncommitted. User decisions: export to
  the sandbox only this revision (delivery next); add-on calls the CLI (no
  live link); keep bone presets, hand follow, reset to stock, editing setup;
  also "Save Clip As" to any file. Built: `blender/weaponlab_bridge/`
  add-on (`__init__.py` panel/operators, `pose_io.py` shared with
  `build_weapon.py`, `launch.py` session loader); build now samples
  `input/reference/<clip>.json.gz`; Core `Export/` (`EditDeltas` port of
  CS2AL delta logic, `PoseFile`, `ClipExporter` → `exports/<resource path>`
  + `exports/<clip>.export.json`); CLI `export-clip`, `dump-poses`; app
  shows export status; App exe renamed `CS2WeaponLab.exe` (Windows
  case-insensitive clash with `weaponlab.exe`), App references Cli so
  weaponlab.exe ships beside it. 54 tests pass. Verified headless: no-op
  export byte-identical; 10° arm_lower_L edit → only that bone changed,
  decoded 9.9993°, encode error 0.0043°; reset → identical again. Panel
  checked in Blender UI. Unverified: user posing by hand; in game.
- 2026-10-23: First user test hit "No reference sample": they opened their
  pre-0.5 .blend (Open in Blender glowed) and saved it; it held no keyed
  edits (likely posed without auto-key). Fixes: `BlendBuilder.NeedsUpdate` +
  "Update for export" (references-only throwaway build, .blend untouched;
  CLI `blender update`), full rebuilds back up to `blender/backups/`, panel
  warns when references are missing, export refuses unkeyed pose changes.
  Verified on a copy of the user's sandbox. The user still has to click
  Update for export on their real rifle_ak (left untouched by me).
- 2026-10-23: User then rebuilt and exported idle_ak (armUpperShoulder_L)
  successfully; feedback: plugin lacks features, export "doesn't do
  anything" visible, start the VData editor.
- 2026-10-23: Revision 6 (0.6.0), uncommitted (0.5.0 also uncommitted).
  Add-on: markers named `#<index> <Kind>: <name>` (old blends migrated on
  open), events box (jump, change sound, Reset Events), sound retime by
  dragging markers, gameplay markers read-only, Export All Clips,
  player/free view toggle. Core: `EventPatcher` (sound retime/rename,
  verified), `ClipEdit.Events`, report `ChangedEvents`. App: FileSystemWatcher
  on `*.export.json` → History + Exports tab + "edited" tree marker; VData
  tool tab (blocks, inheritance via `_base`, addon diff, typed edits to
  `workspace/vdata/overrides.json`), weapon→block via `m_szAnimSkeleton`.
  CLI `vdata list|show`. 65 tests pass. All UI driven on a scratch copy.
  VRF 20.1 reads current weapons.vdata_c (old type-26 failure gone);
  compiled blocks store full values + `_base`. User's `weapon_template`
  addon vdata is an old 178-block prefab format (flagged in the UI).
- 2026-10-23: User chose CS2AL-style delivery (custom clip paths + cloned
  graphs, backups, vdata compiled into the addon).
- 2026-10-23: Revision 7 (0.7.0), uncommitted (0.5.0–0.7.0 all uncommitted).
  Core `Delivery/`: `ResourceId` (MurmurHash64B, 0 mismatches over 1,044
  shipped refs), `GraphTree` (CS2AL selection: graphs playing edited clips +
  child branches + parents, reachable from root, always clone root to
  viewmodel_custom), `GraphPatcher` (m_resources + RERL, verified),
  `ResourceSplicer.ReplaceBlocks` (16-byte aligned), `VDataCompiler` (stock
  BinaryKV3 + overrides → KV3 text via WriteText → `resourcecompiler -nop4
  -fshallow -i content/.../scripts/weapons.vdata` → verify), `AddonDelivery`
  (plan/deliver/rollback, journal + backups in workspace/deliveries, addon
  manifest `weaponlab_delivery.json` incl. vdata hashes, obsolete cleanup by
  hash). CLI `deliver`, `deliver rollback`. App Deliver tab (review list with
  orange warnings for foreign files, re-plans on Deliver, undo, history).
  71 tests pass. Verified with user permission in a temporary addon
  `weaponlab_selftest` (created, delivered AK idle + 2 vdata edits, compiled
  OK: 85 blocks, AK damage 40 Int32, M4 unchanged; graphs decoded with
  correct redirects; rollback → empty; obsolete vdata removal) and then
  deleted. weapon_template was only reviewed, never delivered to.
- Facts: 133 viewmodel graphs; AK clips are played by
  `viewmodel_gun.vnmgraph+ak47` and `viewmodel_inspects.vnmgraph+ak47`;
  80 agent models have hudmodel → viewmodel.vnmgraph (manual hookup remains).
  weapon_template has its own content scripts/weapons.vdata source.
- Next: user delivers to an addon and tests in game; automate the
  player-model hudmodel hookup if feasible.
- Known: the sidebar opens on Item; Blender ignores scripted
  `active_panel_category`, user clicks the Weapon Lab tab once.
- Incident 2026-10-23: I deleted unlocked files in `src/WeaponLab.App/bin`
  while the user's app was running (build output only; rebuilt after).
  Never clean bin/obj while the app runs; build to scratch with `-o` instead.
- 2026-10-24: Revision 8 (0.8.0), uncommitted (0.7.0 committed as a5dd53d).
  Profiles (`workspace/profiles/<name>.wlprofile.json` + `active.txt`): per
  weapon Custom/Default animations + preview model, vdata block→path→value,
  target addon; `VDataOverrides` now backed by the profile (old
  overrides.json still readable). `ProfileStatus`: outliner yellow
  (NeedsApply) / green (Applied) with reasons; the outliner selection drives
  every tab. `DeliveryPlanner` + rewritten `AddonDelivery`: Default removes
  a weapon's delivered files and resets its vdata; hash-mismatched delivered
  files are repaired, not refused; rollback refuses while a newer delivery is
  active. VData edits the addon's own vdata (create-from-CS2 prompt), writes
  the `content/.../scripts/weapons.vdata` source, compiles, verifies.
  `VDataGlossary`: friendly grouped names, tooltip = raw key - description;
  "Inherited (smg)" / "This weapon". Preview models per weapon; Automatic =
  target addon's `m_szWorldModel` (verified: cs_hl_template Galil → AR2).
  Add-on 0.8.0: Edit Arms / Edit Weapon, Auto Key Pose Edits toggle,
  Weapon hands follow hands, viewport hint to open the N panel. Rebuild asks
  before overwriting. 78 tests pass; selftest addon delivery verified then
  deleted. Not tested in game.
- Incident 2026-10-24: the independent probe check of compiled vdata did not
  run in the last selftest (probe deleted from scratch); internal
  `VerifyCompiled` passed. Keep a probe until a check has run.
- 2026-10-24: 0.8.0 committed (7dbce89). Revision 9 (0.9.0), uncommitted.
  VData class change: `VDataClassSwap` (profile `VDataClasses`: block → donor
  + parts; replaces whole top-level keys with CS2's donor block, removes keys
  the donor lacks; `m_szName` never copied; `_class` only via the opt-in
  "entity" part). Applied in `VDataCompiler.BuildText` before single values;
  verified after compile; manifest `VDataClasses` drives resets. Test
  confirms CZ75 ← Bizon text (compile of a swap not run yet).
  User's cs_hl_template CZ75 keeps `_class weapon_cz75a` but base smg, anim
  class/skeleton bizon, grenade-launcher model/sound (the motivating case).
- 2026-10-24: Graph editor. Format facts (verified): compiled vnmgraph DATA
  has `m_nodes` (each `_class` CNm*::CDefinition, links are int fields
  ending NodeIdx/NodeIndices), `m_nodePaths` (folder-like editor paths that
  define the levels), `m_resources`, state machines in `m_stateDefinitions`
  (global transitions are expanded per state). Weapon variations
  (`viewmodel_gun.vnmgraph+ak47`) share the default graph's nodes and differ
  in `m_nDataSlotIdx` → `m_resources`. Core `Graphs/`: `GraphDocument`,
  `GraphGlossary` (node/field names, editable rules, enum choices),
  `GraphEdits` (keys `node:path`, `node:clip` swaps/adds a clip resource +
  RERL), `GraphLayout` (flow layers; state machines on a circle),
  `GraphExplain` (plain-language conditions), `GraphStatus` (addon custom
  graph holds edits?). `GraphPatcher` applies edits and verifies every
  other node fingerprint-identical; all 133 viewmodel graphs patched with
  sample edits OK. Profile `Graphs`; delivery clones edited graphs + parents;
  graph owners = weapon(s) with most clips (AK graph borrows an M249 clip);
  Default skips a weapon's graph edits. Wiring (links, state indices) is
  read-only. 82 tests pass. Not tested in game.
- 2026-10-24: User feedback on 0.9: the addon must be the baseline (Profile
  column = addon values, edits coloured, no stomping manual work); prefers
  Source 2 Viewer's AG2 graph layout (whole graph, pins, typed wire colours,
  filters); skeletons (vmdl bones vs .vnmskel) come later, custom skeletons
  unproven (AR2 attempt with Codex never tested).
- Verified 2026-10-24: cs_hl_template already has CS2AL output at Weapon
  Lab's own paths and no Weapon Lab manifest: 8 AUG + 8 SCAR-20 clips under
  animation/anims/custom/, viewmodel_custom + gun/inspects custom variations
  for aug and scar20. Before 0.10 a delivery there would have rebuilt
  viewmodel_custom from stock and dropped that routing.
- 2026-10-24: Revision 10 (0.10.0), uncommitted. `AddonOriginals`
  (workspace/addons/<addon>/originals + index.json; recorded per manifest
  key before Weapon Lab first touches a file; files a pre-0.10 manifest
  owns count as Weapon Lab's) and a one-time `first-snapshot` of game
  animation/, scripts/ and content scripts/. Planner (rewritten around a
  Context): graphs patch the addon's own file when there is one (edits
  skipped if node counts differ), clips replacing addon files are noted,
  removals restore originals (`DeliveryFile.Restores`), vdata resets and
  class resets go to the addon's original values (`VDataKeySwap.FromOriginal`).
  `AddonInventory` maps addon clips/graphs to weapons and finds clip
  overrides by slot. UI: "addon" badge in outliner + weapon panel summary,
  Graph tab shows "addon: <clip> (custom)", VData Profile column baseline =
  addon original, yellow/green value states. Tests: 83 pass, incl. the
  cs_hl_template case on a fake addon (keeps AUG routing, Default restores
  byte-identical). Dry-run plan on the real cs_hl_template kept SCAR-20
  routing and 6 custom AUG clips (nothing written). Not tested in game.
- 2026-10-24: 0.10.0 committed (9c966d4). 0.10.1 (uncommitted), user
  feedback: no separate blue "addon" colour; the addon's own work (clips,
  graphs, vdata differing from CS2 outside Weapon Lab's values) counts as
  green "matches" since the profile starts from the addon
  (`AddonInventory.OwnVDataBlocks`). Selector boxes (editable ComboBox) for
  every bool/text value: `VDataChoices` (values used by stock + addon vdata,
  addon models for m_szWorldModel); graph inspector lists values the graph
  uses per field. Verified: cs_hl_template rebalances nearly every weapon
  (armor ratio 1, jump/move inaccuracy halved, prices, models) and has
  addon-only blocks weapon_sphield, weapon_hegrenade_sticky (not shown in
  VData yet).
- 2026-10-24: 0.10.2 (uncommitted), user feedback: outliner colours follow
  the open tab (`StatusAspect` Animations/Sounds/VData/Graph/All;
  `WeaponStatus.Part`), addon's own work counted per aspect (clips, graphs,
  vdata); Deliver shows everything. Exports can be deleted
  (`ClipExporter.DeleteExport`, Delete button per export row). 84 tests pass.
  cs_hl_template now also has custom Galil clips + graphs (added by the user
  after the first scan).
- Verified 2026-10-24 (skeleton pipeline): a .vnmskel is compiled from a KV3
  source (`m_sourceFilename` FBX/DMX, `m_rootBoneName`, `m_flGlobalScale`,
  `m_bIsAttachableProp`, `m_previewModelName`, masks, secondary skeletons);
  compiler id CompileNmSkeleton. Referenced by: model .vmdl `NmSkeletonList`
  → `m_vecNmSkeletonRefs`; every clip's `m_secondaryAnimations[].m_skeleton`
  (weapon tracks = skeleton bones; arms = viewmodel.vnmskel); vdata
  `m_szAnimSkeleton`; viewmodel.vnmskel `m_secondarySkeletons` (11 entries,
  Galil absent, apparently optional). Standalone resourcecompiler.exe has NO
  compiler for .vnmskel or .vnmclip sources ("Failed to find compiler") →
  build compiled files by patching (CS2AL did the same for clips).
  Codex's AR2 uses stock galil.vnmskel; its 5 extra model bones only follow
  parents.
- 2026-10-24: Custom skeleton experiment (user-approved temporary addon
  `weaponlab_skeltest`, game + content folders; cs_hl_template only read).
  Core: `SkeletonBuilder` (patch galil.vnmskel_c with new bones/poses),
  `ClipRetarget` (weapon secondary animation onto another skeleton,
  `TrackEncoder.EncodeFresh`), `ReferencePatcher` (swap resource refs in DATA
  + RERL, verified). Addon holds ar2_test.vnmskel_c (AR2's 13 bones), a
  patched copy of weapon_ar2.vmdl_c (+ particles; references resolve), all 6
  Galil clips at stock paths retargeted to 13 tracks (all four ar2_* bones
  spin once during lookat01/_transfix), and weapons.vdata (galil model →
  AR2, m_szAnimSkeleton → ar2_test; compiled + verified). All decode-checked.
  In-game test 1 (user, 2026-10-24): the game LOADS the patched custom
  skeleton, model and retargeted clips (AR2 renders and animates; reload is
  the vanilla Galil one, as built). One AR2 part floated in the sky. Cause:
  the AR2 model's extra bones have bind poses (inverse bind matrices) that
  do not match their parent-space values (ar2_chamber_cell bind at
  (-4.9, 15.5, 0)); the game leaves unanimated bones at bind, so Codex's
  setup looks right, but posing them from parent-space values shifted the
  part. Also econ/muzzle are stored turned 90° vs the Galil skeleton.
  Fix: `SkeletonBuilder.AlignTo(modelBones, bindPoses, animated)` keeps the
  animated skeleton's rest for shared bones and places extra bones exactly
  at the model's bind pose (gap 0); test motion changed to a 3-unit slide
  (bone pivots are far from their parts, so a spin would orbit). Rebuilt;
  AWAITING in-game test 2. Delete the addon afterwards.
- In-game test 2 (skeleton): weapon parts completely static (gun follows
  hands, no part moves); likely the game ignores a clip's weapon animation
  on the patched skeleton (fingerprint/hash or expected-secondary-skeleton
  check suspected). Unresolved; user switched to testing the plain pipeline.
- VERIFIED IN GAME 2026-10-24 (user + screenshots): Weapon Lab's real
  pipeline works. Obvious AK idle edit (wpn rolled 30°, clip -4 units) via
  ClipExporter → DeliveryPlanner/AddonDelivery into weaponlab_skeltest
  (custom clip + viewmodel_custom + gun/inspects custom ak47 graphs) played
  in CS2 Workshop Tools (`cs2.exe -tools -addon weaponlab_skeltest -novid
  +sv_cheats 1 +map de_dust2`). Hookup that worked: a copy of the delivered
  root graph at the STOCK path animation/graphs/viewmodel/viewmodel.vnmgraph_c
  inside the addon (addon files override game files; no player-model edits).
  cs_hl_template instead points its own agent models' hudmodel AnimGraph2 at
  viewmodel_custom.
  DECISION (user, 2026-10-24): stock-path overrides are TEST-ONLY. Workshop
  uploads get stock paths overwritten by CS2, so addons must ship their own
  graphs/skeletons/clips at custom paths, and the game reaches them through
  the addon's own player models (hudmodel → viewmodel_custom). Weapon Lab
  must assume custom player models and warn when it delivers custom graphs.
  Open question: weapons.vdata_c is also a stock path (weapon_template ships
  one) — check whether it survives Workshop. Scratch workspace .claude/scratch/ws holds this test's
  sandbox/profile. Test addon now holds only this delivery (AR2 files removed).
- 2026-10-24: Revision 11 (0.11.0), uncommitted. `AddonInventory.PlayerModels`
  (vmdl_c whose RERL mentions animation/graphs/, hudmodel from
  m_animGraph2Refs; cs_hl_template: 9 Combine/HEV models → viewmodel_custom).
  `AddonChecks`: Hookup warning (custom graphs but no hooked player model)
  and Skeletons warning (weapon model's m_vecNmSkeletonRefs must contain its
  vdata m_szAnimSkeleton; custom skeleton flagged experimental; found
  cs_hl_template weapon_knife_t crowbar targets knife_default_t, not
  knife_skeleton). Plan carries Warnings + HookedPlayerModels; Review lists
  them first; heads-up at first graph edit / model-or-skeleton vdata edit.
  `Kv3Text` (span-keeping KV3 parser/writer) + `VDataSourcePatcher`: VData
  saves edit content weapons.vdata in place (flags, float form, CRLF kept;
  missing keys written from compiled; swap keys replaced/removed; verified).
  "Save to addon source" vs "Save and compile"; failures open a dialog.
  Build .blend creates the sandbox and asks where to save
  (sandbox blender/location.txt); Create sandbox button removed. Blender
  preview: fixed 39.37 scale, bones follow the rig only where heads match
  (verified: ipistol 23.4 units). Deliver tab List/Tree view with action
  colours. 86 tests pass.
- VERIFIED IN GAME 2026-10-24 (user): graph edits work. AK graph idle clip
  box swapped to lookat01_ak (GraphEdits clip key, no clip edits) delivered
  to weaponlab_skeltest; the AK played the inspect while idle. Local hookup
  was the stock-path root copy (test-only; Workshop needs hooked player models).
- VERIFIED IN GAME 2026-10-24 (user): CUSTOM SKELETONS WORK. Test 3 on the
  proven route (custom clip paths + cloned graphs, local stock-path root
  copy as hookup): AK on weaponlab_ak47_copy.vnmskel (ak47 bones, new name,
  patched AK model copy at models/weapons/weaponlab/) animated normally;
  Galil slot on weaponlab_ar2.vnmskel (13 AR2 bones, AlignTo bind poses) +
  patched AR2 model: Galil-derived parts animate and the 4 extra ar2_* bones
  slide in the inspect. Skeletons built by SkeletonBuilder (patched galil/
  ak47 vnmskel_c, template m_nSpecialDependencyHash kept), clips by
  ClipRetarget, models by ReferencePatcher. Tests 1–2 failed because their
  clips sat at STOCK clip paths (the game kept its own clips there); use
  custom clip paths. Test files live in weaponlab_skeltest (scratch ws).
- DECISION (user, 2026-10-24): weaponlab_skeltest stays as the standing
  in-game TEST ADDON (game + content folders; user-approved for repeated
  writes). Its delivery state lives in .claude/scratch/ws (sandboxes,
  addons/weaponlab_skeltest originals, deliveries); do NOT wipe that ws when
  cleaning scratch (clean the rest). Launch: cs2.exe -tools -addon
  weaponlab_skeltest -novid +sv_cheats 1 +map de_dust2. Current contents
  (2026-10-24 22:36, VERIFIED IN GAME by the user: "seems to be working great"): AR2 removed. M4A4
  pyramid test: models/weapons/weaponlab_m4test/m4a4_pyramids.vmdl (content
  source + SMD generated by .claude/scratch/m4tool; compiled with
  resourcecompiler), skeleton weaponlab_m4a4_pyramids.vnmskel (m4a4.vnmskel's
  7 bones unchanged + sight, ag1_hand_r, pyr_00..29 = 39), 5 M4A4 clips at
  custom paths (idle is 1 frame = rest check; draw/reload/shoot/inspect move
  the pyramids along their tips / spin), cloned graphs + stock-path hookup
  copy, all written by .claude/scratch/m4/build_skeleton_test.cs (NOT the
  program's skeleton code). vdata (weapon_m4a1 model + skeleton, MAG-7) via the
  ws profile and CLI deliver. Never touch cs_hl_template.
- Found 2026-10-24 (FIXED in 0.11): Blender preview scale bug. bind_preview
  derives scale from the first shared non-zero bone distance; the Bizon
  slot's weapon_ipistol model uses MP5-SD bones (bolt 8.8 vs Bizon 2.8 units)
  → ~1/3 size. In cs_hl_template weapon_bizon uses m_szAnimSkeleton
  mp5sd.vnmskel. Planned fix: fixed metre→unit factor (39.37), constrain only
  bones whose names and rest positions match, and build the slot on the
  skeleton the addon's vdata names.
- 2026-10-24: Revision 12 (0.12.0), uncommitted (0.11 also uncommitted).
  Custom skeletons as a feature: profile `WeaponProfile.CustomSkeleton`
  (model key + skeleton path); sandbox `skeleton/` (skeleton.json setup,
  skeleton.vnmskel_c, re-targeted templates) via `Skeletons/CustomSkeletons`
  (Create/Remove/Preview/ModelBytes, `ModelSkeleton` reads vmdl bones +
  inverse binds). Re-targeted templates feed RigPackage + ClipExporter, so
  Blender gets the extra bones (own bone collection) and exports keep them;
  existing exports are moved onto the new skeleton. Delivery ships the
  skeleton, a model copy (refs swapped) at models/weapons/weaponlab/<weapon>/,
  ALL clips of the weapon at custom paths, and injects m_szAnimSkeleton +
  m_szWorldModel for its vdata blocks (also kept on VData-only saves).
  Source model must be CS2's or the target addon's. UI: weapon panel
  "Custom skeleton…" dialog; CLI `skeleton show|create|remove`; CLI deliver
  prints warnings. 87 tests pass. Verified headless: Galil+AR2 (13 bones,
  5 new incl. ag1_hand_r), .blend built with 13 weapon bones, exporter
  changed only the 4 ar2 bones, delivery compiled vdata. Not tested in game.
- VERIFIED IN GAME 2026-10-24 (user): the 0.12 product path works (AR2 on the
  Galil animates, arms OK, extra cells slide in the inspect) but ar2_chamber_cell
  and ar2_feed_cell floated. Cause (measured): their parents econ/muzzle keep
  the Galil rest, turned 120° from the AR2's bind, so an offset taken from the
  AR2's own parent pose put them ~25 units off. FIX: `AlignTo` now sets an extra
  bone's local = builtParent⁻¹ · bind(child) (rest gap 0.00 for all 5 extra
  bones). Re-delivered 22:12 (skeleton remade, export redone); AWAITING re-test.
- FINDING 2026-10-24 (verified by raw data): VRF's Bone.InverseBindPose (what
  `ModelSkeleton.BindPoses`/`FromInverseBind` read) is only the bone's
  PARENT-SPACE value inverted, not a model-space bind. So Weapon Lab's
  `SkeletonBuilder.AlignTo` treats parent-space values as model space and puts
  any extra bone whose parent is not at the origin in the wrong place (the
  real cause of the AR2 floats). True rest = compose m_bonePosParent /
  m_boneRotParent. FIXED in the program 2026-10-24 (after the M4 test passed):
  `ModelSkeleton.ModelSpace` composes the parent-space values; `AlignTo(model,
  animated)` = every animation bone unchanged + model extras at the model's own
  parent-space value (checked: reproduces the verified 39-bone M4 skeleton with
  0 difference); an extra bone whose shared parent rests differently in the model
  (AR2 cells under econ/muzzle) is placed relative to weapon_offset instead
  (`Corrected`). Setup/preview: `SkeletonOnlyBones` (kept, animated, no mesh)
  replaces DroppedBones.
- 2026-10-26 (uncommitted): outliner Graphs node per weapon (`Profiles/WeaponGraphs`:
  graphs playing its clips + parents to the root; Own/Shared/Route; states yellow
  pending, green this weapon's change in addon, BLUE new WeaponState.ChangedElsewhere =
  changed for another weapon or addon's own; click opens Graph tab). Set to CS2
  default (right-click outliner/Deliver tree weapon: settings, own graph edits,
  optional sandbox moved to workspace/backups/sandboxes). VData "Reset block…"
  (discard unsaved, or queue CS2 values for keys the addon changed). Skeleton TAB
  (tool index 4, Deliver 5): bone tree, state, Change skeleton… (CS2 default /
  addon's own / build from model / skeleton FILE via `CustomSkeletons.UseFile`:
  in-addon file used in place, outside file copied at its m_ID path, no model;
  profile model "skeleton:<abs path>"), m_szWorldModel reminder. Build .blend
  shows the skeleton read-only. GraphOwners(root) = none. 93 tests; UI checked.
- 2026-10-26 (uncommitted): USER DECISION: "Set to CS2 default" is literal: the
  next apply DELETES every addon file for the weapon, Weapon Lab's or not
  (`animation/anims/custom/<weapon>/*`, its own graph copies
  `*_custom.vnmgraph+<variation>`; its skeleton unless another addon file such as
  a model still uses it), no restoring the addon's originals. Graphs that stay
  (root viewmodel_custom, hookup copies) are patched back to CS2's paths for that
  weapon, other routing kept (the one place to be careful). Core `Delivery/StockReset`,
  `WeaponProfile.ResetToStock` (one-shot: cleared after apply, by a Build .blend or
  an export), planner `PlanStockResets`, plan `StockResets`/`ForgetOriginals`
  (originals dropped after apply). Menu "Set to CS2 default…" lists the files.
  CLI `profile reset <weapon>`. 94 tests. Dry run on weaponlab_skeltest M4: removes
  5 clips + 2 graphs, keeps the pyramid skeleton (m4a4_pyramids.vmdl uses it).
  Not applied to the user's addon; not tested in the app UI or in game.
- 2026-11-09 BUILT + PUBLISHED 0.24.0 (PLAN "DONE 0.24.0"; uncommitted, on top
  of uncommitted 0.23.0; publish\CS2WeaponLab-0.24.0-win-x64): first edit
  creates the sandbox; Game timing = display-only "expected class" picker +
  bands (app and Blender) + plain-words timing warnings (Anim Events, Checks,
  Deliver), "Plays like" wording gone; per-weapon Checks; Set to CS2 default for
  Skeleton and graph rows; Settings window (workspace, new .blend folder) + Move
  .blend; rebuild "Keep my animation" (key carry), Blender "Change gun model…"
  (CLI blender set-model), export guard; Variations (rare draws/inspects, random
  reload). 146 tests. VERIFIED headless on a scratch copy of knife_ursus: the
  carry reproduces every curve and every evaluated pose (worst 1.9e-6). NOT
  clicked in the app UI, Blender operator not run live, nothing in game.
- 2026-11-09 DECISIONS (user): default workspace = `workspace` beside the exe
  for new installs; the chosen workspace is saved in
  %APPDATA%\CS2WeaponLab\settings.json (first 0.24 start pins the workspace in
  use, here F:\CS2WeaponLab\workspace); a new workspace moves nothing. CLAUDE.md
  still says the default is the repo's workspace\ (true for this PC via the pin;
  only the user may change CLAUDE.md). No plays-like picker; keep the timing
  view; no "Fit to gameplay timing" re-time tool; Simple/Advanced + new-gun
  wizard another time; no PC2 tests for now.
- 2026-11-15 GOING PUBLIC CHECK (user asked what to clear before making the
  repo public): no secrets found. Removed `.claude/handoff/annabelle-loop-20261031`
  (user OK; 24 compiled addon/game files). STILL IN GIT HISTORY (blocks making
  this repo public as is): both handoffs, incl. the deleted
  orb-battles-from-pc1-20261101 (cs_hl_template maps, models, textures, .wav
  sounds, combine shield/battery assets), and the user's personal email as the
  author of every commit. .claude/ (agent notes, both PCs' paths) is tracked.
  Options given: a fresh public repo without .claude/, or rewriting this one.
- 2026-11-15 MOVED TO THE PUBLIC REPO (user: "lets do the fresh public repo",
  then "we might as well work in there", .claude/ public too). The old repo
  was renamed github.com/Froddoyo/CS2WeaponLab-Private (archive; its history
  has the handoff assets) and F:\CS2WeaponLab now pushes there: do not work in
  it. The public repo github.com/Froddoyo/CS2WeaponLab started as one commit
  7cc08b2 "CS2 Weapon Lab 0.26.1" (the files without .claude/, built and
  checked), published by the user from GitHub Desktop (first as
  CS2WeaponLab-Public, renamed to CS2WeaponLab). PC1 now works in
  F:\CS2WeaponLab-Public: .claude/ copied in, CLAUDE.md updated (paths and the
  public no-danger-files rule, user-asked), .gitignore blocks game and asset
  files (*_c, .vpk, Source 2 sources, sounds, models, images but the icon and
  docs/), and build/hooks/pre-commit refuses them even force-added
  (core.hooksPath set on PC1; VERIFIED 2026-11-15: a force-added .vmdl_c and
  .png were refused, a .txt passed). Local git user.email is the GitHub
  no-reply. The app's workspace stays at F:\CS2WeaponLab\workspace (pinned in
  %APPDATA%\CS2WeaponLab\settings.json), so F:\CS2WeaponLab must not be
  deleted yet. Whether the public repo is set to Public: not checked by me
  (an anonymous read asked for a login on 2026-11-15).
- 2026-11-15 PUBLISHED 0.26.1 (PLAN "DONE 0.26.1"; committed b5f5a60;
  publish\CS2WeaponLab-0.26.1-win-x64): Clip used names clips by id, the same
  as Exported as (user feedback). The user is trying 0.26 in Blender on the
  M4A4's third-person clips (screenshot 2026-11-14).
- 2026-11-14 BUILT + PUBLISHED 0.26.0 (PLAN "DONE 0.26.0"; uncommitted;
  publish\CS2WeaponLab-0.26.0-win-x64; add-on 0.15.0): third person (world
  clips in a separate manifest list, ids "world_<name>", Body rig + own weapon
  rig in the .blend, worldmodel graph routing, hookup warning), Right Elbow + WPN
  preset, 0.25.1 fixes (no false "newer version" on pre-0.25 .blend files,
  Load Weapon Lab's version asks first). 159 tests. VERIFIED headless (scratch
  AK sandbox + a scratch copy of the user's knife_ursus): untouched world export
  byte-identical, edits change only edited bones, all four animation formats on a
  world clip, keys carried across rebuilds in both views, old sandbox -> Add
  third-person clips -> rebuild with keys kept (14/15 ursus first-person exports
  byte-identical to before), dry-run plan routes world reload_ak via
  worldmodel_custom + worldmodel_gun_custom+ak47. NOT clicked in the app/Blender
  UI; nothing in game.
- 2026-11-14 FOUND (pre-existing since 0.24, not fixed): a bone a clip leaves
  unkeyed holds whatever pose the previously played clip left, so its export
  depends on what was viewed before. The user's knife_ursus lookat01_ursus leaves
  weapon_offset unkeyed: the rebuilt .blend (opens on idle1) and the old one
  (saved on lookat02) export it 0.45 units apart. User 2026-11-14: leave it for
  now; they delete weapon bone keys on purpose ("fuck-ifying my animation";
  animating wpn is cleaner and consistent). A later fix must respect that: an
  unkeyed weapon bone should hold still (rest), not fall back to CS2's motion.
- 2026-11-14 BUILT + PUBLISHED 0.25.0 (PLAN "DONE 0.25.0"; committed d5699c3;
  publish\CS2WeaponLab-0.25.0-win-x64; add-on 0.14.0): Blender "Clip used /
  Exported as" (saved on the Action); one clip's animation export/import in four
  formats on both sides (compiled, baked poses with bone names, keys .blend, FBX;
  Weapon Lab runs Blender in the background for keys/FBX); Load Weapon Lab's
  newer version; teal graph state; compare section removed; prop example swapped.
  152 tests; checked headless on a scratch knife copy. VERIFIED (headless, Blender
  5.2): Blender's FBX importer starts animations 1 frame late unless
  anim_offset=0, and its ignore_leaf_bones drops real fingertip bones; an FBX's
  stored rest is not the rig's rest (pose matrices match, rests do not); Blender's
  FBX export misplaces a prop under the constraint-driven props anchor. NOT
  clicked in the app/Blender UI; FBX from other tools untested; not in game.
- 2026-11-14 READ FROM CS2's GRAPH (glock gun graph, read only; NOT tested in
  game): the Actions SM leaves Reload for Idle only when the control parameter
  `action` == action_idle (0.2 s blend), never on "clip finished"; also to
  Inspect (forced), Deploying, Silencer on/off. Each reload state emits
  WPN_ACTION_COMPLETE when its clip's remaining time reaches 0. Inferred: the
  game sets action_idle from its own (stock root graph) reload end, so on the
  Workshop route a longer custom reload is cut by idle at the stock end, and a
  shorter one holds its last pose until then.
- 2026-11-09 cs_hl_template weapon list (read only from its vdata, models and
  player models; user's gun names): `.claude/scratch/cs_hl_template_weapons.csv`
  (buy-menu order, rows 1-46 match the user's Google Sheet; columns subclass,
  custom gun, animation slot, _class, weapon type, gameplay runs as, notes).
  Every custom model's skeleton matches its vdata anim class. AWP = Railgun,
  not set up yet (still CS2's model). Uncommitted: PLAN queue item to swap the
  "lighter beside a bong" prop example (user: later build).
- 2026-11-09 VERIFIED (CS2 files, read only): graph option weights
  (m_optionWeights) are whole numbers; a 0.5 is refused by the patcher's verify.
  Every stock gun graph checked (AK, Deagle, SSG08, Nova, FAMAS) has the
  duplicate second draw selector (#214, state #213 -> #210), Reload0 selector
  #62 and weapon_ammo_reserve #0 that PC2's random-reload recipe uses; knives
  have none. Nova shell eject: shoot1_nova frame 10 CNmParticleEvent
  weapon_shell_casing_shotgun_nova.vpcf, Create_CFG config shell_eject -> the
  .vpcf's control-point config "shell_eject" -> PATTACH_POINT_FOLLOW on the model
  attachment shell_eject (weapon_offset +4.54,-0.36,-0.71, turned ~93°); muzzle
  flash attachment weapon_offset +25.03. Held molotov flame:
  particles/weapons/cs_weapon_fx/weapon_molotov_held.vpcf (throwcharge_low,
  attachment molotov_particle).
- 2026-11-07 BUILT + PUBLISHED 0.23.0 (PLAN "DONE 0.23.0"; uncommitted; built
  in a side session): props are opt-in per bone ("Keep apart from the gun",
  profile `propBones`, bone details + Build .blend), any skeleton; model check
  (Skeletons/ModelCheck: model uses the skeleton, same parents) warns on the
  Skeleton row, bone details, Build .blend and Deliver; no model copy shipped
  (stale copies removed on push); Copy asset path (no `_c`); left-out weapons
  hidden behind "Show left out (n)"; knives play their own animations (VData
  tab, Build .blend, plays like; class weapon_knife is not a slot; how the
  game picks a knife's variation is UNVERIFIED). Verified headless + tests (141); UI not
  clicked; not in game. USER TODO for the knife: tick lighter_offset (bone
  details or Build .blend), then Build .blend; the next push removes the
  unused copy models/weapons/weaponlab/knife_ursus/broken_knife_ag2.vmdl_c.
  FACT (user): a custom knife shows in first person offline with console
  `ent_create weapon_knife_ursus` while looking at the ground (vdata model);
  `give` shows the default knife.
- 2026-11-07 VERIFIED IN GAME (user, PC1): with `lighter_offset` parented
  under `weapon` in the MODEL (matching the custom skeleton), the 0.22.4
  export "seemed to work perfect" (same pushed clips). Confirms the
  hierarchy hypothesis below: a model's bone hierarchy must match the custom
  skeleton's; props stay apart through animation (anchor + counter-motion),
  never through model parenting. (0.23.0 replaced the "parentless in the
  model" prop detection with the opt-in tick.) User also saw clips reload in
  CS2 without restarting (tools-mode resource hot reload; nothing Weapon Lab
  or the agent did). 2026-11-08 (user, observed): with a custom graph /
  player models, a push from Weapon Lab while CS2 sits in the main menu
  loads the new animation without restarting CS2. Mechanism assumed to be
  the same hot reload (not checked: whether it also needs tools mode, or
  works mid-match).
- 2026-11-07 IN GAME (user, 0.22.4 knife_ursus bong in cs_hl_template): the
  lighter shows but "really far away" and still moves with the bong; their
  lookat01 lighter keys do play. Checked (read only): exports are 0.22.4
  (.blend 19:45, lookat01 changes only lighter_offset), clips pushed 20:11,
  the user's .vmdl recompiled 20:30 with NmSkeletonReference ->
  weaponlab_knife_ursus.vnmskel; vdata m_szWorldModel = their own model
  (not the copy), m_szAnimSkeleton = stock knife_ursus.vnmskel. MISMATCH:
  the .vmdl Skeleton node (and FBX) has lighter_offset as a ROOT bone; the
  custom skeleton has it under `weapon` (rest identity, never animated).
  HYPOTHESIS (unverified): CS2 applies each track parent-relative on the
  MODEL's own hierarchy, so a model root takes the lighter track relative to
  the model origin, not the gun. Proposed test: parent lighter_offset under
  weapon in the model (FBX + .vmdl Skeleton node), recompile, retest with the
  same pushed clips; expected: lighter holds still. Fits the earlier facts
  (AR2: parts posed from parent-space values; pyramid test with matching
  hierarchies worked).
- 2026-11-07 BUILT + PUBLISHED 0.22.5 (PLAN "DONE 0.22.5"; committed 2026-11-07 with 0.22.1-0.22.4):
  Revert to addon default gets a custom-skeleton item (drops it, or adopts the
  addon's own skeleton for the weapon); clips only re-targeted to it fold into
  it. Fixes the user's galil dialog (every row greyed out). A revert with
  nothing revertable shows a message. Fixed double-encoded arrows in
  AddonRevert.cs. Verified: test + scratch copy of the galil. Not clicked in
  the app; adopt branch untested. The user's galil itself is untouched (they
  revert it in the app).
- 2026-11-07 BUILT + PUBLISHED 0.22.4 (PLAN "DONE 0.22.4"; committed 2026-11-07):
  props (a custom skeleton's free bones, e.g. the user's bong `lighter_offset`
  on knife_ursus in cs_hl_template) live in the WEAPON rig under a hidden
  anchor bone that copies the Arms rig object, so the gun's motion does not
  reach them; they hold still at the default clip's frame-0 place in every
  clip (keyed every frame). References keep the stock (carried) motion, so
  every exported clip writes the prop track (untouched = byte for byte except
  it). Game skeleton keeps ONE root. FACT (measured): CS2's ursus clips never
  move `weapon`; first-person gun motion is arms `wpn`. Verified headless only.
  User must Build .blend again; not tested in game. 0.22.3 (prop in the Arms
  rig) is superseded; its .blends still export, Reset asks for a rebuild.
- 2026-11-07 BUILT + PUBLISHED 0.22.2 (PLAN "DONE 0.22.2"; committed 2026-11-07):
  custom skeletons pick the model's root by name (`SkeletonBuilder.ModelRoot`,
  CS2's root `weapon`) and hang any other parentless bone under it; before,
  FromModel took the first parentless bone and AlignTo threw. The user is
  building a bong with a `lighter_offset` prop bone. FACT: the whole weapon
  model rides on the arms' `wpn` bone in game, so no model bone escapes gun
  motion unless a clip animates it. Not tested with the user's model.
- 2026-11-07 BUILT + PUBLISHED 0.22.1 (PLAN "DONE 0.22.1"; committed 2026-11-07, after
  commit 43a4498): viewports and clip views show what the addon plays (its own
  clip when it has one). USER DECISION 2026-11-07: "inherit the custom stuff
  when it's green", not what Weapon Lab remembers. cs_hl_template has its own
  (pre-Weapon Lab) clips for rifle_aug, rifle_galilar, rifle_scar20 and
  Weapon Lab's rifle_ssg08; the profile's rifle_scar20 sandbox was made from
  CS2's clips (editing it would replace the addon's scar20 animation; Build
  .blend → From the addon fixes it). New sandboxes take the addon's clips.
- 2026-11-07 BUILT + PUBLISHED 0.22.0 (PLAN "DONE 0.22.0"; committed in 43a4498
  with 0.20.1 + 0.21): per-clip Set to CS2 default, outliner
  description per tab, tabs Animations|Anim Events|Graph|VData|Deliver, VData
  tab without outliner (all blocks, Jump to, viewport of m_szWorldModel on the
  anim-class slot, info panel, small History). USER DECISION 2026-11-07: the
  addon's vdata SOURCE file is the truth for green/yellow. VERIFIED bug
  cause: SSG08 m_flCycleTime showed 0.7 yellow from the originals snapshot
  (2026-10-24) though the addon (source + compiled 2026-11-04 19:41) has 1 and
  Weapon Lab never wrote that key; fixed. 134/134 tests. UI checked on a
  scratch workspace only. A misplaced test click hit the user's Steam window
  (opened L.A. Noire's library page; nothing else). UI test helpers: scratch
  appclick.ps1 (window-relative), apptype.ps1, appwheel.ps1, screen.ps1;
  run the debug app with WEAPONLAB_WORKSPACE set to a scratch folder.
- 2026-11-07 BUILT + PUBLISHED 0.21.0 (PLAN "DONE 0.21.0"; uncommitted, on
  top of uncommitted 0.20.1): outliner right-click menu + Revert to addon
  default (kept copies of pushed clips in sandbox `pushed/<clip>/`), Deliver
  Show buttons, Anim Events clip colours, viewport Default = CS2's model,
  bridge 0.12.0 (muted finger colours, preview meshes follow bone scale,
  .blend saved elsewhere finds its sandbox). 131/131 tests. Not tested in
  the app UI or in game.
  VERIFIED IN GAME (user, 2026-11-05): the Annabelle (SSG08 slot, plays like
  the Nova) "seems to work good now" after Export All 2026-11-04 19:19 and
  pushes 19:21 (all clips) + 19:31 (shoot1_ssg08 only). Working recipe the
  user settled on: reload 50 frames, sync INTRO 0-11, LOOP 11-24, OUTRO
  24-49, WPN_RELOAD_ADD_AMMO 20; WPN_DROP_MAG and the Clipin sounds removed,
  AddAmmo 18.45 + ClipHit 19.45 at the shell, BoltBack 37.5 + BoltForward
  39.5 in the outro; shoot1 stays 51 frames with BoltBack 9, BoltForward 12;
  silent reloads untouched (open decision). After the push the user edited
  reload_ssg08 again (19:44: removed 3 events, added WPN_RELOAD_SILENT 0-36.5);
  not pushed, and Revert cannot restore it (pushed before 0.21 kept copies).
  Tests: `dotnet test --project tests/WeaponLab.Core.Tests` now reports "Zero
  tests ran" (cause unknown); run
  `tests\WeaponLab.Core.Tests\bin\Debug\net10.0\WeaponLab.Core.Tests.exe`
  (`-class <full name>` to filter) after a build.
- 2026-11-04 18:10 BUILT + PUBLISHED 0.20.1 (PLAN "DONE 0.20.1";
  uncommitted): clip length change and animation-only Blender export (user
  decisions 2026-11-03: "we have the animevent editor for a reason";
  "frames 50 to 111 being blank"). Annabelle sandbox: lengths set with the
  published CLI (reload_ssg08 + _lgcy 50, lookat01_ssg08 + _lgcy 169; stored
  as "frames" in events/*.json, mirrored in blender/input/clip_lengths.json;
  exports rewritten from the OLD samples, so the user must export from
  Blender again). Stale event markers stripped from the .blend (65, keys
  identical); backups: workspace\backups\events\rifle_ssg08-20261104-180840,
  blender\backups\rifle_ssg08-20261104-180920.blend. Open decisions for the
  user: silent reloads (Codex animated 0-49 at normal speed; game's silent
  reload is half speed, events fitted to 22/40/48/98) and shoot1 (Codex
  0-24, clip 51 frames).
- 2026-11-03 21:56 REPAIRED the SSG08 .blend after Codex re-animated it to
  Nova timing (user: "fix this up … the animations are there"). Codex had
  deleted every action's event markers (export refused 10/12 clips), set the
  Dope Sheets to Dope Sheet/Timeline with pose markers hidden, and turned on
  manual frame ranges. Fixed in a copy with a one-off script, since deleted
  (bridge restore_markers for all clips, editors back to the Action Editor,
  use_frame_range off), keyframe fingerprint identical (229b082d…), every
  clip passes collect_event_edits; swapped in after Blender closed. Backup
  of Codex's file: blender\backups\rifle_ssg08-20261103-215624.blend. Rigs,
  previews, scale constraints were already intact. Markers at CS2's frames
  change nothing on export (EventEdits.Merge skips untouched markers), so
  the Nova-fitted Anim Events timings apply. Codex's animation: reload 0-49,
  shoot1 0-24, draw 0-30, lookat01 to 168 (clip ends 159: last 9 frames cut
  on export), silent reloads 0-49 at normal speed (game: half speed, 98).
  The .blend1 vanished meanwhile (not touched by me). Not exported yet.
- 2026-11-03 21:45 BUILT 0.20.0 "Plays like" (details PLAN "DONE 0.20.0";
  uncommitted; published). Facts it rests on: PC2 2026-11-03 (pc2/MEMORY):
  an anim class its group does not list plays like the group's LAST-HELD
  gun (sticky; default only until another is held), a listed class is
  deterministic; scope view needs WEAPONTYPE_SNIPER_RIFLE; viewmodel_custom
  routing moves visuals only; made-up class firing bug not reproduced.
  USER-VERIFIED 2026-11-03: on the Workshop route the visual clip's own
  sound events play, synced to its animation and its event timing.
  Annabelle reload events FITTED to the Nova (CLI events fit, user: "set the
  timings in the animevent tab"; Codex re-animates to Nova timing):
  reload_ssg08 + _lgcy 11-24 shell 20, outro ends 49; reload_silent_ssg08 +
  _lgcy 22-48 shell 40, outro ends 98 (_lgcy/silent had no loop; --from
  42,61,50 and 84,122,100 = Codex's copies; sounds moved with their part).
  Sandbox exports rewritten (Codex's old animation), NOT pushed. Backup of
  the previous events + exports: workspace\backups\events\
  rifle_ssg08-20261103-213159. Blender's markers there are CS2's own frames
  (unmoved), so the fitted events win on export.
- 2026-11-02 21:05 User had Codex animate a copy of the SSG08 .blend:
  F:\CS2AnimationLab\rifle_ssg08forgpt.blend -> rifle_ssg08_completed.blend
  (both in the read-only CS2AL folder; never write there). "No Weapon Lab
  sidebar" cause (VERIFIED): Blender 5.2 was opened from Explorer and the
  optional loader (weaponlab_bridge_loader.py, "Blender add-on…" button) is
  not installed in %APPDATA%\Blender Foundation\Blender\5.2\scripts\addons;
  only Open in Blender loads the bridge. The file's Weapon Lab data is
  intact (scene meta, rigs, roles, scale constraints). Codex changed 10 of
  12 actions (draw and idle main clips unchanged) and made every _lgcy action identical to its main one.
  The sandbox .blend (saved 2026-11-02 20:48) has the same 12 actions as
  the forgpt copy, so replacing it with the completed file loses nothing.
  Export from outside the sandbox cannot work (PLAN .blend-location bug).
  DONE 21:03 (user: "Yes, copy it"): the sandbox rifle_ssg08.blend is now
  Codex's file (sha 0DE01CE3…, same as the source); previous sandbox file
  backed up to backups\rifle_ssg08-20261102-210346.blend; .blend1 kept.
  Not yet opened through Weapon Lab or exported.
  21:10 user exported: 10 clips OK; shoot1_ssg08(+_lgcy) failed on marker
  #1. Bridge bug (Blender's 63-byte marker names cut the muzzle-flash
  path), FIXED in the repo bridge and the published 0.19.0 app (see PLAN);
  user must restart Blender (Open in Blender) and export shoot1 again.
  21:25 Deliver Review then failed on the profile's 29:clip edit (legacy
  SSG08 graph, CS2's shoot1_ssg08_lgcy path, from the Graphs tab
  2026-10-31). FIXED in delivery and the Graphs tab (PLAN "FIXED 21:25");
  user said "go on the node 29 fix" (built the real fix, kept the edit).
  publish\CS2WeaponLab-0.19.0-win-x64 rebuilt in place with both fixes
  (same version number). Not pushed yet.
- 2026-11-01 21:36 HAND-EDITED the user's SSG08 .blend (user: "go", "dont
  overwrite my changes"), as a trial before the queued bridge change.
  File: workspace\profiles\cs_hl_template\sandboxes\rifle_ssg08\blender\
  rifle_ssg08.blend; backup of the user's version (sha C1BEE046…) at
  backups\rifle_ssg08-20261101-213409.blend; .blend1 untouched. Script:
  .claude\scratch\blendfix\apply.py (Blender 5.2.3 background, save copy,
  File.Replace). Changes: (1) each preview-rig bone with COPY_LOCATION +
  COPY_ROTATION also gets COPY_SCALE "Weapon Lab - follow scale" (target
  and owner space POSE, replace, Make Uniform): 54 arms, 44 gloves, 13
  weapon. (2) arms-rig bones get CUSTOM colours (finger hues, meta 0.55x,
  _0 0.78x, _1 full, _2 50% white; hand white, armUpperShoulder teal,
  arm_lower purple, wpn* magenta, helpers grey). VERIFIED in background
  Blender: all 12 actions' keys/handles/markers/slots, poses, object
  matrices and scene props identical to before; scaling hand_R,
  finger_index_1_R or the weapon's lever/hammer/clip1 by 2 now doubles the
  preview bone (39.37 -> 78.74, children too) and moves the mesh further;
  moves unchanged. All clip scale keys are 1.0 (no hidden-by-scale bones).
  The weapon's clip/slide/bolt/trigger/ag1_hand_r carry no mesh on this
  model. NOT seen by the user yet (colours, look of the scaling).
- 2026-10-31 13:50 BUILT 0.19.0 "Clip timeline" (uncommitted; published to
  publish\CS2WeaponLab-0.19.0-win-x64; details in PLAN "DONE 0.19.0"). Key
  code: Core/Clips/EventListEdit.cs (AddedEvent, SyncSection, EventListEdit,
  StoredEventEdits, EventFactory), EventPatcher (adds/removes/sorts events,
  rewrites m_syncTrack, verifies), ShellReload.cs (+ EventCopy), GameplayIds,
  Sounds/SoundLibrary.cs; App/EventTimeline.cs, SoundPlayer.cs (NAudio),
  MainWindow.AnimEvents.cs (rewritten; the viewport block ViewportPanel
  moves between AnimationViewportHost and EventsViewportHost). Avalonia
  quirk: pointer capture is lost BEFORE PointerReleased, so the timeline
  finishes gestures on whichever comes first. WeaponViewport now re-uploads
  meshes after a GL re-init (was blank after a reparent). cs_hl_template
  sandbox state: rifle_ssg08 reload_ssg08 has app-made stored edits (loop
  52/61/56 via the helper) and a regenerated export NOT pushed yet (the live
  addon still has the hand-made clip, same recipe). Smoke-test helpers in
  .claude/scratch/vcon (ui.ps1 window clicks/drags with absolute
  mouse_event moves, crop.ps1, app.ps1); the first click after an app
  launch is often swallowed.
- 2026-10-31 13:00 WORKSHOP-LEGIT SHELL RELOAD (VERIFIED IN GAME ON PC1, me,
  VConsole + window screenshots; NO stock-path root, user rule 12:30: never
  overwrite CS2's viewmodel.vnmgraph in tests unless proving it is needed).
  The gameplay side runs CS2's own root graph: weapon_category (from
  m_WeaponType) picks the WPN group, weapon_type (m_szAnimClass) the state in
  it; a class a group does not list gets the group's default (Shotguns: Nova,
  index 0). An ID comparison holds a LIST (m_comparisionIDs[]).
  * A: SHOTGUN + anim weapon_nova: Nova shell logic, 1/3 -> 2 shells + pump,
    0/3 -> 3 + pump, 2/3 -> 1 + pump.
  * B' (LEFT LIVE): SHOTGUN + anim weapon_ssg08 + viewmodel_custom routing
    edits: #294/#442 (Shotguns group) input weapon_type, IDs nova|xm1014|
    sawedoff|mag7; #366/#448 (SniperRifles group) input weapon_type, IDs awp|
    ssg08|g3sg1|scar20|annabelle; #351/#359 (SSG08 state) + weapon_annabelle.
    Ammo follows the Nova (HUD 0->1->2->3, reserve -1 each), the viewmodel
    plays the Annabelle's custom SSG08-slot clips, SPAS (weapon_nova, anim
    sawedoff) keeps sawed-off anims/sounds/2 shells + pump. Sounds heard are
    the Nova's (gameplay side). USER SAW: shells load one by one but the
    animation does not loop: the SSG08-timed clip (intro 1.73 s) reaches its
    loop after the Nova logic is done (~1.5 s). The Annabelle reload must use
    reload_nova's timing: 50 f @30, INTRO 0-11, LOOP 11-24, ADD_AMMO 20,
    OUTRO 24-49 (draw/shoot timing likely follows the Nova too, unmeasured).
  * B: made-up anim class weapon_annabelle (same edits): fires once, then
    never fires or reloads again. Cause unknown (old guess: third-person
    worldmodel graph has no state for it; 2026-11-14 less likely, world
    clips carry no gameplay events, and the stock viewmodel root runs
    gameplay; see "Third person (worldmodel)").
  Deliveries: 123743-396 (root copy removed), 124239-367 (SHOTGUN + nova),
  124612-906 (routing edits), 124614-757 (anim weapon_annabelle, ROLLED BACK),
  125249-718 (anim back to weapon_ssg08). New code (uncommitted): GraphEdits
  adds a list element for an edit at index == Count; GraphPatcher verifies
  added leaves by the list's first element type; test
  IdComparisonsTakeMoreIdsAndAnotherInput (graph tests 6/6). Harness:
  .claude/scratch/probe (vdata/graph edits pushed via DeliveryPlanner),
  .claude/scratch/vcon (vcon.ps1, run.ps1, test scripts). Tips: console.log
  is truncated at each CS2 launch; screenshots need the "Counter-Strike 2"
  window in front (else a stale loading screen); never pipe vcon.ps1 into
  Select-Object -First (it stops the script).
- 2026-10-31 12:04 (SUPERSEDED: Workshop players never get the copy, and the
  user ruled out overwriting CS2's root graph; copy removed 12:37) WORKED IN GAME ON PC1 (by me, over VConsole):
  ANNABELLE SHELL RELOAD WORKS with a copy of viewmodel_custom at CS2's own
  root graph path. Cause (PC2, pc2/MEMORY "ANNABELLE LOOP = HOOKUP"): CS2's
  gameplay side runs CS2's root graph, not the agent's hudmodel graph that
  cs_hl_template's swappers.js sets with SetModel after spawn. New in 0.18.2
  (uncommitted, published to publish\CS2WeaponLab-0.18.2-win-x64): Plan(...,
  cs2RootCopy: true/false/null) + CLI `deliver --cs2-root-copy true|false`;
  key DeliveryPlanner.Cs2RootCopyKey (animation/graphs/viewmodel/
  viewmodel.vnmgraph_c), manifest-sticky (later pushes keep it equal to
  viewmodel_custom, Set to CS2 default included), false removes it or
  restores the addon's own; excluded from BrokenReferences. Test
  Cs2RootCopyFollowsViewmodelCustomUntilSwitchedOff; core 108/108. App has
  no toggle yet (its pushes keep the copy in sync). Delivered to
  cs_hl_template 20261031-120459-805 (copy sha 7220a8ce = viewmodel_custom).
  In-game results, CS2 I started (pid 17016, -tools -addon cs_hl_template
  -condebug), de_cache, sv_cheats, bots kicked, SSG08 default primary:
  2/3 reload -> 3 server shots then dry; empty reload -> clipout, clipin, 3
  passes (hit+addammo ~0.31 s apart), boltback/forward, then 3 shots + dry;
  fire after 1 pass -> shot fires and the reload stops; a reload after that
  -> 2 passes + bolt, then 3 shots + dry. Not tested: reserve running out,
  silent reload, the user's own eyes. Left running on de_cache for the user.
  TEST METHOD (reusable): .claude/scratch/vcon/vcon.ps1 sends CMND packets
  to VConsole port 29000 (header "CMND" 00 D4 00 00 len16 00 00: protocol
  version 212; 210 is rejected); wait ~1.5 s after connecting before the
  first command. Evidence in console.log: "[Shooting] sv: Running lag
  compensation" = a server shot; `snd_showstart 1` logs each .vsnd. A dry
  shot starts CS2's auto reload. `echo` output is not logged.
- 2026-10-31 11:24 IN GAME (user video D:\Videos\OBS\2026-10-31 11-24-03.mov,
  CS2 started 11:21 with -tools -addon cs_hl_template, map de_cache): the 2nd
  try STILL LOOPS FOREVER. Verified: the cycle is ~0.3 s = the normal clip's
  9-frame LOOP 52->61, so reload_stage never becomes stage_outro. Ruled out
  (verified from files/log): stale vdata (compiled 00:29, CS2 fresh), clip
  data (live reload_ssg08 matches reload_nova's layout event for event), the
  addon's cs_scripts (no ammo setters on the SSG08; Armory gives a plain
  weapon_ssg08). Differences from PC2's passing runs (which matters is
  untested): custom skeleton + Annabelle model; the addon block has NO _base
  (so no taxonomy.sniper_rifle), m_nPrimaryAmmoType BULLET_PLAYER_BUCKSHOT
  (stock 762MM), max clip 3 / reserve 32 (PC2 used 10). Unknown: whether the
  clip count rises in the loop (no HUD in the video). Next: user test with
  HUD (does 2/3 become 3/3; does reserve drop once or every pass; does the
  SPAS-12 shell reload end normally in the same session), or PC2 repeats it
  with these exact clips + vdata block in its lab. Side note: with -condebug
  the cs_script call logging grows game\csgo\console.log ~2 MB/s (1.4 GB at
  11:34).
- 2026-10-31 ANNABELLE SHELL RELOAD, 2nd try (PC2's plan, pc2/MEMORY "For
  PC1"; user "go"), delivered 20261031-094837-914 (in game: see the 11:24
  entry above). State found first:
  at 00:54-00:55 the SSG08 had been Set to CS2 default (profile Default,
  sandbox skeleton removed, addon custom clips + SSG08 graphs removed; the
  skeleton 99af13a0 stayed because the Annabelle .vmdl targets it), and the
  sandbox exports folder held only the 6 reports, no clip files (cause
  unknown; no Weapon Lab code deletes them). Done: CustomSkeletons.Adopt
  (addon's weaponlab_rifle_ssg08.vnmskel + models/weapons/weapon_annabelle,
  from scratch probe; the CLI adopt cannot find it without custom clips) ->
  all 12 templates byte-identical to the .blend's rig.json (the .blend stays
  valid); profile rifle_ssg08 Custom + that skeleton; draw/idle exports
  restored from the 005520 delivery backup (022f4b0b / 853e0128, the user's
  23:43 exports incl. lever/slide2/hammer) with report shas fixed; the four
  reload clips rebuilt from the templates with PC2's recipe: normal loop
  52->61/111, ADD_AMMO moved to 56, SILENT 0..94; silent loop 106->121/222,
  ADD_AMMO 112; DROP_MAG removed; INTRO/LOOP/OUTRO events + sync sections
  cloned from reload_nova (poses, sounds, refs, rest verified unchanged).
  Live check: 12 clips on the custom skeleton, both SSG08 gun graphs play the
  custom reload clips. VData not touched: the compiled block already has
  m_bReserveAmmoAsClips false, reserve 32 (now ROUNDS), single shells,
  reloads_per_shell + shotgun, disallow 0.466667, SNIPER_RIFLE, max clip 3.
  First try (00:36, rolled back): same idea with loop 50->61 and ADD_AMMO at
  60 looped forever in game, although that vdata was compiled at 00:29 with
  reserve false; PC2 says reserve-as-clips causes the endless loop and
  ADD_AMMO 1 frame before the loop end only one extra pass, so the user's
  CS2 session probably predated the vdata change (unverified). Graph facts:
  the loop runs on the clip's sync track (CurrentSyncEventID ==
  WPN_RELOAD_OUTRO && action_reload && reload_stage != stage_outro -> 0 s
  jump to sync ID WPN_RELOAD_LOOP). Hand-made exports: a Blender export or
  Anim Events save of a reload clip drops the loop structure.
- FOUND 2026-10-31 (user's first cs_hl_template delivery of the Annabelle
  SSG08, 2026-10-30 23:45; in game: hands animate, new bones static). Cause
  (verified from files): the addon's weapon_ssg08 m_szWorldModel is still
  the ORIGINAL models/weapons/weapon_annabelle/weapon_annabelle.vmdl (targets
  ssg08.vnmskel; the user recompiled it 23:57 from nova -> ssg08), while
  every delivered clip plays on weaponlab_rifle_ssg08.vnmskel, which only the
  delivered copy models/weapons/weaponlab/rifle_ssg08/weapon_annabelle.vmdl_c
  targets. Delivery has not written vdata since 0.13/0.14 (user rule: vdata
  has no ties to other tabs) and the m_szWorldModel reminder went with it,
  so nothing told the user. Fix for the user: VData tab, weapon_ssg08
  m_szWorldModel -> the copy, Save and compile (m_szAnimSkeleton may stay).
  Draw export carries lever/slide2/hammer motion; idle only arm bones.
  The 23:57 recompile dropped clip/slide/bolt/trigger from the model (skeleton
  keeps them; same 7 new bones); the next delivery refreshes the copy.
- 2026-10-30 (uncommitted, 0.18.1, published): FIXED the 0.18.0
  model-hierarchy bug that lifted the gun out of the hands. FromModel had
  copied the model's ROOT `weapon` bind as the skeleton rest and RestTransfer
  added it to every clip. Every CS2 weapon .vmdl_c binds `weapon` off origin
  (SSG08 model 3.631,-0.272,4.189; Nova + Annabelle 4.611,-0.193,5.192), each
  .vnmskel rests it at 0, stock clips keep it there: the game drops it. Now
  the root keeps CS2's rest (extra model roots hang under it relative to the
  model root). Tests: NextBuildTests root assert + game-backed
  ModelHierarchySkeletonsLeaveTheGunWhereCs2PutsIt (both fail on old code);
  107 pass. Annabelle check (Blender, idle frame 0): hands 4.5/5.8 units off
  the gun and below it -> 1.7/2.8, hands inside the gun. User's
  cs_hl_template rifle_ssg08 sandbox re-created with the fix (its .blend NOT
  rebuilt: open in the user's Blender with unsaved changes). Annabelle .vmdl_c
  targets nova.vnmskel. Advice given: leftover misfit = move the gun in the
  model, not the arms. Not tested in CS2.
- 2026-10-30 (uncommitted, 0.18.0): the 2026-10-29 queue, built + published.
  ANIM EVENTS: ClipEvent kinds Sound/Particle/Gameplay/Material/Legacy/Other
  with plain Fields; EventSchema lists editable fields per class (retime all
  but legacy/unknown; legacy read-only); EventPatcher whitelists, keeps value
  type+flag, fingerprints the rest; a new particle system is added to the
  clip's RERL (verified by test). App edits live in sandbox
  `events/<clip>.json` (EventEdits), merged into every export: a Blender
  marker that differs from the template wins for that event. EventEdits.Apply
  re-runs the last Blender sample, else patches the template; empty -> export
  deleted. HAND GLUE (HandGlue): export sets wpnHand_* = hand_* x CS2's
  per-frame offset from the reference sample (identity when untouched ->
  byte-identical); Blender builds the follow constraint muted for the
  reference sample, then switches it on; add-on toggle removed, old .blend
  files get it switched on at clip switch. SKELETONS: SkeletonBuilder.LeafBones
  (`*_end`, childless, base exists) left out; FromModel = model hierarchy +
  CS2 bones the model lacks; ClipRetarget gets RestTransfer (new rest x old
  rest^-1 x pose); ModelWeights = bones with skin weights via glb export;
  setup.UnweightedBones/SkippedBones/ModelHierarchy; rig.json hiddenBones ->
  Blender "No mesh - hidden" collection. Annabelle check: unweighted clip,
  slide, bolt, trigger, ag1_hand_r (the old DMX anims' bones, as suspected).
  PICKERS: PreviewModels.FromFile rejects .vmdl; Freshness (never throws:
  live pickers call it per keystroke, which crashed twice in testing);
  StockModelFor names Default. Single-instance mutex per workspace. Tests
  follow CS2 1.41.9.3 (AK 12 clips, reload events by name): 106 pass.
  Verified headless/in app only; nothing tested in CS2 yet.
- 2026-10-27 (uncommitted, 0.17.0): FIRST RELEASE BUILD for the user to test.
  `build/publish.ps1` -> `publish\CS2WeaponLab-0.17.0-win-x64\` + .zip (83 MB;
  publish/ is gitignored): CS2WeaponLab.exe + weaponlab.exe, each published
  separately as self-contained single file (compressed, natives self-extract,
  no pdb), + blender\weaponlab_bridge\ + LICENSE. Publishing the app alone
  would drop a weaponlab.exe whose dll is bundled inside the app, hence two
  publishes. No warnings with TreatWarningsAsErrors. VERIFIED from an unzipped
  copy outside the repo: app opens in ~1 s titled "CS2 Weapon Lab 0.17.0",
  connects, viewport + reload playback; CLI discover, verify-encoder (11/11),
  sandbox create, blender build (9 s), export-clip untouched = byte-identical.
  Outside the repo the workspace is %LOCALAPPDATA%\CS2WeaponLab\workspace (did
  not exist before); inside the repo it is the repo workspace. Blender add-on
  install from a build points at that build's folder (moving it breaks the
  loader). Also: app icon from root weapon_lab_icon_1024.png ->
  src/WeaponLab.App/Assets/app.ico (exe, window, dialogs); title shows the
  version; weaponlab.exe double-clicked (owns its console) explains itself
  and waits for a key (user had double-clicked it thinking it was the app).
- 2026-10-27 (uncommitted): viewport fixes + ARMS. Event marks were wrong
  because ClipEvent.StartTime is NORMALISED (0..1 over the clip), not seconds:
  use StartFrame(info). Marks now a TimelineMarks control (draws from its own
  width). Arms: ViewportScene puts the viewmodel rig (viewmodel.vnmskel) first,
  weapon bones parented to the arms' `wpn` bone (weapon model space = wpn
  space, as the Blender build mounts it); NO extra Y-up turn needed (that made
  the gun vertical). USER DECISION: viewport shows GLOVES ONLY, no sleeves
  (GlovesModel from Build .blend, "" none), placed by hand_R. Glove joints map
  BY NAME (agent arm_upper_* -> rig arm_upper_*, as the engine binds; the old
  armUpperShoulder rename stretched the elbow; Blender build_weapon.py still
  renames, unverified there). Unmatched joints ride the nearest mapped
  descendant/ancestor with THAT node's bind; *_TWIST joints (arm_lower_*_TWIST
  @1/3, TWIST1 @2/3) take that share of the child's roll (ViewportTwist):
  held rigid, the forearm pinched (probe: stretched forearm edges 325 -> 74). SKINNING is per mesh
  JOINT: skin_j = joint bind⁻¹ (glTF bind into game space: rotation unscaled,
  offset × 39.37, then placed like the vertices) × bone now, skinned at rest
  too; per-bone current × rest⁻¹ stretched agent sleeves. ViewportClip: arms
  from the primary stream, weapon from the paired stream. Arms toggle in the
  header. Gloves fix checked in the app (AK reload original, rest + frames
  18/36/58: smooth forearms, hands on the gun) and by a numeric probe. 97 tests (viewport test covers arms, wpn mount, hand
  meshes, arm motion, marks range, glove twist shares).
- 2026-10-27 (committed 9486fcb, 0.16.0): USER DECISIONS: Skeleton tab REMOVED (tool
  indices: Deliver = 4); skeleton panel moved to the Animations tab's right
  column above History (`MainWindow.SkeletonTab.cs` RefreshSkeletonPanel, panel
  visible on Animations only). Build .blend has the skeleton CHOICE again (own /
  addon / keep current / built from the custom model above / skeleton file),
  defaults to what the weapon has, suggests "built from model" when a picked
  model has extra bones; ModelPicker got a hint parameter. VIEWPORT in the
  Animations tab (above Events/Tracks/Exports): Core `Viewport/ViewportScene`
  (skeleton in model space, game units, Z up; preview .glb via PreviewExporter,
  cached in workspace/cache/viewport, placed like Blender: anchor joint IBM ×
  metres->units × skeleton rest anchor; SharpGLTF 1.0.6 comes with VRF) and
  `Viewport/ViewportClip` (weapon stream per frame -> model matrices; export /
  sandbox template / CS2). App `WeaponViewport` (Avalonia OpenGlControlBase,
  GLSL 300 es / 330, CPU skinning = current × rest⁻¹, bones on top, amber new
  bones, ICustomHitTest for input, orbit/pan/zoom, click-pick synced with the
  outliner Skeleton node) + timeline (play/pause/scrub, speed, event ticks,
  "Your export" toggle). Checked in the app (stress workspace): AK mesh +
  bones, picking, orbit, reload playback (mag drops); M4 on the pyramid custom
  skeleton (39 bones, 32 amber) plays its reload. 97 tests (viewport placement
  test). Arms not shown yet (phase 3). Profile A in the stress workspace now
  has an M4 custom skeleton (CLI-made).
- 2026-10-26 (uncommitted, 0.15.0): USER DECISION: profiles are back as LOCAL
  WORKSPACES; the addon is the LIVE state. Profile = workspace/profiles/<name>/
  (profile.json + sandboxes/), holds only work not pushed yet; not tied to an
  addon (TargetAddon = last used; a profile with none adopts the picked addon).
  Top bar: Profile picker + "…" (new/rename/delete -> backups/profiles) + Addon.
  ProfileStore.MigrateLegacy moves <addon>.wlprofile.json -> <addon>/profile.json
  and workspace/sandboxes -> active profile (runs on first launch of 0.15; the
  user's real workspace is NOT migrated yet). PUSH MODEL: `Delivery/PendingWork`
  (ProfileClip, PushState Pending/Live/ReplacedInAddon via profile.Pushed
  addon->key->sha); planner merges onto the previous manifest, pushes only
  pending clips/skeletons, builds graphs on the LIVE custom graphs, never
  removes except Set to CS2 default; plan.RecordIn(profile) records pushes,
  removes pushed graph edits, stock reset -> weapon commit off + pushed records
  cleared. Manifest.FileProfiles (key -> last profile) -> plan.Overwrites
  warnings (confirm / CLI --confirm). Warnings: mixed skeletons (pushed clips vs
  addon's other clips, via StockReset.WeaponSkeletonOf/ClipReader), sandbox
  built from another addon's clips, FromAddon skeleton missing -> copied in.
  Graph value edits over a changed live value get a note. Status: yellow =
  pending (committed weapons only), green = addon has it (inventory.For),
  clip BLUE = pushed but replaced in addon. Graph tab: base = live _custom copy
  (GraphStatus.ReadDelivered), green glow where live differs from CS2 (values or
  played resource; GraphDocument now follows m_referencedGraphSlots for child
  graphs), names the _custom file in list/breadcrumb/header. Buttons "Push to
  <addon>", "Undo last push". CLI profile new/use/rename/delete/target; every
  sandbox command uses the profile's sandboxes. 96 tests incl.
  TwoProfilesAndTwoAddonsPushWithoutStompingEachOther, PushedWorkIsLive...,
  mixed-skeleton case. REAL RUN (scratch workspace .claude/scratch/stress,
  profiles A/B, tool .claude/scratch/stresstool): created addon
  weaponlab_skeltest2 (copied test player models + script). skeltest now has
  B's AK reload (-0.6 rad arms twist, over A's after confirm) + B's FAMAS idle
  (0.5); skeltest2 has A's AK reload (+0.6). Root graphs route exactly those.
  Their push backups live in the stress workspace, not the real one. Not
  tested in game.
- 2026-10-26 (uncommitted): VData table: "Addon now" column removed (addon
  value moved into the row tip when unsaved), "Profile (edit)" -> "Addon (edit)",
  ↺ only shown where the value differs from CS2's. Animations tab: Delete .blend…
  (confirm naming the file; deletes .blend + .blend1, keeps blender/backups and
  exports). Graph tab: S2V-style canvas (Core `GraphLayout`: GraphItem.Inputs pins
  per link field + Height, GraphEdge.Field, stacked columns; App
  `MainWindow.GraphCanvas.cs`: title bars, pin rows, wires coloured by source kind
  pose/bool/float/id/vector, dot grid). Edited spots pulse (App.axaml
  `Border.glowPulse`/`Path.glowPulse`): yellow/green graph edits, BLUE for clip
  nodes playing a Blender-edited clip (+ faint on containers). Graph tab shows the
  addon's own graph (`GraphStatus.ReadAddonOwn` via AddonOriginals.Before); edits
  stay keyed by the CS2 path (`_graphPath`); different node layout = read-only;
  graph list marks "● addon". 96 tests. Checked in the app on a scratch workspace
  (layout, pulse, blue idle_ak glow); addon-graph display covered by a unit test
  only (user's M4 graphs were removed by their reset apply).
  Player-model hookup helper: user said LATER.
- 2026-10-26 (uncommitted): USER RULE: vdata has NO ties to other tabs. Done:
  Deliver/All outliner colour ignores addon vdata (HasAddonOwn/AddonOwnSummary
  vdata only on the VData aspect); planner no longer adds AddonChecks.Models
  (kept for the VData tab's own m_szWorldModel warning); Deliver WeaponProblem
  uses plan.BrokenReferences, not vdata; Skeleton tab m_szWorldModel note +
  "Open the VData tab" button removed; `CustomSkeletons.FindInAddon(connection,
  addon, weaponId)` finds the addon's skeleton from its custom clips (model = an
  addon .vmdl_c targeting it, may be null -> Adopt uses UseFile); vdata-based
  preview model (PreviewModels.Effective/FromAddonVData) removed (CLI matches app).
  Kept read-only: the slot line (WeaponSlots reads class from vdata). VData rows
  got a ↺ "back to CS2's value" button. Animations tab outliner: orange Blender
  mark (ExplorerNode.HasBlend) for weapons whose sandbox has a .blend. Checked in
  the app (scratch build): marks show, M4 white on Animations, ↺ column renders.
- 2026-10-25: optional Blender add-on install (Animations tab "Blender add-on…",
  CLI `blender addon status|install|remove`, Core `BlenderAddon` + bridge
  install_addon.py). Installs a loader `weaponlab_bridge_loader.py` into Blender's
  user scripts/addons pointing at the app's blender folder + weaponlab.exe
  (sets WEAPONLAB_CLI), enables it and saves prefs (runs WITHOUT
  --factory-startup so user prefs are kept). Verified on the user's Blender 5.2:
  install -> enabled + panel registered at startup; remove -> gone. Left NOT
  installed (user clicks it). Prefs backups: .claude/scratch/blender-backup.
- VERIFIED IN GAME 2026-10-25 (user): a custom skeleton works with vdata
  m_szAnimSkeleton left at CS2's value. Test: weapon_m4a1 vdata = pyramid model +
  stock m4a4.vnmskel, clips on weaponlab_m4a4_pyramids: animations and pyramids
  worked (model lacked physics, so it looked a bit odd). Only m_szWorldModel
  matters. Weapon Lab removed every vdata-skeleton warning; `AddonChecks.Models`
  only flags an empty or missing m_szWorldModel. Also: GraphOwners(root) = none
  (the root plays C4/Healthshot clips itself; they no longer show in the Deliver
  tree, and root graph edits are never skipped by those weapons being Default).
  Build window label: "CS2 default for this slot".
- 2026-10-25: weaponlab_skeltest now has player models hooked to viewmodel_custom
  (Workshop-style hookup): models/characters/weaponlab/ctm_weaponlab.vmdl_c (from
  ctm_sas) and tm_weaponlab.vmdl_c (from tm_leet_varianta), made with
  ReferencePatcher (only hudmodel viewmodel.vnmgraph -> viewmodel_custom swapped;
  meshes stay CS2's). Weapon Lab detects both as hooked. Players only get them
  via SetModel: cs_hl_template does it in a map point_script (scripts/vscripts/
  swappers.js, OnPlayerReset -> player_pawn.SetModel). Their HEV .vmdl sources
  list AnimGraph2 hudmodel = viewmodel_custom. The stock-path root copy is still
  in the test addon. Script for it: content scripts/vscripts/weaponlab_models.js
  (compiled to game .../weaponlab_models.vjs_c; CT -> ctm_weaponlab, T -> tm_weaponlab,
  deferred SetModel like swappers.js; needs a point_script named
  script_weaponlab_models with cs_script scripts/vscripts/weaponlab_models.vjs).
  NOT tested in game yet. Bash heredocs here turn '\' into '\x27: avoid backslash
  literals in C# written through heredocs.
- 2026-10-25 (uncommitted), user decisions: (1) DELIVERY NEVER WRITES VDATA: Apply
  delivers clips, graphs and skeleton files only and keeps the manifest's vdata
  entries; only the VData tab's Save (DeliveryScope.VDataOnly) writes vdata. A
  custom skeleton the slot's vdata does not name gets a warning (set it in the VData
  tab). VData no longer counts toward the weapon's overall/Deliver status.
  (2) Weapon panel removed: status/commit/slot/Blender build in the Property
  Browser ("In the addon"); History fills the column. (3) Deliver opens in Tree
  view; weapon rows and clip rows have COMMIT checkboxes (weapon = Animations
  Custom/Default, clip = WeaponProfile.ExcludedClips); leaving out something that
  breaks the game turns red with a tooltip (a clip a custom skeleton needs; a
  weapon whose delivered skeleton the addon vdata still names). (4) Edited clips
  not delivered are yellow in the outliner. NOTE: the user delivered an M4 idle
  edit + graphs from the normal workspace; the scratch ws plan would remove them
  (flagged as another workspace) - do not apply from ws without asking.
- 2026-10-25 (uncommitted): FIXED Blender preview binding for real: the 0.11
  head-match check compared bones the glTF importer re-orients, so even stock
  weapons had only 2 bones following (bolt/mag never moved) and arms broke.
  Removed; every shared bone follows. Agent sleeves weighted to body bones the
  rig lacks (clavicle/spine, e.g. tm_leet) now ride their nearest followed
  descendant (was an 80-unit stretch). Verified: M4 37/37 weapon bones, leet
  sleeves render on the arm. UI per user feedback: Skeleton node coloured by
  status; vdata checkbox removed (always writes); weapon model = Default (CS2's,
  or the skeleton's model) or Custom via file browser (`PreviewModels.FromFile`:
  addon .vmdl_c, or content .vmdl with compiled file); custom-skeleton model
  picked the same way; panel preview list and Custom skeleton dialog removed
  (panel shows a Build .blend summary). 92 tests.
- VERIFIED 2026-10-25 (read-only, CS2 root viewmodel graph): the root graph
  switches on control parameter `weapon_type` (165 CNmIDComparison nodes with
  values like weapon_cz75a, weapon_revolver) plus weapon_category. The game
  sets it from the weapon class (m_szAnimClass when set, else _class; inferred,
  matches the user's M249-with-AUG-class). 2026-10-27 vdata scan backs this:
  CS2's OWN variants set m_szAnimClass to their graph ID (weapon_mp5sd class
  mp7, m4a1_silencer class m4a1, usp_silencer class hkp2000, revolver class
  deagle); cs_hl_template remaps 10 (e.g. ssg08 -> weapon_nova, nova ->
  weapon_sawedoff). weapon_template/projectile_mayhem have a placeholder
  weapon_m14 (class weapon_m14, m_szAnimClass "weapon_temp", empty model and
  skeleton, no graph mentions weapon_temp).
- VERIFIED IN GAME 2026-10-27 (me + user): MADE-UP ANIM CLASSES WORK. CS2 sends
  m_szAnimClass to the graph's weapon_type as written. Test in
  weaponlab_skeltest2 (stress workspace, profile `animclass`): root graph
  nodes 38 + 65 (WPN/Pistols/SM entry override + global transition, the only
  ways into the CZ75 state) renamed weapon_cz75a -> weapon_ar2. Anim class
  weapon_ar2 -> CZ75 plays its own animations; vdata rolled back
  (weapon_cz75a) -> "USP everything" (Pistols SM default state = USP, index 1).
  So addons CAN add slots: made-up anim class + a branch in their own root
  graph. Weapon Lab can only rename existing checks today; adding a branch
  needs structural graph edits. worldmodel.vnmgraph and uimodel.vnmgraph also
  check weapon_cz75a (third-person/UI not checked). Custom graph confirmed live
  by skeltest2's AK reload edit. Hookup = test-only stock-path copy.
  Afterwards (user request) weaponlab_skeltest2 was DELETED (game + content
  folders sent to the Recycle Bin); the stress `animclass` profile moved to
  .claude/scratch/stress/backups. Test addon left: weaponlab_skeltest.
  In-game test notes: Tools mode console = VConsole2 window, opened by the
  user's console key ' (not `); inspect = J, F = slot7. `bot_quota 0; bot_kick
  all; mp_warmup_start; mp_warmup_pausetimer 1; god` gives a clean session.
  Single screenshots mislead: use frame bursts (draw/fire/reload).
- VERIFIED IN GAME 2026-10-27: A BRAND-NEW VDATA BLOCK WORKS AT RUNTIME. Test:
  weaponlab_skeltest's vdata source got a hand-appended `weapon_ar2` block
  (copy of weapon_ak47, _class weapon_ak47, m_szName weapon_ar2, clip 50),
  compiled with resourcecompiler (-nop4 -fshallow -i), files restored byte for
  byte afterwards. `give weapon_ar2` = silent no-op (give uses the item
  schema). `subclass_change weapon_ar2 weapon_ak47` -> "Changed entity 250 from
  subclass weapon_ak47 -> subclass weapon_ar2", reload filled to 50, kept
  through a round restart. Second run (user in game): weapon_ar2 given
  m_szAnimClass weapon_m4a1 and the stock M4A4 m_szWorldModel. subclass_change
  on a given AK -> animations switched to the M4A4's at once, model stayed the
  AK (model is set at spawn; anim class is read live). `ent_create weapon_ar2`
  DOES work (spawns where the player looks; my earlier try just was not seen)
  and shows the M4A4 model. So a new block = a complete new gun when spawned
  from it: native _class behaviour + own model, stats, anim class. Reached
  through entities/scripts (ent_create, map entities, subclass_change), not
  give or the buy menu (untested: Hammer-placed subclass, cs_script, HUD/kill
  icons, third person). cs_hl_template already does this: its vdata adds new
  blocks weapon_sphield (decoy) and weapon_hegrenade_sticky, and
  sticky_nade.js switches with the entity input "ChangeSubclass" followed by an
  explicit weapon.SetModel(...) (its comment: that exact pair worked), which is
  why the model changes there; ChangeSubclass alone keeps the model.
  GUNSHOT IS FORCE-INHERITED (verified 2026-10-27, map reloaded): real
  weapon_ak47 block WEAPON_SOUND_SINGLE = Weapon_M4A4.Single, weapon_ar2 block
  = Weapon_AWP.Single -> normal AK fired M4A4, weapon_ar2 ALSO fired M4A4.
  A dupe's gunshot comes from its parent gun's block (the class's own block /
  item definition), not its own; its clip, model and anim class do come from
  its own block. User has seen "a lot of stuff gets force inherited" when
  duping guns before. SORTED (user in game, 2026-10-27, casual mode):
  OWN block: m_iMaxClip1, m_szWorldModel (at spawn), m_szAnimClass,
  m_bIsFullAuto, m_flCycleTime, m_flSpread, m_flMaxSpeed,
  m_nPrimaryReserveAmmoMax, m_nKillAward (5000 paid 2500 = casual's half
  kill reward, so own value). PARENT block (weapon_ak47): WEAPON_SOUND_SINGLE,
  m_nDamage, m_flRecoilMagnitude. So a dupe can differ in fire mode/rate,
  spread, speed, ammo, model, anims, reward, but shares the parent class
  block's damage, recoil and gunshot (repurpose the class block, or script it).
  HUD/kill-feed icon and name not checked.
  Weapon Lab's vdata pipeline cannot add blocks yet. Note: skeltest's vdata
  hashes did not match its delivery manifest before this test (changed
  outside Weapon Lab earlier); left as found.
- MORE VDATA KEYS (VERIFIED IN GAME by me on PC2, 2026-10-29, real blocks):
  WORK: m_flRecoilMagnitude [0,0] (+variance 0) = zero recoil, every shot on one point;
  m_flRecoilAngle [90,90] (+variance 0) = spray goes purely sideways; m_flInaccuracyMove
  [0,0] = running spread sd 7 vs 18-22 stock; m_flAttackMovespeedFactor 0.2 = speed while
  firing 46 (=230*0.2) vs 240; m_bIsRevolver on CZ75 = R8 hammer delay (first shot 0.219 s,
  same as the R8; P250 0.016 s); m_bUnzoomsAfterShot false on AWP = stays scoped after a
  shot; WEAPONSILENCER_INTEGRATED on SG553 = permanently silenced; m_aShootSounds on a
  real block: SINGLE -> AWP shot, NEARLYEMPTY -> C4 beep (verified with snd_showstart).
  NO EFFECT SEEN: ITEM_FLAG_EXHAUSTIBLE on a gun (stays at 0/0), ITEM_FLAG_DOHITLOCATIONDMG
  (body dmg normal; head untested), m_bMeleeWeapon on a gun (still shoots), shared
  m_nPrimaryAmmoType (reserves stay per weapon), taxonomy burns_on_contact on HE (plain
  blast), molotov m_nDamage/m_flRange (fire ramp 2->8/tick same as stock inc; fire dmg =
  convars), decoy m_nDamage (no dmg), m_szBarrelSmokeParticle (+threshold/timeout: no
  smoke), m_szWorldModelAg2Override (third person still the AK).
  UNTESTED/INCONCLUSIVE: flinch (scripted TakeDamage gives no tagging at all; needs real
  hits), m_bAllowFlipping (left-hand mode would not switch on from console), zoom/iron
  sight on M4A1-S (attack2 is its silencer toggle; class wins), m_WeaponCategory /
  m_DefaultLoadoutSlot (account loadout lacked the M4A4), m_bHideViewModelWhenZoomed
  (AWP scope overlay hides it anyway), m_szMuzzleFlashParticleConfig (format unknown).
  Tool: `snd_showstart 1` logs every sound file + time in console (verified); it also
  confirmed the animation test's retimed reload sounds play at frames 5 and 42.
- VERIFIED 2026-10-29 (read-only, CS2 graphs/clips): MULTIPLE RELOADS + SHELL
  LOOP. Every weapon's gun graph is the shared viewmodel_gun.vnmgraph
  (+<weapon> variation, ssg08 included), so all have the same reload logic:
  * Partial vs empty: a ClipSelector on ammo == 0. CZ75 (own graph
    viewmodel_gun_cz75) adds a set chosen by weapon_extra_info ==
    weapon_cz75_front_mag_removed -> reload/reload_empty/reload2/reload2_empty.
    AK: partial_mag_reload and empty_mag_reload (nodes 63/66, 73/76) share
    data slot 2 = reload_ak; a separate empty reload needs a NEW data slot
    (structural; m_nDataSlotIdx is read-only in the editor).
  * Shell by shell (Nova): ONE clip reload_nova (50 f) with gameplay events
    INTRO 0, LOOP 11, ADD_AMMO 20 (game adds a round), OUTRO 24, pump after.
    Transition conduit: at the OUTRO event, if action == action_reload and
    reload_stage != stage_outro, jump (0 s) back to the LOOP marker,
    ping-ponging Reload0/Reload1. Pose at OUTRO must match LOOP. XM1014:
    long clip with 4+/3/2/1-shell entry markers picked by max - current ammo
    at WPN_RELOAD_STAGE_END. Per-shell behaviour = vdata
    m_bReloadsSingleShells true (set on weapon_nova itself) +
    taxonomy.reloads_per_shell (Nova inherits it from `shotgun`); SSG08 has
    neither (setting them on ssg08 untested). Nova disallows attack 0.467 s
    after reload start, SSG08 3.7 s. SSG08 also: zoom levels 2.
  * USER-VERIFIED IN GAME 2026-10-31: m_WeaponType = WEAPONTYPE_SHOTGUN on
    the ssg08 block made the game use the Nova's skeleton slot and clips
    although the rest of the block was right; WEAPONTYPE_SNIPER_RIFLE fixed
    it. The user's Annabelle (SSG08 slot, custom skeleton) now works in game
    after the user fixed a setting in their .vmdl (details not given).
  * Clip event kinds in CS2 viewmodel clips: CNmSoundEvent 2125, CNmIDEvent
    916, CNmParticleEvent 113, CNmMaterialAttributeEvent 15, CNmLegacyEvent 4.
- 2026-10-29: Weapon Lab reads only COMPILED models (.vmdl_c). The user's
  cs_hl_template SSG08 sandbox custom skeleton came from the July 2026
  weapon_annabelle.vmdl_c, built on the Nova's mesh/rig (source DMX named
  weapon_shot_nova_ag2_*; bones loader/pump/shell/trigger like nova.vnmskel)
  plus 5 Blender FBX leaf bones (loader/pump/shell/trigger/ag1_hand_r _end).
  New annabelle.blend not compiled yet. User decisions for the next build are
  in PLAN (vmdl_c-only pickers + compile warning, Anim Events tab, leaf bones).
  User: memory/plan on THIS PC are the source of truth (PC2's discarded).
- Still open (2026-10-25): role of vdata m_szAnimSkeleton (always set together
  with the model in tests). Core `Weapons/WeaponSlots`.
- 2026-10-25 (uncommitted, 0.13): full feedback build: Build .blend window
  (`BuildBlendDialog`: slot, game vs addon clips, skeleton own/addon/custom,
  write-vdata toggle, preview model, sleeves/gloves via `ArmsPreview`; choices
  in WeaponProfile BuildFromAddon/ArmsModel/GlovesModel/WriteSkeletonVData;
  skeleton vdata goes to every block playing the slot); weapon panel slot line;
  Deliver Tree by weapon + Shared, root graph expands to re-routed and all routed
  weapons; outliner Skeleton node (lazy bone tree, ● ○ ⚠, bone properties);
  graph glow (BoxShadow on edited nodes, faint on containers, glow stroke on
  edited transitions); dialog strings shown via TextBlock (Avalonia eats "_").
  91 tests; UI checked via screenshots (window, skeleton node, tree).
- 2026-10-25 (uncommitted, 0.13 work): THE ADDON IS THE PROFILE (user decision
  2026-10-25): `ProfileStore.ForAddon` = one profile per addon named after it
  (first use copies an older profile that targeted it); app top bar = Addon
  picker + "N weapons with undelivered changes"; profile bar (New/Save as/
  Import/Export) removed; CLI `profile target` switches to the addon's profile.
  SANDBOX SOURCE (user decision): Build .blend asks "Build from existing addon"
  vs "Build from game files" when the addon has clips or a skeleton for the
  weapon. `SandboxManifest.SourceAddon`, `SandboxClip.AddonClip`,
  `SandboxStore.AddonClips/Rebuild`; weapon skeleton read from game clips.
  Verified headless: pyramid M4 from the addon = 5 addon clips, adopted 39-bone
  skeleton, Blender input carries the pyramid motion, unedited delivery writes
  no clips. 90 tests. App top bar checked via UI Automation.
- 2026-10-24 (uncommitted): (1) FIXED Blender arm preview regression from 0.11: the
  0.5-unit head-match rule now applies only to weapon previews (arms/gloves come
  from an agent model and follow every shared bone; verified 46 bones, gap 0).
  (2) Build .blend offers the target addon's own custom skeleton
  (`CustomSkeletons.FindInAddon` / `Adopt`, setup `FromAddon`; CLI `skeleton
  adopt`): templates re-target to it, delivery writes only clips + vdata and
  leaves the addon's skeleton and model alone. Verified headless on the pyramid
  M4 (39 weapon bones in Blender). KNOWN GAP: templates still come from CS2's
  clips, not the addon's own clips, so delivering replaces the addon's clip
  motion (originals kept) — the open "edit from the addon's clips" item.
- FIXED 2026-10-24: app crash on Build .blend for a weapon without a sandbox
  (Progress created inside Task.Run → SetStatus on a pool thread → Avalonia
  InvalidOperationException). SetStatus/Log now post to the UI thread; progress
  reporters are created on the UI thread.
- Model compiling FACTS 2026-10-24: resourcecompiler compiles .vmdl (formats
  FBX, DMX, SMD, OBJ; not glTF). SMD import turns the whole model +90° about Z
  (pre-turn root and vertices by -90°). Bones without vertices are pruned even
  with BoneMarkupList bone_cull_type None (kept with a microscopic triangle).
  The stock M4A4 model's root entry is (18.23,-0.19,12.40) with its mesh in that
  frame; m4a4.vnmskel has weapon/weapon_offset at the origin and also econ and
  muzzle, which the model lacks; the model has sight + ag1_hand_r the skeleton lacks.
- 2026-10-24: Delivery safeguards (0.12, from that breakage): manifest
  records `workspace`; `AddonChecks.OtherWorkspace` warns when the last
  delivery came from another workspace; `AddonChecks.BrokenReferences` warns
  about addon graphs/models outside the manifest whose RERL names a file the
  delivery removes. App asks before Apply and VData save when either fires;
  CLI deliver refuses without `--confirm true`. 88 tests pass (one replays it).
- Next: user tests the Galil/AR2 in weaponlab_skeltest; 0.13 Source 2
  Viewer-style graph canvas; skeleton viewer; HL2 v_irifle as AR2 reload
  reference.

## UI direction (user, 2026-10-23)

- The user wants the layout and feel of Hammer 5 Tools: dark, dense, a tool
  tab strip on top (one tab per editor), resizable docked panels (explorer
  tree with filter on the left, property browser, large center work area,
  list + History on the right), compact status bar with the addon picker at
  bottom right. Keep new editors in this style; styles live in `App.axaml`.

## Decisions

Made:
- 2026-11-15 (user, hard rule): NEVER put the user's personal email in the repo:
  not in notes, docs, code or commit metadata. Public commits use the GitHub
  no-reply address (Froddoyo@users.noreply.github.com, repo-local user.email).
- 2026-11-15 (user): the repo is open source and .claude/ may be public ("I'm
  not hiding the fact that this is vibecoded"). What must stay out: game or
  third-party assets, and the user's email.
- 2026-10-23: UI stack is .NET 10 + Avalonia 12.1.4 (user choice).
- 2026-10-23: License is MIT (user choice).
- 2026-10-23: VRF via NuGet `ValveResourceFormat` 20.1.7467 (CS2AL used 20.0.6980
  as local DLLs). Central package versions in `Directory.Packages.props`.
- 2026-10-23: Tests use xunit.v3 on Microsoft.Testing.Platform (`global.json`
  opt-in; .NET 10 `dotnet test` rejects VSTest). Run with
  `dotnet test --project tests/WeaponLab.Core.Tests`.
- 2026-10-23: Workspace default is `F:\CS2WeaponLab\workspace\` (user choice:
  inside the repo, one gitignored folder via `/workspace/`). Found by walking
  up from the build output to `CS2WeaponLab.slnx`; builds outside the repo
  fall back to `%LOCALAPPDATA%\CS2WeaponLab\workspace`. Override with
  `WEAPONLAB_WORKSPACE` or CLI `--workspace`. The user's `c4` sandbox was moved
  there from AppData (hash-verified) and the AppData folder removed. Sandbox folder
  `sandboxes/<weaponId>/` with `sandbox.json` (schema `weaponlab.sandbox/1`,
  camelCase) and `templates/<vpk path>` byte copies. Created via `.staging/`
  then moved; never overwrites an existing sandbox.
- 2026-10-23: Pushing from Claude sessions fails (Git Credential Manager can't
  prompt); the user pushes from their own terminal.
- 2026-10-23: No Git LFS yet; binaries are marked in `.gitattributes`. Add LFS
  only if the repo ever ships binary assets.
- 2026-10-23: CS2WeaponLab is the new project; CS2AnimationLab is read-only
  reference.
- 2026-10-23: All agent files live in `.claude/`; disposable work goes in
  `.claude/scratch/` (gitignored).
- 2026-10-23: Architecture direction: desktop app owns project state and the
  export pipeline; Blender add-on becomes a thin bridge (see `PLAN.md`).
- 2026-10-29: One memory and one plan per PC (user choice), so the two
  clones never edit the same notes: PC1 owns `.claude/MEMORY.md` and
  `.claude/PLAN.md`, PC2 owns `.claude/pc2/`. Rules in CLAUDE.md "Two PCs".

Pending (ask the user):
- Graph editor scope: routing first, or real node-parameter editing early?
- Sound authoring: create new soundevent definitions and import WAVs, or only
  reassign existing events?
- Migrate old `.csal` saves and the AR2 project later, or start clean?

## Legacy CS2AL: what exists (read 2026-10-23)

Location: `F:\CS2AnimationLab\CS2AL` (version 2.0.1-preview.2, VRF 20.0.6980,
private .NET 10.0.10 runtime, Blender 5.2+, Windows x64). Its docs:
`CS2AL/README.txt`, `CS2AL/VALIDATION.md`, `CS2AL/source/README.md`, root
`PROJECT_MEMORY.md`.

C# helper `CSALClip` (`CS2AL/source/backend/`), reusable algorithms:
- `PortableImport.cs`: VPK scan of `animation/anims/viewmodel/**.vnmclip_c`,
  grouped per weapon folder; keeps clips on skeleton
  `animation/skeletons/characters/viewmodel.vnmskel` with at most one secondary
  (weapon) stream. Import writes source templates, gzip pose dumps, skeleton
  JSON, graph index and untextured GLB previews (weapon, `ctm_sas` sleeves,
  `glove_hardknuckle`), then stock sounds. Fingerprint = clip hashes + graph
  hash + model CRCs + skeleton hashes + importer version string.
- `Program.cs`: `build-blender` / `build-dual` apply Blender edits as deltas
  against the decoded template, re-encode only changed tracks (quantized
  ranges, 48-bit quaternions with dropped component), keep untouched tracks'
  bytes, verify metadata fingerprints and pose error tolerances.
  `ReplaceData` requires DATA as the final block. Root-motion children use an
  axis fix quaternion (.5,.5,.5,.5).
- `SoundEventTools.cs`: rename/retime existing `CNmSoundEvent` entries by
  index; times normalized 0..1; verifies all other events unchanged.
- `GraphTools.cs`: rewrite `m_resources` slots and RERL entries; resource IDs
  are MurmurHash64B of the lowercase path, seed 0xEDABCDEF (validated against
  all shipped refs); other blocks are copied verbatim with 16-byte alignment.
- `StockSounds.cs`: parse all `.vsndevts_c` in csgo + core, resolve
  `base`/`soundevent_*` chains, decode requested `.vsnd_c` audio to cache.

Blender add-on (`CS2AL/addon/cs2_animation_lab/`): setup and background import
(`portable.py`), headless library builder (`library_builder.py`), paired
Arms/Weapon slots in one Action and hand-follow constraints (`rigging.py`),
pose sampling and export orchestration (`exporter.py`), graph staging
(`graphs.py`), delivery with backup/rollback (`delivery.py`), soundevent KV3
text parser and preview (`sounds.py`), `.csal` save format
(`animation_store.py`).

Export layout: custom clips at `animation/anims/custom/<weapon>/<clip>`;
reachable graphs cloned to `*_custom.vnmgraph`; root
`animation/graphs/viewmodel/viewmodel_custom.vnmgraph`.

## Known limits to fix in Weapon Lab

- No retiming: frame count and duration must match the template.
- Sound events: cannot add or remove; no soundevent definitions or audio
  authoring; edits keyed by event index.
- Graph editing is only resource redirection.
- The player model's `m_animGraph2Refs` hudmodel entry must be pointed at
  `viewmodel_custom.vnmgraph` manually.
- Per CS2AL memory (2026-10-22): bundled VRF rejects the current
  `weapons.vdata_c` RED2 metadata (KV type 26 / 0x1A); its DATA block decodes
  when isolated. A VRF upgrade is needed before a vdata editor.
- State lives as JSON strings in Blender scene properties; UI is five stacked
  N-panel sections.

## Blender build notes (verified 2026-10-23)

- Blender 5.2.3 LTS at `F:\SteamLibrary\steamapps\common\Blender\blender.exe`;
  an old Blender 3.6 is in Program Files (locator rejects < 4.4).
- VRF glTF: keep `ExportAnimations = true` with a filter matching nothing,
  or the GLB has no skeleton. Weapon model lookup uses `Model.Data`
  (`m_vecNmSkeletonRefs`), not `Model.KeyValues`.
- Rest poses: `m_parentSpaceReferencePose` = [tx,ty,tz, scale, qx,qy,qz,qw]
  (matches VRF's decoder). Arms root_motion children get the axis fix
  (.5,.5,.5,.5) conversion, as in CS2AL.
- Bones under near-zero-scale parents (e.g. grenade `pin` at 0.0001, which
  hides it) can't be recovered exactly through Blender matrices (seen up to
  0.07 units local, invisible in world). Export must work in deltas and
  ignore unedited tracks, as CS2AL did.
- `chicken`/`egg` have no hand-rig clips; Build is disabled for them.
- 2026-11-07, verified in a Blender 5.2 probe: `edit_bones.new()` makes a
  zero-length bone, and setting `.matrix` on it keeps only the head, so
  every bone `create_rig` makes rests pointing along +Z (roll 0). Poses and
  exports are unaffected (keys are computed from the actual `matrix_local`);
  only rest/edit-mode display is off. Not fixed. New bones that need a real
  rest orientation: set `tail = (0, 1, 0)` before `.matrix`.
- 2026-11-07, verified (probe, then 0.22.4 headless): a Weapon-rig bone
  ignores the gun's motion when its parent is a hidden helper bone with
  COPY_TRANSFORMS (WORLD) from the Arms rig *object*, rest = inv(wpn rest).
  Bone constraints alone (Child Of etc.) cannot cancel the rig object's
  motion; the helper parent can (gizmo should act as with any parent; not
  checked in the Blender UI).

## Install facts (verified 2026-10-23)

- CS2 build 1.41.9.2, client 2000935, Oct 22 2026 (`game/csgo/steam.inf`).
- `game/bin/win64/resourcecompiler.exe` exists.
- Blender at `F:\SteamLibrary\steamapps\common\Blender` has version folders up
  to `5.2`; exact patch version not checked.
- .NET SDKs: 6.0.408, 8.0.100, 10.0.302.
- `game/csgo_addons` holds 16 addons plus non-addon folders `vpks`,
  `workshop_items`; `h_conflux` and `de_flick_lambda` have no content folder.
- csgo VPK: 68 viewmodel weapon folders, 674 clips, laid out
  `animation/anims/viewmodel/<category>/<weapon>/`, categories `arms`,
  `equipment`, `grenade`, `knife`, `pistol`, `rifle` (SMGs/shotguns/MGs are
  under `rifle`), plus `chicken` and `egg` with no category. Folder ids look
  like `rifle_ak`, `pistol_deagle`. Several weapons (e.g. M4A1-S, USP-S) have
  no folder of their own; how they map to clips is unverified.

## Second PC "PC2" (spare test machine)

- 2026-10-29: PC2's setup (repo `C:\CS2WeaponLab`, CS2/.NET/Blender paths,
  VDATA LAB, test status) moved to `.claude/pc2/MEMORY.md`. PC2 keeps its
  own memory and plan there; check its "For PC1" section after pulling.
  PC2 results recorded before the split stay in this file (sections below).

## For PC2

Requests for the spare test PC. PC2 copies what it uses into its own plan;
it does not edit this file.

- 2026-11-15 THE REPO IS PUBLIC NOW, CLONE IT AGAIN: development moved to the
  public repo github.com/Froddoyo/CS2WeaponLab (fresh history, .claude/
  included). The old repo is the private archive `CS2WeaponLab-Private`. Your
  clone at C:\CS2WeaponLab has the old history: keep your untracked files
  (workspace, VDATA LAB, scratch), clone the public repo fresh, then run
  `git config core.hooksPath build/hooks` and
  `git config user.email Froddoyo@users.noreply.github.com` in it. Never push
  the old clone's history to the public repo. New hard rule in CLAUDE.md: no
  game, Valve or third-party asset files and never the user's email in the
  repo; the pre-commit hook refuses asset files.
- 2026-11-15 HANDOFFS GONE: the user had `.claude/handoff/` removed (the
  annabelle-loop and orb-battles packages are no longer needed). Do not rely on
  them; references to them in either PC's notes are history only.

- 2026-11-14 0.26.0 HAS THIRD PERSON now (untested in game): Weapon Lab can
  deliver an edited world clip, e.g. world reload_ak, to
  `animation/anims/custom/<weapon>/world/` with `worldmodel_custom.vnmgraph` +
  `worldmodel_gun_custom.vnmgraph+ak47.vnmgraph`; it does NOT edit player
  models: point their "" / `worldmodel` AnimGraph2List entries at
  worldmodel_custom by hand. The questions below can use that build instead of a
  hand-copied graph. Pull first.
- 2026-11-14 THIRD PERSON QUESTIONS (file facts in PC1 MEMORY "Third person
  (worldmodel)"). When convenient, in the lab with custom agents, no root copy:
  1. Which agent graph ref drives third person, "" or `worldmodel`? Copy
     worldmodel.vnmgraph to a custom path with one visible change (e.g. the AK
     reload clip swapped in worldmodel_gun+ak47) and point one ref at a time
     at it. View through a bot with that agent and/or `thirdperson` with
     sv_cheats (CS2 cvar not verified here).
  2. Do other clients/bots see the custom third-person graph (both refs patched)?
  3. Do server hitboxes follow the third-person pose, and from which graph
     (stock or the agent's)? Matters for fairness if custom anims move the head.
  4. What do others hear when a player reloads? World clips have no sound
     events: change a first-person reload sound via the custom hudmodel graph
     and listen to a bot or a second player.
  5. Made-up anim class: which third-person state does the root fall into
     (category default?).

- 2026-11-07 THANKS for the skeleton answers (used in 0.23.0: opt-in "Keep
  apart from the gun" props, a model check that warns when a model's
  parents differ from the skeleton's, no more model copy). The knife slot CAN
  be shown offline: the user spawns it from the console while looking at the
  ground, `ent_create weapon_knife_ursus` (it then uses the vdata model;
  `give` shows the default knife). Worth an in-game check of 0.23.0 when
  convenient: tick a bone (e.g. M1's `prop`) as "Keep apart from the gun",
  Build .blend, export idle/lookat01, push; it should hold still on screen.
  Still open from your list: the two-root skeleton and "Do Not Discard
  Skeleton Bones".

- 2026-11-07 RIGOROUS CUSTOM SKELETON TESTS (user: "set up a plan for pc2 to
  do some rigorous testing on this whole skeleton thing"; the user tries the
  bong fix on PC1 in parallel). Pull first; build Weapon Lab 0.22.5 from source.
  UPDATE (later 2026-11-07): the user's bong fix WORKED on PC1 (lighter under
  `weapon` in the model -> held still as exported), so expect M1 to pass.
  Still wanted: what exactly M2 does (question 2), the bind offset (3), the
  unanimated bone (4) and the two-root option (5).
  BACKGROUND. A custom skeleton (`skeleton create <weapon> --model ...
  [--model-hierarchy true]`) is built from a model's bones with ONE root,
  `weapon`, at CS2's rest; since 0.22.2 any other parentless model bone is
  hung under `weapon` at its model place. Clips are re-targeted onto it and
  delivered with the skeleton. The user's model points at the skeleton with
  ModelDoc's NmSkeletonReference (Source File = `animation/skeletons/weapons/
  <name>.vnmskel`, no `_c`; compiled fine on PC1). 0.22.4 "props": a model
  root other than `weapon` (the user's bong lighter, `lighter_offset`) is held
  still in view in Blender; export writes its track relative to `weapon` so
  it cancels the gun's motion (the gun moves by the arms' `wpn`, which carries
  the whole weapon model). PC1 IN GAME (user, knife_ursus in cs_hl_template):
  the lighter plays its keys but sits "really far away" and still moves with
  the bong. The user's .vmdl has lighter_offset as a ROOT bone; the skeleton
  has it under `weapon`. Hypothesis: CS2 applies each track relative to the
  bone's parent in the MODEL, so a model root takes it relative to the model
  origin. Details: PC1 MEMORY "2026-11-07 IN GAME".
  SETUP. Lab addon weaponlab_vdatalab, wl_vdlab, custom agents, no root copy
  (your test rules). Slot knife_ursus (the user's case); repeat the key
  result on rifle_ak if time allows. Make ONE test model in two variants that
  differ only in the prop's parent, same bind places: a gun-like box weighted
  to `weapon` (+ the slot's other CS2 bones) and a small, clearly visible
  cube weighted to a bone `prop` about 6 units beside the gun.
    M1: `prop` under `weapon` in the model.   M2: `prop` a model root.
  (Blender headless -> FBX without leaf bones, a .vmdl with an
  NmSkeletonReference, resourcecompiler. Give `weapon` an off-origin bind,
  e.g. (0,0,0.5), as CS2 models and the user's model do.) Make the custom
  skeleton once (from M1, model hierarchy; M2 gives the same skeleton) and
  push the clips once; switch the weapon between M1 and M2 (m_szWorldModel or
  wl_model) so only the model changes.
  TRACKS for `prop` (engine parent-local under `weapon`; write a pose file
  and use `weaponlab export-clip`, format in pose_io.py; desired = stock +
  (edited - reference), so start from the build's reference sample):
    R  rest on every frame (as untouched clips carry it);
    S  still in view: inv(weapon(t)) * inv(wpn(t)) * H, wpn(t) = the arms
       stream's wpn in ARMS MODEL space (engine, no Blender axis fix),
       H = its frame-0 place (what 0.22.4 exports; also check root_motion is
       still in the clip, S assumes it);
    K  rest + 10 units along weapon +Y on every frame (shows which frame the
       track is applied in).
  Put them in idle1 (small sway) and lookat01 (big motion); draw if cheap.
  QUESTIONS (answer each for M1 and M2, with R, S and K):
  1. Where is `prop` relative to the gun and to the screen; does it move with
     the gun? Expected if the hypothesis holds: M1 = R on the gun, S still on
     screen, K 10 units along the gun; M2 = wrong frame (far off, moving).
  2. In M2, which frame is the track applied in: the model origin at wpn,
     or the viewmodel/arms origin? (K and R tell.)
  3. Does the model's off-origin `weapon` bind shift `prop` in M2 as in M1
     (the "really far away" offset)?
  4. Unanimated: a third bone in the model that is NOT in the skeleton (no
     track). Where does it sit, and does it follow the gun?
  5. Optional, if you can build one by hand: a skeleton with TWO roots
     (`weapon`, `prop`) for M2. Does CS2 load it, and does S/K behave?
  6. Quick checks on M2: vdata m_szAnimSkeleton stock vs custom (PC1 found
     it does not matter, 2026-10-25); ModelDoc "Do Not Discard Skeleton
     Bones" on vs off.
  REPORT under "For PC1": a table (model x track x clip -> where the prop
  is, follows gun yes/no), the frame CS2 uses for each case, verified vs
  untested, and anything that changes the Weapon Lab design (PC1 PLAN feedback
  queue item 4: warn on hierarchy mismatches; mark props without relying on
  "parentless in the model"). Screenshots only for checks, delete raw frames
  after. Stock files untouched; deliveries backed up.

- 2026-11-03 IN-GAME TESTS FOR "PLAYS LIKE" (user: "Queue for PC2"; design
  in PC1 PLAN "Direction: CS2 gun sandbox", item 1). Weapon Lab will let a
  user pick which CS2 gun a weapon plays like (weapon type picks the root
  graph's group, anim class the gun in it, an unknown class gets the group
  default) separately from the visual slot it animates in. Please answer:
  1. Pistol-group fallback: give a pistol block an anim class from another
     group (as the Annabelle uses weapon_ssg08 in the Shotguns group). Does
     it play like the Pistols default (USP: reload refill time, sounds),
     fire and reload normally, and can viewmodel_custom routing give it its
     own visuals without the real USP changing?
  2. PISTOL weapon type on a scoped gun (e.g. an SSG08 or AWP block with
     WEAPONTYPE_PISTOL + weapon_deagle): does zoom still work (levels,
     scope overlay, m_bHideViewModelWhenZoomed), and does it play like the
     Deagle (refill time)? Anything else the type changes (buy menu, slot,
     movement)?
  3. Why the made-up anim class weapon_annabelle (SHOTGUN type, same
     routing edits) fired once and then stuck on PC1 (MEMORY "WORKSHOP-LEGIT
     SHELL RELOAD" test B) while weapon_ssg08 works: does any made-up class
     break firing, in any group?
  Rules: no copy over CS2's viewmodel.vnmgraph (the user's rule; only when
  a test is about whether the original is needed), stock files untouched,
  test addons only, deliveries backed up. Report verified vs untested.

- 2026-11-01 ORB BATTLES REFERENCES READY (answer to your BRIEF):
  `.claude/handoff/orb-battles-from-pc1-20261101/` (NOTES.md, INDEX.md,
  SHA256SUMS.txt; 103 addon files, 18.4 MB, all hash-equal to the originals,
  + entity dumps + the grenade-subclass vdata blocks). "Lambda" = CS: Lambda,
  cs_hl_template's own mode (its scripts are the Lambda scripts). Shield
  grenade = "Sphield" in projectiles.js 7630-8901; heal = hl_shields.js (+ the
  10 HP/s health-only hl_shields_health.js from fs_jupiter_lambda); bullet
  abort + TakeDamage prior art = projectile_mayhem's
  all_guns_projectile_system.js. Orb mesh candidates measured: sphield.fbx
  sphere r=160, plasma.dmx sphere r=1. PC1 ran nothing in CS2 for this.

- 2026-10-31 13:00: Workshop-legit route found and tested on PC1 (details in
  PC1 memory, "WORKSHOP-LEGIT SHELL RELOAD"): SHOTGUN type + an anim class the
  stock Shotguns group does not list -> the gameplay side runs the Nova's shell
  logic; viewmodel_custom group checks switched to weapon_type lists route the
  visuals to the SSG08 slot. Open for PC2 if the user wants: why a made-up anim
  class (weapon_annabelle) stops firing after one shot; whether draw/shoot
  timing also follows the gameplay side's clips; third person.
- 2026-10-31 12:04: thanks, your hookup finding was right. On PC1 the
  Annabelle shell reload now works in cs_hl_template with Weapon Lab 0.18.2's
  new `deliver --cs2-root-copy true` (copy of viewmodel_custom at CS2's root
  graph path, kept in sync by later pushes). Verified over VConsole: partial,
  empty and interrupted reloads end correctly (details in PC1 memory). Still
  open on PC2 if the user wants it: a Workshop-legit way (no stock-path
  copy) for the gameplay side to use the agent's graph. The handoff folder
  .claude/handoff/annabelle-loop-20261031 was deleted 2026-11-15 (user).

- 2026-10-31 (user: "Have PC2 test it"): the Annabelle shell reload on
  cs_hl_template STILL LOOPS FOREVER in game (fresh CS2, vdata with your keys,
  clips matching reload_nova's layout). Please find which difference from your
  passing runs causes it: the Annabelle model on the custom skeleton, or the
  user's weapon_ssg08 block (no _base so no sniper_rifle tag, BUCKSHOT ammo,
  clip 3 / reserve 32), or cs_hl_template's environment. Test it with the
  exact Annabelle: the package is in git at
  `.claude/handoff/annabelle-loop-20261031/` (live addon files + SHA256SUMS,
  the user's vdata block, BRIEF.md with the full test plan). USER DECISION
  2026-10-31: a one-off exception to the no-game-data rule for this folder
  only (private repo; files force-added past the root .gitignore). Copy them
  into the lab; do not edit or add to the folder. Report under "For PC1": the
  first change that makes the loop end, plus a clip/reserve trace of the
  failing run.

- 2026-10-30: 0.18.0 does your hand-bone request: wpnHand_L/R are always glued
  to hand_L/R (export keeps CS2's offset; the toggle is gone). Worth an in-game
  check in the VDATA LAB: a wpn edit should no longer contort the arms. Also
  new: Anim Events tab (retime/edit particle and gameplay events), worth an
  in-game check that an edited particle or moved WPN_RELOAD_ADD_AMMO plays.
  The 3 tests failing on CS2 1.41.9.3 are fixed.
- 2026-10-31: your Annabelle shell-reload plan ran on PC1 (steps 1, 4, 5;
  step 2/3 skipped: no new Blender reload yet, so the SSG08's own reload
  motion on the Annabelle skeleton). Your seams and ADD_AMMO frames used
  (52->61 @56, 106->121 @112). Delivered to cs_hl_template, awaiting the
  user's in-game test; details in the 2026-10-31 "2nd try" status entry.

## Animation pipeline end-to-end test (PC2, 2026-10-28, VERIFIED IN GAME by me)

- Driven like a user via computer use: app Connect -> addon weaponlab_vdatalab -> Build
  .blend (rifle_ak 12 clips/61 markers, pistol_deagle 10/54; ~20 s each) -> Open in Blender
  -> keyframes by script (`.claude/scratch/anim/anim_lab.py`, run in Blender's Python
  console; per-frame pose_bone.matrix edits) -> add-on Export All / Export -> app Deliver
  Review + Push (13 files; incremental re-push 6 files) -> game.
- In game (de_dust2): AK idle cant (root_motion), inspect 360 roll (wpn), draw flip,
  reload mag flight (weapon clip bone) + 2 sound markers retimed (export verified, not
  heard), Deagle idle roll + draw spins: ALL played. Export reports: only edited bones,
  max error <= 0.0044 deg; unedited clips written unchanged.
- FOUND: in game the hands IK to wpnHand_L/R (children of wpn). With "Weapon hands
  follow hands" OFF (default) a wpn edit makes the game drag the hands onto the gun
  (Deagle 80 deg roll -> contorted, stretched arms) while Blender shows hands unmoved.
  Turning it ON exports wpnHand edits and the game matched Blender (verified).
  User confirms (with Codex earlier): hand_* and wpnHand_* must stay together; they are
  effectively one bone. Measured in all 16 untouched stock clips (AK 8, Deagle 8):
  wpnHand sits on hand within 0.007 units / 0.07 deg (reload_deagle worst).
- NEXT (DECIDED 2026-10-28, do on the main PC): make the Blender add-on ALWAYS keep
  wpnHand_L/R glued to hand_L/R on export, keeping CS2's own tiny per-frame offset from
  the clip's reference poses (input/reference) so untouched clips stay byte-identical
  (plain Copy Transforms would flag reload_deagle as edited: 0.069 deg > 0.05 tolerance).
  Remove/hide the "Weapon hands follow hands" toggle and warn at export if the two drift
  apart. Probe: `.claude/scratch/anim/hand_gap.py` (PC2 scratch).
- Hookup still manual (test-only copy of viewmodel_custom to stock path
  animation/graphs/viewmodel/viewmodel.vnmgraph_c, left in weaponlab_vdatalab). New
  clips need a FULL CS2 RESTART (map reload is not enough; user confirmed).
- Friction seen: computer-use open_application started a SECOND Weapon Lab instance
  (app has no single-instance guard; both write the workspace); Blender sidebar opens on
  Item; CS2 died when Steam closed ("Failed to connect with local Steam Client").
- Workspace (gitignored) now has sandboxes + .blend for rifle_ak and pistol_deagle in
  profile Default; addon weaponlab_vdatalab holds the delivered clips/graphs.

## VData limits (VERIFIED IN GAME by me on PC2, 2026-10-28, CS2 1.41.9.3)

- Whole file: an addon weapons.vdata_c REPLACES the stock file (no merge). A file
  missing loadout weapons (awp/m4a1/deagle/nova) CRASHED CS2 on player connect.
  Weapon Lab must always write the complete file.
- Dead keys (in no binary, ignored): m_DefaultLoadoutPosition, m_flDroppedModelScale.
  Unused in game tests: m_vSmokeColor (smoke stays grey), m_szMuzzleFlashParticle
  (stock flash), knife m_nDamage (hardcoded 40/90/180-style), healthshot m_nDamage
  (heals +50 = convar), m_bTreatAsSingleClip (no visible effect).
- Never-set fields that WORK: m_nBurstShotCount (= extra shots after the first; M4A4 got a
  6-round burst), m_bKeepLoadedAmmo (+1 chambered round, 31/30). Others found in
  server.dll, untested: m_WeaponCategory, m_bAllowBurstHolster, m_DefaultLoadoutSlot,
  m_szWorldModelAg2Override, m_szBarrelSmokeParticle, m_bGenerateMuzzleLight,
  m_flDropSpeed, m_bLinkedCooldowns, m_sMuzzleAttachment.
- Real weapon blocks: m_nDamage, m_flRange (= max bullet travel), m_flRangeModifier
  (dmg*mod^(d/500), >1 grows with distance), m_flHeadshotMultiplier, m_flArmorRatio (0 =
  armor blocks all), m_flPenetration (0 = no wall pen; saturating, 100 ~ 2x of 10),
  m_nNumBullets (each pellet full dmg), m_bIsFullAuto, m_flCycleTime (ceiling ~78 shots/s),
  m_bHasBurstMode (any gun), zoom levels/FOV (add or remove scopes), m_iMaxClip1 (100000
  ok), m_iDefaultClip1 (spawn ammo), m_bReserveAmmoAsClips false (classic rounds),
  m_flMaxSpeed (down to 1 = frozen; CAPPED at 260), m_GearSlot (SMG as pistol),
  m_iFlags ITEM_FLAG_NOAUTORELOAD, m_bShowCrosshair, m_szTracerParticle +
  m_nTracerFrequency, m_szWorldModel (first person too), m_nPrice (buy menu + charge),
  taser range/clip, HE m_nDamage + m_flRange (blast radius), m_flDeployDuration.
  Clamps: m_flThrowVelocity -> 0.9*v capped at 750 (faster throws impossible).
  Silencer DETACHABLE on CZ75: spawns silenced, cannot detach. Shell-by-shell reload
  is shotgun-class only. Negative damage / 0 pellets / range 0 = shots do nothing.
- NEW BLOCKS (dupes): `_base = <any block>` works (compiler fills all keys). Unknown
  `_class` compiles but is dropped at runtime. give / GiveNamedItem / ent_create <new>
  do nothing; `subclass_change <block> <cls>` (held weapon, entity class must match) and
  cs_script `EntFireAtTarget(w, "ChangeSubclass", "<block>")` (NO class check) work.
  A dupe's "parent" is the ECON ITEM DEFINITION the entity was created as (proved with
  one dupe on M4A1-S vs M4A4). Parent supplies: damage, range, range modifier,
  penetration, pellets, recoil, gunshot sound, grenade damage/radius, HUD + kill-feed
  icon/name. Dupe's own block supplies: clip, reserve, fire mode, cycle time, spread,
  inaccuracy, speed, deploy, headshot mult, armor ratio, model, anim class, kill award,
  price/name (GetData). Entity C++ class decides behaviour (knife + HE block = still knife).
  subclass_change does not refresh the first-person model.

## Third person (worldmodel) (read from game files 2026-11-14; nothing tested in game)

Built in 0.26.0 (2026-11-14, user "do a whole build for that ... go"). The two
open decisions were never answered; defaults I chose, to confirm with the user:
weapon actions only (the full draw/reload clips, standing, crouched, silent;
idle/shoot additive read-only; no locomotion or aim), and third-person clips
optional (no warning when a weapon's first-person clips are edited and its
third-person ones are not). Also: custom skeletons first person only; no events
editing, length or viewport for world clips. PC2 questions: "For PC2", 2026-11-14.

VERIFIED from files 2026-11-14: a weapon's world clips sit in
`animation/anims/world/` under the same relative folder as its viewmodel clips
(rifle/rifle_ak, knife/knife_ursus ...). None there for pistol_glock18,
rifle_scar20, knife_default_t, the grenades but molotov, chicken/egg/inspects.
The world root references gun variations directly (worldmodel_gun+ak47), like
the viewmodel root. The body skeleton's engine space is Z-up (head z 70.8,
wpn x 11.5): no axis conversion in Blender, unlike the arms. Agent models'
third-person meshes: thirdperson_body, thirdperson_default_gloves (ctm_sas).

VERIFIED from files (read-only VPK probes, csgo pak01):
- Agent models (`agents/models/<agent>/*.vmdl_c`) have four `m_animGraph2Refs`:
  "" and `worldmodel` -> `animation/graphs/worldmodel/worldmodel.vnmgraph`,
  `hudmodel` -> viewmodel, `uimodel` -> ui; `m_vecNmSkeletonRefs` = worldmodel +
  viewmodel skeletons. The gun model is the same file in both views (vdata
  m_szWorldModel, e.g. weapon_rif_ak47.vmdl on ak47.vnmskel).
- Graphs in `animation/graphs/worldmodel/`: root `worldmodel` (1853 nodes, 47
  params, 317 resources; viewmodel root 457/20/71); `worldmodel_locomotion` +
  variations knife/pistol/rifle; `worldmodel_gun` + per-gun variations (+ak47 ...);
  `worldmodel_gun_cz75`; `worldmodel_knife` + per knife; `worldmodel_grenade` +
  per grenade. Same root -> family -> variation shape as viewmodel. Root compares
  weapon_type (weapon_ak47 ...) and weapon_category_* like viewmodel.
  worldmodel_gun params: action, weapon_type, move_crouch_amount, action_reset,
  reload_stage, deploy_variation, weapon_ammo, is_defusing. Root adds aim
  pitch/yaw, move_*, flinch_*, ladder_*, air_*, foot IK, weapon_is_legacy_model;
  nodes include AimCSNode, FootIKNode, SnapWeaponNode, BoneMaskNode.
- `animation/skeletons/characters/worldmodel.vnmskel`: 75 bones (body, legs,
  head, fingers, wpnPivot, wpn, wpnHand_L/R, wpnTip, wpnEnd, attachHand/Foot,
  wpnAimIntent, attachWorld, camera). viewmodel.vnmskel: 56.
- `animation/anims/world/`: 1444 clips (rifle 487 incl. SMG/shotgun/sniper/MG,
  pistol 358, knife 387, shared 156, equipment 36, grenade 20); 607 with a weapon
  stream, 230 additive (viewmodel: 710 / 689 / 14). Per gun folder (e.g.
  rifle/rifle_ak) 12 clips: idle + idle_crouch (2-frame additive hold pose with a
  `+non_additive` twin), shoot (additive + twin), draw/draw_crouch,
  reload/reload_crouch (+ _silent). AK: idle/idle_crouch/shoot are played by the
  root; reload x4 + draw x2 by worldmodel_gun+ak47. Category sets in
  rifle/_default_rifle (195: 8-way move, crouch, jump, in air, ladder ...).
- Timing matches first person: reload_ak 74 f / 2.433 s, draw_ak 31 f / 1 s,
  shoot 24 f in both views.
- World clips carry NO SoundEvents and no ammo/mag events. IDEvent 430
  (WPN_IK_ACTION_ENDING 294, RinFront 53, USE_CROSSLEG 30, WPN_RELOAD_INTRO/
  LOOP/OUTRO 12 each, KnifeSwingBack 7, WPN_DISABLE_HAND_IK 4, WPN_SHOW_BULLETS 4,
  WPN_C4_ALLOW_TURN 2), ParticleEvent 160, OrientationWarpEvent 22,
  FloatCurveEvent 1. So sounds and gameplay stay first-person only.
- World reload_ak has the same ak47.vnmskel weapon stream but its own gun motion:
  bolt up to 3.97 units and clip up to 12.5 units / 124 deg apart from the
  first-person reload. Copying first-person gun motion = a starting point only.

Not built yet (later): additive idle/shoot export (base-pose rule unknown; maybe
edit the +non_additive twin), third-person length on the first-person timing,
events on world clips, custom skeleton in third person, Weapon Lab writing the
player models' "" / `worldmodel` refs (0.26 only warns), locomotion, aim, the
category clips (_default_rifle ...), the weapons with no world folder of their
own (glock18, scar20 ...: find their clips through the worldmodel graphs).

## Format notes

- VRF 20.1 API (verified 2026-10-23): `AnimationClip.Data` is a
  `ValveKeyValue.KVDocument`; use `.Root` (`KVObject` with `TryGetValue`,
  `IsArray`, `Count`, indexers, `Children`, `KVObject.Array/Collection/Blob`).
  CS2AL's KV code uses the same API, so ports need little adapting.
- Clip writing (verified 2026-10-23): re-serializing an unchanged DATA block
  with VRF is NOT byte-identical (LZ4 output differs; reload_ak 27084 vs 25909
  bytes) but decodes to identical values and poses. So no-op exports return
  the template bytes; edited exports are checked by fingerprint and pose
  tolerance (0.02 units, 0.05°, 0.001 scale). DATA is the last block in all
  674 clips. Field types: `m_nTrackReadOffset` Int32, ranges and constant
  rotation FloatingPoint64, `m_compressedPoseOffsets` UInt32; CS2AL wrote the
  read offset as Int64 (a type change) — Weapon Lab keeps original types.
  Static channels decode from range start / `m_constantRotation`.
- Event times are normalized over `NumFrames - 1` (verified: reload_ak events
  land on whole frames at 30 fps; Clipout = frame 11). 656 of 674 clips are
  on the viewmodel hand rig with ≤1 secondary; the 18 exceptions are all
  `chicken`/`egg`.

- Clip events (`m_events`): `CNmIDEvent` (gameplay, e.g. `WPN_RELOAD_ADD_AMMO`,
  `WPN_DROP_MAG` with `m_secondaryID`) and `CNmSoundEvent` (`m_name`,
  `m_flStartTime.m_flValue`, `m_flDuration.m_flValue`, `m_relevance`,
  `m_position`, ...). Example: `reload_ak` has 74 frames / 2.4333 s.
- Current game `weapons.vdata` (per CS2AL memory, 2026-10-22): 85 named blocks;
  `_prefab` blocks removed; weapons inherit category bases directly
  (e.g. AK/Galil `_base = rifle`); block key, `_class`, `m_szName` and
  `m_szAnimClass` are distinct identifiers (MP5-SD: data `weapon_mp5sd`,
  class `weapon_mp7`).

## Session capability note

- Cloud chat sessions linked to this PC can read/write the connected folders
  but cannot run programs there, and are blocked from writing inside
  `.claude/` (verified 2026-10-23). Claude Code sessions run locally (desktop app
  Code tab, Local environment, folder `F:\CS2WeaponLab`).
