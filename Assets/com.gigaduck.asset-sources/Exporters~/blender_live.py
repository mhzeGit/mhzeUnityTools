"""Live exports from an explicitly opened Blender session; never saves the master."""
import base64
import hashlib
import json
import os
from pathlib import Path
import runpy
import shutil
import sys
import time
import uuid

import bpy


def atomic_json(path, data):
    temp = path.with_suffix(".tmp")
    temp.write_text(json.dumps(data), encoding="utf-8")
    os.replace(temp, path)


class LiveLink:
    def __init__(self, config):
        self.config = Path(config)
        self.directory = self.config.parent
        self.export = runpy.run_path(str(Path(__file__).with_name("blender_export.py")))["export"]
        self.jobs = []
        self.pending = set()
        self.changed = 0
        self.exporting = False
        self.config_stamp = None
        self.failed = None

    def changed_data(self, scene, depsgraph):
        if self.exporting:
            return
        updates = [u for u in depsgraph.updates if u.is_updated_geometry or u.is_updated_transform or u.is_updated_shading]
        if not updates:
            return
        names = {u.id.original.name for u in updates}
        broad = any(not isinstance(u.id.original, (bpy.types.Object, bpy.types.Mesh)) for u in updates)
        for job in self.jobs:
            objects = [o for o in scene.objects if o.type == "MESH" and
                       (o.name in job["meshes"] or o.data.name in job["meshes"])]
            dependencies = set(job["meshes"])
            for obj in objects:
                dependencies.update((obj.name, obj.data.name))
                parent = obj.parent
                while parent:
                    dependencies.add(parent.name)
                    parent = parent.parent
                for modifier in obj.modifiers:
                    for prop in modifier.bl_rna.properties:
                        if prop.type == "POINTER":
                            value = getattr(modifier, prop.identifier, None)
                            if isinstance(value, bpy.types.ID):
                                dependencies.add(value.name)
            if broad or names & dependencies:
                self.pending.add(job["guid"])
        if self.pending:
            self.changed = time.monotonic()

    def undo(self, *_):
        if not self.exporting:
            self.pending.update(job["guid"] for job in self.jobs)
            self.changed = time.monotonic()

    def stop(self):
        for handlers, callback in ((bpy.app.handlers.depsgraph_update_post, self.changed_data),
                                   (bpy.app.handlers.undo_post, self.undo), (bpy.app.handlers.redo_post, self.undo)):
            if callback in handlers:
                handlers.remove(callback)
        shutil.rmtree(self.directory, ignore_errors=True)
        print("[Asset Sources] Unity live link stopped")

    def tick(self):
        try:
            lease = self.directory / "unity.alive"
            if not self.config.exists() or not lease.exists() or time.time() - lease.stat().st_mtime > 60:
                self.stop()
                return None
            data = json.loads(self.config.read_text(encoding="utf-8-sig"))
            if os.path.normcase(os.path.realpath(bpy.data.filepath)) != os.path.normcase(os.path.realpath(data["source"])):
                self.stop()
                return None
            (self.directory / "blender.alive").touch()
            stamp = self.config.stat().st_mtime_ns
            if stamp != self.config_stamp:
                self.jobs = data["jobs"]
                self.config_stamp = stamp
            ready = self.directory / "ready.json"
            # Operators must run on the main thread, after a gesture has finished.
            if not self.pending or ready.exists() or time.monotonic() - self.changed < .35:
                return .2
            if bpy.context.window and bpy.context.window.modal_operators:
                return .2
            pending, self.pending = self.pending, set()
            results = []
            self.exporting = True
            try:
                for job in self.jobs:
                    if job["guid"] not in pending:
                        continue
                    directory = self.directory / ("frame-" + uuid.uuid4().hex)
                    directory.mkdir()
                    output = directory / ("output" + Path(job["target"]).suffix)
                    frame = dict(guid=job["guid"], assetPath=job["assetPath"], output=str(output))
                    try:
                        frame["previousHash"] = base64.b64encode(hashlib.sha256(Path(job["target"]).read_bytes()).digest()).decode()
                        self.export(dict(job, output=str(output)))
                        if not output.exists() or not output.stat().st_size:
                            raise RuntimeError("Blender produced no live export output")
                    except Exception as error:
                        frame["error"] = str(error)
                    results.append(frame)
                bpy.context.view_layer.update()
            finally:
                self.exporting = False
            if results:
                atomic_json(ready, dict(results=results))
            self.failed = None
        except Exception as error:
            if str(error) != self.failed:
                print("[Asset Sources] Live link: " + str(error))
                self.failed = str(error)
        return .2

    def start(self):
        bpy.app.handlers.depsgraph_update_post.append(self.changed_data)
        bpy.app.handlers.undo_post.append(self.undo)
        bpy.app.handlers.redo_post.append(self.undo)
        bpy.app.timers.register(self.tick, first_interval=.2)
        print("[Asset Sources] Connected to Unity; linked meshes update after editing pauses (no save needed)")


if __name__ == "__main__":
    LiveLink(sys.argv[sys.argv.index("--") + 1]).start()
