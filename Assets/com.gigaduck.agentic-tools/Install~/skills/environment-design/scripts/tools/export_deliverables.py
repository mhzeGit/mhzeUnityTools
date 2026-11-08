"""Collect the durable deliverables of the environment into <env>/ (everything else lives in git-ignored .utmp).

python export_deliverables.py [--unity-shots <dir with hero/walk PNGs>]

Writes
  Models/blend/<Asset>.blend                 the 49 editable Blender sources (untextured geometry, final tag)
  Verification/clay_<module>.jpg             re-imported FBX clay views (3/4, side, top) per asset module, from .utmp/<slug>/renders
  Verification/textured_<group>.jpg          Graphmat UV model + final albedo, unlit views per texture group (gm_sheet.py output)
  Verification/unity_*.jpg                   screenshots of the finished Unity scene (copied from --unity-shots)
  Verification/assets.json                   per asset: vertices, triangles, dimensions, parts (measured on the re-imported FBX)
"""
import glob
import importlib
import json
import os
import shutil
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
ROOT = envcfg.ROOT
UTMP = envcfg.WORK
OUT = envcfg.ENV_DIR
sys.path.insert(0, envcfg.ASSETS_DIR)

MODULES = envcfg.asset_modules()


def asset_names(module):
    """Asset names without importing bpy: read the ASSETS = {...} registry text."""
    text = open(os.path.join(envcfg.ASSETS_DIR, module + ".py"), encoding="utf-8").read()
    start = text.index("ASSETS = {")
    block = text[start:]
    names = []
    import re
    for m in re.finditer(r'"([A-Za-z0-9_]+)"\s*:', block):
        names.append(m.group(1))
    return names


def clay_sheet(module, names, width=420):
    h = int(width * 0.78)
    views = ["q", "side", "top"]
    sheet = Image.new("RGB", (len(views) * (width + 4) + 4, len(names) * (h + 20) + 4), (20, 20, 24))
    d = ImageDraw.Draw(sheet)
    for r, n in enumerate(names):
        y = 2 + r * (h + 20)
        d.text((6, y), f"{n}  (re-imported FBX, clay)  views: 3/4, side, top", fill=(255, 220, 120))
        for c, v in enumerate(views):
            p = os.path.join(UTMP, "renders", f"{n}_final_{v}.png")
            if os.path.exists(p):
                sheet.paste(Image.open(p).convert("RGB").resize((width, h), Image.LANCZOS), (4 + c * (width + 4), y + 16))
    return sheet


def main():
    shots_dir = None
    if "--unity-shots" in sys.argv:
        shots_dir = sys.argv[sys.argv.index("--unity-shots") + 1]
    os.makedirs(os.path.join(OUT, "Models", "blend"), exist_ok=True)
    os.makedirs(os.path.join(OUT, "Verification"), exist_ok=True)
    stats = {}
    n_blend = 0
    for module in MODULES:
        names = asset_names(module)
        for n in names:
            src = os.path.join(UTMP, "blender", f"{n}_final.blend")
            if os.path.exists(src):
                shutil.copy2(src, os.path.join(OUT, "Models", "blend", f"{n}.blend"))
                n_blend += 1
            log = os.path.join(UTMP, "logs", f"{n}_final.json")
            if os.path.exists(log):
                d = json.load(open(log))
                stats[n] = {"module": module, "verts": d["imported_verts"], "tris": d["imported_tris"], "dims_xyz_m": d["dims_xyz_m"], "parts": d["parts"]}
        clay_sheet(module, names).save(os.path.join(OUT, "Verification", f"clay_{module}.jpg"), quality=82)
    json.dump(stats, open(os.path.join(OUT, "Verification", "assets.json"), "w"), indent=1)
    # textured sheets per group (from gm_sheet.py output)
    n_tex = 0
    for p in sorted(glob.glob(os.path.join(UTMP, "gmat", "*", "review", "_sheet_final.png"))):
        group = os.path.basename(os.path.dirname(os.path.dirname(p)))
        Image.open(p).convert("RGB").save(os.path.join(OUT, "Verification", f"textured_{group}.jpg"), quality=82)
        n_tex += 1
    n_shots = 0
    if shots_dir:
        for p in sorted(glob.glob(os.path.join(shots_dir, "*.png"))):
            name = os.path.splitext(os.path.basename(p))[0]
            Image.open(p).convert("RGB").save(os.path.join(OUT, "Verification", f"unity_{name}.jpg"), quality=86)
            n_shots += 1
    total_tris = sum(v["tris"] for v in stats.values())
    print(f"blends {n_blend}, clay sheets {len(MODULES)}, textured sheets {n_tex}, unity shots {n_shots}, assets {len(stats)}, kit triangles {total_tris}")


if __name__ == "__main__":
    main()
