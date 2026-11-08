"""Random local search of the room grade against the concept image statistics (hero view): mean colour, luminance percentiles, saturation and
(optionally) hue/saturation/value of named image patches from env.json "grade_patches".

TONE=1 python grade_search.py <rounds> [view]   -> prints the best parameter string after every round; writes .utmp/<slug>/search_best_tone<TONE>.json
The result is a STARTING POINT: look at the best image next to the concept and finish by eye. Never write it over a profile the user has tuned.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import numpy as np  # noqa: E402
from PIL import Image  # noqa: E402

import bo  # noqa: E402
from grade_sweep import stats, REF  # noqa: E402

retry = bo.retry

VIEW = "0|1.65|-3.0|0|1.5|3.0|80|836|470"
SPEC = [  # key, lo, hi, start, sigma
    ("exposure", 0.0, 2.0, 0.9, 0.2),
    ("contrast", -10.0, 40.0, 6.0, 5.0),
    ("sat", -50.0, 0.0, -20.0, 6.0),
    ("hue", -10.0, 14.0, 3.0, 3.0),
    ("temp", -20.0, 15.0, -3.0, 5.0),
    ("tint", -15.0, 15.0, 0.0, 4.0),
    ("bloomI", 0.0, 0.8, 0.4, 0.15),
    ("bloomT", 1.0, 2.0, 1.3, 0.12),
    ("vig", 0.0, 0.6, 0.3, 0.08),
    ("sr", 0.2, 0.8, 0.5, 0.06), ("sg", 0.2, 0.8, 0.5, 0.06), ("sb", 0.2, 0.8, 0.5, 0.06),
    ("hr", 0.3, 1.0, 0.5, 0.06), ("hg", 0.3, 1.0, 0.5, 0.06), ("hb", 0.3, 1.0, 0.5, 0.06),
    ("balance", -50.0, 50.0, 0.0, 12.0),
]
TONE = int(os.environ.get("TONE", "0"))


def to_str(v):
    d = dict(zip([s[0] for s in SPEC], v))
    return (f"tonemap={TONE};hue={d['hue']:.2f};bloomS=0.5;exposure={d['exposure']:.3f};contrast={d['contrast']:.2f};sat={d['sat']:.2f};temp={d['temp']:.2f};tint={d['tint']:.2f};"
            f"bloomI={d['bloomI']:.3f};bloomT={d['bloomT']:.3f};vig={d['vig']:.3f};"
            f"shadowTone={d['sr']:.3f},{d['sg']:.3f},{d['sb']:.3f};highlightTone={d['hr']:.3f},{d['hg']:.3f},{d['hb']:.3f};balance={d['balance']:.2f}")


import colorsys
BOXES = [(p["concept"], p["render"]) for p in envcfg.CFG.get("grade_patches", [])]     # [{"concept": [x0,y0,x1,y1], "render": [x0,y0,x1,y1]} ...] normalised 0..1


def hsv_patch(im, box):
    W, H = im.size
    x0, y0, x1, y1 = [int(v) for v in (box[0] * W, box[1] * H, box[2] * W, box[3] * H)]
    a = np.asarray(im.convert("RGB").crop((x0, y0, x1, y1))).astype(float).reshape(-1, 3).mean(0)
    return colorsys.rgb_to_hsv(*(a / 255))


REFIM = Image.open(REF)


def patch_loss(path):
    im = Image.open(path)
    tot = 0.0
    for rb, ob in BOXES:
        h1, s1, v1 = hsv_patch(REFIM, rb)
        h2, s2, v2 = hsv_patch(im, ob)
        dh = abs(h1 - h2); dh = min(dh, 1 - dh) * 360
        tot += dh / 15.0 + abs(s1 - s2) / 0.15 + abs(v1 - v2) / 0.12
    return tot / len(BOXES) if BOXES else 0.0


def loss(path, ref):
    s = stats(path)
    mean, lum, pct, sat = s
    rm, rl, rp, rs = ref
    return (2.0 * float(np.sum(np.abs(mean - rm) / rm)) + float(np.sum(np.abs(pct - rp) / (rp + 6.0))) + 4.0 * abs(sat - rs) + 1.2 * patch_loss(path))


def main():
    rounds = int(sys.argv[1]) if len(sys.argv) > 1 else 5
    view = sys.argv[2] if len(sys.argv) > 2 else VIEW
    ref = stats(REF)
    rng = np.random.default_rng(7)
    best = np.array([s[3] for s in SPEC], dtype=float)
    lo = np.array([s[1] for s in SPEC]); hi = np.array([s[2] for s in SPEC]); sig = np.array([s[4] for s in SPEC])
    best_loss = None
    for rd in range(rounds):
        scale = 1.0 / (1.0 + 0.5 * rd)
        cands = [best.copy()] if best_loss is None else []
        while len(cands) < 24:
            # perturb a random subset of the parameters
            c = best.copy()
            mask = rng.random(len(c)) < 0.4
            c[mask] += rng.normal(0, 1, mask.sum()) * sig[mask] * scale
            cands.append(np.clip(c, lo, hi))
        lst = os.path.join(envcfg.WORK, "sweep_list.txt")
        open(lst, "w").write("\n".join(f"sr{rd}_{i}|{to_str(c)}" for i, c in enumerate(cands)))
        out = envcfg.SHOTS.replace("\\", "/")
        retry(lambda: bo.call({"op": "GradeSweep", "list": lst, "json": out, "text": view}))
        scored = []
        for i, c in enumerate(cands):
            p = os.path.join(envcfg.SHOTS, f"sr{rd}_{i}.png")
            if os.path.exists(p):
                scored.append((loss(p, ref), i))
        scored.sort()
        l0, i0 = scored[0]
        if best_loss is None or l0 < best_loss:
            best_loss, best = l0, cands[i0].copy()
            keep = os.path.join(envcfg.SHOTS, f"sr{rd}_{i0}.png")
            Image.open(keep).save(os.path.join(envcfg.SHOTS, f"search_best_tone{TONE}.png"))
        print(f"round {rd}: best loss {best_loss:.3f} (this round {l0:.3f})")
        print("   ", to_str(best))
        # tidy intermediate pngs
        for i in range(len(cands)):
            p = os.path.join(envcfg.SHOTS, f"sr{rd}_{i}.png")
            if os.path.exists(p):
                os.remove(p)
    json.dump({"tone": TONE, "params": to_str(best), "loss": best_loss}, open(os.path.join(envcfg.WORK, f"search_best_tone{TONE}.json"), "w"))


if __name__ == "__main__":
    main()
