"""Blender viewer for Graphmat results: import the UV OBJ + a flat albedo and render per-asset unlit views (the actual UV mesh with the actual map).

blender -b -P gm_review.py -- <group.json> <albedo.png> <tag> [uv.obj]
Writes <work>/review/<Asset>_<tag>_{q,qb,front,side,top}.png (900x700, unlit) and a contact sheet per asset.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
ROOT = envcfg.ROOT
sys.path.insert(0, envcfg.ASSETS_DIR)
import bpy  # noqa: E402
import bq  # noqa: E402
from mathutils import Vector  # noqa: E402

SEP = "@@"


def main():
    argv = sys.argv[sys.argv.index("--") + 1:]
    spec = json.load(open(argv[0]))
    albedo, tag = argv[1], argv[2]
    uv_obj = argv[3] if len(argv) > 3 else "uv.obj"
    work = spec["work"] if os.path.isabs(spec["work"]) else os.path.join(ROOT, spec["work"])
    out = os.path.join(work, "review")
    os.makedirs(out, exist_ok=True)
    bq.clean_scene()
    bq.TEXTURED = True
    import gm_obj
    built = gm_obj.build(work)
    # unlit material that shows the albedo exactly
    mat = bpy.data.materials.new("review")
    mat.use_nodes = True
    nt = mat.node_tree
    for n in list(nt.nodes):
        nt.nodes.remove(n)
    tex = nt.nodes.new("ShaderNodeTexImage")
    img = bpy.data.images.load(os.path.join(work, albedo) if not os.path.isabs(albedo) else albedo)
    img.colorspace_settings.name = 'sRGB'
    tex.image = img
    tex.interpolation = 'Linear'
    emis = nt.nodes.new("ShaderNodeEmission")
    outn = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(tex.outputs["Color"], emis.inputs["Color"])
    nt.links.new(emis.outputs["Emission"], outn.inputs["Surface"])
    objs = []
    by_asset = {}
    for asset, parts in built.items():
        for (_, o) in parts:
            o.data.materials.clear()
            o.data.materials.append(mat)
            objs.append(o)
            by_asset.setdefault(asset, []).append(o)
    s = bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    sh = s.display.shading
    sh.light = 'FLAT'
    sh.color_type = 'TEXTURE'
    sh.show_cavity = False
    sh.show_object_outline = True
    sh.object_outline_color = (0.0, 0.0, 0.0)
    s.view_settings.view_transform = 'Standard'
    s.world = s.world or bpy.data.worlds.new("w")
    s.world.color = (0.12, 0.13, 0.15)
    for asset, group in by_asset.items():
        mn, mx = bq.bounds(group)
        # hide everything else
        for o in objs:
            o.hide_render = o not in group
        paths = {}
        for k in ("q", "qb", "front", "side", "top"):
            paths[k] = bq.render_view(os.path.join(out, f"{asset}_{tag}_{k}.png"), mn, mx, k, 900, 700)
        print("GM_REVIEW", asset, json.dumps({k: os.path.basename(v) for k, v in paths.items()}))


main()
