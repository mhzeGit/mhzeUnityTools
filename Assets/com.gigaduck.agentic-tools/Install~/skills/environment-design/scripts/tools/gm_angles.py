"""Record the smooth angle each part of every asset was authored with: groups/smooth_angles.json  {asset: {part: degrees}}

blender -b -P gm_angles.py

gm_obj.py rebuilds the final normals of the textured FBX from these angles (edges sharper than the angle stay hard, flatter ones are smooth,
vertex normals are weighted by face area so a large flat face keeps its own normal next to a small bevel facet).
"""
import importlib
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
sys.path.insert(0, envcfg.ASSETS_DIR)
import bq  # noqa: E402

MODULES = envcfg.asset_modules()


def main():
    out = {}
    for module in MODULES:
        mod = importlib.import_module(module)
        for name, fn in mod.ASSETS.items():
            bq.clean_scene()
            res = fn()
            objs = res[0] if isinstance(res, tuple) else res
            out[name] = {o.name: o.get("smooth_angle", 40.0) for o in objs}
    os.makedirs(envcfg.GROUPS_DIR, exist_ok=True)
    path = os.path.join(envcfg.GROUPS_DIR, "smooth_angles.json")
    json.dump(out, open(path, "w"), indent=1, sort_keys=True)
    print("GM_ANGLES", len(out), "assets ->", os.path.abspath(path))


main()
