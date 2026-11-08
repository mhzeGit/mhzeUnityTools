"""TEMPLATE final placement of every modelled asset (phase 5). One item per placed instance of a prefab.

Units: metres. Unity axes: +X east, +Y up, +Z north; origin = floor centre, floor top y = 0.
Item = {name, asset, pos, rot | ax, scale, group}.
  asset  asset name = key of an ASSETS registry = prefab <prefix><asset>.prefab
  rot    Unity Euler degrees (applied Z, X, Y)
  ax     put_ax(): orient the asset's local Y and Z axes (pipes, beams between two points); see tube() / elbow()
  group  "Folder/Sub" path in the Hierarchy (floor, walls, boilers, pipes ...): one group per family, one chapter-like root per environment
The asset frame is: visible/front face toward local -Z, up = +Y (see scripts/tools/bq.py export_fbx).

python layout_room.py [out.json]       then:  python unity/tools.py assemble
"""
import json
import math
import sys

HX, HZ, H = 5.0, 3.5, 4.8
items = []


def v3(a):
    return {"x": round(float(a[0]), 4), "y": round(float(a[1]), 4), "z": round(float(a[2]), 4)}


def put(name, asset, pos, rot=(0, 0, 0), scale=(1, 1, 1), group=""):
    items.append({"name": name, "asset": asset, "pos": v3(pos), "rot": v3(rot), "scale": v3(scale), "group": group, "ay": v3((0, 0, 0)), "az": v3((0, 0, 0)), "useAx": 0})


def put_ax(name, asset, pos, y_axis, z_axis, scale=(1, 1, 1), group=""):
    items.append({"name": name, "asset": asset, "pos": v3(pos), "rot": v3((0, 0, 0)), "scale": v3(scale), "group": group, "ay": v3(y_axis), "az": v3(z_axis), "useAx": 1})


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def norm(a):
    l = math.sqrt(sum(x * x for x in a))
    return tuple(x / l for x in a)


def tube(name, p0, p1, s=1.0, zhint=(0, 0, 1), group="Pipes"):
    """A 1 m pipe asset stretched between two points: scale Y = length, scale X/Z = pipe thickness."""
    d = sub(p1, p0)
    L = math.sqrt(sum(x * x for x in d))
    if L > 1e-3:
        put_ax(name, "Pipe_Test", p0, norm(d), zhint, (s, L, s), group)


def build():
    items.clear()
    for i, x in enumerate((-3.6, -1.2, 1.2, 3.6)):                       # four 2.4 m wall panels along the north wall
        put(f"Wall_N_{i}", "Wall_Brick_Panel", (x, 0, HZ), (0, 0, 0), group="Walls/North")
    put("Crate_0", "Crate_Test", (-3.5, 0, -2.0), (0, 8, 0), group="Props")
    tube("Pipe_Run_0", (4.5, 0, -2.4), (4.5, H, -2.4), group="Pipes")
    return items


def main(out):
    build()
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"items": items}, f, indent=1)
    print(f"{len(items)} items -> {out}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "layout_room.json")
