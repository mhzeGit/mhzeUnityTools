"""Fit a pinhole camera (position, yaw, pitch, vertical FOV) so that landmark 3D points of the assembled room project onto
their pixel positions in the concept perspective image. Needs scipy. Treat a fit outside the room as a sign the concept perspective is
inconsistent with the plan: keep the room right and the camera approximate."""
import math
import numpy as np
from scipy.optimize import least_squares

import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
import envcfg  # noqa: E402

# landmarks: <env>/layout/landmarks.json = [["gauge_C", [x, y, z], [px, py]], ...]  3D point in room metres, pixel in the concept perspective image
LM = os.path.join(envcfg.LAYOUT_DIR, "landmarks.json")
from PIL import Image  # noqa: E402
_ref = os.path.join(envcfg.REF_DIR, envcfg.CFG.get("references", {}).get("persp", "perspective.png"))
W, H = [float(v) for v in Image.open(_ref).size]
pts = [(n, tuple(p3), tuple(p2)) for n, p3, p2 in json.load(open(LM))]


def project(p, params):
    cx, cy, cz, yaw, pitch, fov = params
    fy = (H / 2) / math.tan(math.radians(fov) / 2)
    fx = fy
    d = np.array(p, dtype=float) - np.array([cx, cy, cz])
    ya, pa = math.radians(yaw), math.radians(pitch)
    # forward = (sin yaw cos pitch, sin pitch, cos yaw cos pitch) ; yaw about +Y from +Z toward +X
    fwd = np.array([math.sin(ya) * math.cos(pa), math.sin(pa), math.cos(ya) * math.cos(pa)])
    right = np.array([math.cos(ya), 0.0, -math.sin(ya)])
    up = np.cross(fwd, right)
    # Unity is left-handed: right = up x fwd ; use explicit basis
    up = np.array([-math.sin(ya) * math.sin(pa), math.cos(pa), -math.cos(ya) * math.sin(pa)])
    z = d @ fwd
    x = d @ right
    y = d @ up
    if z < 0.05:
        return (1e5, 1e5)
    return (W / 2 + fx * x / z, H / 2 - fy * y / z)


def resid(params):
    r = []
    for name, p3, p2 in pts:
        u, v = project(p3, params)
        r += [u - p2[0], v - p2[1]]
    return np.array(r)


best = None
rng = np.random.default_rng(1)
for it in range(300):
    x0 = [rng.uniform(-3, 2), rng.uniform(1.4, 1.9), rng.uniform(-6.5, -3.0), rng.uniform(-10, 30), rng.uniform(-10, 8), rng.uniform(50, 85)]
    try:
        s = least_squares(resid, x0, loss="soft_l1", f_scale=15)
    except Exception:
        continue
    if best is None or s.cost < best.cost:
        best = s
p = best.x
print("params cx,cy,cz,yaw,pitch,fov =", np.round(p, 3), "cost", round(best.cost, 1))
for name, p3, p2 in pts:
    u, v = project(p3, p)
    print(f"{name:9s} target {p2} -> {int(u)},{int(v)}  err {math.hypot(u - p2[0], v - p2[1]):.0f}px")
