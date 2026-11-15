# CS2 Weapon Lab: PC2 plan

Started 2026-10-29. PC2's own task list. Only PC2 edits this file; see
"Two PCs" in `.claude/CLAUDE.md`. The project plan is PC1's
`.claude/PLAN.md` (read-only from here).

## Role

Spare test machine: run builds and tests against the live game, check
things in game in the VDATA LAB (addon `weaponlab_vdatalab`, map
`wl_vdlab`), and report results to PC1 under "For PC1" in
`.claude/pc2/MEMORY.md`.

## Open

- 2026-11-07: PC1's custom-skeleton tests answered under "For PC1" (model hierarchy
  decides the frame; root props are placed from the camera). Not run: two-root skeleton,
  "Do Not Discard Skeleton Bones", K track, the ursus slot itself (econ knife).
- Queued after that (user asked for a list 2026-11-03): animgraph edits - clip speed
  multiplier on gameplay clips (root copy), state entry/timed events, cross-group
  routing, then speed/transition/ammo-driven visual edits.

- 2026-11-03: PC1's three "plays like" tests answered under "For PC1" in
  `.claude/pc2/MEMORY.md` (sticky fallback, PISTOL-type zoom has no scope view,
  made-up class fire bug not reproduced). Not committed yet.

- User testing ADS + barrel heat on PC2 (2026-10-31): CS2 tools mode, `map de_dust2`,
  `exec wl_dust2`. Agent hookup only (test rule in memory).
- Waiting on the user: approve handing `wl_vdlab.vmap` to PC1 for a lighting bake.
- Gameplay anim-event IDs (root copy allowed in the lab, user 2026-10-31): next ideas -
  C4 plant timing (needs a real held attack, not VConsole), WPN_DROP_MAG visibility,
  multi-ADD_AMMO shell reloads on rifles, draw-reload, grenade/knife clips.
- Answered by PC1 (2026-10-31, 0.18.2): the Annabelle reload works with a
  `--cs2-root-copy` (local play only). Still open on PC2: can the gameplay side use an
  agent's hudmodel graph (agent set before spawn / via loadout)? Why did the user's
  2/3 reload loop?

## Backlog (test ideas; user: "just test it all", 2026-10-31)

Goal (user, 2026-10-31): learn as much as possible about what custom guns can
do in CS2. Third person is for later (user, 2026-10-31).

- Third person: worldmodel.vnmgraph routing for custom clips/anim classes/skeletons.
- New skeleton bones animating in game (needs the Annabelle model from PC1).
- Zoom on non-scoped guns: confirm class-gating (AK/P90 did not zoom); iron sights
  depend on it.
- Left-handed mode: find how to switch it (cl_prefer_lefthanded did nothing).
- Dupe kill-feed icon (needs a bot that can die), buy menu, bots using dupes.
- Material attribute events (none sampled yet), tracers (m_szTracerParticle).
- cs_script: charge-up shot, custom falloff, alt-fire behaviour.
- Grenade vdata beyond throw velocity (fuse/bounce/flash duration keys).

## Done

- 2026-10-31: ADS + heat rebuilt for the user's test through the agent hookup only
  (AK reset to stock, new ADS shot clip with recoil + muzzle flash), script-only map
  `wl_script` loaded on lit de_dust2 by `cfg/wl_dust2.cfg`.
- 2026-10-31: AK aim-down-sights (AUG class + AK pose clip), overheated barrel via
  material attribute events (no script), subclass anim-class switching (gameplay
  follows). User: no cs_script gameplay tests, no dual wield.

- 2026-10-31 cook (all in Findings): Workshop-legit hookup via custom agents +
  map-script precache; weighted selectors = rare draws/inspects and a random
  reload; muzzle flash/shell/sound clip events; custom .vsnd + addon soundevents;
  silenced AK; dupe HUD/gunshot from parent; multi-shot taser; cs_script explosive
  rounds, grenade launcher, lifesteal; Weapon Lab 0.18 tests 106/107 on PC2.
- 2026-10-31: SSG08 shell reload investigated and verified in game with stock
  assets (cause of the endless loop, why shotgun type forces the Nova, loop-event
  recipe incl. ADD_AMMO timing). Results under Findings in `.claude/pc2/MEMORY.md`.
- 2026-10-29: AK separate empty reload, third reload by rewiring, random-reload
  limits (in game).
