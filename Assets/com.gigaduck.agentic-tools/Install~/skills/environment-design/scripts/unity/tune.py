#!/usr/bin/env python
"""Lighting + volume + capture in one call (look-dev loop).   python tune.py "<Lighting args>" "<Volume args>" <shot name> ["<shot spec>" ...]

Lighting args  key=value;...   sky equator ground (trilight ambient)  fog fogColor fogDensity  lantern lanternRange lanternColor lanternPrefab lanternPos
                               furnace furnaceRange furnaceColor furnacePrefab furnacePos furnacePitch furnaceAngle fill fillColor fillEuler   (see cs/EnvOps2.cs)
Volume args    key=value;...   bloomI bloomT bloomS bloomTint vig vigS exposure contrast sat filter temp tint shadowTone highlightTone balance tonemap (0 none 1 neutral 2 aces)
NOTE: the Volume op also writes the scene's PostProcess volume: never run it on a scene whose profile the USER has tuned (see references/07-lighting-and-post.md).
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import bo  # noqa: E402


def apply(light, volume, name, specs):
    r1 = bo.retry(lambda: bo.call({"op": "Lighting", "text": light}))
    r2 = bo.retry(lambda: bo.call({"op": "Volume", "text": volume}))
    print(r1, "|", r2)
    return bo.retry(lambda: (bo.shots(name, specs), "ok")[1])


if __name__ == "__main__":
    if len(sys.argv) < 4:
        print(__doc__)
        raise SystemExit(1)
    light, volume, name = sys.argv[1], sys.argv[2], sys.argv[3]
    specs = sys.argv[4:] or ["0 1.65 -3.1 0 1.5 3.0 80 pp"]
    print(apply(light, volume, name, specs))
