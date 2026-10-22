"""Run with blender --background --factory-startup --python this_file -- <external-work-folder>."""
import hashlib
import json
import os
from pathlib import Path
import runpy
import sys

import bmesh
import bpy


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
    session = folder / "session"
    session.mkdir(exist_ok=True)
    config = session / "request.json"
    config.write_text(json.dumps(dict(source=str(master), jobs=[job])), encoding="utf-8")
    (session / "unity.alive").touch()
    live_config = "\\\\?\\" + str(config.resolve()) if os.name == "nt" else str(config)
    link = live_type(live_config)
    link.start()
    link.tick()
    assert not (session / "ready.json").exists(), "Opening must not publish unchanged files"

    # Object transforms must dirty the linked target automatically through the depsgraph.
    cube.location.x = 2
    bpy.context.view_layer.update()
    assert "cube" in link.pending, "Object transform did not trigger the live handler"
    link.changed = 0
    link.tick()
    ready = session / "ready.json"
    response = json.loads(ready.read_text())
    frame = response["results"][0]
    assert not frame.get("error"), frame
    assert Path(frame["output"]).read_bytes() != baseline
    assert target.read_bytes() == baseline, "Blender must stage exports, not replace Unity files itself"
    ready.unlink()
    assert not link.pending, "The exporter generated a feedback loop"

    # Edit-mode geometry must export without leaving edit mode or losing selection.
    bpy.ops.object.mode_set(mode="EDIT")
    mesh = bmesh.from_edit_mesh(cube.data)
    mesh.verts.ensure_lookup_table()
    mesh.verts[0].co.x -= 1
    bmesh.update_edit_mesh(cube.data)
    bpy.context.view_layer.update()
    assert "cube" in link.pending, "Edit-mode geometry did not trigger the live handler"
    link.changed = 0
    link.tick()
    frame = json.loads(ready.read_text())["results"][0]
    assert not frame.get("error"), frame
    assert cube.mode == "EDIT" and cube.select_get() and bpy.context.view_layer.objects.active == cube
    assert not link.pending, "Restoring edit mode generated a feedback loop"
    ready.unlink()

    # Every actual save must export all linked meshes, including two unchanged saves.
    for _ in range(2):
        bpy.ops.wm.save_as_mainfile(filepath=str(master))
        master_hash = hashlib.sha256(master.read_bytes()).hexdigest()
        assert link.save_requested, "A successful save did not reach the bridge"
        assert link.changed == 0, "Save refresh must bypass the editing debounce"
        link.tick()
        frame = json.loads(ready.read_text())["results"][0]
        assert not frame.get("error"), frame
        assert frame["forceImport"], "Unchanged saves must force a Unity import"
        assert cube.mode == "EDIT" and cube.select_get()
        assert hashlib.sha256(master.read_bytes()).hexdigest() == master_hash
        ready.unlink()

    # Failed mapping must leave the target/master intact and restore editing state.
    bad = dict(job, meshes=["UnknownMesh"], output=str(folder / "should-not-exist.fbx"))
    try:
        export(bad)
        raise AssertionError("An unknown mesh was exported")
    except ValueError:
        pass
    assert cube.mode == "EDIT" and cube.select_get()
    assert not Path(bad["output"]).exists()
    assert hashlib.sha256(master.read_bytes()).hexdigest() == master_hash, "Live editing saved the master"
    link.undo()
    assert "cube" in link.pending
    config.unlink()
    assert link.tick() is None and not session.exists()
    assert link.saved not in bpy.app.handlers.save_post
    print("ASSET_SOURCES_BLENDER_TESTS_PASSED")


if __name__ == "__main__":
    main(Path(sys.argv[sys.argv.index("--") + 1]))
