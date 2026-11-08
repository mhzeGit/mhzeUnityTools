"""Placement of every modelled asset in the boiler room (metres; Unity axes: +X east, +Y up, +Z north; origin = floor centre).

Every item is {name, asset, pos, rot | ax, scale, group}.  `rot` is Unity Euler degrees (applied Z, X, Y); `ax` = {"y": [...], "z": [...]} orients the
asset's local Y and Z axes (used for pipes).  The asset frame is: front/visible face toward local -Z, up = +Y, see tools/bq.py.

python layout_room.py [out.json]
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


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def sub(a, b):
    return tuple(x - y for x, y in zip(a, b))


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


def mul(a, k):
    return tuple(x * k for x in a)


def norm(a):
    l = math.sqrt(sum(x * x for x in a))
    return tuple(x / l for x in a)


def tube(name, p0, p1, s=1.0, zhint=(0, 0, 1), group="Pipes"):
    d = sub(p1, p0)
    L = math.sqrt(sum(x * x for x in d))
    if L < 1e-3:
        return
    put_ax(name, "Pipe_Tube_1m", p0, norm(d), zhint, (s, L, s), group)


def elbow(name, corner, h1, h2, s=1.0, group="Pipes"):
    """Elbow whose straight lines meet at 'corner': one pipe arrives along h1 (the elbow starts 0.54 s before the corner), the other leaves along h2."""
    start = sub(corner, mul(h1, 0.54 * s))
    put_ax(name, "Pipe_Elbow90", start, h1, cross(h2, h1), (s, s, s), group)
    return start, add(corner, mul(h2, 0.54 * s))


def band(name, center, axis, s=1.0, group="Pipes"):
    put_ax(name, "Pipe_Band", center, axis, (0, 0, 1) if abs(axis[2]) < 0.9 else (1, 0, 0), (s, s, s), group)


def rot_y_face(face):
    return {"S": 0, "N": 180, "W": 90, "E": -90}[face]     # which way the visible face points (S = toward -Z)


def build():
    items.clear()
    # ------------------------------------------------------------------ floor
    for i, x in enumerate((-3.75, -1.25, 1.25, 3.75)):
        put(f"Floor_S_{i}", "Floor_Panel_A" if i % 2 == 0 else "Floor_Panel_B", (x, 0, -1.915), (0, 180 if i % 2 else 0, 0), group="Floor")
        put(f"Floor_N_{i}", "Floor_Panel_B" if i % 2 == 0 else "Floor_Panel_A", (x, 0, 1.915), (0, 0 if i % 2 else 180, 0), group="Floor")
    put("Floor_Strip_W", "Floor_Strip_W", (-4.125, 0, 0), group="Floor")
    put("Floor_Strip_E", "Floor_Strip_E", (4.9, 0, 0), group="Floor")
    seg = 8.05 / 6
    for k in range(6):
        x = -3.25 + seg * (k + 0.5)
        put(f"Trench_Channel_{k}", "Trench_Channel", (x, 0, 0), (0, 0, 0), (seg / 1.343, 1, 1), group="Floor/Trench")
        put(f"Drain_Grate_{k}", "Drain_Grate", (x, -0.002, 0), (0, 0, 0), (seg / 1.34, 1, 1), group="Floor/Trench")

    # ------------------------------------------------------------------ wall panels
    for i, x in enumerate((-3.6, -1.2, 1.2, 3.6)):
        put(f"Wall_N_{i}", "Wall_Brick_Panel", (x, 0, HZ), (0, 0, 0), group="Walls/North")
    # south wall: two plaster panels on each side of the door and a lintel panel above it
    for sgn in (-1, 1):
        for j, xc in enumerate((4.08, 2.24)):
            put(f"Wall_S_{'W' if sgn < 0 else 'E'}{j}", "Wall_Plaster_Panel", (sgn * xc, 0, -HZ), (0, 180, 0), (0.767, 1, 1), group="Walls/South")
    put("Wall_S_Lintel", "Wall_Plaster_Panel", (0, 2.45, -HZ), (0, 180, 0), (0.62, 0.49, 1), group="Walls/South")
    # east and west walls: three bays each
    for k, zc in enumerate((-2.21, 0.0, 2.21)):
        put(f"Wall_E_{k}", "Wall_Plaster_Panel", (HX, 0, zc), (0, 90, 0), (0.92, 1, 1), group="Walls/East")
        put(f"Wall_W_{k}", "Wall_Plaster_Panel", (-HX, 0, zc), (0, -90, 0), (0.92, 1, 1), group="Walls/West")

    # ------------------------------------------------------------------ posts, rails, braces
    for i, x in enumerate((-4.82, -2.4, 0.0, 2.4, 4.82)):
        put(f"Post_N_{i}", "Post_Timber", (x, 0, HZ - 0.15 + 0.18), group="Frame/Posts")
    for i, x in enumerate((-4.82, -3.16, 3.16, 4.82)):
        put(f"Post_S_{i}", "Post_Timber", (x, 0, -HZ + 0.15 - 0.18), group="Frame/Posts")
    for i, z in enumerate((-1.105, 1.105)):
        put(f"Post_E_{i}", "Post_Timber", (HX - 0.15 + 0.18, 0, z), group="Frame/Posts")
        put(f"Post_W_{i}", "Post_Timber", (-HX + 0.15 - 0.18, 0, z), group="Frame/Posts")
    for sgn in (-1, 1):
        put(f"Pillar_S_{'W' if sgn < 0 else 'E'}", "Pillar_Entrance", (sgn * 1.05, 0, -HZ + 0.38), group="Entrance")

    heights = (1.05, 3.05, 4.62)
    # north bays: spans between post centres
    for i, xc in enumerate((-3.6, -1.2, 1.2, 3.6)):
        for j, y in enumerate(heights):
            put(f"Rail_N_{i}_{j}", "Beam_Timber", (xc, y, HZ - 0.1 + 0.122), (0, 0, 0), (1.2, 1, 1), group="Frame/Rails")
        for sgn in (-1, 1):
            put(f"Brace_N_{i}_{'a' if sgn < 0 else 'b'}", "Brace_Timber", (xc + sgn * 0.78, 3.85, HZ - 0.1 + 0.075), (0, 0, -sgn * 52), (1.0, 1, 1), group="Frame/Braces")
    # south bays
    for i, (xc, ln) in enumerate(((-3.99, 1.66), (-2.24, 1.835), (2.24, 1.835), (3.99, 1.66))):
        for j, y in enumerate(heights):
            put(f"Rail_S_{i}_{j}", "Beam_Timber", (xc, y, -HZ + 0.1 - 0.122), (0, 180, 0), (ln / 2.0, 1, 1), group="Frame/Rails")
        for sgn in (-1, 1):
            put(f"Brace_S_{i}_{'a' if sgn < 0 else 'b'}", "Brace_Timber", (xc + sgn * 0.62, 3.85, -HZ + 0.1 - 0.075), (0, 180, sgn * 52), (0.85, 1, 1), group="Frame/Braces")
    for j, y in enumerate((3.05, 4.62)):
        put(f"Rail_S_Door_{j}", "Beam_Timber", (0, y, -HZ + 0.1 - 0.122), (0, 180, 0), (1.325, 1, 1), group="Frame/Rails")
    # east / west bays
    for i, zc in enumerate((-2.21, 0.0, 2.21)):
        for j, y in enumerate(heights):
            put(f"Rail_E_{i}_{j}", "Beam_Timber", (HX - 0.1 + 0.122, y, zc), (0, -90, 0), (1.105, 1, 1), group="Frame/Rails")
            put(f"Rail_W_{i}_{j}", "Beam_Timber", (-HX + 0.1 - 0.122, y, zc), (0, 90, 0), (1.105, 1, 1), group="Frame/Rails")
        for sgn in (-1, 1):
            put(f"Brace_E_{i}_{'a' if sgn < 0 else 'b'}", "Brace_Timber", (HX - 0.1 + 0.075, 3.85, zc + sgn * 0.7), (0, -90, sgn * 52), (0.95, 1, 1), group="Frame/Braces")
            put(f"Brace_W_{i}_{'a' if sgn < 0 else 'b'}", "Brace_Timber", (-HX + 0.1 - 0.075, 3.85, zc + sgn * 0.7), (0, 90, -sgn * 52), (0.95, 1, 1), group="Frame/Braces")

    # ------------------------------------------------------------------ ceiling
    for i, x in enumerate((-3.75, -1.25, 1.25, 3.75)):
        for j, z in enumerate((-1.75, 1.75)):
            put(f"Ceiling_{i}_{j}", "Ceiling_Bay", (x, H, z), (0, 0, 0), (1, 1.06, 1), group="Ceiling")

    # ------------------------------------------------------------------ entrance + vestibule
    put("Doorframe_Entrance", "Doorframe_Entrance", (0, 0, -HZ), (0, 180, 0), group="Entrance")
    put("Door_Entrance", "Door_Entrance", (0.72, 0, -HZ - 0.05), (0, 100, 0), group="Entrance")
    put("Vestibule_Floor", "Floor_Panel_A", (0, 0, -4.95), (0, 0, 0), (0.8, 1, 0.757), group="Entrance/Vestibule")
    put("Vestibule_Wall_L", "Wall_Plaster_Panel", (-1.0, 0, -4.95), (0, -90, 0), (1.0, 0.54, 1), group="Entrance/Vestibule")
    put("Vestibule_Wall_R", "Wall_Plaster_Panel", (1.0, 0, -4.95), (0, 90, 0), (1.0, 0.54, 1), group="Entrance/Vestibule")
    put("Vestibule_Wall_End", "Wall_Plaster_Panel", (0, 0, -6.15), (0, 180, 0), (0.83, 0.54, 1), group="Entrance/Vestibule")
    put("Vestibule_Door", "Door_Landing", (0, 0, -6.1), (0, 180, 0), group="Entrance/Vestibule")
    put("Vestibule_Ceiling", "Ceiling_Bay", (0, 2.6, -4.95), (0, 0, 0), (0.8, 1, 0.73), group="Entrance/Vestibule")
    put("Vestibule_Lantern", "Lantern_Wall", (-0.98, 1.9, -4.9), (0, -90, 0), group="Entrance/Vestibule")

    # ------------------------------------------------------------------ stairs + landing (NW)
    put("Stair", "Stair_Brick_9", (-4.125, 0, -0.54), group="Stairs")
    put("Stair_Handrail", "Stair_Handrail", (-4.82, 0, -0.54), group="Stairs")
    put("Landing_Block", "Landing_Block", (-4.125, 0, 2.695), group="Stairs")
    put("Landing_Deck", "Landing_Deck", (-4.125, 1.75, 2.695), group="Stairs")
    put("Landing_Rail", "Landing_Rail", (-3.3, 1.8, 1.97), group="Stairs")
    put("Landing_Door", "Door_Landing", (-HX + 0.02, 1.8, 2.55), (0, -90, 0), group="Stairs")
    put("Landing_Shelf", "Shelf_Table", (-4.4, 1.8, 3.23), group="Stairs/Props")
    put("Landing_Bottle_Jug", "Bottle_Jug", (-4.65, 2.586, 3.23), group="Stairs/Props")
    put("Landing_Bottle_Tall", "Bottle_Tall", (-4.4, 2.586, 3.23), (0, 15, 0), group="Stairs/Props")
    put("Landing_Bottle_Stone", "Bottle_Stone", (-4.15, 2.586, 3.2), (0, -30, 0), group="Stairs/Props")
    put("Landing_Crate", "Crate_Lattice", (-3.65, 1.8, 3.12), (0, 8, 0), group="Stairs/Props")
    put("Lantern_Landing", "Lantern_Wall", (-4.1, 3.2, HZ), group="Lanterns")

    # ------------------------------------------------------------------ boilers (fronts aligned near z = 0.52)
    spec = {"Left": dict(x=-1.96, z=1.42, size="Small", plinth="Plinth_Small", ph=0.45, R=0.75, s=0.70, top=2.61, body_mid=1.17),
            "Center": dict(x=0.52, z=1.92, size="Large", plinth="Plinth_Large", ph=0.50, R=1.25, s=1.00, top=4.03, body_mid=1.67),
            "Right": dict(x=3.24, z=1.69, size="Medium", plinth="Plinth_Medium", ph=0.50, R=1.00, s=0.82, top=3.335, body_mid=1.43)}
    az = math.radians(-28)
    for key, b in spec.items():
        put(f"Plinth_{key}", b["plinth"], (b["x"], 0, b["z"]), group=f"Boilers/{key}")
        put(f"Boiler_{key}", f"Boiler_{b['size']}", (b["x"], b["ph"], b["z"]), group=f"Boilers/{key}")
        zg = b["z"] - b["R"] + 0.005
        gy = {"Small": 1.35, "Large": 2.14, "Medium": 1.78}[b["size"]]
        put(f"Gauge_{key}", "Gauge", (b["x"] - 0.02, gy, zg), (0, 0, 0), (b["s"], b["s"], b["s"]), group=f"Boilers/{key}")
        out = (math.cos(az), 0.0, math.sin(az))
        base = (b["x"] + b["R"] * out[0], b["body_mid"], b["z"] + b["R"] * out[2])
        ry = -62
        put(f"ValveBody_{key}", "ValveBody", base, (0, ry, 0), (b["s"], b["s"], b["s"]), group=f"Boilers/{key}")
        wheel = add(base, mul(out, 0.33 * b["s"]))
        put(f"ValveWheel_{key}", "ValveWheel", wheel, (0, ry, 0), (b["s"], b["s"], b["s"]), group=f"Boilers/{key}")

    # ------------------------------------------------------------------ pipes
    YR, ZR = 3.9, 3.0                      # run height and distance (centre line) from the north wall plane
    # west run (flows east-west) with a riser to the ceiling at its west end
    st, en = elbow("Elbow_W_up", (-2.9, YR, ZR), (1, 0, 0), (0, 1, 0))
    tube("Run_W", (-2.36, YR, ZR), (-1.32, YR, ZR))
    tube("Run_W_Up", en, (en[0], 5.0, en[2]), zhint=(0, 0, 1))
    put("CeilingCollar_W", "Pipe_CeilingCollar", (-2.9, H, ZR), (0, 0, 0), group="Pipes")
    # branch from the west run into the centre boiler's west shoulder
    elbow("Elbow_CL_a", (-0.78, YR, ZR), (1, 0, 0), (0, 0, -1))
    tube("Run_W_b", (-1.32, YR, ZR), (-0.78 - 0.54, YR, ZR))
    tube("Branch_CL", (-0.78, YR, ZR - 0.54), (-0.78, YR, 1.92 + 0.54), zhint=(0, 1, 0))
    st2, en2 = elbow("Elbow_CL_b", (-0.78, YR, 1.92), (0, 0, -1), (0, -1, 0))
    tube("Drop_CL", en2, (-0.78, 3.0, 1.92), zhint=(0, 0, 1))
    # east run
    elbow("Elbow_CR_a", (1.82, YR, ZR), (-1, 0, 0), (0, 0, -1))
    tube("Run_E", (2.36, YR, ZR), (4.75 - 0.54, YR, ZR))
    tube("Branch_CR", (1.82, YR, ZR - 0.54), (1.82, YR, 1.92 + 0.54), zhint=(0, 1, 0))
    st3, en3 = elbow("Elbow_CR_b", (1.82, YR, 1.92), (0, 0, -1), (0, -1, 0))
    tube("Drop_CR", en3, (1.82, 3.0, 1.92), zhint=(0, 0, 1))
    # east wall pipe: south along the wall, then down to the floor
    elbow("Elbow_E_a", (4.75, YR, ZR), (1, 0, 0), (0, 0, -1))
    tube("Run_E_South", (4.75, YR, ZR - 0.54), (4.75, YR, 0.55 + 0.54), zhint=(1, 0, 0))
    st4, en4 = elbow("Elbow_E_b", (4.75, YR, 0.55), (0, 0, -1), (0, -1, 0))
    tube("Drop_E", en4, (4.75, 0.3, 0.55), zhint=(1, 0, 0))
    put("BaseFlange_E", "Pipe_BaseFlange", (4.75, 0, 0.55), (0, 0, 0), group="Pipes")
    # left boiler riser to the run
    elbow("Elbow_L", (-1.96, YR, 1.42), (0, 1, 0), (0, 0, 1))
    tube("Riser_L", (-1.96, 2.61, 1.42), (-1.96, YR - 0.54, 1.42), zhint=(0, 0, 1))
    tube("Feed_L", (-1.96, YR, 1.42 + 0.54), (-1.96, YR, ZR), zhint=(1, 0, 0))
    # right boiler riser to the run
    elbow("Elbow_R", (3.24, YR, 1.69), (0, 1, 0), (0, 0, 1))
    tube("Riser_R", (3.24, 3.335, 1.69), (3.24, YR - 0.54, 1.69), zhint=(0, 0, 1))
    tube("Feed_R", (3.24, YR, 1.69 + 0.54), (3.24, YR, ZR), zhint=(1, 0, 0))
    # chimney through the ceiling
    tube("Chimney", (0.52, 4.03, 1.92), (0.52, 5.3, 1.92), 1.85, (0, 0, 1))
    put("CeilingCollar_Chimney", "Pipe_CeilingCollar", (0.52, H, 1.92), (0, 0, 0), (1.85, 1.85, 1.85), group="Pipes")
    band("Band_Chimney_a", (0.52, 4.35, 1.92), (0, 1, 0), 1.85)
    band("Band_Chimney_b", (0.52, 4.62, 1.92), (0, 1, 0), 1.85)
    # bands on the runs
    for i, x in enumerate((-2.0, -1.0, 3.0, 3.9)):
        band(f"Band_Run_{i}", (x, YR, ZR), (1, 0, 0))
        put(f"Bracket_Run_{i}", "Pipe_Bracket", (x + 0.35, YR, ZR), (0, 0, 0), (1, 1.5, 1), group="Pipes")
    band("Band_Branch_CL", (-0.78, YR, 2.4), (0, 0, 1))
    band("Band_Branch_CR", (1.82, YR, 2.4), (0, 0, 1))
    band("Band_Drop_CL", (-0.78, 3.18, 1.92), (0, 1, 0))
    band("Band_Drop_CR", (1.82, 3.18, 1.92), (0, 1, 0))
    band("Band_Riser_L", (-1.96, 3.2, 1.42), (0, 1, 0))
    band("Band_Feed_L", (-1.96, YR, 2.4), (0, 0, 1))
    band("Band_Feed_R", (3.24, YR, 2.4), (0, 0, 1))
    band("Band_E_Drop", (4.75, 2.2, 0.55), (0, 1, 0))
    band("Band_E_South", (4.75, YR, 1.6), (0, 0, 1))

    # corner pipes with valves
    sw, se = (-4.4, -1.95), (4.45, -0.95)     # SW pipe moved 0.95 m south of the concept plan so the foot of the stairs can be reached on foot
    put("SW_Pipe_Flange", "Pipe_BaseFlange", (sw[0], 0, sw[1]), (0, 0, 0), (1.8, 1.8, 1.8), group="Pipes/SW")
    tube("SW_Pipe", (sw[0], 0.54, sw[1]), (sw[0], 5.0, sw[1]), 1.8, (0, 0, 1), "Pipes/SW")
    put("SW_CeilingCollar", "Pipe_CeilingCollar", (sw[0], H, sw[1]), (0, 0, 0), (1.8, 1.8, 1.8), group="Pipes/SW")
    band("SW_Band_a", (sw[0], 3.5, sw[1]), (0, 1, 0), 1.8, group="Pipes/SW")
    band("SW_Band_b", (sw[0], 2.7, sw[1]), (0, 1, 0), 1.8, group="Pipes/SW")
    put("SW_ValveBody", "ValveBody", (sw[0] + 0.36, 1.62, sw[1]), (0, -90, 0), (1.15, 1.15, 1.15), group="Pipes/SW")
    put("SW_ValveWheel", "ValveWheel", (sw[0] + 0.36 + 0.38, 1.62, sw[1]), (0, -90, 0), (1.15, 1.15, 1.15), group="Pipes/SW")
    put("SE_Footing", "Brick_Footing", (4.4, 0, -0.95), group="Pipes/SE")
    put("SE_Pipe_Flange", "Pipe_BaseFlange", (se[0], 0.46, se[1]), (0, 0, 0), (1.55, 1.55, 1.55), group="Pipes/SE")
    tube("SE_Pipe", (se[0], 0.46 + 0.46, se[1]), (se[0], 5.0, se[1]), 1.6, (0, 0, 1), "Pipes/SE")
    put("SE_CeilingCollar", "Pipe_CeilingCollar", (se[0], H, se[1]), (0, 0, 0), (1.6, 1.6, 1.6), group="Pipes/SE")
    band("SE_Band_a", (se[0], 3.4, se[1]), (0, 1, 0), 1.6, group="Pipes/SE")
    band("SE_Band_b", (se[0], 2.4, se[1]), (0, 1, 0), 1.6, group="Pipes/SE")
    put("SE_ValveBody", "ValveBody", (se[0] - 0.32, 1.25, se[1]), (0, 90, 0), (1.0, 1.0, 1.0), group="Pipes/SE")
    put("SE_ValveWheel", "ValveWheel", (se[0] - 0.32 - 0.34, 1.25, se[1]), (0, 90, 0), (1.0, 1.0, 1.0), group="Pipes/SE")

    # ------------------------------------------------------------------ props (SW corner)
    put("Crate_Xbrace", "Crate_Xbrace", (-2.95, 0, -2.2), (0, 8, 0), group="Props")
    put("Crate_Large", "Crate_Large", (-3.95, 0, -3.0), (0, -6, 0), group="Props")
    put("Crate_Small", "Crate_Small", (-3.9, 0.728, -3.0), (0, 20, 0), group="Props")       # stacked on the large crate
    put("Bucket", "Bucket", (-2.15, 0, -3.05), group="Props")
    put("Broom", "BroomHandle", (-2.45, 0, -3.15), (8, 0, 10), group="Props")
    # ------------------------------------------------------------------ lanterns
    put("Lantern_West", "Lantern_Wall", (-HX, 2.5, -0.1), (0, -90, 0), group="Lanterns")
    put("Lantern_North", "Lantern_Wall", (2.6, 3.0, HZ), group="Lanterns")
    put("Lantern_South", "Lantern_Wall", (-1.9, 2.6, -HZ), (0, 180, 0), group="Lanterns")
    put("Lantern_East", "Lantern_Wall", (HX, 2.6, -0.4), (0, 90, 0), group="Lanterns")
    return items


if __name__ == "__main__":
    build()
    out = sys.argv[1] if len(sys.argv) > 1 else "room_layout.json"
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"items": items}, f, indent=1)
    print(len(items), "items ->", out)
