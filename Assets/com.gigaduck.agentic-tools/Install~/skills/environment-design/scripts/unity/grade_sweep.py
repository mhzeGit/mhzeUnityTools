"""Render several post-processing grades on a COPY of the room profile (nothing is saved, the scene volume is swapped for one second per image) and
print statistics vs the concept.

python grade_sweep.py <tag> "<view px|py|pz|tx|ty|tz|fov|w|h>" name1="k=v;k=v" name2="k=v;..."   -> .utmp/<slug>/shots/<tag>_<name>.png + a table
keys: tonemap hue exposure contrast sat temp tint bloomI bloomT bloomS vig shadowTone highlightTone balance   (see cs/EnvOps6.cs ApplyGrade)
Do NOT run this while the user is tuning the same volume in the editor (the swap is visible and a save in that second would lose the reference)."""
import os
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import numpy as np  # noqa: E402
from PIL import Image  # noqa: E402

import bo  # noqa: E402

retry = bo.retry
REF = os.path.join(envcfg.REF_DIR, envcfg.CFG.get("references", {}).get("persp", "perspective.png"))


def stats(path):
    im = np.asarray(Image.open(path).convert("RGB").resize((836, 470))).astype(float)
    lum = 0.2126 * im[..., 0] + 0.7152 * im[..., 1] + 0.0722 * im[..., 2]
    sat = (im.max(2) - im.min(2)) / (im.max(2) + 1e-6)
    return im.reshape(-1, 3).mean(0), lum.mean(), np.percentile(lum, [5, 25, 50, 75, 95, 99]), sat.mean()


def main():
    tag = sys.argv[1]
    view = sys.argv[2]
    cands = []
    for a in sys.argv[3:]:
        k, v = a.split("=", 1)
        cands.append((f"{tag}_{k}", v))
    lst = os.path.join(envcfg.WORK, "sweep_list.txt")
    open(lst, "w").write("\n".join(f"{n}|{v}" for n, v in cands))
    out = envcfg.SHOTS.replace("\\", "/")
    res = retry(lambda: bo.call({"op": "GradeSweep", "list": lst, "json": out, "text": view}))
    print(res)
    r = stats(REF)
    print(f"{'concept':28s} rgb {r[0].round(1)} lum {r[1]:.1f} pct {r[2].round(0)} sat {r[3]:.3f}")
    for n, _ in cands:
        p = os.path.join(envcfg.SHOTS, n + ".png")
        if not os.path.exists(p):
            print(n, "missing")
            continue
        s = stats(p)
        print(f"{n:28s} rgb {s[0].round(1)} lum {s[1]:.1f} pct {s[2].round(0)} sat {s[3]:.3f}")


if __name__ == "__main__":
    main()
