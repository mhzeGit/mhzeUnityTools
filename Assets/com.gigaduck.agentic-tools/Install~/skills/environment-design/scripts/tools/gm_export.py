"""Blender side of the Graphmat texturing pipeline.

blender -b -P gm_export.py -- export <group.json>   build the group's assets, lay them out on a 3-axis staircase `gap` m apart inside a cube
                                                    (so AO/raycasts never see each other and triplanar tiles are not stretched),
                                                    rotate texture-frame assets (long axis -> Z for grain) and write model.obj + sidecar.json
blender -b -P gm_export.py -- import <group.json>   read the Graphmat UV model (uv.obj), undo layout/rotation, write textured <Asset>_uv.fbx files

group.json: {"group": "crates", "work": ".utmp/<slug>/gmat/crates", "assets": [{"name": "Crate_Large", "module": "props", "grm": [0,0,0]}, ...]}
  grm = Euler degrees (XYZ) applied to the asset before texturing (undone after); parts must be named <Region>__<Part>.
"""
import importlib
import json
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
ROOT = envcfg.ROOT
sys.path.insert(0, envcfg.ASSETS_DIR)

import bpy  # noqa: E402
import bq  # noqa: E402
from mathutils import Matrix, Vector, Euler  # noqa: E402

SEP = "@@"
GAP = 3.0


def build_asset(module, name):
    mod = importlib.import_module(module)
    res = mod.ASSETS[name]()
    return res[0] if isinstance(res, tuple) else res


ANCHOR = "ANCHOR"


def do_export(spec):
    """Staircase layout on all three axes (every asset >= gap away from the others on every axis) inside a CUBE [0, L]^3.

    Graphmat's Mesh Triplanar tiles the texture every (mesh AABB extent per axis x Scale), so a cubic AABB gives the same tile length
    on all axes (no stretch). The cube is completed with one 1 cm anchor triangle at (L, L, L); it is dropped again on import.
    """
    work = os.path.join(ROOT, spec["work"]) if not os.path.isabs(spec["work"]) else spec["work"]
    os.makedirs(work, exist_ok=True)
    bq.clean_scene()
    gap = float(spec.get("gap", GAP))
    side = {"group": spec["group"], "assets": []}
    placed = []
    for a in spec["assets"]:
        # build in the same scene; objects keep their authoring names until renamed
        objs = build_asset(a["module"], a["name"])
        grm = Euler([math.radians(v) for v in a.get("grm", [0, 0, 0])], 'XYZ').to_matrix().to_4x4()
        for o in objs:
            o.matrix_world = grm @ o.matrix_world
        bpy.context.view_layer.update()
        mn, mx = bq.bounds(objs)
        placed.append((a, objs, mn, mx))
    cursor = Vector((0.0, 0.0, 0.0))
    all_objs = []
    cylinder = spec.get("cylinder")                 # "Y": the asset (rotated by its grm) is a body of revolution around the Y axis
    cube_req = float(spec.get("cube", 0.0))
    if cylinder:
        # one round asset: its axis (through the asset origin) goes to the centre of the cube so that Graphmat's bounding-box-centred cylinder
        # projection wraps round the real axis; the cube side is given by the spec (chosen so the brick courses line up with the modelled ones)
        a, objs, mn, mx = placed[0]
        reach = max(abs(mn.x), abs(mx.x), abs(mn.z), abs(mx.z))
        if cube_req < 2 * reach + 0.05:
            raise RuntimeError(f"cube {cube_req} is smaller than the asset ({2 * reach + 0.05:.2f} needed)")
        off = Vector((cube_req / 2, -mn.y, cube_req / 2))
        for o in objs:
            o.data.transform(Matrix.Translation(off) @ o.matrix_world)
            o.matrix_world = Matrix.Identity(4)
        for o in objs:
            o.name = f"{a['name']}{SEP}{o.name}"
        all_objs += objs
        side["assets"].append({"name": a["name"], "grm": a.get("grm", [0, 0, 0]), "offset": list(off), "parts": [o.name for o in objs]})
        ext = Vector((cube_req, cube_req, cube_req))
        cube = cube_req
    else:
        for a, objs, mn, mx in placed:
            off = cursor - mn
            for o in objs:
                o.data.transform(Matrix.Translation(off) @ o.matrix_world)
                o.matrix_world = Matrix.Identity(4)
            for o in objs:
                o.name = f"{a['name']}{SEP}{o.name}"
            cursor = cursor + (mx - mn) + Vector((gap, gap, gap))
            all_objs += objs
            side["assets"].append({"name": a["name"], "grm": a.get("grm", [0, 0, 0]), "offset": list(off), "parts": [o.name for o in objs]})
        ext = cursor - Vector((gap, gap, gap))
        cube = max(ext.x, ext.y, ext.z, cube_req)
    e = 0.01
    corners = [("Anchor__Max", (cube, cube, cube), (-e, 0, 0), (0, -e, 0))]
    if cylinder:
        corners.append(("Anchor__Min", (0.0, 0.0, 0.0), (e, 0, 0), (0, e, 0)))
    for nm, c0, d1, d2 in corners:
        me = bpy.data.meshes.new(nm)
        pts = [c0, tuple(c0[i] + d1[i] for i in range(3)), tuple(c0[i] + d2[i] for i in range(3))]
        me.from_pydata(pts, [], [(0, 1, 2)])
        me.update()
        anchor = bpy.data.objects.new(f"{ANCHOR}{SEP}{nm}", me)
        bpy.context.scene.collection.objects.link(anchor)
        all_objs.append(anchor)
    side["cube"] = cube
    side["extent"] = list(ext)
    # Hard edges: bq.obj_from_bm flags edges sharper than the part's smooth angle as sharp, but Blender's OBJ exporter writes per-VERTEX normals for
    # smooth-shaded faces and ignores sharp edges, which smooths every box corner (45-55 degrees off the face normal) and then poisons Graphmat's
    # world-normal bake (triplanar blending, AO) and the normals of the final FBX. Splitting the sharp edges makes vertex normals equal split normals.
    for o in all_objs:
        if o.name.startswith(ANCHOR + SEP):
            continue
        m = o.modifiers.new("EdgeSplitSharp", 'EDGE_SPLIT')
        m.use_edge_angle = False
        m.use_edge_sharp = True
    bpy.ops.object.select_all(action='DESELECT')
    for o in all_objs:
        o.select_set(True)
    obj_path = os.path.join(work, "model.obj")
    bpy.ops.wm.obj_export(filepath=obj_path, export_selected_objects=True, forward_axis='Y', up_axis='Z', export_uv=False, export_normals=True,
                          export_materials=False, apply_modifiers=True, export_smooth_groups=False, export_triangulated_mesh=True)
    verts, tris = bq.mesh_stats(all_objs)
    side["obj"] = obj_path
    side["verts"], side["tris"] = verts, tris
    json.dump(side, open(os.path.join(work, "sidecar.json"), "w"), indent=1)
    print("GM_EXPORT", json.dumps({"group": spec["group"], "obj": obj_path, "verts": verts, "tris": tris, "assets": len(spec["assets"]), "cube": round(cube, 3), "extent": [round(v, 3) for v in ext]}))


def do_import(spec):
    import gm_obj
    work = os.path.join(ROOT, spec["work"]) if not os.path.isabs(spec["work"]) else spec["work"]
    bq.clean_scene()
    built = gm_obj.build(work)
    outdir = envcfg.FBX_UV
    os.makedirs(outdir, exist_ok=True)
    report = []
    for asset, parts in built.items():
        for (part, o) in parts:
            o.name = part
            o.data.name = part
        objs = [o for (_, o) in parts]
        v, t = bq.mesh_stats(objs)
        has_uv = all(len(o.data.uv_layers) > 0 for o in objs)
        fbx = os.path.join(outdir, f"{asset}_uv.fbx")
        bq.export_fbx(fbx, objs)
        report.append({"asset": asset, "fbx": fbx, "has_uv": has_uv, "verts": v, "tris": t, "parts": sorted(o.name for o in objs)})
        for o in objs:
            me = o.data
            bpy.data.objects.remove(o)
            bpy.data.meshes.remove(me)
    json.dump(report, open(os.path.join(work, "import_report.json"), "w"), indent=1)
    print("GM_IMPORT", json.dumps(report)[:1800])


if __name__ == "__main__":
    argv = sys.argv[sys.argv.index("--") + 1:]
    spec = json.load(open(argv[1]))
    {"export": do_export, "import": do_import}[argv[0]](spec)
