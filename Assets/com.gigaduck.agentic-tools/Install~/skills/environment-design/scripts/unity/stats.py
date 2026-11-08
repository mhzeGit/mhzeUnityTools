#!/usr/bin/env python
"""Colour / value statistics of renders vs the concept (mean RGB, mean luminance, percentiles, saturation, 5x8 luminance grid).

  python stats.py shot1.png [shot2.png ...]          compares against <env>/reference/<references.persp>
Use it to tune lighting and the volume profile NUMERICALLY (target: same mean colour, same percentiles, same saturation), then LOOK at the images.
"""
import os
import sys

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import envcfg  # noqa: E402

REF = os.path.join(envcfg.REF_DIR, envcfg.CFG.get("references", {}).get("persp", "perspective.png"))


def stats(path):
    im = np.asarray(Image.open(path).convert("RGB").resize((1672, 941))).astype(float)
    lum = 0.2126 * im[..., 0] + 0.7152 * im[..., 1] + 0.0722 * im[..., 2]
    sat = (im.max(2) - im.min(2)) / (im.max(2) + 1e-6)
    H, W = lum.shape
    grid = [[int(lum[r * H // 5:(r + 1) * H // 5, c * W // 8:(c + 1) * W // 8].mean()) for c in range(8)] for r in range(5)]
    return dict(mean_rgb=im.reshape(-1, 3).mean(0).round(1).tolist(), mean_lum=round(lum.mean(), 1), pct=np.percentile(lum, [5, 25, 50, 75, 95, 99]).round(0).tolist(), sat=round(sat.mean(), 3), grid=grid)


if __name__ == "__main__":
    for p in [REF] + sys.argv[1:]:
        s = stats(p)
        print(os.path.basename(p), "rgb", s["mean_rgb"], "lum", s["mean_lum"], "pct", s["pct"], "sat", s["sat"])
        for row in s["grid"]:
            print("   ", row)
