# CS2 Weapon Lab: plan

Drafted 2026-10-23 after reviewing CS2AnimationLab. Update phase status as work
lands; record decisions in `MEMORY.md`.

## Goal

Open the app, connect to CS2, pick a weapon, and get a sandbox for it:
vdata, animations (through Blender), graph routing, and sound event timing.
One export verifies everything and delivers it to the chosen addon.

## Architecture

```
 ┌──────────────── Weapon Lab (desktop app) ────────────────┐
 │ Game connection · Weapon browser · Sandbox per weapon:   │
 │  VData | Animations | Events/Sounds | Graph | Export     │
 │                                                          │
 │  WeaponLab.Core (C#): VPK, clips, graphs, vdata,         │
 │  soundevents, project model, export/deliver pipeline     │
 └──────────────┬───────────────────────────────────────────┘
                │ local connection (commands both ways)
 ┌──────────────▼──────────────┐
 │ Blender: thin bridge add-on │  rig building, keyframe editing,
 │ one small status panel      │  pose sampling, timeline markers
 └─────────────────────────────┘
```

- **App:** .NET 10 + Avalonia (decided 2026-10-23). The core links
  ValveResourceFormat directly.
- **Bridge:** the app launches Blender or attaches to a running one. Commands:
  build weapon `.blend`, open project, switch clip, sample poses for export,
  read/write sound markers. Blender pushes events: frame change, save, clip
  switched, marker moved.
- **Sandbox project:** one folder per weapon with plain files: weapon id,
  game-version snapshot, target addon, vdata overrides, per-clip event edits,
  graph routing, the `.blend`, and export manifests.
- **Export:** Blender sends sampled poses; the core patches clips, applies
  event edits, rebuilds graphs, writes vdata, verifies, then delivers with
  backup and rollback.

## Repo layout

```
src/WeaponLab.Core/        formats + pipeline
src/WeaponLab.App/         UI
src/WeaponLab.Cli/         headless commands (tests, automation)
blender/weaponlab_bridge/  Blender add-on
tests/  docs/  build/
.claude/                   agent instructions, memory, plan (+ scratch/, ignored)
```

User workspaces (imported game data) live in the repo's gitignored
`workspace/` folder when running from a checkout, else under
`%LOCALAPPDATA%\CS2WeaponLab`; `WEAPONLAB_WORKSPACE` overrides both.

## Phases

- [x] **0. Skeleton.** Root `.gitignore` and binary rules (no LFS needed yet),
      solution that builds, README, MIT license. Done 2026-10-23.
- [ ] **1. Core port.** ~~Game discovery, VPK scan~~ (done 2026-10-23),
      ~~clip metadata/event decoding, template import into a sandbox~~
      (done 2026-10-23), ~~pose decoding, template-preserving pose writer
      with golden tests over all game clips~~ (done 2026-10-23; edited-output
      comparison against CS2AL still open, needs CS2AL calibration inputs), clip pose
      patching, sound patching, graph patching, delivery, as clean C#.
      Golden tests against CS2AL: no-op exports byte-identical; edited
      exports match CS2AL for the same inputs.
- [ ] **2. App shell.** ~~Connect to the game, choose an addon, browse
      weapons, list clips, clip and event view, track view, create a sandbox,
      Hammer 5 Tools style layout~~ (2026-10-23). Still to do: create an
      addon; read-only views of graphs and vdata.
- [ ] **3. Blender bridge.** ~~Build the `.blend` from the app (rigs, one
      Action per clip with Arms/Weapon slots, event markers, previews)~~
      (done 2026-10-23 as a headless build script); ~~thin add-on with clip
      switching, presets, hand follow, reset, export to sandbox / Save Clip
      As via the CLI~~ (done 2026-10-23, 0.5.0); ~~delivery to the addon,
      CS2AL-style, with backups/undo and vdata compile~~ (done 2026-10-23,
      0.7.0). Remaining: in-game test, player-model hookup, optional live link; Thin add-on with a two-way connection; build the
      `.blend` from the app (port `library_builder.py`); open and switch clips;
      full Blender → Weapon Lab export round trip.
- [ ] **4. Events/sound timeline.** Per-clip event lanes, drag to retime,
      swap soundevents, audition, synced with Blender timeline markers. Then
      extend the patcher to add and remove sound events.
- [ ] **4 (partial).** ~~Sound retime/rename via Blender markers~~ (done
      2026-10-23, 0.6.0); app timeline, audition, add/remove still open.
- [ ] **5 (started).** ~~Read-only view with inheritance and addon diff, typed
      overrides stored in the workspace~~ (done 2026-10-23, 0.6.0; VRF upgrade
      turned out unnecessary); ~~edit the addon's own vdata source, compile,
      glossary names~~ (done 2026-10-24, 0.8.0). Remaining: merge on game update.
- [x] **Profiles (0.8.0, 2026-10-24).** Save/load profiles, outliner status
      colours, Default resets and repair on delivery, per-weapon preview
      models (automatic from the addon's `m_szWorldModel`).
- [ ] **5. VData editor.** Upgrade VRF first. Typed property editor showing
      stock vs. overrides with inheritance; compile via Workshop Tools; a
      merge step when a game update changes the stock file.
- [ ] **6. Graph editor.** ~~Visual level-by-level editor, node parameter
      edits, clip swaps, change colours, delivery~~ (done 2026-10-24, 0.9.0).
      Remaining: in-game test; structural edits (rewire, add states or
      transitions) only after research.
- [ ] **7. Packaging.** ~~Release build script (self-contained single-file
      app + CLI, zip)~~ (2026-10-27, 0.17.0, `build/publish.ps1`). Remaining:
      user test of the build, GitHub release, installer (optional), update
      check, signing (optional).

## Addon-aware roadmap (user, 2026-10-24)

- [x] **0.10** Addon as baseline: originals + snapshot, patch addon files,
      restore on reset, inventory, badges, VData Profile = addon values.
- [x] **0.11** Edit from the addon's existing animations and graphs
      (sandbox templates from addon clips; graph editor opens addon graphs).
- [x] **0.12** Custom weapon skeletons from a model's bones (sandbox,
      Blender, delivery of skeleton + model copy + all clips). In-game test pending.
- [x] **0.13** Source 2 Viewer-style graph canvas (level view with pins, typed
      wires, filters), keeping the inspector and explanations.
- [ ] **Later** Read-only skeleton viewer (vmdl bones vs .vnmskel); HL2
      v_irifle reload as the AR2 hopper reference.

## Feedback queue

Done in 0.11–0.13: delivery tree (now by weapon), Build .blend window,
in-place vdata saves, Blender preview fixes, addon = profile, build from the
addon, outliner skeleton node, graph glow; 2026-10-26: literal Set to CS2
default, vdata isolated, VData ↺ and no "Addon now", Blender mark + Delete
.blend, S2V-style graph canvas with pulsing glow, Graph tab shows addon graphs;
0.15: profiles as local workspaces + push model (addon = live state); 0.16:
Skeleton tab removed (panel above History), skeleton choice back in Build
.blend, Animations viewport with clip playback.

Open:
- DONE IN 0.23.0 (see "DONE 0.23.0"; was QUEUED, user 2026-11-07, "feedback for later build … dont go just talk"):
  1. Deliver tab: AK-47 always listed ("left out of this push", no reason).
     Cause: `CommittableWeapons()` lists any sandbox with an edited export or
     a custom skeleton; cs_hl_template's rifle_ak has an edited idle_ak export
     from 2026-10-23 23:48 but Animations = Default, and the row's summary is
     empty when left out. Fix idea: say what is left out ("idle_ak edited
     2026-10-23"), group left-out weapons in a collapsed "Left out (n)" node,
     and point at Revert / Set to CS2 default to clear it.
  2. Stop shipping the custom skeleton's model copy
     (models/weapons/weaponlab/<weapon>/<model>.vmdl_c = the source vmdl_c
     with its skeleton refs swapped by ReferencePatcher; the user's own model
     is never edited). User decision: the user points their own .vmdl at the
     skeleton in ModelDoc after pushing; Weapon Lab warns. Check the model the
     weapon uses (`ModelSkeleton.SkeletonRefs`) and warn in the Deliver tree /
     outliner until it targets the skeleton; replace the "set m_szWorldModel
     to the copy" note. cs_hl_template has no copies yet (checked 2026-11-07:
     nothing in models/weapons/weaponlab, no "weaponlab" in its content vdata);
     the knife's would be the first. Untested: whether ModelDoc accepts a
     compiled-only .vnmskel (no content source) as the model's skeleton.
     DECIDED (user 2026-11-07 "me in model doc"): the user sets the skeleton
     in ModelDoc; Weapon Lab never edits their model, only warns.
  3. "Copy asset path" (user 2026-11-07): copies the whole path, e.g.
     animation/skeletons/weapons/weaponlab_knife_ursus.vnmskel. Today the
     outliner's Skeleton row has no Copy item (`HasName` excludes it) and
     Copy name on a clip copies only its name. Add it to the Skeleton row,
     clip rows (custom clip path), Deliver tree file rows and the item-2
     warning (a Copy button). Format DECIDED by the user (2026-11-07, after
     trying ModelDoc): NO `_c`, i.e.
     `animation/skeletons/weapons/weaponlab_knife_ursus.vnmskel`. Seen in
     the user's ModelDoc screenshot: the model's skeleton is an
     NmSkeletonReference node, field "Source File", which takes that form
     (it also has a "Do Not Discard Skeleton Bones" checkbox). Whether the
     model compiles and animates with a compiled-only skeleton is untested.
     (User's model compiled with it at 20:30 and the clips play, so it works.)
  4. NEXT BUILD (user 2026-11-07: "not now but queue it for OUR NEXT
     BUILD"; the re-parent test WORKED, see MEMORY "VERIFIED IN GAME").
     WAIT for PC2's skeleton report first (user: "We'll see what pc2
     figures out then do a build"); fold its findings into this design.
     Design agreed in chat: (b) an OPT-IN per-bone tick "keep apart from the
     gun", default off (every bone a normal gun part, as today), remembered
     in the sandbox/profile (not "parentless in the model", which the
     user's re-parented model no longer is; their setup has no FreeBones
     field and reads the model live, so today the next Build .blend drops
     the lighter's prop treatment). Ticked bones get the 0.22.4 anchor +
     hold + counter-motion export; either kind stays animatable (Child Of
     can pin a prop to the gun or a hand for part of a clip). Parenting in
     the model is free (weapon, weapon_offset, ...) as long as it matches
     the skeleton (model hierarchy). (a) warn when the model's hierarchy
     differs from the custom skeleton's, above all a second model root next
     to `weapon` (Weapon Lab hangs it under `weapon` = the mismatch that
     sent the lighter far away), with the fix "parent it under weapon".
     Keep the 0.22.2 fallback (hang extra roots under the root) but warn.
- DONE IN 0.24.0 (was QUEUED, user 2026-11-08, "Queue for later"): Anim Events, the first edit
  creates the sandbox. Today a clip without a sandbox is read-only (status
  "Read-only: create the weapon's sandbox to edit its events.", from
  `CommitEvents` in MainWindow.AnimEvents.cs) and the user must find the
  "Make editable (create the sandbox)" button in the clip info panel; they
  thought a .blend was needed. Design (agreed in chat, not built): browsing
  stays read-only (no folders made by looking); the first real edit (drag,
  add, delete, length, gameplay/sound pick) runs `EnsureSandboxAsync`,
  reopens the clip, then applies the edit and says "Created the sandbox for
  <weapon>" in the status line. Add/Copy buttons enabled while read-only;
  drop the Make editable button. Catch: if the new sandbox's template differs
  from the bytes the read-only view showed (BuildFromAddon = false while the
  addon has its own clip), do not apply the edit (event indices may not
  match); say so and ask the user to redo it.
- DONE IN 0.25.0 (was QUEUED, user 2026-11-14, "queue it and explain it back to me"): a new TEAL/aqua
  state for graph rows in the outliner (WeaponGraphs.For State + WeaponState +
  colours + the outliner legend): a shared or routing graph (the root
  viewmodel_custom, a graph another weapon owns) that the NEXT PUSH changes
  because of OTHER weapons' pending work (clips or graph edits not delivered
  yet), while this weapon has nothing pending. Precedence on a row: yellow
  (this weapon's own pending change) > teal (others' pending change) > green
  (the addon's copy carries this weapon's work) > blue (the addon's copy is
  changed, already pushed, for other weapons) > white (CS2's). Today such a row
  shows blue (live state only). The parent "Graphs" row takes the strongest
  child colour as now. Root: "others pending" = any other weapon of the profile
  with pending clips/graph edits/stock resets; shared graph: its owners pending.
  Not the earlier idea of leaving the root uncoloured (user kept blue).
- DONE IN 0.25.0 (was QUEUED, user 2026-11-14, "seems redundant. queue to remove" / "i dont think
  we'll need that"): Anim Events shell panel, remove the compare section: the
  "Game timing: the X's reload" / "Compare with the Nova's/Sawed-Off's reload"
  dropdown (TimingReferences, _shellReference), the two timing lines
  (TimingLines; its warnings already come from GameTimings.Warnings via
  Checks/Deliver, keep them shown once under Game timing), and the dashed ghost
  on the Sync lane (ShowGhost's non-band part; user 2026-11-14 saw it as the
  dashed "Nova timing" bar: "lets just remove that all together"). Proposed, not confirmed: keep
  "Use the X's timing" (FitToReference, events only) as a button under the Game
  timing picker, fitted to the expected class; CLI `events fit` stays.
- DONE IN 0.25.0 (was QUEUED, user 2026-11-09, "swap it in a later build"): replace the "a lighter
  beside a bong" prop example with a neutral one, e.g. "a spare magazine held in
  the other hand": BuildBlendDialog.cs Keep apart label, MainWindow.Bones.cs
  Prop row, Profile.cs PropBones comment.
- DONE IN 0.24.0 as the Blender gun-model swap + Keep my animation (was QUEUED, user 2026-11-08, "this can be used as feedback"; not designed with
  the user yet): adding bones to a custom model mid-animation, keeping the
  work. Found (read only, knife_ursus bong, model-hierarchy skeleton):
  a) Build .blend does not remake the skeleton when the SAME model is picked
     after it was recompiled with new bones (`ApplyBuildChoicesAsync`
     rebuilds only when path/model/hierarchy differ). Today only CLI
     `skeleton create <weapon> --model <key> --model-hierarchy true` remakes
     it. Idea: notice the model changed (file time / bone list vs setup) and
     offer "Update the skeleton from the model (n new bones)".
  b) A rebuilt .blend starts from the templates, so Blender work is not in
     it (exports are kept and moved onto the new skeleton; the old .blend is
     in blender\backups). Idea: Build .blend carries each clip's keys over
     from the previous .blend for bones that still exist (append its actions
     in the build script), else keys from the clip's sample/export; new bones
     start from the clip's own motion.
  c) Guard: exporting (above all Export All) from a fresh .blend overwrites
     exports/ and edits/ with original motion. Warn when an export would
     replace an edited export with an unedited one.
  d) After a skeleton change, Anim Events edits on clips with a Blender
     sample are refused ("template changed … Rebuild the .blend", then
     "weapon has N bones; the clip has M") until the clip is exported again:
     safe, but say it in plain words.
- DONE 0.18.0 (2026-10-30, the 2026-10-29 queue): compiled-only model pickers
  with compile date + newer-source warning; Default model label names the
  model; Sounds tab -> Anim Events (all event kinds: retime + listed fields,
  stored per clip in the sandbox `events/`, merged into exports, Reset to
  CS2); FBX leaf bones left out; "use the model's hierarchy" skeletons
  (clips keep motion relative to each bone's rest); mesh-less bones flagged
  and hidden in Blender; wpnHand always glued to hand (PC2 request);
  single-instance guard; tests follow CS2 1.41.9.3 data. Not tested in CS2:
  edited particle/gameplay events, a model-hierarchy skeleton in game.
  Open from it: adding/removing events (new event kinds need the patcher),
  the Blender add-on still shows only sound markers as editable.
- DONE 0.18.1 (2026-10-30): model-hierarchy skeletons keep CS2's root rest,
  so the gun stays in the hands (0.18.0 lifted it by the model's root bind).
- DONE 0.19.0 "Clip timeline" (2026-10-31, user "Go with 0.19"; uncommitted,
  published): Anim Events = one clip at a time (outliner clip stays on the
  tab; weapon = clip overview, _lgcy behind a toggle), the shared viewport
  moved in above an EventTimeline (ruler, Sync lane, kind lanes with row
  stacking; select/drag/resize/box/boundary drags, nudges, delete,
  duplicate, undo/redo, double-click adds), inspector with sound picker + ▶
  and gameplay-ID picker (GameplayIds), add/remove/copy events and sync
  tracks in the core (EventListEdit, EventPatcher, StoredEventEdits file
  format, EventCopy), ShellReload helper (+ seam check, Nova/Sawed-Off
  timing ghost + warnings, N-shell playback plan), SoundLibrary +
  SoundPlayer (NAudio.Core/WinMM, pitch not applied); VData colours by the
  CS2 column; Right Arm + WPN preset; Action Editor channel list on (build
  + on load); viewport gun model picker (skeleton panel); bugs 2026-10-31
  fixed (missing export file, reset patch-back recorded, adopt fallback to
  the weaponlab_<weapon> skeleton); m_szWorldModel note for a model copy
  (delivery + skeleton panel); Save Clip As samples to
  edits/<clip>.save-as.json.gz. Blender: app owns events, Blender sound
  markers still win for their event (unchanged). Tests 118/118. Smoke-tested
  in the app (debug + published): drag/nudge/undo, helper 52/61/56, 3-shell
  playback. Not tested: hearing the sounds (no errors logged), an app-made
  loop clip in game (structurally the in-game-verified recipe).
- Queued (user feedback, 2026-10-31, after 0.19.0): on Anim Events the
  outliner's "edited" clips stay white. Cause: ApplyStatusToTree only
  colours clips for the Animations/All aspects (clipAspect), so the Sounds
  aspect leaves them Untouched. Fix: colour clips on Anim Events too,
  yellow while the addon lacks the clip's current export (PendingWork
  state Pending), green when it has it, blue when replaced elsewhere.
- DONE (clip rows 0.22, skeleton and graph rows 0.24.0; was Queued, user feedback, 2026-10-31): right-click "Set to CS2 default…"
  per item instead of the whole weapon (today the menu shows on every
  child row but always resets the weapon). Proposed:
  * Clip row: "Set clip to CS2's…" drops that clip's export, Blender
    sample and event edits in the sandbox, so the next push delivers CS2's
    animation for it; on a custom skeleton that is the re-targeted
    template (stays on the custom skeleton, so the gun still animates); on
    CS2's skeleton the addon's custom clip is removed and the gun graph
    slot points back at CS2's clip. The .blend keeps its keys (warn).
  * Skeleton row: "Set skeleton to CS2 default…" = the Change skeleton →
    CS2 default action (clips move back; next push removes the skeleton
    and model copy unless a model uses them; m_szWorldModel note).
  * Graph row: drop that graph's profile/delivered edits and rebuild it
    from CS2's keeping the clip routing; Graphs row: all the weapon's
    graph edits (e.g. the Annabelle's routing list edits).
  * Weapon row keeps today's full reset. Each shows what it removes first.
  The user said "later" to building both (2026-10-31).
- Queued (user feedback, 2026-10-31): the "Viewport gun model" dialog's
  Default should be CS2's model, not the custom skeleton's (it shows
  "Default (weapon_annabelle)"). Cause: PreviewModel null means
  "custom skeleton's SourceModel, else CS2's" (ViewportScene.Build line
  ~181, MainWindow.Animations ~420, CLI ~174, DefaultViewportModelName).
  Proposed: three choices: "CS2's model (<stock name>)" = Default (null),
  "The skeleton's model (weapon_annabelle)" (only with a custom skeleton
  built from a model), "Custom model". Null then always means CS2's in the
  viewport and Build .blend. Building or adopting a custom skeleton from a
  model stores that model as the explicit pick, and existing sandboxes
  with a model-built skeleton and no pick get it stored once on load, so
  the Annabelle keeps its mesh in the viewport and the next .blend.
  Setting the skeleton back to CS2 default clears a pick equal to the
  skeleton's model.
- Queued (user feedback, 2026-11-01, "dont go"): more colourful bones in the
  built .blend so finger sections tell apart (finger_ring_1 vs _2). Trial
  colours applied to the user's SSG08 .blend 2026-11-01 (MEMORY 21:36).
  User feedback 2026-11-01 ("dont go"): nice, but too vibrant and "in my
  face"; wants something like transparency. Blender has no per-bone alpha
  in solid shading (overlay bone_wire_alpha only works in wireframe
  shading), so: next trial = muted colours (about half the saturation,
  slightly darker, segment ramp kept but narrower, hand/arm closer to
  grey), and optionally the Wire display (thin lines) instead of Stick.
  DONE 0.21 (muted palette in bridge rig_style.py, applied on build and on
  open; user has not seen the muted version yet).
  Trial on the SSG08 .blend again before porting (user: "Later"). Today
  build_weapon.bone_collections gives every arm bone THEME03, weapon bones
  THEME04, custom-skeleton extras THEME09, no-mesh THEME04. Proposed, with
  custom bone colours (palette CUSTOM, normal/select/active set):
  * One hue per finger: thumb red, index orange, middle yellow, ring green,
    pinky blue. Segment by brightness: meta darkest, then _0, _1, _2
    lightest at the tip. Same scheme on L and R.
  * hand_* white, arm_upper/arm_lower their own two colours, helpers
    (armUpperShoulder, armUpperStraighten_*) dim grey, wpn and wpnHand_*
    magenta so the weapon mount stands out.
  * Weapon bones stay blue; custom-skeleton extras keep their own colour.
  * One shared function in the bridge, called by build_weapon and by the
    bridge's load_post handler (as show_action_channels is), so existing
    .blend files get the colours on open without a rebuild. Colours only,
    no bone or collection changes. Check that Stick display shows them in
    Blender 5.2 (rig.data.show_bone_colors).
- Queued (user feedback, 2026-11-01): scaling a bone in Blender does not
  scale the preview mesh (CS2AL's skinned FBX preview did). Cause:
  build_weapon.follow() gives each hidden preview-rig bone only
  COPY_LOCATION + COPY_ROTATION (world) on the edit rig's bone; scale was
  left out because the preview rig sits at 39.37x (glTF metres) and a plain
  world Copy Scale would shrink the mesh. CS2AL's stock preview had the same
  limit; its "replace preview" FBX path skinned the mesh straight to the
  rig. Export already carries scale (pose_io averages to CS2's one uniform
  scale per bone; EditDeltas applies it).
  Fix (trialled 2026-11-01 on the user's SSG08 .blend, see MEMORY 21:36;
  works in background checks, user has not looked yet): after the two
  constraints add COPY_SCALE "Weapon Lab - follow scale", target_space and
  owner_space POSE, replace (no offset), Make Uniform (CS2 stores one
  scale per bone; also hides the preview bones' different axes). Pose
  space keeps the preview object's 39.37x and the weapon mount's wpn scale
  out of it; replace mode means a scaled parent is not applied twice, so
  no inherit_scale change is needed. Port: build_weapon.follow() adds it,
  and the bridge's load_post adds it to older .blend files (skip bones
  that already have a COPY_SCALE). DONE 0.21 (rig_style.scale_follow /
  add_scale_follow; the scratch reference script is deleted). Still to check:
  an export after scaling, and the scaled bone in game.
- Queued (user feedback, 2026-11-02, "for later builds"): a button beside a
  Deliver error/warning that jumps to the thing it names, e.g. "Graph node
  29 … in …ssg08_legacy" opens the Graphs tab on that graph with node 29
  selected (and a clip error opens Anim Events on that clip). The user
  loved that delivery explained the error; this is the next step.
- FIXED 2026-11-02 21:25 (uncommitted; publish\CS2WeaponLab-0.19.0-win-x64
  rebuilt in place): GraphEdits.Apply takes clipAliases (CS2 path -> addon
  copy) and plays the slot holding the clip under either name, returning
  the chosen slots; GraphPatcher.Patch takes clipAliases (merged with the
  map) and Verify checks the chosen slot and the played file name;
  DeliveryPlanner.PlanClips builds aliases from the profile's clips that are
  pending or already in the addon. Graphs tab: GraphStatus.SameClip; the
  picker lists a weapon clip once when the graph plays its addon copy, an
  older CS2-path edit shows as that copy, and picking what the node
  already plays removes the edit. Test ClipEditsPlayTheSlotHoldingTheClip
  UnderEitherName; 119/119. The user's real push dry-runs clean with the
  node 29 edit kept (node 29 plays custom shoot1_ssg08_lgcy, 7 resources).
  Not pushed; not checked in the app UI or in game.
  BUG (found 2026-11-02, Deliver Review on cs_hl_template): "Graph node 29
  value clip did not take the edit (…/rifle_ssg08/shoot1_ssg08_lgcy.vnmclip)
  in …viewmodel_gun_custom.vnmgraph+ssg08_legacy.vnmgraph_c". VERIFIED:
  the profile holds 29:clip = CS2's shoot1_ssg08_lgcy path (saved
  2026-10-31 15:29 from the Graphs tab, no agent active then). The Graphs
  tab shows the addon's graph, where node 29 (Shoot) already plays
  custom/rifle_ssg08/shoot1_ssg08_lgcy (slot 0); its clip picker lists that
  clip twice (the addon's copy and the weapon's own path marked ✎ edited),
  and picking the ✎ entry saves CS2's path. Delivery builds on the live
  graph: GraphEdits.Apply cannot find CS2's path, appends it as slot 7,
  the clip map renames it to the custom path, so slots 0 and 7 are equal
  and Verify's IndexOf picks 0. When the clip is not in the push's map the
  same edit would silently make node 29 play CS2's clip on the custom
  skeleton. Without the edit the plan works and node 29 plays the custom
  clip (in-memory dry run). Fix: (1) delivery resolves a clip edit through
  stock -> custom for the profile's clips the addon has or gets (pending
  and live), preferring a slot that already holds it; Apply returns the
  chosen slot and Verify checks that slot and its name; test both bases
  (CS2's graph and the addon's). (2) Graphs tab picker: one entry per clip
  (the addon's copy and the ✎ clip are the same choice), and an edit that
  resolves to what the node already plays is not saved.
- FIXED 2026-11-02 (uncommitted; also copied into publish\CS2WeaponLab-
  0.19.0-win-x64\blender\weaponlab_bridge): pose_io.stored_name /
  fits_marker / MARKER_NAME_BYTES; collect_event_edits accepts a name
  Blender cut short; migrate_markers matches cut names; Change Sound
  refuses names too long for a marker. Background test on the SSG08 .blend:
  old bridge fails shoot1 x2 with the user's error, fixed bridge passes all
  12 clips, still refuses a moved or renamed muzzle flash, still sends
  sound moves/renames. Not yet exported by the user.
  BUG (found 2026-11-02, user export of the SSG08): shoot1_ssg08 and
  shoot1_ssg08_lgcy cannot export from Blender: "Gameplay marker #1
  (particles/unified_weapon_fx/weapon_muzzleflash_snip_fps.vpcf) was
  changed". Cause (VERIFIED in the .blend, also in the user's pre-Codex
  backup): Blender caps marker names at 63 bytes, so "#1 Particle:
  particles/unified_weapon_fx/weapon_muzzleflash_snip_fps.vpcf" is stored
  cut short, and collect_event_edits compares the full name. #3 (shell
  casing) is cut too. Reset Events cannot help (it recreates the same
  long names). Fix: in collect_event_edits treat a marker name equal to
  the expected name cut to Blender's limit as unchanged (non-Sound: check
  only the frame; Sound: never send a cut name as a rename), and test
  with a long particle path. Ship in the bridge (repo + the published
  app's blender\weaponlab_bridge), restart Blender after.
- DONE IN 0.24.0 (Settings: workspace + new .blend folder, Move .blend…, Show .blend; was Queued, user feedback, 2026-11-02): "where does a .blend go and how can
  the user control it?" Today: <workspace>\profiles\<profile>\sandboxes\
  <weapon>\blender\<weapon>.blend. A full Build/Rebuild opens a Save dialog
  starting there (ChooseBlendLocationAsync); another place is remembered in
  blender\location.txt (BlendBuilder.BlendPath); "Update for export" never
  asks. Backups always go to the sandbox's blender\backups. The workspace
  moves only with the WEAPONLAB_WORKSPACE variable (no UI).
  BUG found by reading code (not run): the bridge finds input\ and the
  sandbox from the .blend's own folder (blend_folder()/input,
  sandbox_folder() = its parent), so a .blend saved anywhere else cannot
  read rig.json and would write edits to <that folder's parent>\edits.
  Proposed:
  * Fix first: build_weapon stores the sandbox's absolute path in the
    scene meta; the bridge uses it (falls back to the .blend's folder),
    and says clearly when neither has input\rig.json.
  * Show the .blend's full path on the Animations tab with "Show in
    Explorer" and "Move .blend…" (moves .blend and .blend1, updates
    location.txt; refuses while Blender has it open).
  * The Build .blend window shows the target path with a Change… button
    instead of a separate OS save dialog after Build.
  * Settings: a default folder for new .blend files per profile (e.g.
    D:\Anims\<profile>\<weapon>.blend), and a workspace folder picker
    (today env var only), which moves nothing by itself.
- Next for the Annabelle (2026-10-31, Workshop-legit route live in
  cs_hl_template): re-time its reload to reload_nova (50 f @30, INTRO 0-11,
  LOOP 11-24, ADD_AMMO 20, OUTRO 24-49) so the viewmodel loop matches the
  Nova logic the gameplay side runs. 2026-11-02 in game (user, after
  pushing Codex's animations): "it starts number reloading before
  animating" = the ammo counter (Nova timing, re-read from the game: Insert
  shell 11, AddAmmo sound 19, ADD_AMMO 20, OUTRO 24, Pump 35) runs ahead of
  the pushed reload_ssg08 (111 f: INTRO 0-42, LOOP 42-61, ADD_AMMO 50,
  OUTRO 61-111; stored in events/reload_ssg08.json): first shell 1.0 s
  early, +0.2 s per shell. Options put to the user: (a) automatic re-time
  in the .blend (piecewise: 0-42 -> 0-11, 42-50 -> 11-20, 50-61 -> 20-24,
  61-111 -> 24-49, hold after; events and sections moved to match; backup
  first) or (b) the user re-animates inside Nova's frame budget. Feature idea: "borrow a slot's logic" in
  Weapon Lab = weapon type + routing edits in viewmodel_custom (ID list
  edits now supported by GraphEdits) + a timing template from the borrowed
  slot's clips.
- Follow-up of 0.18.2 (not queued by the user yet): a Deliver tab toggle for
  the CS2 root copy (CLI-only now; the app's pushes keep an existing copy in
  sync), shown with its "local play only" note; and a Deliver warning when a
  pushed clip has WPN_RELOAD_LOOP sync sections but the addon has no copy.
- DONE IN 0.24.0 (Variations…; not tested in game; was QUEUED, user, 2026-10-31): RANDOM RELOADS, RARE DRAWS AND RARE INSPECTS
  (PC2 verified the mechanism in game; pc2/MEMORY "WEIGHTED SELECTORS").
  A ParameterizedClipSelector with m_bHasWeightsSet picks an option as a
  fixed function of its parameter: deploy_variation is re-rolled per draw,
  inspect_variation per inspect, weapon_ammo_reserve changes every reload.
  Rare draw/inspect: put a clip in an empty option and give it a weight > 0
  (weights are per-variation node values; AK inspects lookat01 5, lookat03
  1). Random reload: repurpose a spare selector (AK: Deploy2 #214, its state
  #213 child -> Deploy0's #210) with two reload clips 1:1 and point reload
  selector #62's option at it (parameter deploy_variation or the reserve).
  Feature: per weapon, let the user add extra draw/inspect/reload clips with
  weights in the app (graph edit + clip slots, delivered like other graph
  edits), with a note on which parameter drives the pick. Needs the graph
  patcher to set weights and child indices; check which variations have a
  spare selector.
- Later: FBX/glTF/OBJ as a preview-only model source (draft skeleton,
  recheck once compiled); multiple reloads (separate data slot for the AK's
  empty_mag_reload, see MEMORY "MULTIPLE RELOADS"); custom guns (MEMORY
  2026-10-27).
- Clips from another slot (e.g. AUG clips in another slot) with role pairing.
- Viewport phase 3 (arms done 2026-10-27): first-person camera preset,
  compare old/new skeleton.
- DONE 0.21 (see DONE 0.21.0): OUTLINER RIGHT-CLICK MENU. Before 0.21 only
  "Set to CS2 default…" on weapons (MainWindow.axaml, Grid.ContextMenu). User
  wants more options, including "Revert to addon default", shown only when the
  node is yellow (NeedsApply). Meaning as I read it: throw away the pending
  change so the item matches what the addon has (green); not confirmed.
  Menu draft per node kind, options not given by the user: weapon = open in
  Blender, open sandbox folder, Set to CS2 default, revert; clip = show in Anim
  Events, copy name, revert; Graphs = show in Graphs, revert; Skeleton = show,
  revert. Revert follows the tab's aspect (the colour does: CurrentAspect) and
  lists what it discards with checkboxes, like Set to CS2 default.
  Per kind (ProfileStatus reasons): VData values and graph edits live in the
  profile, so dropping them is simple; a custom skeleton has
  CustomSkeletons.Remove. Clips are the hard case: the addon holds only the
  compiled clip while the sandbox holds edits/, exports/ and events/, so
  copying the addon's clip back leaves stale edits/ that the next Anim Events
  save re-exports from. Needs a snapshot at push or decoding the addon's clip
  into edits/. The .blend keeps its keys either way, so the next Export All
  goes yellow again; the dialog must say so. Revert never writes to the addon
  or the .blend, and backs up what it discards.
- 0.21 scope (2026-11-05; built 2026-11-07, see DONE 0.21.0): outliner menu + revert;
  Checks list + jump buttons (direction item 4, 2026-11-02 feedback); small
  queued items (softer bone colours, viewport model default = CS2's, per-item
  Set to CS2 default in Anim Events, .blend location + bridge bug). Plays-like
  picker after it. Waiting on: the user's in-game result for 0.20.1 (their
  Export All 2026-11-04 19:19 and pushes 19:21, 19:31 seen from file times; no
  result reported) and a commit of 0.20.1 (21 files uncommitted).

## DONE 0.26.1 (2026-11-15, user "Publish 0.26.1"; uncommitted; published 0.26.1)

User feedback 2026-11-14 (screenshot): "the clip used should just be the name of the exported as by default".
Blender's Clip used picker now lists clips by id like Exported as (world_reload_crouch_m4a4, not
"reload_crouch_m4a4 (third person)"); the tooltip still says third person. Compiled in Blender; not clicked.

## DONE 0.26.0 (2026-11-14, user "add Right Elbow+WPN ... pull plans from your third person session and lets go ahead and do a whole build for that ... go"; uncommitted; published 0.26.0)

User asks 2026-11-14: third-person (worldmodel) animation support as a whole build; existing .blend files
(the user's ssg, ursus) have no third-person clips and that is fine for now. Also the Right Elbow + WPN preset,
and "I guess so" to fixing the false "Load Weapon Lab's newer version" button. The third-person session's two
open decisions were never answered; defaults: weapon actions only, third-person clips optional.
1. Catalog and sandbox: WeaponEntry.WorldClipPaths (animation/anims/world/<same folder>/); SandboxManifest.WorldClips
   (kept apart from Clips, so first-person code is unchanged), AllClips(), FindClip(id); clip ids "world_<name>"
   (RigPackage.ClipId/WorldPrefix); ClipInfo.IsEditableWorld (body skeleton, <=1 weapon stream, not additive, not
   a +non_additive twin); new sandboxes and Rebuild take them; SandboxStore.AddWorldClips for older sandboxes
   (CLI `sandbox add-third-person`, outliner right-click Third person -> Add third-person clips).
2. Blender (add-on 0.15.0): rig.json skeletons.body/worldWeapon, clips "view": "world", previews.body (the agent's
   thirdperson_body + thirdperson_default_gloves). build_weapon: Body rig (no axis conversion: engine Z-up), its
   own weapon rig mounted on body wpn, world Actions (action["weaponlab_view"]), references, key carry per view,
   collection "04 - Third person". pose_io: is_world, skeletons_of, scene_rigs, show_view, VIEW_FRAMING. Add-on:
   rigs()/current_clip() per view (scene "weaponlab_current"), switch_clip shows the clip's view (hides the
   other, moves the 3D view, keeps posing on the matching rig), Edit Body, presets use arm_upper_* on the body,
   reset/sample/export/import/animation files per view, third person cannot export into first person.
   animation_io/animation_job per view. CLI `blender build --keep true`.
3. Export: ClipExporter picks skeletons by view (RigSkeletons.For), HandGlue/EditDeltas get a 10x noise allowance
   for world clips (EditDeltas.WorldTolerance: the body is ~60 units out, untouched exports drifted 2e-5).
4. Delivery: GraphTree.CustomClipPath -> custom/<weapon>/world/<file>; Load(includeWorld); Plan keeps the
   worldmodel root only when a world graph is selected (-> worldmodel_custom); plan note; AddonChecks.Hookup
   warns when no player model's ""/worldmodel entry is worldmodel_custom (PlayerModel.WorldGraphs); Checks too.
   A custom skeleton stays first person (note in the plan); WarnMixedSkeletons ignores world clips.
5. App: outliner "Third person" group per weapon (status colours, detail: not in the sandbox / not in the
   .blend), properties page, world rows skip the viewport and Anim Events, no set-to-default/revert for them;
   status, Deliver tree and Checks count world exports.
6. Quick fixes: Right Elbow + WPN (arm_lower_R + hand + fingers + wpn around the elbow); stamp_synced on load;
   Load Weapon Lab's version asks first.
Checked: 159/159 tests (Build026Tests). Headless (Blender 5.3): scratch AK sandbox (12 + 6 world actions; mount
gap 0; body mesh z 0..73), untouched world export byte-identical, an edit changes only the edited bones, all
four animation formats on a world clip, refusal across views, presets on the body; old-sandbox flow on a
scratch copy of the user's knife_ursus (keys carried 63 bones / 15 clips; 14/15 exports byte-identical, the
15th leaves weapon_offset unkeyed: pre-existing state-dependence, see MEMORY); dry-run delivery plan. NOT
clicked in the app or Blender UI; nothing in game.

## DONE 0.25.0 (2026-11-14, user "lets do all 4 file types ... rip another version ... go"; committed d5699c3; published 0.25.0)

User asks 2026-11-14: export/import of one clip's animation on both Weapon Lab and Blender in all four formats
(compiled .vnmclip_c, baked poses .json.gz, keys .blend, FBX), to bring external and pre-existing work in; and in
Blender two pickers, "Clip used" and "Exported as", saved in the .blend (CS2AL did this), to overwrite e.g. legacy
clips. Folded in today's queue: compare section removed, teal graph state, the bong example swapped.
1. Blender "Clip used / Exported as": WeaponLabSettings.export_as (get/set on the Action prop
   weaponlab_export_as); export_target/check_target (additive and hasWeapon must match); run_export samples the
   used clip into edits/<target>.json.gz and runs export-clip <target> --from <source>; ClipExportReport.SourceClip;
   Export All skips a clip another one exports into ("Not exported ..."), and a second claim on a target.
2. Animation files, Blender (add-on 0.14.0): new plain module animation_io.py (export_keys = libraries.write of the
   Action + both rigs; import_keys = copy curves by bone when same parent+rest (pose_io.same_rest/copy_curve, moved
   from build_weapon); sample_action; export_fbx at 30 fps (frame_rate), bones only, baked; import_fbx: bones by name,
   armature-space pose copied as is per frame (object map per frame: the Weapon rig rides wpn), anim_offset 0,
   ignore_leaf_bones False, constraint-driven bones and props skipped). Operators export_animation (4 buttons),
   import_animation (.vnmclip_c/.json.gz via CLI `animation convert` + pose_io.key_document on the matched bones),
   load_weaponlab_version (+ "newer" hint via action weaponlab_synced vs the sample's mtime). pose_io.sample writes
   "bones"/"parents"; pose_io.key_document keys a pose document (props via prop_places/prop_values).
3. Animation files, Weapon Lab: Core Export/AnimationFiles.cs (FromClip/FromPoseFile/Read/ToSample by name+parent on
   the build reference/Current/Write; PoseFile.Write with names), Export/AnimationTransfer.cs (ExportAsync/ImportAsync;
   keys/FBX via animation_job.py on a copy of the saved .blend). App: clip row menu Export animation… / Import
   animation… (MainWindow.AnimationFiles.cs). CLI: animation export|import|convert. An import becomes the clip's
   export (events stay) with its sample in edits/.
4. Queue: Anim Events compare dropdown, timing lines (warnings kept) and dashed ghost removed, the fit button is
   "Put my loop on the game timing"; WeaponState.PendingElsewhere (teal #4FD1C5, legend line) for shared/routing
   graphs other weapons' pending work changes; prop example now "a spare magazine held in the other hand".
Checked: 152/152 tests (Build025Tests). Headless on a scratch copy of knife_ursus: CLI export in all four formats;
imports of lookat02 onto lookat01: poses and compiled match within 0.06 deg (clip encoding), keys within 0.003
units, FBX the same for all but the prop (which FBX export puts elsewhere: skipped by design). Add-on registered in
Blender 5.2 background: Exported as (lookat02 -> lookat01, report sourceClip), the four exports, the four imports
on idle1, Load Weapon Lab's version, Export All skipping lookat01. NOT clicked in the app or in Blender's UI (the
file browsers, the panel); FBX from other tools untested (needs Blender bone axes); not in game.

## DONE 0.24.0 (2026-11-09, user "GO. cook"; uncommitted on top of uncommitted 0.23.0; published 0.24.0)

User answers 2026-11-09: 1 yes; 2 = the Blender add-on changes the gun model and
skeleton (CS2AL had Replace Preview FBX), applied when picked (agreed), FBX or
vmdl_c; 3 yes; 4 settings menu with a workspace picker (moves nothing) and the
default workspace beside the exe; 5 yes; 6: no plays-like picker, keep a
display-only "expected class" for timing bands (visualize, not edit), Checks
yes, timing bands + plain-words warnings yes, Fit tool no, Simple/Advanced +
wizard another time; no PC2 tests for now.
1. First edit creates the sandbox: Anim Events guards relaxed to the session;
   CommitEvents validates, then MakeSandboxForEditAsync (EnsureSandboxAsync,
   reopen, template bytes must equal the edited ones and no edits there, else
   refuse with a message). "Make editable" button removed.
2. Expected class: WeaponProfile.TimingGun ("Group/Name", display only);
   GameTiming.cs TimingGun/Resolve/Guns, GameTiming.Bands (intro/loop/outro/
   done, before/after the refill), GameTimings.Warnings (shared plain words),
   game_timing.json schema /2 ("source", "bands"; bridge reads old "playsLike").
   Anim Events "Game timing" picker (ShellReloadPanel), EventTimeline.Bands drawn
   behind the lanes (ghost only for other compare references); weapon panel row
   "Game timing"; CLI events fit/WriteGameTiming use the pick. Bridge: bands in
   Dope Sheet (all modes incl. Timeline) and Graph Editor (draw handler).
   PlaysLike class kept internally (it computes what the vdata gives).
3. Checks: Core Checks/WeaponChecks.cs (Blender: NeedsUpdate, template sha vs
   rig.json after a skeleton change; Timing per reload as played: export, addon
   clip, CS2) + app MainWindow.Checks.cs (model check, hookup, vdata model,
   timing, Blender), Checks… button/menu, dialog with Show/Fix (Build .blend,
   Update for export, Copy skeleton path, Show clip/VData/Deliver/skeleton),
   outliner weapon Detail "⚠n · clips" (RefreshCheckCounts after status).
   Deliver: DeliveryPlanner.WarnTiming for pushed reloads. RigPackage.ReadRig.
4. Set to CS2 default: Skeleton row (CustomSkeletons.Remove, backup to
   workspace\backups\stock-skeleton) and graph rows (Profile.StockGraphs; the
   planner rebuilds own graphs from CS2's with ResourceMap + clipAliases, notes
   it, clears after push; shared graphs only lose pending edits).
5. Settings: Core Settings/AppSettings.cs (%APPDATA%\CS2WeaponLab\settings.json,
   WEAPONLAB_SETTINGS for tests): WorkspaceFolder, BlendFolder.
   SandboxStore.DefaultWorkspacePath: env var, setting, (no settings file) an
   earlier workspace with profiles, else BesideApplication(). App Program
   PinWorkspace saves the workspace in use on the first 0.24 start (skipped
   with WEAPONLAB_WORKSPACE). SettingsDialog (top bar Settings…; restart for a
   new workspace). Move .blend… (button + menu), Show .blend, new .blend
   suggestion follows BlendFolder (ChooseBlendLocationAsync; the Build .blend
   window itself still has no path row: the save dialog is the "Change").
6. Gun model/skeleton from Blender + keep keys: build_weapon.carry_keys (old
   actions + rig objects loaded from the backup; per clip/slot, bones with the
   same Blender parent and matrix_local get the old curves, handles restored
   after update(); bones the old clip left unkeyed get no keys and the old pose
   basis; new or changed bones keep the clip's motion). BlendBuilder.BuildAsync
   carryKeys (carries from the backup it makes; BlendBuildResult.Carried).
   Rebuild dialog: Keep my animation / From the clips only (Dialogs.ChooseAsync).
   CustomSkeletons.ModelChangedSince -> Build .blend remakes the skeleton when
   its addon model was compiled again. CLI `blender set-model <weapon> --model
   <vmdl_c|key> [--skeleton] [--hierarchy] [--keep]`. Bridge 0.13.0: "Gun model
   and skeleton" box, Change gun model… (vmdl_c -> CLI set-model then opens the
   rebuilt file; fbx -> Blender-only preview skinned to the Weapon rig).
   Export guard: ClipExporter.Export(keepEditedExport) -> ExportGuardException,
   CLI exit 6 "WEAPONLAB_GUARD", add-on popup "Replace the export anyway"
   (force), Export All skips; the add-on samples to <clip>.json.gz.new and only
   renames after a successful export. App reloads the profile when the window
   is activated and profile.json changed outside (ReloadProfileIfChangedAsync).
7. Variations: Core Graphs/Variations.cs (Graphs = weapon-owned graphs, addon
   version when same layout; Groups = ParameterizedClipSelectors, draw under
   /Deploying/, inspect per state, merged across selectors; EditsFor; whole
   number weights (CS2 stores ints: 0.5 was refused by the patcher's verify);
   RandomReloadFor = PC2's AK recipe generalised: duplicate second draw
   selector, its state -> the first, parameter -> weapon_ammo_reserve, Reload0
   selector option 0 -> it; found on AK, Deagle, SSG08, Nova, FAMAS; not on
   knives). App MainWindow.Variations.cs dialog (Variations… button/menu).
Checked: 146/146 tests (Build024Tests: picked gun + bands + warnings, AK
variations = PC2 recipe, random-reload and rare-inspect edits patch CS2's AK
graphs and read back, settings workspace). Scratch copy of the user's
knife_ursus sandbox: CLI set-model (same model, skeleton + carry) -> 15 clips,
9290/9290 curves identical, evaluated poses of every bone on every frame equal
(worst 1.9e-6; before the handle fix 0.017 on fingers, and lookat01's unkeyed
weapon_offset was re-keyed before the unkeyed fix); export of lookat01 from the
rebuilt .blend works; the export guard refused an unedited sample over an
edited export and left it untouched. Published app starts on a scratch
workspace and shows the new buttons (screenshot). NOT clicked in the UI
(Checks, Variations, Settings, Move .blend, Game timing picker, bands,
first-edit sandbox, rebuild choice, skeleton/graph resets); the Blender
operator (file browser, set-model run, reopen; FBX preview) not run in a live
Blender; a real model with added bones not tried; graph StockGraphs rebuild
not dry-run on real data; nothing tested in game.

## DONE 0.23.0 (2026-11-07, user "go" with answers 1 warn, 2 both, 3 yes, 4 clips AND skeletons, 5 hidden; uncommitted; published 0.23.0)

Built in a side session from the draft (the user's answers: root-bone props
WARN only, no camera-space tracks; tick in BOTH bone details and Build .blend;
remove the knife's unused copy; Copy asset path on clips and especially
skeletons, no `_c`; left-out weapons hidden).
1. Props by choice: `WeaponProfile.PropBones` (profile JSON `propBones`) ->
   `RigPackage.Write(propBones)` (via `BlendBuilder.BuildAsync`, app and CLI
   `blender build`) -> rig.json `freeBones` for ANY weapon skeleton (stock too);
   `RigPackage.PropBones` drops the root, unknown bones and bones under a ticked
   one. Removed "parentless in the model" (`CustomSkeletons.FreeBones`,
   `SkeletonBuilder.FreeBones`, setup `FreeBones`). Blender side unchanged
   (0.22.4 anchor/hold/counter-motion). UI: Property Browser rows can be a
   checkbox (`PropertyRow.Toggled`, `OnPropertyToggleChanged`), bone details
   row "Prop" (root: explains it cannot), plus "In the model" (model check per
   bone); Build .blend window section "Keep apart from the gun" (checkbox per
   bone of the current skeleton; ticks for other skeletons' bones kept).
2. Model check: `Skeletons/ModelCheck.cs` (model = vdata m_szWorldModel via
   profile edit > addon vdata > CS2 for the weapon's blocks, else the
   skeleton's source model; compiled from the addon or game, cached by file
   time; `TargetsSkeleton` from SkeletonRefs; `Mismatches` per shared bone;
   `Problems()` sentences with the fix). Shown: outliner Skeleton row (detail
   "⚠ model check: n problem(s)" + tooltip; colour unchanged), bone details,
   Build .blend window (+ "Copy skeleton path (for ModelDoc)"), Deliver
   warnings (subject weaponId; the tree now matches warnings by weaponId).
3. No model copy: `PlanSkeleton` ships only the skeleton; `CheckModel` adds the
   warnings; `PlanStaleModelCopies` removes manifest-listed
   models/weapons/weaponlab/*.vmdl_c that no compiled vdata, profile edit or
   vdata source names (backed up; note per file). `CustomSkeletons.ModelBytes`
   removed. Texts: Skeleton panel, Deliver Skeleton group, tab text.
4. Copy asset path (no `_c`): outliner Skeleton row (custom path, else the
   slot's own) and clip rows (`AddonInventory.ClipFor`, else CS2's); Deliver
   tree file/removed rows and warnings naming a skeleton (`SkeletonPathIn`).
5. Deliver: weapons left out with nothing in the push and no warnings are
   hidden; "Show left out (n)" checkbox beside View Tree/List.
6. Knife animation slots (added mid-build, user "Add to 0.23.0" via the main
   session): the VData tab put weapon_knife_ursus on the "CT Knife (default
   knife)" slot and played lookat01_knife on the bong. Every CS2 knife block
   has _class weapon_knife and no m_szAnimClass; the root graph has no per-knife
   weapon_type (knives run through viewmodel_knife.vnmgraph, a variation per
   knife). `WeaponSlots.PicksPerKnife` (class weapon_knife, no anim class):
   `WeaponSlots.For` gives a knife its own blocks only (`PerKnife`), `Describe`
   says "own animations ... per knife, not by class" (Build .blend window and
   plays like follow); VData tab `AnimationsOf(block)` maps a knife block to its
   own weapon (info text "Animations: its own ...", viewport clips and title),
   and knives leave the class slot map. Guns keep the anim-class/_class rule.
   UNVERIFIED: how the game picks the knife variation. Test
   EachKnifePlaysItsOwnAnimations (141/141).
Checked: 141/141 tests (new: model check mismatches + Problems, opt-in props, knives,
ticked bone reaches rig.json on the AK; DeliveryTests skeleton test now asserts
no copy, the model-check warning and the stale copy removal). Real data, read
only: knife_ursus (user's recompiled model) and rifle_ssg08 (Annabelle) pass
the model check. Scratch copy of the knife: ticked lighter_offset -> rig.json
freeBones, .blend has it under the anchor, hold drift 7.6e-6 over idle,
lookat01, draw; untouched idle exports only lighter_offset. Unticked: lighter
under weapon, all 15 clips export identical. NOT clicked in the app UI (bone
checkbox, Build .blend props section, Show left out, Copy asset path menus);
the copy removal not run against the real addon (next push will do it); not
tested in game.

## DONE 0.22.5 (2026-11-07, user "Yes, fix it"; committed 2026-11-07; published 0.22.5)

User: the Revert to addon default dialog for rifle_galilar (cs_hl_template)
"appears to be broken": every row greyed out, Revert disabled. Cause: the galil's
only pending work is its custom skeleton (AR2 model, `weaponlab_rifle_galilar`,
never delivered, no exports), so all 7 clips were "only re-targeted" rows that
cannot revert, and nothing in the dialog could drop the skeleton. Built:
`RevertKind.Skeleton` / `RevertParts.Skeleton` (in All; the app passes it for the
weapon row on Animations/Sounds/Deliver and alone for the Skeleton row).
`AddonRevert.SkeletonItem`: offered when the profile's custom skeleton is not
FromAddon and `PendingWork.StateOf` says pending (same test as ProfileStatus);
the re-targeted clip rows fold into it. Apply runs it last: copies the
sandbox `skeleton/` and `exports/` to the backup, then `CustomSkeletons.Adopt`
(when `FindInAddon` finds the addon's own skeleton for the weapon) or `Remove`,
and sets the profile's CustomSkeleton / PreviewModel like ApplySkeletonAsync.
Apply takes an optional GameConnection (skeleton items need it). A single clip
row still cannot drop it and says which row can. App: when no item can revert,
a message lists the reasons instead of the dead checklist; Apply's catch also
takes ArgumentException. Also fixed double-encoded `â†’`/`â€¦` in
AddonRevert.cs (VData/graph revert text since 43a4498). Checked: new game-backed
test (AK custom skeleton -> one item, Apply -> CS2 default, nothing pending);
the user's galil on a scratch copy: one item "Its 7 clip(s)…", after Apply no
pending clips and nothing left to revert, 9 files in the backup. 137/137. Not
clicked in the app UI; the adopt branch is untested (no addon skeleton case).

## DONE 0.22.4 (2026-11-07, user "Yes, build it"; committed 2026-11-07; published 0.22.4)

User on 0.22.3: the prop should be in the Weapon rig, not the Arms rig, and its
keys (copied from wpn x weapon pose) made it look parented. Built: the prop stays
in the Weapon rig; its Blender parent is a hidden bone `pose_io.PROP_ANCHOR`
("Props anchor - stays with the arms", collection "Props anchor - hidden",
rest = inverse(wpn rest)) with COPY_TRANSFORMS world from the Arms rig OBJECT,
which cancels the mount. Prop in "Props - edit these" (amber,
`rig_style.apply_prop_colors`). `sample` reads each bone relative to its GAME
parent (allows PROP_ANCHOR as Blender parent); `channel_values(skip=props)`;
`prop_places` (carried) / `prop_hold` (default clip frame 0) / `prop_values`
(basis under the anchor, anchor pose = inverse(wpn) per frame) / `held` /
`clear_bones`. Build keys props carried, samples the references (stock), then
re-keys them held. Reset holds them; on a 0.22.3 .blend it refuses with "Build
.blend again". Bridge 0.12.2. Checked headless on scratch copies: layout right;
references = stock within 1e-5; hold drifts <8e-6 over all frames of all 15
clips; untouched idle/draw export changes only lighter_offset, game vs Blender
within 1.2e-4 units; wpn +4 -> lighter moves 4e-6, export edits wpn, wpnHand_L/R,
lighter_offset; Reset back to hold. AK: all 11 untouched clips identical.
User's own 0.22.3 .blend (copy): all 15 untouched clips identical, Reset refuses
as designed. 136/136. Not tested in the user's Blender or in game; the user
must Build .blend again. Hold place is the default clip's start (user picked
over each clip's own start).

## DONE 0.22.3 (2026-11-07, user "build 0.22.3 and fix the issue. go"; committed 2026-11-07; published 0.22.3)

User built the knife_ursus .blend in 0.22.2 (cs_hl_template, custom skeleton
from addon model custom_knife/smoking_apparatus/broken_knife_ag2.vmdl_c, model
hierarchy, made 14:23 before 0.22.2) and the lighter still moved with the bong.
Measured: the 15 ursus clips never move `weapon` (0.00 units, 0 deg); all bong
motion is arms `wpn`, which carries the whole Weapon rig (mount), so a root bone
in the Weapon rig would not help either. Built: free bones (the model's
parentless bones besides its root; `SkeletonBuilder.FreeBones`, setup
`FreeBones` from Create, `CustomSkeletons.FreeBones` reads the model for older
setups) go to rig.json `freeBones`. Blender: `add_prop_bones` makes an
unparented Arms-rig bone (amber, "Props - edit these", `weaponlab_prop`) keyed
from wpn x the clip's weapon pose (`pose_io.prop_values`); the Weapon-rig bone
(still under weapon, as the game skeleton) gets COPY_TRANSFORMS world from it and
moves to the hidden anchors collection, so `sample` writes it relative to the
gun unchanged. `mount_weapon` now runs before the reference samples (the world
constraint needs the mount; references were wrong without it). Reset Clip
re-keys props. Bridge 0.12.1. Checked headless on a scratch copy of the user's
sandbox: untouched idle1 exports byte for byte; wpn +4 units -> prop stays put
in Blender (0.000000), game composition matches Blender within 0.000006, export
edits wpn + lighter_offset only; Reset restores the prop exactly. 136/136. The
game skeleton keeps one root (two-root skeletons never tried in game). Not
tested in the user's Blender or in game; the user must Build .blend again.

## DONE 0.22.2 (2026-11-07, user "Yes, fix it"; committed 2026-11-07; published 0.22.2)

User is making a bong model with a lighter on its own parentless bone
(`lighter_offset`, beside `weapon`) so the lighter does not follow `weapon`.
Found: SkeletonBuilder.FromModel took the FIRST parentless bone as the root
(compiler order decides; lighter first = whole gun under the lighter with the
model's off-origin weapon bind = the 0.18.0 lifted-gun bug), and AlignTo (the
default) threw on any extra parentless bone. Fixed: `SkeletonBuilder.ModelRoot`
(parentless bone named like CS2's skeleton root, else one CS2's skeleton has,
else the first); FromModel emits it first; both modes hang other parentless
bones under it at inverse(root bind) x bone bind (`UnderRoot`). Test
NextBuildTests.ASecondRootBoneHangsUnderWeaponWhicheverComesFirst (fails on the
old code, checked). 136/136. Advice given: the model rides on arms `wpn` in
game, an unanimated bone stays at bind, so the lighter needs its own keys or a
Child Of to hand_L in the .blend (pose_io samples the evaluated pose); weight
the lighter mesh to the bone or the compiler prunes it. Not tested with the
user's model or in game.

## DONE 0.22.1 (2026-11-07, user "fix this. go."; committed 2026-11-07; published 0.22.1)

User bug: the M249 on the AUG slot (VData viewport) played CS2's reload_aug,
though cs_hl_template plays its own rifle_aug clips (animation/anims/custom/
rifle_aug, 8 clips, 2026-10-24, made before Weapon Lab, CS2's AUG skeleton);
Animations and Anim Events showed CS2's too. User: "we need to be inheriting
the custom stuff when its green in the viewports, not what weaponlabs
remembers". Fixed: AddonInventory.ClipFor/ClipFile (graph overrides, else a
clip standing in by name); ViewportClips.Load order = your export (when
ticked) → the addon's own → sandbox template → CS2 (falls back to the
template when the addon's clip drives another skeleton); "· the addon's" on
the timeline; ClipPresenter reads the addon's version (+ skeletons only the
addon has); Anim Events with no sandbox shows the addon's; EnsureSandboxAsync
takes the addon's clips when it has some (BuildFromAddon != false) - before,
a first Anim Events edit made the sandbox from CS2's and the push would have
replaced the addon's animation; WarnSandboxFromGame logs when an existing
sandbox came from CS2's while the addon has its own (the user's rifle_scar20
sandbox in cs_hl_template is in that state; rifle_galilar and rifle_aug have
none). Checked: AddonClipTests (real addon: reload_aug resolves to the
custom file and plays differently from CS2's), 135/135; the debug app on a
scratch workspace with cs_hl_template: VData M249 plays "reload_aug · the
addon's" with the hands on the launcher.

## DONE 0.22.0 (2026-11-07; committed in 43a4498 with 0.20.1 + 0.21; published 0.22.0)

All 5 items below built. Core: VData/AddonVData.cs (source text merged on the
compiled values through each block's _base chain in the source;
VDataFile.FromBlocks; used by ProfileStatus, AddonRevert and the VData tab),
VDataView.Rows writtenByLab (keys Weapon Lab never wrote start from the
addon's current value), WeaponProfile.StockClips + DeliveryPlanner
PlanStockClips (shared RedirectGraphs with PlanStockResets; plan.StockClips,
RecordIn clears them and the Pushed record) + ProfileStatus reason,
PreviewModels.ForVDataModel. App: clip-row Set to CS2 default (MainWindow.
Reset.cs), outliner description per tab, tab constants reordered, VData tab
(MainWindow.VData.cs all blocks + jump + scroll-follow, MainWindow.
VDataView.cs info + viewport; outliner column collapsed and RightColumn rows
*,150 on VData; the shared ViewportPanel moves into VDataViewportHost).
Checked: 134/134 tests (AddonVDataTests 2: synthetic + the real
cs_hl_template source equals its compile on every weapon value;
RevertTests.OneClipGoesBackToCs2s); the debug app run on a scratch workspace
(addon weapon_template): VData layout, scroll-follow, Jump to weapon_ssg08
(viewport shows weapon_snip_ssg08 + arms playing idle_ssg08, info panel).
Not tested: per-clip CS2 default in the app UI and in game; the user's own
addon in the new VData tab; editing values in the all-blocks list.

## PLAN 0.22 (user 2026-11-07 "ask 3 questions then we build this and our 0.22 plans, lay it all out. go")

User answers (2026-11-07, second ask; the first did not show): the addon's
vdata SOURCE file is the truth for green/yellow (green as soon as it is saved
outside Weapon Lab, before compiling); VData layout = editor on the left
(a bit narrower), viewport in the middle, info on the right with History small
underneath; the queued items (Checks list, Move .blend, plays-like picker,
new-gun wizard) wait for a later build. Also: top tabs reordered to
Animations | Anim Events | Graph | VData | Deliver. The 3D viewport shows "what
the vdata has for a vmdl" (the block's m_szWorldModel).
Order:
1. BUG VData external edit (user 2026-11-07): SSG08 "Time between shots" showed
   0.7 yellow though the addon (source + compiled 2026-11-04 19:41) has 1 and
   the profile has no edit. VERIFIED cause: VDataRow.Baseline = AddonOriginal
   (originals snapshot 2026-10-24 has 0.7) for keys Weapon Lab never wrote
   (manifest.VData has no ssg08 keys). Fix: a key Weapon Lab never wrote
   starts from the addon's current value; the originals only matter for keys
   it wrote (reset goes back to them). Also re-read vdata_c when its time
   changes (was cached per session), and say when the source is newer.
2. Set to CS2 default per clip (outliner clip rows): sets the clip's work
   aside; profile WeaponProfile.StockClips; push removes the addon's custom
   clip and points graph slots back at CS2's (custom skeleton: the
   re-targeted CS2 clip is pushed instead); cleared after the push.
3. Outliner description line per tab (Animations = animation slots: CS2 picks
   first-person animations by anim class; Anim Events; Graph = the graphs a
   slot plays through, picked by CS2's root graph by anim class, the addon's
   _custom copies start from CS2's; Deliver).
4. VData tab rework: no outliner on this tab; editor on the left; every block
   in one scroll (block header rows) with a jump selector; the block in view
   drives the right side: 3D viewport (the block's m_szWorldModel, the slot
   its anim class plays, clip picker + play) on top, info below (changed from
   CS2, not saved, set outside Weapon Lab, class based on, buy-menu slot,
   recoil inherited from, animation slot). Header text: a vdata block is the
   item the game expects (buy-menu slot, inherited recoil pattern).
5. Top tabs: Animations | Anim Events | Graph | VData | Deliver.
Later (user: "come back to these in a later build"): per-weapon Checks list;
Move .blend… + default folder; plays-like picker; new-gun wizard.

## DONE 0.21.0 (2026-11-07, user "start building 0.21. go"; uncommitted on top of 0.20.1; published 0.21.0)

User 2026-11-05: the pushed Annabelle "seems to work good now" (0.20.1 reload
at 50 frames on Nova timing, in game). Built:
* Outliner right-click menu per row kind (Rows.cs IsWeaponRow/IsClipRow/
  IsGraphRow/IsSkeletonRow/HasName/IsPending; MainWindow.OutlinerMenu.cs):
  open in Anim Events/Animations/Graphs/VData/Deliver, open/show .blend, show
  sandbox, copy name, collapse all; Set to CS2 default only on weapon rows now.
* Revert to addon default (yellow rows only): Core Profiles/AddonRevert.cs
  (Plan/Apply, parts follow the tab), Export/PushedClips.cs (pushed/<clip>/:
  clip, report, edits sample, events; Keep/Restore/SetAside),
  PendingWork.KeepLive (app status refresh + CLI deliver). Clip: kept copy ==
  addon sha -> restore; addon has none or its own -> set aside; pushed before
  0.21 and changed since -> refused with reason. Graph: drop pending keys
  (delivery builds on the addon's graph). VData: drop or pin to the addon's
  value; class swap dropped when not in the addon. App MainWindow.Revert.cs
  (checkbox dialog, backups to workspace\backups\revert\<weapon>-<stamp>).
  The user's reload_ssg08 event edit of 2026-11-04 19:44 (after the 19:21
  push) is in that refused case.
* Show buttons: Core Checks/CheckTargets.cs (graph node + custom->CS2 graph
  path, longest clip id, weapon id, vdata block); PlanRow/DeliveryTreeNode
  Target; planning/apply errors shown in both Deliver views; Graph tab
  _requestedGraphNode -> GoToGraphNode.
* Anim Events outliner colours clips whose events are not pushed (eventsOnly).
* Viewport default model = CS2's: ViewportScene/Build .blend/CLI drop the
  skeleton-model fallback; Profile.ViewportDefaultIsCs2 +
  ViewportModels.KeepOldDefaults (once per profile, app load and CLI
  LoadProfile); a skeleton made/adopted from a model stores it as the pick;
  back to CS2 default clears a pick equal to it.
* Bridge 0.12.0: rig_style.py (muted finger colours, scale follow), build
  stores meta "sandbox", blend_folder() falls back to meta sandbox then
  WEAPONLAB_SANDBOX (BlendBuilder.Open sets it; ignored for another weapon),
  upgrade_blend() on open (launch.py + load_post).
* Fixed: ClipLengths.Write threw on a sandbox without blender\input (0.20.1
  bug: saving Anim Events on a weapon with no .blend).
Checked: 131/131 tests (RevertTests 4); headless Blender on the user's SSG08
.blend (56 arm bones coloured, 54/44/13 preview bones follow scale, a second
open changes nothing, moved copy finds the sandbox via meta, range 0-49);
fresh CLI build of pistol_deagle in scratch (meta sandbox, colours, 1.5x bone
scale reaches the preview bone). Not tested: the app UI (the user's 0.20.1
app was open; single-instance guard), in game.
Still open from the queue: per-item Set to CS2 default for clips/skeleton/
graphs (needs delivery support to remove one clip); a per-weapon Checks list
(only Deliver Show buttons so far); Move .blend… and a default .blend folder
setting; plays-like picker.

## DONE 0.20.1 (2026-11-04, user "Go"; uncommitted; published 0.20.1)

Clip length + animation-only Blender export, as designed below. Core:
Clips/ClipLength.cs (Resample, Rescale times/spans/sections, At, SetFrames,
Apply on the KV data), ClipEdit.FrameCount (writer re-encodes every track
at the new length, rewrites m_nNumFrames/m_flDuration/root-motion frames
and event/sync times first, refuses float channels/root motion transforms),
StoredEventEdits.Frames ("frames" in events/<clip>.json), ClipExporter cuts
or holds stock/reference/sample to the length and no longer merges Blender
markers, Blender/ClipLengths.cs (blender/input/clip_lengths.json). App:
Anim Events clip info "Length [n] Set" (session.Template = clip at the set
length), Reset keeps the length. CLI: `events length`. Bridge 0.11.0: export
samples poses only (no markers), clip_frames/clip_at_length from
clip_lengths.json (scene range, sampling, same fps), Events box = note;
build_weapon makes no markers; marker code removed. Tests 127/127 (new
ClipLengthTests 4: rescale rules, SSG08 reload cut to 50 decodes with
events on their frames and template poses, inspect lengthened holds the
last pose, length stored with edits). Checked headless: add-on scene range
0-49 / 50-frame sample; Codex's reload_ssg08 sampled at 50 and exported with
the published CLI to a scratch file (events 11/20/24, sync 24-49, error
0.0002 units). Annabelle: reload_ssg08(+_lgcy) 50, lookat01(+_lgcy) 169 set;
silent reloads and shoot1 left for the user to decide; the .blend's 65 stale
markers stripped (keys identical). Not tested: in game; the app's Length
control in the UI; a real Blender export from the user's session.

Bridge 0.11.1 (2026-11-04 evening, user report "extra empty keyframes still
exist"): the scene range was only set on a clip switch, so the SSG08 .blend
opened at its saved 0-111. fit_scene_to_clip now runs on open (sync_settings)
and a 2 s timer refits when clip_lengths.json changes. Verified headless on
the user's .blend: opens 111 -> 49; switches give 168/50/49. Copied into
publish\CS2WeaponLab-0.20.1-win-x64 (no rebuild). Export sampling was never
affected (it always used the clip's length).

## (was) Queued 0.20.1: Blender export = animation only (user decision 2026-11-03)

User, after Codex's re-animated SSG08 .blend (saved 21:50, every action has
0 pose markers) failed to export 10 of 12 clips ("Event marker #0 … is
missing"): Blender export should not touch events, "we have the animevent
editor for a reason". Reverses the 0.19 rule "a sound marker moved in
Blender wins for its event". Proposed:
1. Bridge export samples poses only: no collect_event_edits, no marker
   checks; missing, moved or renamed markers are ignored.
2. Event timing comes only from Anim Events' stored edits (events/<clip>
   .json), applied on every export as today (check export-clip/EventEdits
   .Merge with no Blender events keeps the stored edits as they are).
3. Sidebar: the Events box (markers, Change Sound, Reset Events) becomes a
   read-only note "edit events in Weapon Lab's Anim Events tab"; builds stop
   adding event markers (they would go stale). Later: show the app's
   current events read-only in Blender, like game_timing.json.
ALSO (user 2026-11-03, after the repair: "frames 50 to 111 being blank"):
CLIP LENGTH CHANGE belongs in this build. Codex animated reload_ssg08 to
the Nova's 50 frames (0-49), but the clip keeps the SSG08's 112 frames, so
frames 50-111 just hold the last pose (2 s; whether the graph cuts it when
the game's reload ends is untested). I had said "hold after 49" without
flagging that dead time. Needed: a per-clip frame count (Anim Events +
CLI; Blender action/scene range from rig.json), the export writing every
track at the new length from the Blender sample (template pose data can no
longer be reused), m_nNumFrames/m_flDuration updated, events and sync
sections kept on their frames (re-normalized), reference samples and
EditDeltas at the new length; golden test: an untouched clip stays
byte-identical; in-game test of a 50-frame reload_ssg08. Targets now:
reload_ssg08(+_lgcy) 50 frames (Nova), reload_silent_ssg08(+_lgcy) 99
(Nova silent) or 50 if the user prefers, lookat01_ssg08(+_lgcy) 169
(Codex's keys run to 168).
Also seen in that .blend (tell the user): lookat01_ssg08(+_lgcy) keys run
to frame 168 but the clip is 160 frames (0-159), so the last 9 frames are
cut on export; reload_silent_ssg08(+_lgcy) is animated at normal speed
(0-49) while the clip is 223 frames and the Nova's silent reload runs at
half speed (loop 22-48, shell 40, outro ends 98; events fitted to that).

## DONE 0.20.0 "Plays like" (2026-11-03, user "lets build 0.20 … go"; uncommitted)

Published publish\CS2WeaponLab-0.20.0-win-x64 (0.19.0 folder kept). Tests
123/123 (new PlaysLikeTests 4: groups/defaults read from CS2's graph, the
type->category map holds for every stock weapon, the Annabelle setup =
Shotguns/unlisted/Nova/no scope with reload_nova marks 11/20/24/49, AK with
the FAMAS class refills at 1.67 s, Fit moves every event through the
piecewise map).
* Core: Weapons/PlaysLike.cs (GunSelection read from the root graph: WPN SM
  states gated by weapon_category, group SMs by weapon_type, defaults;
  PlaysLike.For/ForSlot/WeaponOf/MatchingClip; PlaysLikeResult.Describe/
  Warnings: sticky fallback, no scope view off the sniper type), Weapons/
  GameTiming.cs (GameTimings.For: the gun's same-role clip -> loop marks or
  refill; Write: blender/input/game_timing.json), Clips/EventRetime.cs
  (FrameMap, EventRetime.Remap), ShellReload.Fit/Scale/Read(template,edits).
* App: weapon panel "Plays like" row; Anim Events "In game" section, compare
  list starting with the plays-like gun's clip, counter-language warnings,
  outro note, "Use the …'s timing" button (events only); game_timing.json
  written on Build/Update and Open in Blender.
* CLI: `events fit <weapon> <clip> [--from s,e,shell] [--dry-run true]`;
  blender open/build write game_timing.json.
* Bridge 0.10.0: sidebar "Game timing" box (marks + jump buttons, plays-like
  text, warnings), Action Editor orange lines + labels (draw handler).
* Checked: app UI screenshot (Anim Events on reload_ssg08), Blender UI
  screenshot (sidebar + lines). Not checked: in game; magazine-reload text
  in the app UI; the Blender lines at other zoom levels.
* Decided with the user: "Fit" moves events only; Codex/the user animate to
  the frames (keyframe re-time deferred).
Next (0.21 candidates, from the direction section): Checks list + jump
buttons; a "plays like" picker (sets type + class + routing; listed class =
replace that gun's visuals, unlisted = sticky fallback, with warnings);
keyframe re-time Fit; clip length change; random reloads / rare draws /
inspects; particle events; Deliver warning when a pushed reload's timing
differs from its plays-like gun.

## Direction: "CS2 gun sandbox" (user, 2026-11-03, "dont go"; proposal, not started)

User: the engine limits (gameplay runs CS2's own graph: weapon type picks
the group, anim class picks the gun, an unknown class falls back to the
group default, e.g. the Nova) make Weapon Lab unfriendly to users who do
not know them; wants an easy "CS2 gun sandbox" with optional advanced
settings. Proposed principle: the app knows the engine's rules and turns
each one into a visible guide or a one-click fix, so users never need to
learn them first.
1. "Plays like": per weapon, derive from weapon type + anim class + CS2's
   root graph which gun's gameplay it runs (Annabelle: "Nova, shell by
   shell") and show it on the weapon, with what that fixes (timing per
   clip role, shell reload yes/no, what can and cannot change).
   User question 2026-11-03 ("Deagle slot with a sniper over it: are we
   targeting a weapon type?"). Model to build: two separate choices.
   * Plays like = one CS2 gun, set as a pair: weapon type picks the group,
     anim class the gun in it; a class not in that group gets the group's
     default gun (Shotguns -> Nova; Pistols default = USP). Options: every
     gun state in CS2's root graph, grouped. Fixed by it: reload style and
     refill/loop timing (verified: AK with the FAMAS class refills at 1.67 s
     instead of 1.11 s, pc2/MEMORY). Free via vdata: clip, fire mode/rate,
     spread, speed, reserve, model, zoom; damage/recoil/gunshot come from
     the parent class's block (MEMORY dupes).
   * Animates in = the visual slot (graph + skeleton), any slot through
     viewmodel_custom routing; defaults to the plays-like gun's.
   Catch to surface: routing is keyed on the anim class, so a gun sharing
   another gun's class shares its visuals (the real Deagle would show the
   sniper's animations): offer "replace that gun" or "fall back to the
   group default with a class from another group" (Annabelle route).
   Open questions for an in-game test (PC2): a class from another group in
   the Pistols group (plays like USP, own visuals?); PISTOL type on a
   scoped gun (does zoom still work?); why the made-up weapon_annabelle
   class broke firing while weapon_ssg08 did not.
2. Timing budgets everywhere: the borrowed gun's intro/loop/ADD_AMMO/outro
   as bands on the Anim Events timeline (auto-picked, not a manual
   compare), as Blender timeline markers/frame ranges in the built .blend,
   and as an export/Deliver warning in plain words ("the counter adds a
   shell 1.0 s before your animation shows it") with a fix button.
3. Fix tools: "Fit to gameplay timing" (piecewise re-time of keys + events
   per section, previewed, undoable), and on a new gun a starting reload
   built from the borrowed gun's motion at its timing.
   The 0.19 shell-reload helper stays and becomes the base of this (user
   asked 2026-11-03 if it is useless). On the Workshop route its INTRO/
   LOOP/OUTRO sections still shape the viewmodel's loop (without them the
   SSG08-slot clip plays straight through, seen 2026-10-31), but its
   ADD_AMMO/SILENT/DROP_MAG changes only drive gameplay when the game reads
   our clip for gameplay (the local-only root copy; PC2: 7 passes = 7
   rounds). Change: start from the plays-like gun's timing (Nova: sections
   11/24, ADD_AMMO 20), warn on drift, offer Fit. Untested: whether our
   clip's sound events play at all on this route (B' 2026-10-31: the
   reload sounds heard were the Nova's); check with snd_showstart 1.
4. One "Checks" list per weapon and per push: every known problem in plain
   language with Show (jump to the graph node / clip / bone, the
   2026-11-02 jump-button feedback) and Fix buttons. Backed by a rules file
   of the limits found so far (MEMORY), also shown as in-app help.
5. Simple vs Advanced: Simple = Guns -> New gun wizard ("what kind of gun:
   pump shotgun, bolt sniper, lever action…" -> picks weapon type, anim
   class, routing, timing templates, skeleton), Animate (Blender), Sounds &
   events, Stats (friendly vdata subset), Test in CS2 (launch the addon on
   a map with the gun, binds ready; VConsole tooling exists). Advanced =
   today's Graphs, raw VData, skeleton and routing tools, unchanged.
Proposed order: 0.20 = 1 + 2 + 3 (fold in the 0.20 items above: clip
length change, borrow a slot's logic); 0.21 = 4 + jump buttons; 0.22 = 5.
Needs: a model of the root graph's weapon selection (category -> group ->
anim class -> state, default state) read from CS2's graph, not hard-coded.

## Later candidates

- Retiming (changing frame count/duration).
- New soundevent definitions and WAV import.
- Player-model hookup helper: give the addon's player models a hudmodel
  graph of viewmodel_custom (user: "save that for LATER", 2026-10-26).
- Migrating `.csal` saves and the AR2 project.
