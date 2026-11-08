"""Orientation / scale test: export asymmetric markers to FBX and read them back in Unity."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import bq
from mathutils import Matrix

import envcfg  # noqa: E402
OUT = os.path.join(envcfg.WORK, "fbx")
os.makedirs(OUT, exist_ok=True)

bq.clean_scene()
parts = {}
for name, size, pos in (("Core", (1.0, 1.0, 1.0), (0, 0, 0.5)),
                        ("E", (0.2, 0.2, 0.2), (1.5, 0, 0.5)),     # east  (+X)
                        ("N", (0.3, 0.3, 0.3), (0, 1.5, 0.5)),     # north (+Y in Blender)
                        ("U", (0.4, 0.4, 0.4), (0, 0, 2.0))):      # up    (+Z in Blender)
    bm = bq.new_bm()
    bq.box(bm, size, center=(0, 0, 0), bevel=0.0)
    parts[name] = bq.obj_from_bm(name, bm, location=pos)

objs = list(parts.values())
print("stats", bq.mesh_stats(objs))
bq.export_fbx(os.path.join(OUT, "Marker_rot180.fbx"), objs)
bq.export_fbx(os.path.join(OUT, "Marker_raw.fbx"), objs, export_matrix=Matrix.Identity(4))
print("exported to", OUT)
