#!/usr/bin/env python
"""Blockout, clay import and scene assembly (C# in cs/EnvBlockout.cs and cs/EnvTools.cs).

  python tools.py blockout          layout/layout_blockout.py -> primitive blockout scene <Name>_Blockout.unity (REPLACES the open scene: only when it is clean)
  python tools.py import            copy every .utmp/<slug>/fbx/*_final.fbx into the Unity project (models dir) and import them
  python tools.py prefabs           clay prefabs <prefix><Asset>.prefab (region-coloured clay materials) for every imported model
  python tools.py assemble [clean]  layout/layout_room.py -> assembled scene <Name>.unity from the prefabs (new scene file; never overwrite a hand-authored one)

The assembled scene is generated ONCE. From then on it is hand-authored: change prefabs (tex.py), never regenerate the scene.
"""
import glob
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import ucs  # noqa: E402

P = envcfg.unity_paths()
TOOLS = os.path.join(HERE, "cs", "EnvTools.cs")
BLOCK = os.path.join(HERE, "cs", "EnvBlockout.cs")
CLAY_MATS = P["materials"] + "/Clay"


def tools(cmd):
    return ucs.run(TOOLS, "ClaudeTmp.EnvTools.Run", json.dumps(cmd))


def main():
    what = sys.argv[1] if len(sys.argv) > 1 else ""
    work = os.path.join(envcfg.WORK, "unity")
    os.makedirs(work, exist_ok=True)
    if what == "blockout":
        out = os.path.join(work, "layout_blockout.json")
        subprocess.check_call([sys.executable, os.path.join(envcfg.LAYOUT_DIR, "layout_blockout.py"), out])
        print(ucs.run(BLOCK, "ClaudeTmp.EnvBlockout.Run", json.dumps({"cmd": "build", "json": out, "scene": P["blockout_scene"]})))
    elif what == "import":
        lines = []
        for f in sorted(glob.glob(os.path.join(envcfg.WORK, "fbx", "*_final.fbx"))):
            name = os.path.basename(f)[:-len("_final.fbx")]
            lines.append(f"{f.replace(chr(92), '/')}|{P['models']}/{name}.fbx")
        lst = os.path.join(work, "import_list.txt")
        open(lst, "w").write("\n".join(lines))
        print(tools({"cmd": "importmodels", "json": lst}))
    elif what == "prefabs":
        print(tools({"cmd": "makeprefabs", "models": P["models"], "prefabs": P["prefabs"], "matDir": CLAY_MATS}))
    elif what == "assemble":
        out = os.path.join(work, "room_layout.json")
        subprocess.check_call([sys.executable, os.path.join(envcfg.LAYOUT_DIR, "layout_room.py"), out])
        mode = sys.argv[2] if len(sys.argv) > 2 else "clean"
        print(tools({"cmd": "assemble", "json": out, "prefabs": P["prefabs"], "scene": P["scene"], "mode": mode, "name": envcfg.CFG["name"]}))
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
