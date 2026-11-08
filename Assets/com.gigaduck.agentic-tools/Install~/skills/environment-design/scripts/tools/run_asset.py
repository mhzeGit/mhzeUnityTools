"""Build one or more assets, save the .blend, export FBX, RE-IMPORT the FBX in a clean scene and render clay views.

blender -b -P run_asset.py -- <module> <Asset[,Asset2,...]|ALL> <tag> [wire]

* <module> is a file in <env>/assets/ (without .py) exposing ASSETS = {"Name": build_fn}
  build_fn() -> list of mesh objects, or (list, meta_dict) with optional keys: pivot_note, regions
* Outputs go to .utmp/<slug>/{blender,fbx,renders,logs}/ ; <Asset>_<tag>_<view>.png from the RE-IMPORTED FBX.
* Prints one JSON line per asset: vertices/triangles/dimensions/parts measured on the re-imported FBX.
"""
import importlib
import json
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
WORK = envcfg.WORK
sys.path.insert(0, envcfg.ASSETS_DIR)

import bpy  # noqa: E402
import bq  # noqa: E402


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    module_name, which, tag = argv[0], argv[1], argv[2]
    want_wire = "wire" in argv[3:]
    mod = importlib.import_module(module_name)
    names = list(mod.ASSETS.keys()) if which == "ALL" else which.split(",")
    for d in ("blender", "fbx", "renders", "logs"):
        os.makedirs(os.path.join(WORK, d), exist_ok=True)
    for name in names:
        t0 = time.time()
        bq.clean_scene()
        res = mod.ASSETS[name]()
        objs, meta = (res if isinstance(res, tuple) else (res, {}))
        blend = os.path.join(WORK, "blender", f"{name}_{tag}.blend")
        bpy.ops.wm.save_as_mainfile(filepath=blend)
        verts_a, tris_a = bq.mesh_stats(objs)
        fbx = os.path.join(WORK, "fbx", f"{name}_{tag}.fbx")
        bq.export_fbx(fbx, objs)
        size_kb = os.path.getsize(fbx) // 1024
        # ---- isolated re-import of the exported file
        imp = bq.import_fbx_clean(fbx)
        v, tr = bq.mesh_stats(imp)
        mn, mx = bq.bounds(imp)
        dims = mx - mn
        prefix = os.path.join(WORK, "renders", f"{name}_{tag}")
        bq.render_set(prefix, imp)
        if want_wire:
            bq.render_wire_q(prefix + "_wire.png", imp)
        info = {
            "asset": name, "tag": tag, "fbx": fbx, "fbx_kb": size_kb, "blend": blend,
            "authoring_verts": verts_a, "authoring_tris": tris_a, "imported_verts": v, "imported_tris": tr,
            "parts": sorted(o.name for o in imp), "dims_xyz_m": [round(dims.x, 3), round(dims.y, 3), round(dims.z, 3)],
            "min": [round(mn.x, 3), round(mn.y, 3), round(mn.z, 3)], "max": [round(mx.x, 3), round(mx.y, 3), round(mx.z, 3)],
            "meta": meta, "seconds": round(time.time() - t0, 1),
        }
        with open(os.path.join(WORK, "logs", f"{name}_{tag}.json"), "w") as f:
            json.dump(info, f, indent=1)
        print("ASSET_RESULT " + json.dumps({k: info[k] for k in ("asset", "tag", "imported_verts", "imported_tris", "dims_xyz_m", "seconds")}))


main()
