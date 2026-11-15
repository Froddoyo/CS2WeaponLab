"""Animation files in and out of a Weapon Lab .blend (0.25), shared by the add-on and Weapon Lab's background jobs.

Four formats, one clip at a time:
- keys (.blend): the clip's Action with both rigs, written with bpy.data.libraries.write. Exact keys.
- baked poses (.json.gz): every bone's pose on every frame (pose_io.sample), with bone names, so Weapon Lab
  and other weapons can map it by name.
- compiled (.vnmclip_c): Weapon Lab's export (the add-on's Save Clip As).
- FBX (.fbx): both rigs with the clip baked, through Blender's FBX exporter; imported back by matching bone names
  and baking (visual keying) onto the rigs.

This module must not use relative imports: the background job loads it as a plain module.
"""
from pathlib import Path

import bpy
from mathutils import Matrix
try:
    from . import pose_io  # As part of the add-on.
except ImportError:
    import pose_io  # As a plain module (animation_job.py).

SLOT_KEYS = ("weaponlab_arms_slot", "weaponlab_weapon_slot")


def rig_pairs(arms, weapon):
    """(role, rig, slot key) for the rigs this .blend has: the primary rig (Arms, or Body for a third-person clip) and
    its weapon rig. The role is the rig's own weaponlab_role (arms, weapon, body, weapon_world)."""
    return [(rig.get("weaponlab_role"), rig, key) for key, rig in zip(SLOT_KEYS, (arms, weapon)) if rig is not None]


# ---- Keys (.blend) ----

def export_keys(path, action, arms, weapon):
    """Writes the clip's Action and both rigs (their rests let an import tell which keys still fit) to a .blend."""
    blocks = {action, arms} | ({weapon} if weapon else set())
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    bpy.data.libraries.write(str(path), blocks, fake_user=True, compress=True)


def import_keys(path, action, arms, weapon):
    """Copies a keys file's curves onto this clip's Action, bone by bone: every bone this rig has with the same
    parent and rest gets the file's keys (exact: handles, sparse keys); others keep theirs. A file without rigs
    (an Action alone) is taken as the same rig. Returns {"copied": [...], "skipped": [...]}."""
    before_actions, before_objects = set(bpy.data.actions), set(bpy.data.objects)
    with bpy.data.libraries.load(str(path), link=False) as (source, loaded):
        loaded.actions = list(source.actions)
        loaded.objects = list(source.objects)
    actions = [item for item in set(bpy.data.actions) - before_actions]
    objects = [item for item in set(bpy.data.objects) - before_objects]
    try:
        source_action = next((item for item in actions if item.get("weaponlab_clip")), actions[0] if actions else None)
        if source_action is None:
            raise RuntimeError("The file has no Action.")
        source_rigs = {item.get("weaponlab_role"): item for item in objects if item.type == "ARMATURE"}
        copied, skipped = set(), set()
        for role, rig, key in rig_pairs(arms, weapon):
            new_bag, old_bag = pose_io.bag_of(action, key), pose_io.bag_of(source_action, key)
            if new_bag is None or old_bag is None:
                continue
            old_rig = source_rigs.get(role)
            if source_rigs and old_rig is None:
                continue  # The file's rigs are another view's (third person onto first, or back): nothing fits.
            by_bone = {}
            for curve in old_bag.fcurves:
                if curve.data_path.startswith('pose.bones["'):
                    by_bone.setdefault(curve.data_path.split('"')[1], []).append(curve)
            for bone_name, curves in by_bone.items():
                bone = rig.data.bones.get(bone_name)
                old_bone = old_rig.data.bones.get(bone_name) if old_rig else None
                if bone is None or (old_rig is not None and (old_bone is None or not pose_io.same_rest(bone, old_bone))):
                    skipped.add(bone_name)
                    continue
                pose_io.clear_bones(new_bag, [bone_name])
                for curve in curves:
                    pose_io.copy_curve(new_bag, curve, bone_name)
                copied.add(bone_name)
        return {"copied": sorted(copied), "skipped": sorted(skipped - copied)}
    finally:
        for item in actions:
            bpy.data.actions.remove(item)
        for item in objects:
            data = item.data
            bpy.data.objects.remove(item)
            if data is not None and data.users == 0 and isinstance(data, bpy.types.Armature):
                bpy.data.armatures.remove(data)


# ---- Baked poses (.json.gz) ----

def sample_action(scene, rig_doc, arms, weapon, action, frames):
    """The clip's poses as a pose document (pose_io.sample on both rigs), whatever is assigned now is restored."""
    previous = [(rig, rig.animation_data.action if rig.animation_data else None, rig.animation_data.action_slot if rig.animation_data else None)
                for rig in (arms, weapon) if rig]
    primary_skeleton, weapon_skeleton, primary = pose_io.skeletons_of(rig_doc, pose_io.is_world(action))
    try:
        pose_io.assign(arms, action, pose_io.slot_for(action, "weaponlab_arms_slot"))
        document = {"arms": pose_io.sample(scene, arms, primary_skeleton, primary, frames)}
        if weapon and "weaponlab_weapon_slot" in action and weapon_skeleton:
            pose_io.assign(weapon, action, pose_io.slot_for(action, "weaponlab_weapon_slot"))
            document["weapon"] = pose_io.sample(scene, weapon, weapon_skeleton, False, frames)
        return document
    finally:
        for rig, old_action, old_slot in previous:
            if old_action is not None:
                pose_io.assign(rig, old_action, old_slot)


# ---- FBX ----

def export_fbx(path, scene, arms, weapon, action, frames):
    """Both rigs with this clip, baked to an FBX (Blender's exporter; bones only, no meshes)."""
    for rig, key in ((arms, "weaponlab_arms_slot"), (weapon, "weaponlab_weapon_slot")):
        if rig is not None and key in action:
            pose_io.assign(rig, action, pose_io.slot_for(action, key))
    for item in bpy.context.view_layer.objects:
        item.select_set(item in (arms, weapon))
    bpy.context.view_layer.objects.active = arms
    scene.frame_start, scene.frame_end = 0, frames - 1
    Path(path).parent.mkdir(parents=True, exist_ok=True)
    with frame_rate(scene):  # One FBX frame per clip frame, whatever speed the scene plays the clip at.
        bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True, object_types={"ARMATURE"}, add_leaf_bones=False,
                                 bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                                 bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)


class frame_rate:
    """30 fps while exporting or importing an FBX, so its frames are the clip's frames (FBX stores time, not frames);
    the scene's own speed (Weapon Lab plays each clip at its real rate) comes back afterwards."""

    def __init__(self, scene):
        self.scene = scene

    def __enter__(self):
        self.saved = (self.scene.render.fps, self.scene.render.fps_base)
        self.scene.render.fps, self.scene.render.fps_base = 30, 1.0
        return self

    def __exit__(self, *_):
        self.scene.render.fps, self.scene.render.fps_base = self.saved


def import_fbx(path, scene, arms, weapon, action, frames):
    """An FBX animation onto this clip, one key per frame. Bones are matched by name and take the FBX bone's pose
    in armature space as it is (the FBX armature's object transform mapped onto this rig's), so the FBX must use
    Blender's bone axes, as Weapon Lab's own FBX export does. The FBX's stored rest is not used: Blender's exporter
    writes a bind pose that is not the rig's rest (checked 2026-11-14). Bones the FBX lacks keep their keys.
    Returns {"copied": [...], "skipped": [...]} (skipped = FBX bones this weapon's rigs lack)."""
    if bpy.context.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    before_objects, before_actions = set(bpy.data.objects), set(bpy.data.actions)
    # Leaf bones are real bones here (fingertips): Weapon Lab's export adds no extra leaf bones.
    with frame_rate(scene):
        # anim_offset 0: Blender's FBX importer otherwise starts the animation one frame late.
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True, anim_offset=0.0, automatic_bone_orientation=False, ignore_leaf_bones=False)
    imported = [item for item in set(bpy.data.objects) - before_objects]
    try:
        sources = [item for item in imported if item.type == "ARMATURE"]
        if not sources:
            raise RuntimeError("The FBX has no armature.")
        copied, props, matched_any = set(), set(), False
        for role, rig, key in rig_pairs(arms, weapon):
            if key not in action:
                continue
            # This rig's bones in the FBX: prefer the FBX armature that has most of them.
            source = max(sources, key=lambda item: sum(1 for bone in rig.data.bones if bone.name in item.data.bones))
            # Bones a constraint drives (the props anchor, the glued wpnHand bones) keep the rig's own pose.
            # Props (under the props anchor) are skipped: Blender's FBX export puts them in the wrong place (2026-11-14).
            names = [bone.name for bone in rig.data.bones if bone.name in source.data.bones and not rig.pose.bones[bone.name].constraints
                     and not (bone.parent and bone.parent.name == pose_io.PROP_ANCHOR)]
            props.update(bone.name for bone in rig.data.bones if bone.parent and bone.parent.name == pose_io.PROP_ANCHOR and bone.name in source.data.bones)
            if not names:
                continue
            matched_any = True
            pose_io.assign(rig, action, pose_io.slot_for(action, key))
            matched = set(names)
            order = hierarchy(rig)
            bases = {name: [] for name in names}
            previous = scene.frame_current
            try:
                for frame in range(frames):
                    scene.frame_set(frame)
                    depsgraph = bpy.context.evaluated_depsgraph_get()
                    rig_now, source_now = rig.evaluated_get(depsgraph), source.evaluated_get(depsgraph)
                    # Per frame: the Weapon rig rides on wpn, and the FBX weapon armature carries that as object motion.
                    to_rig = rig_now.matrix_world.inverted() @ source_now.matrix_world
                    posed = {}
                    for bone in order:
                        posed[bone.name] = (to_rig @ source_now.pose.bones[bone.name].matrix
                                            if bone.name in matched else rig_now.pose.bones[bone.name].matrix.copy())
                    for name in names:
                        bone = rig.data.bones[name]
                        parent = bone.parent
                        bases[name].append(bone.convert_local_to_pose(
                            posed[name], bone.matrix_local,
                            parent_matrix=posed[parent.name] if parent else Matrix.Identity(4),
                            parent_matrix_local=parent.matrix_local if parent else Matrix.Identity(4),
                            invert=True))
            finally:
                scene.frame_set(previous)
            bag = pose_io.bag_of(action, key)
            pose_io.clear_bones(bag, names)
            pose_io.key_channels(bag, bases)
            copied.update(names)
        if not matched_any:
            raise RuntimeError("None of the FBX's bones are named like this weapon's rigs.")
        rig_names = {bone.name for rig in (arms, weapon) if rig for bone in rig.data.bones}
        fbx_names = {bone.name for source in sources for bone in source.data.bones}
        skipped = sorted(name for name in fbx_names if name not in rig_names) + sorted("%s (a prop: use keys or poses)" % name for name in props)
        return {"copied": sorted(copied), "skipped": skipped}
    finally:
        for item in imported:
            data = item.data
            bpy.data.objects.remove(item)
            if data is not None and data.users == 0:
                if isinstance(data, bpy.types.Armature):
                    bpy.data.armatures.remove(data)
                elif isinstance(data, bpy.types.Mesh):
                    bpy.data.meshes.remove(data)
        for item in set(bpy.data.actions) - before_actions:
            bpy.data.actions.remove(item)


def hierarchy(rig):
    """The rig's bones, parents before children."""
    ordered = []

    def visit(bone):
        ordered.append(bone)
        for child in bone.children:
            visit(child)

    for bone in rig.data.bones:
        if bone.parent is None:
            visit(bone)
    return ordered


# ---- Formats by extension ----

FORMATS = {".blend": "keys", ".gz": "poses", ".json": "poses", ".vnmclip_c": "compiled", ".fbx": "fbx"}


def format_of(path):
    name = str(path).lower()
    if name.endswith(".json.gz"):
        return "poses"
    return FORMATS.get(Path(name).suffix, "")
