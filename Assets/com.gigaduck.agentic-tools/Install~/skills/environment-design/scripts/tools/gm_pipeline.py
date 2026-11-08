"""Graphmat texturing pipeline for one texture group (see gm_export.py for the Blender side).

python gm_pipeline.py <group.json> setup      OBJ -> project: embed textures, Auto UV (2048, pad 8), position/normal bake, AO bake  -> uv.gmat
python gm_pipeline.py <group.json> masks      one editable Paint-masked folder per region with a distinct diagnostic colour + actual-model renders
python gm_pipeline.py <group.json> textures   assign textures (triplanar), multiply AO, evaluate, export albedo + UV OBJ + final renders
Everything runs through the standalone background graphmat-cli (no GUI, no MCP).

group.json fields: group, work, gap, assets[{name,module,grm}], uv{padding,thorough}, ao{rays,radius,bias,strength,apply}, textures[{name,file}],
                   regions{Region: {texture, tile (metres per repeat) | scale, rotation, textureRotation, origin, mode (box|cylinder|sphere), color, ao}}
                   cube (side of the bounding cube), cylinder (centre one round asset's axis in the cube)
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
ROOT = envcfg.ROOT
TEXDIR = envcfg.TEX_WORK
_CLI = []


def cli_path():
    """Discovered once: a graphmat-cli build that knows bakeAo/applyAo (see references/graphmat-cli-extensions.patch)."""
    if not _CLI:
        _CLI.append(envcfg.graphmat_cli())
        if not envcfg.graphmat_has_ao(_CLI[0]):
            raise SystemExit("graphmat-cli at " + _CLI[0] + " lacks the bakeAo/applyAo operations: apply references/graphmat-cli-extensions.patch to the Graphmat repo and rebuild")
    return _CLI[0]

COLORS = ["#FF2D55", "#00C7FF", "#FFD60A", "#30D158", "#BF5AF2", "#FF9F0A", "#64D2FF", "#FF375F", "#A2FF00", "#5E5CE6", "#00FFC6", "#FF6B00"]


def cli(*args, cwd=None, timeout=1800):
    res = subprocess.run([cli_path(), *args], cwd=cwd, capture_output=True, text=True, timeout=timeout)
    if res.returncode != 0:
        raise SystemExit(f"graphmat-cli {' '.join(args)} failed ({res.returncode}):\n{res.stdout[-2000:]}\n{res.stderr[-2000:]}")
    return res.stdout


def load(spec_path):
    spec = json.load(open(spec_path))
    spec["_work"] = spec["work"] if os.path.isabs(spec["work"]) else os.path.join(ROOT, spec["work"])
    return spec


def parse_obj_parts(obj_path):
    """Return [(object_name, first_triangle, triangle_count)] in file order (polygons are fan-triangulated: n-2 triangles)."""
    parts = []
    tri = 0
    cur = None
    with open(obj_path, encoding="utf-8") as f:
        for line in f:
            if line.startswith("o "):
                cur = [line[2:].strip(), tri, 0]
                parts.append(cur)
            elif line.startswith("f ") and cur is not None:
                n = len(line.split()) - 1
                cur[2] += n - 2
                tri += n - 2
    return [tuple(p) for p in parts]


def region_of(obj_name):
    base = obj_name.split("@@", 1)[1] if "@@" in obj_name else obj_name
    return base.split("__")[0]


def region_components(spec, meshinfo):
    """Map region -> [component ids] using component seed faces and the OBJ part triangle ranges."""
    parts = parse_obj_parts(os.path.join(spec["_work"], "model.obj"))
    owner = {}
    for name, start, count in parts:
        for t in range(start, start + count):
            owner[t] = region_of(name)
    out = {}
    assigned = {}
    for c in meshinfo["components"]:
        r = owner.get(c["seedFace"])
        if r is None:
            raise SystemExit(f"component {c['id']} seed face {c['seedFace']} not inside any OBJ part")
        out.setdefault(r, []).append(c["id"])
        assigned[r] = assigned.get(r, 0) + c["triangles"]
    expected = {}
    for name, start, count in parts:
        expected[region_of(name)] = expected.get(region_of(name), 0) + count
    bad = {r: (expected[r], assigned.get(r, 0)) for r in expected if expected[r] != assigned.get(r, 0)}
    if bad:
        # Graphmat welds coincident vertices: two parts that touch exactly become ONE component and get the seed face's region
        raise SystemExit(f"regions share welded components (region: expected triangles, assigned): {bad}")
    return out


def write_json(path, obj):
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=1)


def do_setup(spec):
    w = spec["_work"]
    ops = []
    for t in spec.get("textures", []):
        ops.append({"op": "importTexture", "name": t["name"], "path": os.path.relpath(os.path.join(TEXDIR, t["file"]), w).replace("\\", "/")})
    uv = spec.get("uv", {})
    ops.append({"op": "autoUv", "resolution": 2048, "padding": uv.get("padding", 8), "respectHardEdges": uv.get("respectHardEdges", True), "thoroughPacking": uv.get("thorough", True)})
    ops.append({"op": "bakeProjectionMaps"})
    ao = spec.get("ao")
    if ao:
        ops.append({"op": "bakeAo", "rays": ao.get("rays", 64), "radius": ao.get("radius", 0.4), "bias": ao.get("bias", 0.01), "strength": ao.get("strength", 1.0)})
    job = {"create": {"mesh": "model.obj", "resolution": 2048, "name": spec["group"]}, "operations": ops, "export": {"model": "uv_unmasked.obj"}}
    write_json(os.path.join(w, "setup.json"), job)
    print(cli("author", "--job", "setup.json", "--out", "uv.gmat", "--timeout", "1500", "--overwrite", cwd=w, timeout=2000)[-1500:])
    info = cli("mesh-info", "uv.gmat", cwd=w)
    open(os.path.join(w, "meshinfo.json"), "w").write(info)
    d = json.loads(info)
    print("mesh-info: tris", d["triangles"], "components", len(d["components"]), "islands", len(d["islands"]), "bakes", [b["key"] for b in d["bakes"]])


def do_masks(spec):
    w = spec["_work"]
    d = json.load(open(os.path.join(w, "meshinfo.json")))
    comps = region_components(spec, d)
    regs = list(spec["regions"].keys())
    ops = []
    legend = {}
    for i, r in enumerate(regs):
        if r not in comps:
            print("WARNING region without faces:", r)
            continue
        color = spec["regions"][r].get("color", COLORS[i % len(COLORS)])
        legend[r] = {"color": color, "components": len(comps[r])}
        ops.append({"op": "addRegion", "name": r, "color": color, "selection": {"components": comps[r]}})
    ops.append({"op": "evaluate"})
    write_json(os.path.join(w, "masks.json"), {"operations": ops})
    print(cli("author", "uv.gmat", "--job", "masks.json", "--out", "diagnostic.gmat", "--timeout", "1500", "--overwrite", cwd=w, timeout=2000)[-600:])
    write_json(os.path.join(w, "legend.json"), legend)
    views = [("front", 0, 12), ("back", 180, 12), ("left", 90, 12), ("right", -90, 12), ("top", 0, 85), ("under", 0, -70), ("q1", 35, 25), ("q2", 215, 25)]
    for name, yaw, pitch in views:
        cli("render-model", "diagnostic.gmat", "--out", f"diag_{name}.png", "--yaw", str(yaw), "--pitch", str(pitch), "--size", "1100", "--shading", "albedo", "--overwrite", cwd=w)
    # flat diagnostic albedo + UV mesh so the per-asset Blender viewer can show region ownership close up
    cli("extract", "diagnostic.gmat", "--channel", "albedo", "--size", "2048", "--out", "diag_albedo.png", cwd=w)
    cli("export-model", "diagnostic.gmat", "--out", "uv.obj", "--overwrite", cwd=w)
    print("legend", legend)


def do_textures(spec):
    w = spec["_work"]
    d = json.load(open(os.path.join(w, "meshinfo.json")))
    cube = json.load(open(os.path.join(w, "sidecar.json")))["cube"]
    ops = []
    ao = spec.get("ao")
    for r, cfg in spec["regions"].items():
        # Graphmat tiles the image every (mesh AABB extent x Scale) per axis; the export makes the AABB a cube of side `cube`,
        # so `tile` (metres per texture repeat) maps to scale = tile / cube
        scale = cfg["scale"] if "scale" in cfg else cfg.get("tile", 1.0) / cube
        a = {"op": "assignTexture", "folder": r, "texture": cfg["texture"], "projection": cfg.get("projection", "triplanar"), "scale": scale}
        for k in ("rotation", "origin", "textureRotation", "mode"):
            if k in cfg:
                a[k] = cfg[k]
        ops.append(a)
        if ao and cfg.get("ao", ao.get("apply", 1.0)) > 0:
            ops.append({"op": "applyAo", "folder": r, "strength": cfg.get("ao", ao.get("apply", 1.0))})
    ops.append({"op": "evaluate"})
    job = {"operations": ops, "export": {"albedo": "albedo.png", "model": "uv.obj"}}
    write_json(os.path.join(w, "textures.json"), job)
    print(cli("author", "diagnostic.gmat", "--job", "textures.json", "--out", "textured.gmat", "--timeout", "1500", "--overwrite", cwd=w, timeout=2000)[-800:])
    views = [("front", 0, 12), ("back", 180, 12), ("q1", 35, 25), ("q2", 215, 25), ("top", 0, 85)]
    for name, yaw, pitch in views:
        cli("render-model", "textured.gmat", "--out", f"final_{name}.png", "--yaw", str(yaw), "--pitch", str(pitch), "--size", "1100", "--shading", "albedo", "--overwrite", cwd=w)
    print("textured render views written")


if __name__ == "__main__":
    spec = load(sys.argv[1])
    {"setup": do_setup, "masks": do_masks, "textures": do_textures}[sys.argv[2]](spec)
