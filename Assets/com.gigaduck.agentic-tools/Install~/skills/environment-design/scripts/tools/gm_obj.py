"""Read Graphmat's UV model (uv.obj) back into Blender objects in the authoring frame.

Graphmat merges every OBJ object into one 'Imported Model', recentres it (pure translation) and keeps triangle order. The original
model.obj written by gm_export.py has the per-object triangle ranges, so each part is rebuilt from its range:
  uv.obj triangle i == model.obj triangle i  (verified by centroid comparison; any deviation aborts).
Frame handling: undo Graphmat's translation, then the layout offset and texture-frame rotation recorded in sidecar.json.
"""
import json
import math
import os
import re

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector, Euler

SEP = "@@"


def parse_obj(path):
    v, vt, vn, faces, parts = [], [], [], [], []
    tri = 0
    cur = None
    with open(path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("v "):
                v.append([float(x) for x in line.split()[1:4]])
            elif line.startswith("vt "):
                vt.append([float(x) for x in line.split()[1:3]])
            elif line.startswith("vn "):
                vn.append([float(x) for x in line.split()[1:4]])
            elif line.startswith("o "):
                cur = [line[2:].strip(), tri, 0]
                parts.append(cur)
            elif line.startswith("f "):
                corners = []
                for tok in line.split()[1:]:
                    s = tok.split("/")
                    corners.append((int(s[0]) - 1, int(s[1]) - 1 if len(s) > 1 and s[1] else -1, int(s[2]) - 1 if len(s) > 2 and s[2] else -1))
                for k in range(1, len(corners) - 1):
                    faces.append((corners[0], corners[k], corners[k + 1]))
                    tri += 1
                    if cur is not None:
                        cur[2] += 1
    return np.array(v), np.array(vt), np.array(vn), faces, [tuple(p) for p in parts]


_ANGLES = None


def smooth_angle_for(asset, part):
    """Authored smooth angle (degrees) of a part, from groups/smooth_angles.json (written by gm_angles.py); 40 if unknown."""
    global _ANGLES
    if _ANGLES is None:
        import envcfg
        path = os.path.join(envcfg.GROUPS_DIR, "smooth_angles.json")
        _ANGLES = json.load(open(path)) if os.path.exists(path) else {}
    return float(_ANGLES.get(asset, {}).get(part, 40.0))


def rebuild_normals(me, angle_deg):
    """Final shading normals of a triangle mesh whose vertices are split along UV seams.

    Corners at the same POSITION share a normal when their face normals are within `angle_deg` of each other (so smooth surfaces stay smooth across
    UV seams and edges sharper than the angle stay hard); the shared normal is weighted by face area, so a large flat face keeps its own normal next
    to a small bevel facet instead of pillowing. Written as custom split normals, which the FBX exporter and Unity import as they are."""
    nloops = len(me.loops)
    if nloops == 0 or nloops != 3 * len(me.polygons):
        return False
    co = np.empty(len(me.vertices) * 3)
    me.vertices.foreach_get("co", co)
    co = co.reshape(-1, 3)
    lv = np.empty(nloops, dtype=np.int64)
    me.loops.foreach_get("vertex_index", lv)
    tri = lv.reshape(-1, 3)
    a, b, c = co[tri[:, 0]], co[tri[:, 1]], co[tri[:, 2]]
    n = np.cross(b - a, c - a)
    ln = np.linalg.norm(n, axis=1)
    ok = ln > 1e-12
    area = 0.5 * ln
    n[ok] /= ln[ok, None]
    key = np.round(co / 1e-5).astype(np.int64)
    _, pid = np.unique(key, axis=0, return_inverse=True)
    cpid = pid[tri]                                               # position id of every corner
    groups = {}
    for t in range(len(tri)):
        if ok[t]:
            for k in range(3):
                groups.setdefault(int(cpid[t, k]), []).append(t)
    cos_lim = math.cos(math.radians(angle_deg))
    out = np.zeros((nloops, 3))
    for t in range(len(tri)):
        for k in range(3):
            if not ok[t]:
                out[3 * t + k] = (0.0, 0.0, 1.0)
                continue
            ts = np.array(groups[int(cpid[t, k])])
            mask = (n[ts] @ n[t]) >= cos_lim
            v = (n[ts[mask]] * area[ts[mask], None]).sum(axis=0)
            nn = np.linalg.norm(v)
            out[3 * t + k] = v / nn if nn > 1e-12 else n[t]
    me.normals_split_custom_set([tuple(x) for x in out])
    return True


def build(work, collection=None):
    """Returns {asset: [(part_name, bpy_object)]} in the authoring frame (objects have UVs and custom normals)."""
    side = json.load(open(os.path.join(work, "sidecar.json")))
    v0, _, _, f0, parts0 = parse_obj(os.path.join(work, "model.obj"))
    v1, vt1, vn1, f1, _ = parse_obj(os.path.join(work, "uv.obj"))
    if len(f0) != len(f1):
        raise RuntimeError(f"triangle count changed: {len(f0)} -> {len(f1)}")
    c0 = np.array([v0[[c[0] for c in f]].mean(0) for f in f0[:50]])
    c1 = np.array([v1[[c[0] for c in f]].mean(0) for f in f1[:50]])
    delta = (c1 - c0).mean(0)
    if np.abs((c1 - c0) - delta).max() > 1e-4:
        raise RuntimeError("uv.obj is not a pure translation of model.obj")
    # verify the whole model once
    cc0 = np.array([v0[[c[0] for c in f]].mean(0) for f in f0])
    cc1 = np.array([v1[[c[0] for c in f]].mean(0) for f in f1])
    dev = np.abs((cc1 - cc0) - delta).max()
    if dev > 1e-3:
        raise RuntimeError(f"triangle order/positions changed (max deviation {dev})")
    assets = {a["name"]: a for a in side["assets"]}
    out = {}
    for name, start, count in parts0:
        asset, part = name.split(SEP, 1)
        if asset == "ANCHOR":
            continue
        a = assets[asset]
        part = re.sub(r"\.\d{3}$", "", part)     # Blender added .001 when the group's assets were built in one scene
        off = Matrix.Translation(Vector(a["offset"]))
        grm = Euler([math.radians(x) for x in a["grm"]], "XYZ").to_matrix().to_4x4()
        inv = grm.inverted() @ off.inverted()
        tris = f1[start:start + count]
        remap = {}
        verts, uvs_per_vert, nrm_per_vert = [], [], []
        for tri in tris:
            for (vi, ti, ni) in tri:
                if vi not in remap:
                    remap[vi] = len(verts)
                    p = Vector((v1[vi][0] - delta[0], v1[vi][1] - delta[1], v1[vi][2] - delta[2]))
                    verts.append(inv @ p)
                    uvs_per_vert.append(vt1[ti] if ti >= 0 else (0.0, 0.0))
                    nrm_per_vert.append((inv.to_3x3() @ Vector(vn1[ni])).normalized() if ni >= 0 else Vector((0, 0, 1)))
        me = bpy.data.meshes.new(part)
        me.from_pydata([tuple(p) for p in verts], [], [tuple(remap[c[0]] for c in tri) for tri in tris])
        me.update()
        uv = me.uv_layers.new(name="UVMap")
        for poly in me.polygons:
            for li in poly.loop_indices:
                vi = me.loops[li].vertex_index
                uv.data[li].uv = uvs_per_vert[vi]
        for p in me.polygons:
            p.use_smooth = True
        if not rebuild_normals(me, smooth_angle_for(asset, part)):
            try:
                me.normals_split_custom_set_from_vertices([tuple(n) for n in nrm_per_vert])     # fallback: the normals Graphmat exported
            except Exception:
                pass
        ob = bpy.data.objects.new(f"{asset}{SEP}{part}", me)   # unique in the scene; do_import renames to the clean part name per asset
        (collection or bpy.context.scene.collection).objects.link(ob)
        out.setdefault(asset, []).append((part, ob))
    return out
