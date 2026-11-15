# CS2 Weapon Lab: agent instructions

Read this file, then work out which PC you are on (see "Two PCs") and read
that PC's memory and plan before doing project work. Skim the other PC's
pair too. Check the actual files before trusting any note.

## What this project is

A lightweight desktop app plus a thin Blender bridge add-on for CS2 weapon
modding. Connect to the installed game, import a weapon and all its
animations, and work in a per-weapon sandbox:

- weapons vdata editing
- animation editing in Blender, driven by the app
- vnmgraph routing/editing, applied automatically on export
- sound event selection and timing, written into the `.vnmclip_c` events

The app owns the project state and the export/delivery pipeline. Blender
owns rigs, keyframes, the viewport and pose sampling.

## Hard rules

- **Never write, move or delete anything in `F:\CS2AnimationLab`.** It is the
  previous project (CS2AL 2.0.1-preview.2), kept as a read-only reference.
  Port its ideas and code deliberately; do not copy it in wholesale.
- **Never modify stock game files** (`game/csgo`, `game/core`, any `pak*.vpk`).
  Addon folders under `csgo_addons` are written only by the tool's delivery
  step, only to the addon the user chose, always with backups and rollback.
- **This repository is public: never commit danger files** (user,
  2026-11-15). That means Valve's files and anything extracted, decoded or
  compiled from them (clips, skeletons, graphs, models, materials, textures,
  sounds, particles, maps, vdata, `.vpk`), generated `.blend` files and pose
  files, populated workspaces, other people's assets, and the user's email
  (commits use `Froddoyo@users.noreply.github.com`). `.gitignore` and
  `build/hooks/pre-commit` block them: never force-add past them or use
  `--no-verify`. Each clone turns the hook on once with
  `git config core.hooksPath build/hooks`. Never stage anything under a
  workspace. PC1's app keeps its workspace at `F:\CS2WeaponLab\workspace`
  (pinned in Settings), outside this repo.
- **All agent files live in `.claude/`.** Durable: `CLAUDE.md` (shared) and
  one memory + plan per PC (see "Two PCs"). Disposable: `.claude/scratch/`
  (gitignored, local to each PC) for probe scripts, logs, test runs and
  temporary exports. Do not create agent notes, reports or `AGENTS.md`
  anywhere else in the repo. Clean scratch when a task ends.
- No renders, videos or image sequences unless a check needs them; delete
  raw frames after inspecting.
- Commit or push only when the user asks. Stage specific files, never the
  whole tree blindly. Report whether a push actually succeeded.

## Two PCs

The repo is cloned on two machines. Tell them apart by the repo path:

- **PC1, the main PC** (RTX 3090): repo `F:\CS2WeaponLab-Public` (since
  2026-11-15). Development. Owns `.claude/MEMORY.md` and `.claude/PLAN.md`.
  `F:\CS2WeaponLab` is the old private clone (history with game files,
  pushes to the private archive): never work or commit there.
- **PC2, the spare test PC** (GTX 1080): repo `C:\CS2WeaponLab`, to be
  cloned again from the public repo (its old clone points at the private
  archive). In-game tests and the VDATA LAB. Owns `.claude/pc2/MEMORY.md`
  and `.claude/pc2/PLAN.md`.

If the repo is anywhere else, ask the user which PC this is.

- Edit only your own PC's memory and plan. The other PC's pair is
  read-only: read it for context, never edit it, not even a typo. That way
  a `git pull` never conflicts on these files.
- Anything the other PC should act on goes in your own memory under "For
  PC1" or "For PC2". The other PC copies what it uses into its own files.
- The other PC's notes are only as fresh as your last pull.
- `CLAUDE.md` is shared: change it only when the user asks.

## Environment (PC1, main)

PC2's environment is in `.claude/pc2/MEMORY.md`.

- Windows x64. Repo: `F:\CS2WeaponLab-Public`, remote
  `https://github.com/Froddoyo/CS2WeaponLab.git` (public), branch `main`.
  The old history (with game files) is the private archive
  `Froddoyo/CS2WeaponLab-Private`; nothing goes there any more.
- CS2: `F:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive`
  (`game\csgo\pak01_dir.vpk`, `game\core\pak01_dir.vpk`).
- Addons: `game\csgo_addons\<name>`; matching sources in
  `content\csgo_addons\<name>`. `weapon_template` already ships an addon
  `scripts\weapons.vdata_c` override.
- Blender 5.2.3 LTS appears to live under `F:\SteamLibrary\steamapps\common\Blender`
  (seen in a CS2AL log). Verify before relying on it.
- CS2AL targeted .NET 10. Check `dotnet --list-sdks` before assuming an SDK.
- CS2 Workshop Tools are installed; `game\bin\win64\resourcecompiler.exe`
  exists (verified 2026-10-23, not yet run).

## Engineering standards

- Readable code: small modules, clear names, typed C#. Avoid the dense
  one-liner style found in CS2AL.
- Template-preserving writes: patch Valve's compiled resources, decode the
  result, and verify by fingerprint that everything meant to stay unchanged
  did. Write to a new file, then replace atomically. Never overwrite originals.
- Keep project state in plain, inspectable files (JSON) in the sandbox,
  not as JSON strings inside Blender scene properties.
- Say what is verified and what is untested. Nothing "works in game" until
  the user has tested it in CS2.
- Port with golden tests: an untouched export must stay byte-identical, and
  edited exports should match CS2AL's output for the same inputs.

## Memory upkeep

Update your own PC's memory when work settles a decision, verifies a
finding, learns an important path or sets a next step. Keep entries dated
(America/Edmonton), short and factual, and mark verified vs. unverified.
Replace superseded entries instead of piling up history. No credentials or
raw logs.
