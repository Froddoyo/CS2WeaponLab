"""How a Weapon Lab .blend's rigs look and follow (0.21).

* Arm bones are coloured so finger sections tell apart: one muted hue per
  finger, darker at the base and lighter at the tip (user feedback 2026-11-01:
  the first trial was too vivid; Blender has no per-bone transparency in solid
  shading, so the colours are muted instead).
* Preview meshes follow bone scale as well as location and rotation, so
  scaling a bone scales its part of the mesh (user feedback 2026-11-01).

Used when a .blend is built and when one is opened, so older files catch up
without a rebuild. Only colours and constraints change; bones and keys do not.
"""

SCALE_CONSTRAINT = "Weapon Lab - follow scale"

# One hue per finger (the vivid trial's), muted before use.
FINGER_HUES = {
    "thumb": (0.92, 0.22, 0.22),   # red
    "index": (1.00, 0.55, 0.10),   # orange
    "middle": (0.98, 0.86, 0.12),  # yellow
    "ring": (0.25, 0.82, 0.30),    # green
    "pinky": (0.28, 0.52, 1.00),   # blue
}
# Segment -> (brightness, mix toward white): meta darkest, tip lightest; a narrower ramp than the trial.
SEGMENT_SHADES = {"meta": (0.72, 0.0), "0": (0.86, 0.0), "1": (1.0, 0.0), "2": (1.0, 0.25)}

HAND = (0.70, 0.70, 0.70)
UPPER_ARM = (0.15, 0.78, 0.80)   # teal, muted before use
LOWER_ARM = (0.66, 0.38, 0.98)   # purple, muted before use
MOUNT = (1.00, 0.25, 0.75)       # magenta, muted before use: wpn, wpnHand, wpnEnd, wpnTip
HELPER = (0.40, 0.41, 0.44)      # grey: straighten/twist helpers, attach points, root motion
PROP = (1.00, 0.62, 0.15)        # amber, muted before use: a prop kept apart from the gun (0.22.3, 0.22.4)

SATURATION = 0.5   # Half the trial's colour.
BRIGHTNESS = 0.85  # A little darker.


def mix(color, other, amount):
    return tuple(a + (b - a) * amount for a, b in zip(color, other))


def muted(color):
    luminance = 0.2126 * color[0] + 0.7152 * color[1] + 0.0722 * color[2]
    return tuple(c * BRIGHTNESS for c in mix(color, (luminance,) * 3, 1.0 - SATURATION))


def arm_color(name):
    """The colour of a CS2 arms-rig bone (the same on both sides)."""
    base = name[:-2] if name.endswith(("_L", "_R")) else name
    if base.startswith("finger_"):
        parts = base.split("_")  # finger, <finger>, <segment>
        if len(parts) == 3 and parts[1] in FINGER_HUES and parts[2] in SEGMENT_SHADES:
            brightness, whiten = SEGMENT_SHADES[parts[2]]
            return mix(tuple(c * brightness for c in muted(FINGER_HUES[parts[1]])), (1.0, 1.0, 1.0), whiten)
    if base == "hand":
        return HAND
    if base == "armUpperShoulder":
        return muted(UPPER_ARM)  # The real upper arm (a model's arm_upper maps to it).
    if base == "arm_lower":
        return muted(LOWER_ARM)
    if base.startswith("wpn"):
        return muted(MOUNT)
    return HELPER


def _same(a, b):
    # Blender keeps bone colours as 8-bit values, so a colour reads back up to 1/255 off.
    return all(abs(x - y) < 0.01 for x, y in zip(a, b))


def apply_arm_colors(arms):
    """Gives the arms rig's bones their colours. Returns how many bones changed (0 when already set)."""
    if hasattr(arms.data, "show_bone_colors") and not arms.data.show_bone_colors:
        arms.data.show_bone_colors = True
    changed = 0
    for bone in arms.data.bones:
        # 0.22.3 kept props in the Arms rig; 0.22.4 keeps them in the Weapon rig (apply_prop_colors).
        color = muted(PROP) if bone.get("weaponlab_prop") else arm_color(bone.name)
        changed += set_color(bone, color)
    return changed


def apply_prop_colors(weapon):
    """Gives the Weapon rig's props (bones flagged weaponlab_prop) their amber. Returns how many changed."""
    return sum(set_color(bone, muted(PROP)) for bone in weapon.data.bones if bone.get("weaponlab_prop"))


def set_color(bone, color):
    """Returns 1 when the bone's colour changed, else 0."""
    custom = bone.color.custom
    if bone.color.palette == "CUSTOM" and _same(custom.normal, color):
        return 0
    bone.color.palette = "CUSTOM"
    custom.normal = color
    custom.select = mix(color, (1.0, 1.0, 1.0), 0.45)
    custom.active = mix(color, (1.0, 1.0, 1.0), 0.75)
    custom.show_colored_constraints = False
    return 1


def scale_follow(bone, target, name):
    """Makes a preview bone take the rig bone's scale. Pose space: the rig bone's scale within its rig,
    applied inside the preview rig, so the preview object's metre-to-unit factor and the weapon mount
    stay out of it. Uniform, as CS2 stores one scale per bone."""
    constraint = bone.constraints.new("COPY_SCALE")
    constraint.name = SCALE_CONSTRAINT
    constraint.target = target
    constraint.subtarget = name
    constraint.target_space = "POSE"
    constraint.owner_space = "POSE"
    constraint.use_offset = False
    constraint.use_add = False
    constraint.use_make_uniform = True
    return constraint


def add_scale_follow(preview):
    """Older .blend files: preview bones that copy a rig bone's location and rotation also copy its scale.
    Returns how many were added (bones that already copy scale are left alone)."""
    added = 0
    for bone in preview.pose.bones:
        location = next((c for c in bone.constraints if c.type == "COPY_LOCATION"), None)
        rotation = next((c for c in bone.constraints if c.type == "COPY_ROTATION"), None)
        if location is None or rotation is None or any(c.type == "COPY_SCALE" for c in bone.constraints):
            continue
        scale_follow(bone, location.target, location.subtarget)
        added += 1
    return added
