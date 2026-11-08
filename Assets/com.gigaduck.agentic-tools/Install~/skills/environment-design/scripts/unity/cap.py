#!/usr/bin/env python
"""Capture a still from the ACTIVE Unity scene (blockout and clay stages; for the finished scene use bo.py shots, which refreshes the volume stack).

  python cap.py out.png px py pz tx ty tz fov [w h] [--pp] [--ortho SIZE] [--roll DEG] [--hide a,b,c*]
  --hide hides objects by exact name, or by prefix with a trailing * (walls, ceiling and entrance pieces hide for a top-down or cut-away view)
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import ucs  # noqa: E402

SRC = os.path.join(HERE, "cs", "EnvBlockout.cs")


def capture(png, px, py, pz, tx, ty, tz, fov, w=1672, h=941, pp=False, ortho=0.0, roll=0.0, hide=""):
    cmd = {"cmd": "capture", "png": png, "px": px, "py": py, "pz": pz, "tx": tx, "ty": ty, "tz": tz,
           "fov": fov, "w": w, "h": h, "pp": pp, "ortho": ortho > 0, "orthoSize": ortho, "roll": roll, "hide": hide}
    return ucs.run(SRC, "ClaudeTmp.EnvBlockout.Run", json.dumps(cmd))


def main():
    a = sys.argv[1:]
    if len(a) < 8:
        print(__doc__)
        return
    flags = {"pp": False, "ortho": 0.0, "roll": 0.0, "hide": ""}
    pos = []
    i = 0
    while i < len(a):
        if a[i] == "--pp":
            flags["pp"] = True
        elif a[i] in ("--ortho", "--roll", "--hide"):
            flags[a[i][2:]] = a[i + 1] if a[i] == "--hide" else float(a[i + 1])
            i += 1
        else:
            pos.append(a[i])
        i += 1
    png = pos[0]
    nums = [float(x) for x in pos[1:]]
    px, py, pz, tx, ty, tz, fov = nums[:7]
    w, h = (int(nums[7]), int(nums[8])) if len(nums) >= 9 else (1672, 941)
    print(capture(png, px, py, pz, tx, ty, tz, fov, w, h, flags["pp"], flags["ortho"], flags["roll"], flags["hide"]))


if __name__ == "__main__":
    main()
