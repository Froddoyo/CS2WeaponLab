"""Loads the Weapon Lab Bridge add-on for this Blender session only.

Weapon Lab opens a weapon like this, so nothing is installed into the
user's Blender profile:

    blender <weapon>.blend --python launch.py

The weaponlab command-line tool's path arrives in the WEAPONLAB_CLI
environment variable.
"""
import sys
from pathlib import Path

import bpy

package_parent = str(Path(__file__).resolve().parent.parent)
if package_parent not in sys.path:
    sys.path.insert(0, package_parent)

import weaponlab_bridge  # noqa: E402  (needs the path above)

if not hasattr(bpy.types.Scene, "weaponlab"):
    weaponlab_bridge.register()
for scene in bpy.data.scenes:
    weaponlab_bridge.sync_settings(scene)
weaponlab_bridge.upgrade_blend()  # The file opened before the add-on loaded, so load_post did not run for it.

def show_weapon_lab_tab():
    """Opens the sidebar on the Weapon Lab tab once the UI exists."""
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type != "VIEW_3D":
                continue
            area.spaces.active.show_region_ui = True
            for region in area.regions:
                if region.type == "UI":
                    try:
                        region.active_panel_category = "Weapon Lab"
                    except (AttributeError, TypeError):
                        pass  # Older Blender: the user picks the tab once.
    return None  # Run once.


bpy.app.timers.register(show_weapon_lab_tab, first_interval=0.5)
