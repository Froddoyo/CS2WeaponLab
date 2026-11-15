# CS2 Weapon Lab

**Make your own weapon animations for Counter-Strike 2.**

Animate any gun or knife in Blender, choose when its sounds play, and put it in
your CS2 Workshop addon. Weapon Lab takes care of the game files for you.

> **Early version (0.26.1).** Things still change between versions.
> Third-person animations are new and have not been tried in game yet.

## What you can do

- **Re-animate a weapon:** draw, reload, inspect, idle, shoot.
- **Change its sounds:** move when a sound plays, or pick another one.
- **Use your own gun model,** even one with extra bones.
- **Change what other players see** when you draw or reload (third person, new).
- **Bring in animations** from other mods or tools (FBX and more).
- **Tweak weapon stats** like damage, fire rate and ammo.

## What you need

- A 64-bit Windows PC
- **Counter-Strike 2** and the **CS2 Workshop Tools** (a free download in Steam)
- A **Workshop addon** to put your work in (make one in the Workshop Tools
  launcher)
- **Blender** 4.4 or newer (free, from blender.org or Steam)

## Install

1. Download the `.zip` from **Releases** (on the right of this page).
2. Unzip it anywhere, for example your Documents folder. Keep the files
   together.
3. Open **CS2WeaponLab.exe**. If Windows says "Windows protected your PC",
   click **More info**, then **Run anyway** (the app is not code-signed).

That's it: nothing to install into Blender. Weapon Lab finds Blender and loads
its panel when it opens your file.

## Your first animation

1. Click **Connect**. Weapon Lab finds CS2 through Steam.
2. Pick your addon in the top bar.
3. In the list on the left, open a weapon (for example **rifle → rifle_ak**)
   and click a clip, like **reload_ak**.
4. Click **Build .blend** (the defaults are fine), then **Open in Blender**.
5. In Blender, press **N** and open the **Weapon Lab** tab. Pick your clip in
   **Clip used**, click **Edit Arms** and pose away. Every change is keyed for
   you.
6. Click **Export to Weapon Lab**.
7. Back in Weapon Lab, the weapon is now **yellow**: changed, but not in your
   addon yet. Open the **Deliver** tab and click **Push to** your addon.
8. Start CS2 with your addon and try it. If CS2 is already open at the main
   menu, the new animation often shows up without a restart.

## Good to know

- **Your game is safe.** Weapon Lab never changes CS2's own files. It only
  writes into the addon you picked, backs it up first, and **Undo last push**
  (Deliver tab) puts it back.
- **Your work is saved** in the `workspace` folder next to the app (change it
  in **Settings…**). It holds your Blender files, so back it up.
- **Colours:** yellow means waiting to be pushed, green means it is in your
  addon.
- **Sounds** are set in the **Anim Events** tab, not in Blender.

## If you don't see it in game

- **The animation doesn't play.** CS2 only plays custom animations through
  your addon's own player model. In ModelDoc, its first-person graph
  (`hudmodel` in the AnimGraph2List) must be
  `animation/graphs/viewmodel/viewmodel_custom.vnmgraph`. For third person,
  the `""` and `worldmodel` entries need
  `animation/graphs/worldmodel/worldmodel_custom.vnmgraph`. **Checks…** warns
  when no player model is set up.
- **Nothing changed.** Is the weapon still yellow? Push it.
- **The reload ends before or after the animation.** Ammo still follows CS2's
  own reload timing. **Anim Events** shows that timing as coloured bands, so you
  can animate to it.

Weapon Lab is a fan-made tool, not affiliated with or endorsed by Valve.

---

## Full guide

Everything below goes into detail: every tab and option, how Weapon Lab works
with the game's files, and how to build it from source.

**Editing in Blender.** **Open in Blender** loads the Weapon Lab Bridge add-on for that session: pick
a clip, pose with bone presets (Right Arm + WPN swings the gun from the
shoulder, Right Elbow + WPN from the elbow), then **Export to Weapon Lab** (verified clip into
the sandbox's `exports` folder, shown live in the app's Exports tab) or
**Save Clip As** (a `.vnmclip_c` anywhere). Blender exports the animation
only: sounds, gameplay events and their timing live in the app's **Anim
Events** tab, which adds them to every export (since 0.20.1; Blender markers
are no longer read or made).

**Profiles and addons.** The top bar has two pickers. A **profile** is your
local workspace (`workspace\profiles\<name>\`): its own sandboxes (Blender
files and exports), per-weapon choices, graph edits and unsaved vdata edits,
in short the work that is **not pushed yet**. The **addon** is the live state
in the game: green in the outliner is what it already has, yellow is the
profile's pending work. **Push to <addon>** (Deliver tab) writes the pending
work into the picked addon and then it leaves the pending list. A profile is
not tied to an addon (it remembers the last one): two profiles can push into
the same addon, and one profile can push into several. New, rename and delete
are in the "…" menu next to the profile picker (delete moves the folder to
`workspace\backups\profiles`). The top bar shows how many weapons have work
not pushed yet. Profiles from 0.13–0.14 (one per addon) are moved over the
first time, with the old shared sandboxes going to the active profile.

**Weapons are animation slots.** The game picks a weapon's first-person
animations by its class (`m_szAnimClass`, else `_class`): the root viewmodel
graph switches on that `weapon_type`. Addons can change what a slot plays and
which weapons use it; a made-up `m_szAnimClass` reaches the graph as written
(verified in game), so an addon's own root graph can give it a branch (adding
branches is not in Weapon Lab yet).
An M249 given the AUG's class stays an M249 everywhere else but animates in
the AUG's slot. Knives are the exception (0.23): every knife block has class
`weapon_knife` and no anim class, and each knife plays its own animations
(the root graph has no per-knife `weapon_type`; knives run through
`viewmodel_knife.vnmgraph`, which has a variation per knife). How the game
picks a knife's variation is not verified. The Property Browser shows each weapon's slot and every weapon that
plays it, with its state in the addon and its Blender build choices.

**Build .blend** opens a window for the selected slot: animations from the
game files or from the addon's own clips, the weapon model shown in Blender
(Default is CS2's model for the slot; a skeleton built from a model shows
that model, stored as the weapon's pick when the skeleton is made;
or a compiled .vmdl_c picked with Browse, for example one you built in Blender
with its own bones. Weapon Lab reads compiled models only, so compile in
ModelDoc first: the window shows when the model was compiled and warns when
its .vmdl source was saved later), the skeleton the clips drive (CS2's for
the slot, the addon's own, the current one, **built from the custom model
above**, or a skeleton file), and the sleeve and glove models on the arms (or
none). Picking a custom model with bones CS2's skeleton lacks suggests
building the skeleton from it, with the shared and new bones listed, the bones
the model hangs under another parent than CS2, and Blender FBX leaf bones
(`*_end`, left out). **Use the model's own hierarchy** builds the skeleton the
model's way (parents and rest) for a full custom animset; CS2's clips keep their
motion relative to each bone's rest. Bones no mesh is weighted to (often from
animation files the .vmdl lists) and CS2 bones the model lacks are hidden in
Blender. Nothing reaches the addon until your next push. Choices are saved per slot in the
profile.

The Animations tab has a **viewport** above the clip lists: the weapon model
shaded with the skeleton its clips drive drawn on top (a custom skeleton's new
bones in amber). Left drag turns, right or middle drag pans, the wheel zooms,
**Frame** fits the weapon; clicking a joint selects that bone in the outliner
and the other way round. Selecting a clip loads it on the **timeline** below
the view: play, pause, step a frame and scrub, speed, sound (blue) and
gameplay (amber) event marks, **Sound** to hear the clip's sound events, and
**Your export** to switch between your Blender export and the original. The
model is the weapon's pick (Default: CS2's own since 0.21; weapons that showed
their custom skeleton's model by default keep it as their pick), also set
with **Viewport gun model…** in the skeleton panel; meshes are
exported once and cached in
`workspace\cache\viewport`. **Arms** shows the viewmodel arms wearing the
gloves picked in Build .blend (no sleeves), played by the clip's arm stream,
with the weapon on their `wpn` bone; each glove joint follows the bone of the
same name from its own bind pose, and the forearm twist bones share the
hand's roll, as in game.

The **Anim Events** tab is a timeline editor for one clip at a time (pick it
in the outliner; a weapon alone shows its clips with their event counts,
`_lgcy` clips behind **Legacy clips**). The viewport moves in above a
**timeline** with a frame ruler, the clip's **sync sections** (a shell
reload's INTRO, LOOP and OUTRO) and a lane per kind: gameplay markers
(reload stages, when ammo is added), sounds, particles, material curves and
legacy events; overlapping events stack in rows. Click selects (Ctrl adds),
drag moves (whole frames; Alt for fractions), drag a bar's end to change its
length, drag on empty space to box-select, click the ruler to scrub; ←/→
nudge (Shift ×10), Delete removes, Ctrl+D duplicates, Ctrl+Z / Ctrl+Y undo and
redo, Space plays, double-click a lane adds an event there. **+ Sound** and
**+ Gameplay** add at the playhead; **Copy from…** copies events (and the
loop sections) from any CS2 clip at the same point of the clip. The
**inspector** edits the selected event: a soundevent picker with ▶ to listen,
a gameplay ID picker with what each ID does, start and length in frames, and
the kind's own fields; legacy events stay read-only. Sound events play as
playback passes them (CS2's and the addon's soundevents, through Windows
audio; **Sound** turns it off; pitch is not applied).

The **shell-by-shell reload** panel turns a reload into a loop the game
repeats once per shell: pick the loop's start and end frames and the frame
the shell counts, and it writes the sync sections and their gameplay events,
moves WPN_RELOAD_ADD_AMMO (at least 4 frames before the loop ends), adds
WPN_RELOAD_SILENT to a normal reload and removes WPN_DROP_MAG; the section
edges on the Sync lane drag too. It checks the pose at the loop's end against
its start. The transport's **Clip / N shells** plays the reload as the game
would: the intro, the loop once per shell, the outro.

**Game timing (0.20, reworked in 0.24).** In game, a weapon's ammo and
reload timing come from CS2's own root graph, not from the clips you edit:
its weapon type (`m_WeaponType`) picks a group (pistols, rifles, shotguns, …)
and its anim class picks the gun in that group. Weapon Lab reads that choice
from CS2's graph: the **expected class**. Anim Events' **Game timing** picker
shows it ("From the vdata: Nova") and lets you pick another gun to see its
timing instead (display only: it changes no vdata, so the game still runs
what the vdata gives). The timeline then draws that gun's reload as tinted
**bands** behind the lanes (intro, the shell loop, outro, "game done: firing
cuts this"; for a magazine reload, before and after the refill): the parts
your clip cannot change. Time your animation, sounds and particles to them.
Plain-words warnings say what the player sees ("the ammo counter adds the
first shell 1.00 s before your animation shows it"), in Anim Events, in the
weapon's **Checks** and in Deliver. Costs it also states: an anim class the
group does not list follows whichever gun of the group the player held last,
and a weapon that zooms gets no scope view unless its type is the sniper
rifle type (both verified in game 2026-11-03). **Use the …'s timing** puts
the loop and the shell on that gun's frames and moves every other event,
sounds too, with its part. In Blender the Dope Sheet, Timeline and Graph
Editor show the same bands and marks (`blender/input/game_timing.json`,
written when Weapon Lab builds or opens the .blend, and when you pick another
gun). CLI: `events fit <weapon> <clip>` does the same as the button. (The
weapon panel's "Plays like" row is now **Game timing**.)

**First edit creates the sandbox (0.24).** A weapon without a sandbox opens
read-only in Anim Events; your first edit there creates it (the addon's own
clips when it has them) and then applies the edit. If the sandbox's copy of
the clip is not the one you edited (the weapon builds from CS2's clips while
the addon has its own), the edit is not applied and you are told to redo it.

Edits are kept in the sandbox (`events/<clip>.json`: changes, added and
removed events, the sync track, the clip's length), written into the clip's
export alongside your Blender pose edits, so a Blender re-export keeps them,
and pushed in the Deliver tab. **Reset clip to CS2's events** takes the events
back (the length stays).

**Clip length (0.20.1).** The clip info's **Length** sets a clip's frame
count, e.g. the Annabelle's 112-frame SSG08 reload cut to the Nova's 50, or an
inspect lengthened to fit longer animation. Shorter cuts the end, longer holds
the last pose; the frame rate stays, and events and loop sections keep their
frames (anything past a new end moves to it). Blender then shows and samples
that many frames (`blender/input/clip_lengths.json`), so export from Blender
again after changing it; every track is re-encoded at the new length and the
written clip is decoded again to check it. CLI: `events length <weapon> <clip>
<frames>`. Verified in game 2026-11-05: the Annabelle's reload cut to 50
frames on the Nova's timing plays in step with the ammo counter.

In Blender the weapon's hand targets (`wpnHand_L/R`) always follow the hands,
and export keeps them on the hands with CS2's own small offset, so moving the
gun no longer drags the arms in game.

Blender add-on 0.12 (Weapon Lab 0.21): arm bones are coloured so finger
sections tell apart (one muted hue per finger, darker at the base, lighter at
the tip; hands grey, the gun mount magenta), and the preview meshes follow
bone scale as well as location and rotation, so scaling a bone scales its part
of the mesh. Older .blend files get both when they open; only colours and
constraints change. A .blend saved outside its sandbox finds the sandbox it
was built from (stored in the file since 0.21, or told by Open in Blender),
and the timeline fits the clip's length when a file opens.

The Animations outliner has a **Graphs** node under each weapon: its own
graphs, shared ones and the root that routes to them, yellow while its change
is not in the addon, green when it is, and blue when the addon's version was
changed for another weapon (or is the addon's own); click one to open it in
the Graph tab. Right-click a weapon (outliner or Deliver tree) for **Set to
CS2 default…**: the next apply deletes every file the addon has for that
weapon, whoever made it (its custom clips, its own graph copies, and its
skeleton unless a model still uses it), and points the addon's remaining
graphs back at CS2's for it while keeping their other routing. The window
lists the files first; everything is backed up and **Undo last push**
restores it. The VData tab has **Reset block…** for vdata.

**Outliner right-click (0.21).** Every row has a menu: open it in Anim
Events, Animations, Graphs, VData or Deliver, open or show the weapon's
.blend, show its sandbox folder, copy a name, collapse all. A yellow row
adds **Revert to addon default…**: it throws away the work there that the
addon does not have yet, so the row matches the addon again. It follows the
open tab (clips on Animations and Anim Events, graph edits on Graph, values on
VData; clips and graphs on Deliver) and lists what it would undo first, each
with a tick. A clip goes back to exactly what the addon has: every time the
addon is found holding a clip's current export, Weapon Lab keeps a copy of the
sandbox work behind it (`pushed/<clip>/` in the sandbox: the clip, its Blender
sample, event edits and length). A clip the addon has no version of is set
aside instead, and one pushed before 0.21 changed since cannot be put back
(the window says so). Graph edits and vdata values the addon lacks are
dropped. Nothing in the addon or the .blend changes; moved sandbox files go
to `workspace\backups\revert`, and the next Export All from Blender brings
your keys back. On Anim Events, clips whose events are not pushed yet are
yellow too (they stayed white before).

A custom skeleton the addon does not have is one item of its own (0.22.5),
on the weapon's row and its Skeleton row: reverting drops it, so the weapon
goes back to the skeleton the addon gives it (CS2's default when it gives
none), and every clip only re-targeted to it goes back with it. Its files are
copied to the backup first; build the .blend again before exporting from
Blender. Before 0.22.5 those clips were listed greyed out with nothing to
tick. When nothing on a row can be reverted, a message says why instead of
an empty list.

**Set one animation to CS2 default (0.22).** Right-click a clip for **Set
to CS2 default…**: its export, Blender sample and event edits move to
`workspace\backups\stock-clips`, and the next push removes the addon's copy of
that clip and points its graphs back at CS2's clip (on a custom skeleton,
CS2's clip re-targeted to it goes in instead, so the gun keeps animating). The
weapon row's **Set to CS2 default…** still resets the whole weapon. A line at
the top of the outliner says what its rows are on each tab: animation slots
(CS2 picks a weapon's first-person animations by its anim class), their
clips, or the graphs a slot plays through. The tabs run Animations, Anim
Events, Graph, VData, Deliver.

The **skeleton panel** (Animations tab, above History) says which skeleton the
selected weapon's clips drive and whether the addon has it, and **Change
skeleton…** switches it: CS2 default for the slot, the addon's own, one built
from a model, or a skeleton file (`.vnmskel_c`, used in place inside the addon
or copied in on push). It is the same setting as in the Build .blend window.

The Animations outliner has a **Skeleton** node under each weapon: the bone
tree the clips drive, with a custom skeleton's new bones (●), bones without
mesh (○) and bones placed on the weapon body (⚠) marked; a bone shows its
rest pose and the exports that move it. The graph editor glows around edited
nodes and transitions, and faintly around anything holding edits further
down. The **Outliner** drives every tab; a weapon
is yellow when the profile has edits the addon lacks and green when the addon
matches the profile.

The **VData** tab edits the target addon's own `weapons.vdata` (offering to
create one from CS2's if the addon has none). Keys get friendly, grouped names;
hover for the raw key and a description. Values are written to the addon's
`content\...\scripts\weapons.vdata` source and compiled with the Workshop
Tools resourcecompiler. Since 0.22 the tab has no outliner: the list on the
left scrolls through every block (a vdata block is an item the game expects:
its buy-menu slot and the recoil pattern it inherits), **Jump to** goes to a
block, and the block at the top is the one in view. The middle shows it in
3D: the model its `m_szWorldModel` names (the addon's compiled copy, else
CS2's) on the skeleton of the animation slot its anim class plays, with that
slot's clips in **Clip**. The right side sums it up (animation slot, model,
buy-menu slot and price, recoil pattern seed and where it comes from, what is
changed from CS2, not saved yet, or set outside Weapon Lab) above saving, the
class change and a small History. The colours follow the addon's **source**
file: a value you save there yourself (in Visual Studio, say) is the addon's
own and green at once, even before compiling; the tab says when the source is
newer than the compiled file the game runs. A key Weapon Lab never wrote
starts from the addon's current value (before 0.22 it could start from an
older copy and look unsaved).

**Base on another weapon** (VData tab, "Weapon class") makes a block take
another weapon's parts: type and inheritance, animations, model, sounds,
handling, and optionally price, inventory slot or the entity `_class`. A CZ75
that works like the PP-Bizon is one click; single edits still apply on top.

The **Graph** tab is a graph editor laid out like Source 2 Viewer. It shows
one level of a weapon's animation graph at a time: blend trees as node cards
wired left to right, each wire running from a node's output into the named
input pin it feeds (Child, Condition [0], Mask…) and coloured by what it
carries (pose, true/false, number, name); state machines as states with
transition arrows. Double-click to go deeper, use the breadcrumb or search to
move around. The tab shows the **addon's own version** of a graph when it has
one (marked "● addon" in the graph list); edits build on it, and one whose
node layout differs from CS2's is view only. The inspector explains each node
and each transition in plain words ("Idle → Reload when action is
action_reload, blend 0.2 s") and edits tuning values: which clip a clip box
plays, speed, looping, blend times, comparison values, names and flags. The
wiring stays as Valve built it. Spots glow with a slow pulse: yellow for the
profile's edits not pushed yet, green where the addon's live copy differs from
CS2's (its own routing, earlier pushes), blue around a clip node that plays a
clip you edited in Blender; states and folders holding any of these glow
faintly. The graph list, breadcrumb and header name the file shown: the
addon's `*_custom` copy, or CS2's graph (edits are pushed into the addon as
the `_custom` copy; CS2's file is never replaced).

The Blender preview model is picked per weapon in the Build .blend window:
Default (CS2's model) or a model file. On the
Animations tab, an orange Blender mark beside a weapon in the outliner means
its sandbox has a .blend; **Delete .blend…** removes it after asking (exports stay).

**VData stands alone.** Only the VData tab reads, colours or writes vdata.
Pushing, the Deliver colours and warnings, the skeleton panel and
Build .blend never look at it (the slot line, which shows the class the game
picks animations by, is the one read-only exception). Each VData row has a ↺
button that sets the value back to CS2's (yellow until saved), beside
**Reset block…** for a whole block.

**What the game plays is what you see (0.22.1).** When the addon plays its
own version of a clip (green: made by any profile, CS2 Animation Lab or by
hand), the viewports on Animations, Anim Events and VData play that version
("· the addon's" on the timeline), and the clip's details show it; **Your
export** still shows your newer, unpushed work. A sandbox made on a first
edit takes the addon's own clips for the weapon, so an edit lands on the
addon's animation instead of replacing it with CS2's; Anim Events warns when
an older sandbox was made from CS2's clips while the addon has its own (take
them again with Build .blend → From the addon).

**The addon is the live state.** Weapon Lab scans the picked addon for
clips, graphs, skeletons and weapon models it already has (pushed by any
profile, made with CS2 Animation Lab, or by hand); those weapons and clips
show green. Each tool tab colours the outliner by its own part: clips on
Animations, vdata on VData, graphs on Graph, clips and graphs on Deliver. A
clip this profile pushed that the addon now has in another version (another
profile's push, a hand edit) shows blue; it is pushed again only after a new
export. The VData "Addon (edit)" column starts from the addon's own values;
an edited value is yellow until the addon has it. Text and true/false values
use selector boxes (with typing allowed), filled from the values the game and
addon already use; the graph inspector does the same. Pushes build on the
addon's current graphs instead of CS2's, so whatever they already route
survives; the first push to an addon also keeps a one-time snapshot of its
animation and scripts folders (`workspace\addons\<addon>\first-snapshot`).

**Hooking up custom graphs.** Workshop addons cannot replace CS2's own
files, so delivered graphs only play for player models that ship with the
addon and name `animation/graphs/viewmodel/viewmodel_custom.vnmgraph` as
their `hudmodel` graph. Weapon Lab finds the addon's player models, lists
the ones hooked up, and warns when none are. The VData tab warns when a
weapon's `m_szWorldModel` is empty or missing.

That hookup only reaches what the player sees. CS2's gameplay side (when a
reload adds ammo, the stages of a shell-by-shell reload) runs CS2's own root
graph, so a custom reload whose events drive gameplay misbehaves: a
shell-by-shell reload loops forever. For local play, CLI `deliver <addon>
--cs2-root-copy true` also writes a copy of `viewmodel_custom` over CS2's
root graph path (`animation/graphs/viewmodel/viewmodel.vnmgraph`); every
later push keeps it in sync, and `--cs2-root-copy false` removes it. Workshop
players do not get the copy. Verified in game 2026-10-31 (Annabelle on the
SSG08 slot: partial, empty and interrupted shell reloads end correctly).

**Custom skeletons.** A weapon can get its own animation skeleton built from
a model's bones (Build .blend window; CLI `skeleton create`),
so parts the weapon's own skeleton lacks, such as an AR2's energy cells, can
be animated. The new skeleton keeps every bone of the weapon's own skeleton, so all clip
motion stays, and adds the model's extra bones where the model has them. Every clip of the weapon moves onto the new
skeleton in the sandbox (exported edits too), the .blend gets the new bones
in their own bone collection, and delivery ships the skeleton and all of the
weapon's clips at custom paths. Since 0.23 it ships no model copy: point
your own model at the skeleton in ModelDoc (NmSkeletonReference, Source File
`animation/skeletons/weapons/weaponlab_<weapon>.vnmskel`, no `_c`; right-click
the outliner's Skeleton row, **Copy asset path**) and compile it after the
push has put the skeleton in the addon. The slot's `m_szWorldModel` names
that model; `m_szAnimSkeleton` can stay CS2's (verified in game). The next
push removes a copy an earlier version shipped to
`models/weapons/weaponlab/<weapon>/` when no vdata names it (backed up).
Verified in game with an M4A4 carrying 32 extra bones.

**The model must match the skeleton (0.23).** CS2 applies each bone's track
relative to the bone's parent in the *compiled model*, and a model's root
bone other than `weapon` takes its track from the camera (verified in game on
PC2, 2026-11-07). So a model must use the custom skeleton and give every
shared bone the same parent. Weapon Lab checks the model the weapon shows
(its vdata `m_szWorldModel`, else the skeleton's source model) and warns on
the outliner's Skeleton row (tooltip), the bone details, the Build .blend
window (with a copy button for the skeleton path) and the Deliver tree. Fix
it in the model, never by editing it from Weapon Lab: parent the bone like
the skeleton (Blender/FBX and ModelDoc's Skeleton node) and compile. ModelDoc
drops bones no mesh is weighted to unless they are set to do_not_discard,
which also changes the compiled hierarchy.

**Props kept apart from the gun (0.22.2 to 0.23).** A bone ticked **Keep
apart from the gun** (bone details in the outliner, or the Build .blend
window; off by default, saved in the profile, works on CS2's own skeletons
too, e.g. a magazine) is a prop that holds still while the gun moves (a
spare magazine held in the other hand). Build the .blend again after ticking. Parent the
prop under a gun bone in the model, as the skeleton has it (a model root is
read from the camera in game): the gun's motion comes from the arms' `wpn`
bone, which carries the whole model, so a prop stays apart through its
animation, not its parenting. In the .blend the prop is a bone of the
**Weapon** rig (amber, in "Props - edit these"; Edit Weapon selects it)
whose Blender parent is a hidden bone, "Props anchor - stays with the arms",
that copies the Arms rig object and cancels the gun's motion. The prop holds
still in every clip, where it sits on the first frame of the default clip
(idle), keyed on every frame with the same values (Auto Key only replaces
keys, so keep them). Animate it yourself, or give it a Child Of constraint to
`hand_L` to carry it in the left hand (exports bake constraints). Export
writes the prop's place relative to its parent on every frame, so the game
shows what Blender shows; every exported clip writes the prop's track (an
untouched clip exports byte for byte except that track). Clips you do not
export keep CS2's motion (the prop rides with the gun). Reset Clip to Stock
puts the prop back to its hold. Verified in game on PC1 (a custom knife's prop, 0.22.4
export after parenting the prop under `weapon`) and PC2.

To see a custom knife in first person offline, spawn it from the console
while looking at the ground: `ent_create weapon_knife_ursus` (the knife then
uses the vdata model; a knife given by `give` shows the default knife's).

The Deliver tab opens in **Tree** view (List is optional): the review grouped
by weapon slot (animations, graphs, skeleton, warnings) with shared files
below, and the root graph opening to the weapons it re-routes. Each weapon
and clip has a **commit** checkbox; an unticked item is left out of this push
(the addon keeps whatever it already has), and turns red with an explanation
when leaving it out breaks something in game. Since 0.23 a weapon left out
with nothing in the push is hidden; **Show left out (n)** lists them so you
can tick one back in. Right-click a file or warning row for **Copy asset
path**.

The **Deliver** tab pushes the profile's pending work into the addon, CS2
Animation Lab style: edited clips go to `animation/anims/custom/<weapon>/`,
the graphs that play them are updated (or cloned from CS2's) as
`*_custom.vnmgraph` up to `viewmodel_custom.vnmgraph`. Pushing never writes
vdata (the VData tab saves it on its own) and removes nothing but unused model copies of earlier versions (0.23); otherwise only
**Set to CS2 default** does. Pushed graph edits leave the profile; the Graph
tab then shows them as part of the addon's graph. **Review** lists every file
first and warns before a push that replaces a clip another profile pushed,
pushes clips on a different skeleton from the weapon's other clips in the
addon, or leaves addon files pointing at files it removes; every touched file
is backed up to `workspace\deliveries` and **Undo last push** restores them.
An error or warning that names a graph node, a clip or a weapon has a
**Show** button (0.21): it opens the graph at that node, the clip in Anim
Events, the weapon's vdata, or selects the weapon.
For the game to use the custom
graphs, the player model's `hudmodel` animation graph (`m_animGraph2Refs`)
must point at `animation/graphs/viewmodel/viewmodel_custom.vnmgraph`; that
step is still manual. Clip and graph delivery are verified in game (Workshop
Tools, with a test-only hookup).

**Settings (0.24).** The **Settings…** button (top bar) picks the workspace
folder, where every profile lives, and a folder for new `.blend` files
(`<folder>\<profile>\<weapon>.blend`). They are saved in
`%APPDATA%\CS2WeaponLab\settings.json`, shared by every version, so a new
version finds your profiles wherever you unpack it. Changing the workspace
moves nothing (copy the old folder first if you want it along) and takes
effect on the next start. The default for a new install is `workspace` beside
the app; the first 0.24 start keeps using the workspace an earlier version
used (the repo's `workspace\` or `%LOCALAPPDATA%\CS2WeaponLab\workspace`) and
saves it. `WEAPONLAB_WORKSPACE` still wins over all of it. **Move .blend…**
(Animations tab or right-click a weapon) moves a weapon's `.blend`; Build,
Open and the add-on follow it. Each profile keeps its sandboxes in
`<workspace>\profiles\<profile>\sandboxes\<weapon>`, each with a readable
`sandbox.json` and a `templates/` folder; push backups are shared in
`<workspace>\deliveries`.

**Checks (0.24).** **Checks…** (Animations tab, or right-click a weapon) lists
everything known to be wrong with a weapon in plain words, each with Show or
Fix: the model against its skeleton, the player-model hookup, the vdata model,
the game's reload timing, and a `.blend` that needs updating or rebuilding
(after a skeleton change). The outliner shows the count (⚠2 · 15).

**Variations (0.24).** **Variations…** (Animations tab, or right-click a
weapon) edits the weapon's weighted draw and inspect choices: each option's
clip and its whole-number weight. A rare draw or inspect is a clip in an empty
option with a small weight; the game rolls again on every draw or inspect
(verified on PC2). **Random reload** plays one of two reload clips, picked by
the reserve ammo, which changes every reload so it looks random (PC2's
recipe, verified with the AK; it reuses the graph's duplicate second draw
choice). All of it is saved as graph edits and delivered on your next push.
Weapon Lab-made variations are not tested in game yet.

**Set to CS2 default per row (0.24).** Besides weapons and clips, the
Skeleton row (back to CS2's skeleton: clips and exports move back, the extra
bones' motion is dropped, backed up first) and graph rows (the weapon's own
graphs are rebuilt from CS2's on the next push with this profile's clips kept;
shared graphs only lose this profile's pending edits) can be set back.

**Rebuilding keeps your animation (0.24).** Rebuild .blend asks **Keep my
animation** or **From the clips only**. Keeping carries every key from the
current `.blend` (backed up first) to bones that still exist with the same
parent and rest, handles and unkeyed bones included; new bones start with the
clip's motion. Build .blend also makes the custom skeleton again when its
model was compiled again (bones added in ModelDoc). In Blender, **Change gun
model…** (Weapon Lab panel) picks a compiled `.vmdl_c` from CS2 or your addon:
with **Use its bones as the skeleton** Weapon Lab makes the skeleton from it,
moves your exports onto it, rebuilds the `.blend` keeping your keys and reopens
it (CLI: `blender set-model`). An `.fbx` only swaps the preview mesh in
Blender. Export refuses to replace a clip's edited export with an unedited one
(a popup offers **Replace the export anyway**; Export All skips those clips).

**Clip used / Exported as (0.25).** The Weapon Lab panel in Blender has two
pickers: **Clip used** is the clip you animate, **Exported as** is the clip it
exports into (itself by default). Pick another clip to overwrite it with this
animation, e.g. a legacy clip you do not need. The choice is saved on the
Action, so it stays in the `.blend`. Export All then skips the overwritten
clip's own Action. Clips must match (both additive or not, both moving the
weapon or not). The export records where it came from.

**Animation files (0.25).** One clip's animation in four formats, both ways:
- **Compiled** (`.vnmclip_c`): what the game plays; any CS2 clip or another
  mod's can be imported.
- **Baked poses** (`.json.gz`): every bone's pose on every frame, with bone
  names; Weapon Lab and other weapons map it by name.
- **Keys** (`.blend`): the Action with both rigs, exact (curves, handles).
- **FBX** (`.fbx`): both rigs with the clip baked, for other tools. Props kept
  apart from the gun do not survive Blender's FBX export, so an FBX import
  leaves them alone (use keys or poses for those).

In Blender the panel's **Animation files** box exports the current clip in any
of the four and **Import animation…** puts a file on the current clip. In Weapon
Lab, right-click a clip: **Export animation…** / **Import animation…** (keys and
FBX run Blender in the background on a copy of the saved `.blend`). Bones are
matched by name and parent; matched bones take the file's motion, the rest keep
theirs. A Weapon Lab import becomes the clip's export (its events stay), and
Blender then shows **Load Weapon Lab's newer version** for that clip (it asks
before replacing the clip's keys; a `.blend` from before 0.25 no longer shows
it for its own exports). CLI:
`animation export|import <weapon> <clip> <file>`. Checked headless on a copy of
a knife sandbox: compiled and pose imports match the source within the clip
encoding (0.06°), keys within 0.003 units, FBX the same for every bone but
props. Additive clips cannot be imported yet.

**Third person (0.26).** What other players see: each weapon's world clips
(`animation/anims/world/<category>/<weapon>/`, e.g. 12 for the AK) come into
the sandbox beside its first-person ones, under **Third person** in the
outliner. The `.blend` gets a **Body** rig (`worldmodel.vnmskel`, 75 bones)
with the agent's third-person mesh, and a second weapon rig riding the body's
`wpn` (the gun moves differently in third person). Picking a third-person clip
in **Clip used** shows the body and hides the arms; the bone presets, export,
Export All, Save Clip As and the four animation file formats work as in first
person. Editable: the full clips (draw, reload, crouched and silent ones).
Read-only: idle and shoot, which add onto other poses (how CS2 builds their
base is unknown), and their `+non_additive` twins. Delivery puts edited clips
in `animation/anims/custom/<weapon>/world/` and clones the worldmodel graphs
that play them up to `worldmodel_custom.vnmgraph`. Player models need that
graph in their AnimGraph2List `""` and `worldmodel` entries; Weapon Lab warns
when none has it. Third-person clips carry no sounds or ammo events (those play
from first person), keep CS2's weapon skeleton (a custom skeleton is first
person only) and are not shown in Weapon Lab's viewport.

Sandboxes made before 0.26 have no third-person clips: right-click **Third
person** → **Add third-person clips**, then **Build .blend** with **Keep my
animation** (CLI: `sandbox add-third-person <weapon>`, then
`blender build <weapon> --keep true`). Your first-person keys carry over.
Checked headless (2026-11-14): an untouched third-person export is
byte-identical to CS2's clip, edits change only the edited bones, keys carry
across rebuilds in both views, and the delivery plan routes the AK reload
through `worldmodel_gun_custom+ak47`. Nothing is tested in game yet, including
which player-model entry drives third person and whether others see it.

## Requirements

- Windows x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), to build from source only (release builds are self-contained)
- Counter-Strike 2 with the CS2 Workshop Tools
- Blender 4.4 or newer (5.2 LTS tested). Found automatically in Steam
  libraries and Program Files; set `WEAPONLAB_BLENDER` to override. Nothing has to be
  installed into Blender: **Open in Blender** loads the Weapon Lab panel for
  that session. Optionally, **Blender add-on…** (Animations tab) installs it
  into your Blender so it is there when you open a .blend directly (CLI:
  `blender addon install|remove|status`).

## Build and run

```bash
dotnet build CS2WeaponLab.slnx
```

```bash
dotnet run --project src/WeaponLab.App
```

Headless commands:

```bash
dotnet run --project src/WeaponLab.Cli -- discover
```

Other commands: `weapons`, `clips <weapon>`, `clip <weapon> <clip>` (timing
and events), `poses <weapon> <clip> [--frame <n>]` (decoded bone poses),
`verify-encoder [<weapon>]` (checks the clip writer against game clips),
`sandbox create <weapon> [--addon <name>]`, `sandbox list`,
`sandbox check <weapon>` (reports clips the game changed since import),
`blender locate`, `blender build <weapon>`, `blender update <weapon>`,
`blender open <weapon>`, `vdata list`, `vdata show <block> [--addon <name>]`,
`deliver <addon> [--profile <name>] [--dry-run true] [--confirm true] [--cs2-root-copy true|false]` (push the profile's pending work), `deliver rollback <addon>`, `profile list|new|use|rename|delete|target|set|reset`,
`skeleton show|create|remove <weapon> [--model <key>] [--model-hierarchy true]`,
`export-clip` (used by the add-on) and `dump-poses`. Add
`--game <path>` for an install Steam does not know, and `--workspace <path>`
for another workspace.

Tests:

```bash
dotnet test --project tests/WeaponLab.Core.Tests
```

Release build (self-contained, nothing to install):

```bash
powershell -File build/publish.ps1
```

It writes `publish\CS2WeaponLab-<version>-win-x64\` and a `.zip` of it:
`CS2WeaponLab.exe` (the app), `weaponlab.exe` (the command-line tool the
Blender add-on runs) and `blender\weaponlab_bridge\`. Keep them together. A
new install keeps its workspace in a `workspace` folder beside the app (since
0.24; **Settings…** picks another, saved in `%APPDATA%\CS2WeaponLab\settings.json`).
An install that already had work in an earlier workspace (the repository's
`workspace` folder, or `%LOCALAPPDATA%\CS2WeaponLab\workspace`) keeps using it.

## Layout

```
src/WeaponLab.Core/        Valve formats and the export pipeline
src/WeaponLab.App/         Avalonia desktop app
src/WeaponLab.Cli/         headless commands
blender/weaponlab_bridge/  Blender add-on + build_weapon.py (builds the .blend)
tests/                     unit and golden tests
build/publish.ps1          packs a release build into publish/
```

## Game data

Weapon Lab reads the game's VPKs and never modifies stock game files.
Exports are written only to the addon you choose, with backups. Extracted
game data stays in your workspace, which is gitignored and never committed.

## License

MIT. See [LICENSE](LICENSE). Uses
[ValveResourceFormat](https://github.com/ValveResourceFormat/ValveResourceFormat) (MIT)
and [Avalonia](https://avaloniaui.net/) (MIT). Not affiliated with Valve.
