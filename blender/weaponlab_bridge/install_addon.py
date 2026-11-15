"""Installs, removes or checks the Weapon Lab Bridge in this Blender's user add-ons.

Weapon Lab runs it in the background (never with --factory-startup, so the
user's own preferences are loaded and saved back with the change):

    blender --background --python install_addon.py -- install|remove|status <bridge parent> <cli>

What gets installed is a small loader, not a copy of the bridge: it imports
weaponlab_bridge from the Weapon Lab install, so the panel always matches
the Weapon Lab version, and it tells the panel where weaponlab.exe is so
exporting works in .blend files opened directly. Opening through Weapon
Lab still works: the bridge registers only once.
"""
import os
import sys

import bpy

MODULE = "weaponlab_bridge_loader"
RESULT = "WEAPONLAB_ADDON "

LOADER = '''bl_info = {{
    "name": "Weapon Lab Bridge",
    "author": "Weapon Lab",
    "version": (1, 0, 0),
    "blender": (4, 4, 0),
    "location": "3D Viewport > Sidebar > Weapon Lab",
    "description": "Loads the Weapon Lab Bridge from your Weapon Lab install, so .blend files opened directly have the panel",
    "category": "Animation",
}}

import os
import sys

# Written by Weapon Lab's "Install add-on in Blender". Reinstall from Weapon Lab if it moves.
BRIDGE_PARENT = {parent!r}
CLI = {cli!r}


def register():
    if CLI and not os.environ.get("WEAPONLAB_CLI"):
        os.environ["WEAPONLAB_CLI"] = CLI
    if not os.path.isdir(os.path.join(BRIDGE_PARENT, "weaponlab_bridge")):
        print("Weapon Lab Bridge: Weapon Lab is no longer at", BRIDGE_PARENT, "- reinstall the add-on from Weapon Lab.")
        return
    if BRIDGE_PARENT not in sys.path:
        sys.path.insert(0, BRIDGE_PARENT)
    import bpy
    import weaponlab_bridge
    if not hasattr(bpy.types.Scene, "weaponlab"):
        weaponlab_bridge.register()


def unregister():
    import bpy
    bridge = sys.modules.get("weaponlab_bridge")
    if bridge is not None and hasattr(bpy.types.Scene, "weaponlab"):
        bridge.unregister()
'''


def main():
    args = sys.argv[sys.argv.index("--") + 1:]
    action = args[0]
    folder = bpy.utils.user_resource("SCRIPTS", path="addons", create=True)
    path = os.path.join(folder, MODULE + ".py")

    if action == "install":
        parent, cli = args[1], args[2]
        with open(path, "w", encoding="utf-8") as file:
            file.write(LOADER.format(parent=parent, cli=cli))
        bpy.ops.preferences.addon_enable(module=MODULE)
        bpy.ops.wm.save_userpref()
        print(RESULT + "installed " + path)
    elif action == "remove":
        if os.path.exists(path):
            try:
                bpy.ops.preferences.addon_disable(module=MODULE)
            except RuntimeError:
                pass  # Not enabled; removing the file is enough.
            os.remove(path)
            bpy.ops.wm.save_userpref()
        print(RESULT + "removed " + path)
    else:
        print(RESULT + ("installed " if os.path.exists(path) else "missing ") + path)


main()
