"""Exports from an explicitly opened Blender session, on every save only; never saves the master."""
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
    """Listens for saves only. No depsgraph, undo or redo handler runs while the artist edits or poses, so the
    editing session pays nothing; each save exports the linked targets, preserving any un-keyed pose."""

    def __init__(self, config):
        self.config = Path(config)
        self.directory = self.config.parent
        exporter = runpy.run_path(str(Path(__file__).with_name("blender_export.py")))
        self.export = exporter["export"]
        self.jobs = []
        self.source = ""
        self.config_stamp = None
        self.failed = None
        self.save_requested = False

    def saved(self, *_):
        self.save_requested = True

    def stop(self):
        if self.saved in bpy.app.handlers.save_post:
            bpy.app.handlers.save_post.remove(self.saved)
        shutil.rmtree(self.directory, ignore_errors=True)
        print("[Asset Sources] Unity live link stopped")

    def tick(self):
        try:
            lease = self.directory / "unity.alive"
            if not self.config.exists() or not lease.exists() or time.time() - lease.stat().st_mtime > 60:
                self.stop()
                return None
            stamp = self.config.stat().st_mtime_ns
            if stamp != self.config_stamp:
                data = json.loads(self.config.read_text(encoding="utf-8-sig"))
                self.jobs, self.source, self.config_stamp = data["jobs"], data["source"], stamp
            if os.path.normcase(os.path.realpath(bpy.data.filepath)) != os.path.normcase(os.path.realpath(self.source)):
                self.stop()
                return None
            (self.directory / "blender.alive").touch()
            ready = self.directory / "ready.json"
            if not self.save_requested or ready.exists():
                return .2
            # Operators must run on the main thread, after a gesture has finished.
            if bpy.context.window and bpy.context.window.modal_operators:
                return .2
            self.save_requested = False
            results = []
            for job in self.jobs:
                directory = self.directory / ("frame-" + uuid.uuid4().hex)
                directory.mkdir()
                output = directory / ("output" + Path(job["target"]).suffix)
                frame = dict(guid=job["guid"], assetPath=job["assetPath"], output=str(output), forceImport=True)
                try:
                    frame["previousHash"] = base64.b64encode(hashlib.sha256(Path(job["target"]).read_bytes()).digest()).decode()
                    self.export(dict(job, output=str(output)))
                    if not output.exists() or not output.stat().st_size:
                        raise RuntimeError("Blender produced no live export output")
                except Exception as error:
                    frame["error"] = str(error)
                results.append(frame)
            if results:
                atomic_json(ready, dict(results=results))
            self.failed = None
        except Exception as error:
            if str(error) != self.failed:
                print("[Asset Sources] Live link: " + str(error))
                self.failed = str(error)
        return .2

    def start(self):
        bpy.app.handlers.save_post.append(self.saved)
        bpy.app.timers.register(self.tick, first_interval=.2)
        print("[Asset Sources] Connected to Unity; linked meshes update on each save")


if __name__ == "__main__":
    LiveLink(sys.argv[sys.argv.index("--") + 1]).start()
