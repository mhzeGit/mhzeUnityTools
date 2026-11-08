"""Choose the final flat texture per material, make it tile, resample to 2048 and record every step.

python finalize_textures.py            reads textures/selection.json (material -> candidate file in .utmp/<slug>/tex/cands)
Writes  .utmp/<slug>/tex/<material>_2k.png   (2048 x 2048, seamless)
        .utmp/<slug>/tex/_tiles/<material>_before.png / _after.png   (2x2 tiling check, 800 px)
        <env>/textures/finalize-report.json                               (edge metrics before/after + the steps applied)

Post-process (recorded separately from generation): the generator does not return seamless images, so each pick is cross-faded
with a half-offset copy of itself inside a soft band along its four edges (the offset copy is continuous across the wrap, the
original is continuous in the interior), then resampled to 2048 with Lanczos. Colours are not graded.
"""
import json
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
TEX = envcfg.TEX_WORK
CANDS = os.path.join(TEX, "cands")
TILES = os.path.join(TEX, "_tiles")
OUT_DOC = envcfg.TEXT_DIR
BAND = 0.34          # fade length as a fraction of the half-size (0.34 -> 17 % of the width on each side)


def find_candidate(name):
    """A candidate is a picture in tex/cands/ (hand-picked copies) or tex/raw/ (what gen_textures.py saved); png/jpg/webp."""
    for d in (CANDS, os.path.join(TEX, "raw")):
        for ext in (".png", ".jpg", ".jpeg", ".webp"):
            p = os.path.join(d, name + ext)
            if os.path.exists(p):
                return p
    raise SystemExit("candidate not found in tex/cands or tex/raw: " + name)


def smoothstep(a, b, x):
    t = np.clip((x - a) / (b - a), 0.0, 1.0)
    return t * t * (3 - 2 * t)


def make_seamless(img):
    h, w, _ = img.shape
    off = np.roll(img, (h // 2, w // 2), axis=(0, 1))
    x = np.arange(w)
    y = np.arange(h)
    dx = np.minimum(x, w - 1 - x) / (w / 2.0)
    dy = np.minimum(y, h - 1 - y) / (h / 2.0)
    mx = 1.0 - smoothstep(0.0, BAND, dx)
    my = 1.0 - smoothstep(0.0, BAND, dy)
    m = np.maximum(mx[None, :], my[:, None])[..., None]
    return img * (1 - m) + off * m


def edge_metrics(a):
    return {
        "wrap_x": round(float(np.abs(a[:, 0] - a[:, -1]).mean()), 2),
        "adjacent_x": round(float(np.abs(a[:, 1:] - a[:, :-1]).mean()), 2),
        "wrap_y": round(float(np.abs(a[0] - a[-1]).mean()), 2),
        "adjacent_y": round(float(np.abs(a[1:] - a[:-1]).mean()), 2),
    }


def tile_preview(a, path, size=800):
    im = Image.fromarray(np.clip(a, 0, 255).astype(np.uint8))
    t = Image.new("RGB", (im.width * 2, im.height * 2))
    for ix in range(2):
        for iy in range(2):
            t.paste(im, (ix * im.width, iy * im.height))
    t.resize((size, size), Image.LANCZOS).save(path)


def main():
    sel = json.load(open(os.path.join(OUT_DOC, "selection.json")))
    os.makedirs(TILES, exist_ok=True)
    report = {}
    for material, info in sel["materials"].items():
        src = find_candidate(info["candidate"])
        a = np.asarray(Image.open(src).convert("RGB")).astype(np.float64)
        before = edge_metrics(a)
        b = make_seamless(a)
        after = edge_metrics(b)
        tile_preview(a, os.path.join(TILES, material + "_before.png"))
        tile_preview(b, os.path.join(TILES, material + "_after.png"))
        im = Image.fromarray(np.clip(b + 0.5, 0, 255).astype(np.uint8)).resize((2048, 2048), Image.LANCZOS)
        out = os.path.join(TEX, material + "_2k.png")
        im.save(out)
        chk = Image.open(out)
        report[material] = {"candidate": info["candidate"], "source_size": list(a.shape[:2]), "final_size": list(chk.size), "before": before, "after": after,
                            "mean_rgb_before": [round(float(v), 1) for v in a.reshape(-1, 3).mean(0)],
                            "mean_rgb_after": [round(float(v), 1) for v in b.reshape(-1, 3).mean(0)]}
        print(material, info["candidate"], before, "->", after, chk.size)
    json.dump({"steps": ["seamless cross-fade with half-offset copy (band %.2f of half-size)" % BAND, "Lanczos resample to 2048"], "materials": report},
              open(os.path.join(OUT_DOC, "finalize-report.json"), "w"), indent=1)


if __name__ == "__main__":
    main()
