"""TEMPLATE blockout layout: the primitive-shape version of the environment (phase 3). Replace the example room with the real measurements.

Single source of truth for the blockout. Units are metres. Unity axes: +X east (right on the plan), +Y up, +Z north (up on the plan).
Origin: centre of the floor rectangle at floor level (floor top is y = 0).
Run:  python layout_blockout.py [out.json]        the JSON is read by unity/cs/EnvBlockout.cs (python tools: bo.py / tools.py blockout)

Items: {name, shape: box|cylinder|sphere|empty, pos, size, rot, mat, parent, collider}
  box      size = full x/y/z extents
  cylinder size = (diameter, length, diameter): the cylinder axis is local Y (rotate with rot for X/Z runs)
  mat      palette key of EnvBlockout.cs: plaster stone dark iron brick wood wood_light iron_teal iron_dark red cream glass glow (add your own)
  parent   "Group/Sub" path (created on demand) so the Hierarchy mirrors the asset families
Derive EVERY number from the reference (grid overlay on the plan, door/stair anchors on the perspective): see references/02-reference-analysis-and-blockout.md.
"""
import json
import sys

W, D, H = 10.0, 7.0, 4.8          # interior width (x), depth (z), height
WALL_T = 0.35
HX, HZ = W / 2.0, D / 2.0
items = []


def v(x, y, z):
    return {"x": round(float(x), 4), "y": round(float(y), 4), "z": round(float(z), 4)}


def _add(**kw):
    kw.setdefault("parent", "")
    kw.setdefault("rot", v(0, 0, 0))
    kw.setdefault("prefab", "")
    kw.setdefault("collider", 1)
    items.append(kw)


def box(name, pos, size, mat, parent="", rot=(0, 0, 0), collider=1):
    _add(name=name, shape="box", pos=v(*pos), size=v(*size), mat=mat, parent=parent, rot=v(*rot), collider=collider)


def cyl(name, pos, d, h, mat, parent="", rot=(0, 0, 0), collider=1):
    _add(name=name, shape="cylinder", pos=v(*pos), size=v(d, h, d), mat=mat, parent=parent, rot=v(*rot), collider=collider)


def sph(name, pos, d, mat, parent="", yscale=1.0, collider=0):
    _add(name=name, shape="sphere", pos=v(*pos), size=v(d, d * yscale, d), mat=mat, parent=parent, collider=collider)


def build():
    items.clear()
    g = "Structure"
    box("Floor", (0, -0.1, 0), (W + 2 * WALL_T, 0.2, D + 2 * WALL_T), "stone", g)
    box("Ceiling", (0, H + 0.1, 0), (W + 2 * WALL_T, 0.2, D + 2 * WALL_T), "wood", g, collider=0)
    box("Wall_N", (0, H / 2, HZ + WALL_T / 2), (W + 2 * WALL_T, H, WALL_T), "plaster", g)
    box("Wall_S", (0, H / 2, -HZ - WALL_T / 2), (W + 2 * WALL_T, H, WALL_T), "plaster", g)
    box("Wall_E", (HX + WALL_T / 2, H / 2, 0), (WALL_T, H, D), "plaster", g)
    box("Wall_W", (-HX - WALL_T / 2, H / 2, 0), (WALL_T, H, D), "plaster", g)
    g = "Props"
    box("Crate", (-3.5, 0.2, -2.0), (0.5, 0.4, 0.5), "wood_light", g)
    cyl("Pipe", (4.5, H / 2, -2.4), 0.36, H, "iron_teal", g)
    return items


def main(out):
    build()
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"items": items}, f, indent=1)
    print(f"{len(items)} items -> {out}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "layout_blockout.json")
