"""Run with blender --background --factory-startup --python this_file -- <external-work-folder>."""
import hashlib
import json
import math
import os
from pathlib import Path
import runpy
import shutil
import struct
import sys

import bmesh
import bpy


def unit_scale_factor(fbx):
    """Reads GlobalSettings UnitScaleFactor from a binary FBX (1.0 = metres as authored, 100.0 = the centimetre default)."""
    data = fbx.read_bytes()
    index = data.find(b"UnitScaleFactor")
    return struct.unpack("<d", data[data.find(b"D", index + 40) + 1:][:8])[0]


def open_link(live_type, folder, master, job):
    session = folder / "session"
    shutil.rmtree(session, ignore_errors=True)
    session.mkdir()
    config = session / "request.json"
    config.write_text(json.dumps(dict(source=str(master), jobs=[job])), encoding="utf-8")
    (session / "unity.alive").touch()
    live_config = "\\\\?\\" + str(config.resolve()) if os.name == "nt" else str(config)
    link = live_type(live_config)
    link.start()
    return link, session, config


def check_rig(folder, live_type):
    """A keyed rig with a bone-attached empty: saving exports it, keeps the artist's un-keyed pose and honours the sidecar."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.object.armature_add()
    arm = bpy.context.object
    arm.name = "Armature"
    bpy.ops.object.mode_set(mode="EDIT")
    arm.data.edit_bones[0].name = "Bone"
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.mesh.primitive_cube_add(size=.5, location=(0, 0, 1))
    skin = bpy.context.object
    skin.name = skin.data.name = "Skin"
    skin.parent = arm
    skin.modifiers.new("Armature", "ARMATURE").object = arm
    skin.vertex_groups.new(name="Bone").add(list(range(len(skin.data.vertices))), 1.0, "REPLACE")
    contact = bpy.data.objects.new("Contact", None)
    bpy.context.collection.objects.link(contact)
    contact.parent, contact.parent_type, contact.parent_bone = arm, "BONE", "Bone"
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    bone = arm.pose.bones["Bone"]
    bone.rotation_mode = "XYZ"
    bone.keyframe_insert("rotation_euler", frame=1)
    bone.rotation_euler = (0, 0, math.radians(40))
    bone.keyframe_insert("rotation_euler", frame=10)
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.scene.frame_set(1)

    master, target = folder / "Rig.blend", folder / "Rig.fbx"
    target.write_bytes(b"existing unity asset")
    bpy.ops.wm.save_as_mainfile(filepath=str(master))
    job = dict(source=str(master), target=str(target), assetPath="Assets/Rig.fbx", guid="rig", meshes=["Skin"])
    link, session, config = open_link(live_type, folder, master, job)
    ready = session / "ready.json"

    # Posing a bone by hand is invisible to the bridge and is never reset: no handler runs, nothing exports.
    manual = (math.radians(70), 0.0, 0.0)
    bone.rotation_euler = manual
    bpy.context.view_layer.update()
    link.tick()
    assert not ready.exists(), "Posing exported without a save"
    assert tuple(round(v, 4) for v in bone.rotation_euler) == tuple(round(v, 4) for v in manual)

    # Saving exports, and the un-keyed pose is still there afterwards even though the action is active on frame 1.
    bpy.ops.wm.save_as_mainfile(filepath=str(master))
    link.tick()
    frame = json.loads(ready.read_text())["results"][0]
    assert not frame.get("error"), frame
    assert tuple(round(v, 4) for v in bone.rotation_euler) == tuple(round(v, 4) for v in manual), "Export reset the un-keyed pose"
    assert arm.animation_data.action is not None and bpy.context.scene.frame_current == 1
    default_output = Path(frame["output"])
    assert unit_scale_factor(default_output) == 100.0
    ready.unlink()

    # The bone-attached empty is exported, and a sidecar can ask for metre units.
    (folder / "Rig.assetsources.json").write_text(json.dumps({"rig.fbx": {"fbx": {"apply_unit_scale": False}}}), encoding="utf-8")
    link.save_requested = True
    link.tick()
    frame = json.loads(ready.read_text())["results"][0]
    assert not frame.get("error"), frame
    assert unit_scale_factor(Path(frame["output"])) == 1.0, "The sidecar export options were ignored"
    exported = Path(frame["output"]).read_bytes()
    assert b"Contact" in exported, "The bone-attached empty was not exported"
    assert tuple(round(v, 4) for v in bone.rotation_euler) == tuple(round(v, 4) for v in manual)
    ready.unlink()
    config.unlink()
    assert link.tick() is None and not session.exists()
    assert link.saved not in bpy.app.handlers.save_post


def main(folder):
    folder.mkdir(parents=True, exist_ok=True)
    exporter = Path(__file__).parents[1] / "Exporters~"
    export = runpy.run_path(str(exporter / "blender_export.py"))["export"]
    live_type = runpy.run_path(str(exporter / "blender_live.py"))["LiveLink"]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.mesh.primitive_cube_add()
    cube = bpy.context.object
    cube.name = cube.data.name = "LiveCube"
    for vertex in cube.data.vertices:
        vertex.co.y *= 2
        vertex.co.z *= 3
    master, target = folder / "LiveCube.blend", folder / "LiveCube.fbx"
    bpy.ops.wm.save_as_mainfile(filepath=str(master))
    master_hash = hashlib.sha256(master.read_bytes()).hexdigest()
    job = dict(source=str(master), target=str(target), assetPath="Assets/LiveCube.fbx", guid="cube", meshes=["LiveCube"])
    export(dict(job, output=str(target)))
    baseline = target.read_bytes()
    link, session, config = open_link(live_type, folder, master, job)
    ready = session / "ready.json"
    link.tick()
    assert not ready.exists(), "Opening must not publish unchanged files"

    # Editing never touches the bridge: it listens to saves only, so no handler can slow down editing or posing.
    for handlers in (bpy.app.handlers.depsgraph_update_post, bpy.app.handlers.undo_post, bpy.app.handlers.redo_post):
        assert not any(getattr(callback, "__self__", None) is link for callback in handlers), "The bridge listens to edits"
    cube.location.x = 2
    bpy.context.view_layer.update()
    link.tick()
    assert not ready.exists(), "An edit exported without a save"

    # A save exports the edit and stages it: Blender never replaces Unity's file itself.
    bpy.ops.wm.save_as_mainfile(filepath=str(master))
    master_hash = hashlib.sha256(master.read_bytes()).hexdigest()
    assert link.save_requested, "A successful save did not reach the bridge"
    link.tick()
    frame = json.loads(ready.read_text())["results"][0]
    assert not frame.get("error"), frame
    assert Path(frame["output"]).read_bytes() != baseline
    assert target.read_bytes() == baseline, "Blender must stage exports, not replace Unity files itself"
    assert frame["forceImport"], "A save must force a Unity import"
    ready.unlink()
    assert not link.save_requested, "The exporter generated a feedback loop"

    # Edit-mode geometry saved in edit mode exports without leaving edit mode or losing selection.
    bpy.ops.object.mode_set(mode="EDIT")
    mesh = bmesh.from_edit_mesh(cube.data)
    mesh.verts.ensure_lookup_table()
    mesh.verts[0].co.x -= 1
    bmesh.update_edit_mesh(cube.data)
    bpy.ops.wm.save_as_mainfile(filepath=str(master))
    link.tick()
    frame = json.loads(ready.read_text())["results"][0]
    assert not frame.get("error"), frame
    assert cube.mode == "EDIT" and cube.select_get() and bpy.context.view_layer.objects.active == cube
    assert not link.save_requested, "Restoring edit mode generated a feedback loop"
    ready.unlink()

    # A renamed single mesh is still the confirmed source, with the old Unity mesh identity.
    cube.name, cube.data.name = "RenamedObject", "RenamedMesh"
    cube.location.y += 1
    bpy.ops.wm.save_as_mainfile(filepath=str(master))
    link.tick()
    frame = json.loads(ready.read_text())["results"][0]
    assert not frame.get("error"), frame
    assert (cube.name, cube.data.name) == ("RenamedObject", "RenamedMesh")
    renamed_target = folder / "RenamedCube.fbx"
    shutil.copyfile(frame["output"], renamed_target)
    ready.unlink()

    # Every actual save must export all linked meshes, including two unchanged saves.
    for _ in range(2):
        bpy.ops.wm.save_as_mainfile(filepath=str(master))
        master_hash = hashlib.sha256(master.read_bytes()).hexdigest()
        assert link.save_requested, "A successful save did not reach the bridge"
        link.tick()
        frame = json.loads(ready.read_text())["results"][0]
        assert not frame.get("error"), frame
        assert frame["forceImport"], "Unchanged saves must force a Unity import"
        assert cube.mode == "EDIT" and cube.select_get()
        assert (cube.name, cube.data.name) == ("RenamedObject", "RenamedMesh")
        assert hashlib.sha256(master.read_bytes()).hexdigest() == master_hash
        ready.unlink()

    # Failed mapping must leave the target/master intact and restore editing state.
    other = bpy.data.objects.new("OtherObject", cube.data.copy())
    bpy.context.collection.objects.link(other)
    bad = dict(job, meshes=["UnknownMesh"], output=str(folder / "should-not-exist.fbx"))
    try:
        export(bad)
        raise AssertionError("An unknown mesh was exported")
    except ValueError:
        pass
    assert cube.mode == "EDIT" and cube.select_get()
    assert not Path(bad["output"]).exists()
    assert hashlib.sha256(master.read_bytes()).hexdigest() == master_hash, "Live editing saved the master"
    config.unlink()
    assert link.tick() is None and not session.exists()
    assert link.saved not in bpy.app.handlers.save_post
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete()
    bpy.ops.import_scene.fbx(filepath=str(renamed_target))
    imported = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    assert len(imported) == 1 and imported[0].data.name == "LiveCube", "Renames changed Unity's mesh identity"

    check_rig(folder, live_type)
    print("ASSET_SOURCES_BLENDER_TESTS_PASSED")


if __name__ == "__main__":
    main(Path(sys.argv[sys.argv.index("--") + 1]))
