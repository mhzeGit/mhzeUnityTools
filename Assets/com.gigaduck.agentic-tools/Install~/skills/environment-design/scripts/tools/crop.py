"""Cut reference crops (the reference for every asset, texture and review sheet is a crop, never the whole concept).

python crop.py <reference> <x0> <y0> <x1> <y1> <name> [scale]

  <reference>  file inside <env>/reference/ (extension optional), or an alias from env.json "references"
               e.g. perspective   or   plan.png   or   persp (alias)
  x0 y0 x1 y1  pixel box in the ORIGINAL image (use grid.py to read coordinates off a grid overlay)
  name         output name, saved as <env>/reference/crops/<name>.png
  scale        optional; default upscales so the longest side is ~900 px (never below 1.0)
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402


def resolve(ref):
    ref = envcfg.CFG.get("references", {}).get(ref, ref)
    cands = [ref] if os.path.splitext(ref)[1] else [ref + e for e in (".png", ".jpg", ".jpeg", ".webp")]
    for c in cands:
        p = os.path.join(envcfg.REF_DIR, c)
        if os.path.exists(p):
            return p
    raise SystemExit(f"reference image not found in {envcfg.REF_DIR}: {ref}")


def main():
    if len(sys.argv) < 7:
        print(__doc__)
        raise SystemExit(1)
    src = resolve(sys.argv[1])
    x0, y0, x1, y1 = map(int, sys.argv[2:6])
    name = sys.argv[6]
    im = Image.open(src).convert("RGB").crop((x0, y0, x1, y1))
    sc = float(sys.argv[7]) if len(sys.argv) > 7 else max(1.0, 900.0 / max(im.size))
    im = im.resize((int(im.size[0] * sc), int(im.size[1] * sc)), Image.LANCZOS)
    out_dir = os.path.join(envcfg.REF_DIR, "crops")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, name + ".png")
    im.save(out)
    print(out, im.size)


if __name__ == "__main__":
    main()
