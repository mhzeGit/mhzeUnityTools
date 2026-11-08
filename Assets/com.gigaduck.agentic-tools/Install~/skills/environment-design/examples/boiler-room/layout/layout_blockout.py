"""Blockout layout for "The House's Iron Heart" boiler room.

Single source of truth for the primitive blockout (and, later, for where each modular asset sits).
Units are metres. Unity axes: +X east (right on the map), +Y up, +Z north (up on the map).
Origin: centre of the floor rectangle at floor level (floor top is y = 0).

Run:  python layout_blockout.py [out.json]
The JSON is read by the editor-only blockout builder.
"""
import json
import math
import sys

# ---------------------------------------------------------------- room envelope
W = 10.0           # interior width  (x from -5 to +5)
D = 6.6            # interior depth  (z from -3.3 to +3.3)
H = 4.8            # floor to ceiling underside
WALL_T = 0.35      # wall thickness (outside the interior)
HX, HZ = W / 2.0, D / 2.0

# trench (drain) just south of the boilers
TRENCH_X0, TRENCH_X1 = -3.25, 4.8
TRENCH_Z0, TRENCH_Z1 = -0.33, 0.33
TRENCH_DEPTH = 0.45

# stairs (west wall, climbing north to the landing)
STAIR_X0, STAIR_X1 = -4.70, -3.55
STAIR_Z0 = -0.54
STAIR_STEPS = 9
STAIR_RISE = 0.20
STAIR_TREAD = 0.27
LANDING_Y = STAIR_STEPS * STAIR_RISE          # 1.8
STAIR_Z1 = STAIR_Z0 + STAIR_STEPS * STAIR_TREAD  # 1.89
LANDING_X0, LANDING_X1 = -HX, -3.25

# boilers: (name, cx, cz, plinth_r, shell_r, body_h)
PLINTH_H = 0.55
BOILERS = [
    ("Left",   -1.96, 1.54, 1.00, 0.85, 1.50),
    ("Center",  0.52, 1.72, 1.30, 1.25, 2.35),
    ("Right",   3.24, 1.58, 1.15, 1.05, 1.90),
]

items = []


def _add(**kw):
    kw.setdefault("parent", "")
    kw.setdefault("rot", [0, 0, 0])
    kw.setdefault("prefab", "")
    kw.setdefault("collider", 1)
    items.append(kw)


def v(x, y, z):
    return {"x": round(float(x), 4), "y": round(float(y), 4), "z": round(float(z), 4)}


def box(name, pos, size, mat, parent="", rot=(0, 0, 0), collider=1):
    _add(name=name, shape="box", pos=v(*pos), size=v(*size), mat=mat, parent=parent, rot=v(*rot), collider=collider)


def cyl(name, pos, d, h, mat, parent="", rot=(0, 0, 0), collider=1):
    """Cylinder whose axis is local Y (rotate with rot for X/Z runs). d = diameter, h = length."""
    _add(name=name, shape="cylinder", pos=v(*pos), size=v(d, h, d), mat=mat, parent=parent, rot=v(*rot), collider=collider)


def sph(name, pos, d, mat, parent="", yscale=1.0, collider=0):
    _add(name=name, shape="sphere", pos=v(*pos), size=v(d, d * yscale, d), mat=mat, parent=parent, collider=collider)


def empty(name, pos, parent=""):
    _add(name=name, shape="empty", pos=v(*pos), size=v(1, 1, 1), mat="", parent=parent, collider=0)


def pipe_x(name, x0, x1, y, z, d, mat, parent):
    cyl(name, ((x0 + x1) / 2, y, z), d, abs(x1 - x0), mat, parent, rot=(0, 0, 90), collider=0)


def pipe_z(name, z0, z1, x, y, d, mat, parent):
    cyl(name, (x, y, (z0 + z1) / 2), d, abs(z1 - z0), mat, parent, rot=(90, 0, 0), collider=0)


def pipe_y(name, y0, y1, x, z, d, mat, parent, collider=0):
    cyl(name, (x, (y0 + y1) / 2, z), d, abs(y1 - y0), mat, parent, collider=collider)


def build():
    items.clear()

    # ------------------------------------------------------------ floor & trench
    g = "Floor"
    box("Floor_South", (0, -0.15, (-HZ + TRENCH_Z0) / 2), (W, 0.3, TRENCH_Z0 + HZ), "stone", g)
    box("Floor_North", (0, -0.15, (HZ + TRENCH_Z1) / 2), (W, 0.3, HZ - TRENCH_Z1), "stone", g)
    box("Floor_TrenchWestEnd", ((-HX + TRENCH_X0) / 2, -0.15, 0), (TRENCH_X0 + HX, 0.3, TRENCH_Z1 - TRENCH_Z0), "stone", g)
    box("Floor_TrenchEastEnd", ((TRENCH_X1 + HX) / 2, -0.15, 0), (HX - TRENCH_X1, 0.3, TRENCH_Z1 - TRENCH_Z0), "stone", g)
    box("Trench_Bottom", ((TRENCH_X0 + TRENCH_X1) / 2, -TRENCH_DEPTH, 0), (TRENCH_X1 - TRENCH_X0, 0.1, TRENCH_Z1 - TRENCH_Z0), "dark", g)
    box("Trench_Grate", ((TRENCH_X0 + TRENCH_X1) / 2, -0.04, 0), (TRENCH_X1 - TRENCH_X0, 0.06, TRENCH_Z1 - TRENCH_Z0), "iron", g)
    box("Floor_Underside", (0, -0.6, 0), (W + 1.4, 0.6, D + 1.4), "dark", g, collider=0)

    # ------------------------------------------------------------ walls, ceiling, posts
    g = "Walls"
    box("Wall_West", (-HX - WALL_T / 2, H / 2, 0), (WALL_T, H, D + 2 * WALL_T), "plaster", g)
    box("Wall_East", (HX + WALL_T / 2, H / 2, 0), (WALL_T, H, D + 2 * WALL_T), "plaster", g)
    box("Wall_North", (0, H / 2, HZ + WALL_T / 2), (W, H, WALL_T), "brick", g)
    door_w, door_h = 1.5, 2.3
    seg = HX - door_w / 2
    box("Wall_South_Left", (-(door_w / 2 + seg / 2), H / 2, -HZ - WALL_T / 2), (seg, H, WALL_T), "plaster", g)
    box("Wall_South_Right", ((door_w / 2 + seg / 2), H / 2, -HZ - WALL_T / 2), (seg, H, WALL_T), "plaster", g)
    box("Wall_South_Lintel", (0, (H + door_h) / 2, -HZ - WALL_T / 2), (door_w, H - door_h, WALL_T), "plaster", g)
    g = "Ceiling"
    box("Ceiling_Slab", (0, H + 0.25, 0), (W + 2 * WALL_T, 0.5, D + 2 * WALL_T), "wood", g)
    for i, x in enumerate((-4.3, -2.15, 0.0, 2.15, 4.3)):
        box(f"Ceiling_BeamZ_{i}", (x, H - 0.17, 0), (0.30, 0.34, D), "wood", g, collider=0)
    for i, z in enumerate((-1.7, 0.0, 1.7)):
        box(f"Ceiling_BeamX_{i}", (0, H - 0.40, z), (W, 0.28, 0.26), "wood", g, collider=0)

    g = "Posts"
    px = 0.35
    for i, x in enumerate((-HX + px / 2, -2.4, 0.0, 2.4, HX - px / 2)):
        box(f"Post_N_{i}", (x, H / 2, HZ - px / 2), (px, H, px), "wood", g, collider=0)
    for i, x in enumerate((-HX + px / 2, -2.75, -1.25, 1.25, 2.75, HX - px / 2)):
        box(f"Post_S_{i}", (x, H / 2, -HZ + px / 2), (px, H, px), "wood", g, collider=0)
    for i, z in enumerate((-HZ + px / 2, -1.0, 1.1, HZ - px / 2)):
        box(f"Post_W_{i}", (-HX + px / 2, H / 2, z), (px, H, px), "wood", g, collider=0)
        box(f"Post_E_{i}", (HX - px / 2, H / 2, z), (px, H, px), "wood", g, collider=0)
    # stone pads at the foot of every post would sit here later; entrance flank pillars now
    box("Entrance_Pillar_L", (-door_w / 2 - 0.3, 1.3, -HZ + 0.28), (0.55, 2.6, 0.55), "stone", g)
    box("Entrance_Pillar_R", (door_w / 2 + 0.3, 1.3, -HZ + 0.28), (0.55, 2.6, 0.55), "stone", g)

    # ------------------------------------------------------------ entrance door + vestibule
    g = "Entrance"
    box("Door_Entrance_Leaf", (0.78, 1.1, -HZ - 0.55), (0.1, 2.2, 1.45), "wood", g, rot=(0, 0, 0), collider=0)
    vz0, vz1 = -HZ - WALL_T, -HZ - WALL_T - 2.4
    box("Vestibule_Floor", (0, -0.15, (vz0 + vz1) / 2), (2.0, 0.3, 2.4), "stone", g)
    box("Vestibule_WallL", (-1.05, 1.2, (vz0 + vz1) / 2), (0.1, 2.4, 2.4), "plaster", g)
    box("Vestibule_WallR", (1.05, 1.2, (vz0 + vz1) / 2), (0.1, 2.4, 2.4), "plaster", g)
    box("Vestibule_End", (0, 1.2, vz1 - 0.05), (2.2, 2.4, 0.1), "wood", g)
    box("Vestibule_Ceiling", (0, 2.45, (vz0 + vz1) / 2), (2.2, 0.1, 2.4), "wood", g)

    # ------------------------------------------------------------ stairs + landing
    g = "Stairs"
    sw = STAIR_X1 - STAIR_X0
    for i in range(STAIR_STEPS):
        top = STAIR_RISE * (i + 1)
        zc = STAIR_Z0 + STAIR_TREAD * i + STAIR_TREAD / 2
        box(f"Step_{i + 1:02d}", ((STAIR_X0 + STAIR_X1) / 2, top / 2, zc), (sw, top, STAIR_TREAD), "brick", g)
        box(f"Stringer_E_{i + 1:02d}", (STAIR_X1 + 0.1, (top + 0.12) / 2, zc), (0.2, top + 0.12, STAIR_TREAD), "brick", g)
    lw, ld = LANDING_X1 - LANDING_X0, HZ - STAIR_Z1
    box("Landing_Mass", ((LANDING_X0 + LANDING_X1) / 2, (LANDING_Y - 0.12) / 2, STAIR_Z1 + ld / 2), (lw, LANDING_Y - 0.12, ld), "brick", g)
    box("Landing_Deck", ((LANDING_X0 + LANDING_X1) / 2, LANDING_Y - 0.06, STAIR_Z1 + ld / 2), (lw, 0.12, ld), "wood", g)
    # west strip of mass beside the stairs (between wall and handrail)
    for i in range(STAIR_STEPS):
        top = STAIR_RISE * (i + 1)
        zc = STAIR_Z0 + STAIR_TREAD * i + STAIR_TREAD / 2
        box(f"Step_WestFill_{i + 1:02d}", ((LANDING_X0 + STAIR_X0) / 2, top / 2, zc), (STAIR_X0 - LANDING_X0, top, STAIR_TREAD), "brick", g)
    # handrails
    rail_len = math.hypot(STAIR_STEPS * STAIR_TREAD - STAIR_TREAD, LANDING_Y - STAIR_RISE)
    ang = math.degrees(math.atan2(LANDING_Y - STAIR_RISE, STAIR_STEPS * STAIR_TREAD - STAIR_TREAD))
    zmid = STAIR_Z0 + STAIR_TREAD / 2 + (STAIR_STEPS * STAIR_TREAD - STAIR_TREAD) / 2
    box("Stair_Handrail_W", (STAIR_X0 - 0.12, (STAIR_RISE + LANDING_Y) / 2 + 0.95, zmid), (0.10, 0.10, rail_len), "wood", g, rot=(-ang, 0, 0), collider=0)
    for i, zz in enumerate((STAIR_Z0 + 0.15, STAIR_Z0 + 1.2, STAIR_Z1 - 0.15)):
        yy = STAIR_RISE + (zz - STAIR_Z0 - STAIR_TREAD / 2) * math.tan(math.radians(ang))
        box(f"Stair_Newel_W_{i}", (STAIR_X0 - 0.12, yy + 0.55, zz), (0.14, 1.1, 0.14), "wood", g, collider=0)
    for i, zz in enumerate((STAIR_Z1 + 0.1, STAIR_Z1 + 0.7, HZ - 0.15)):
        box(f"Landing_Post_E_{i}", (LANDING_X1 + 0.05, LANDING_Y + 0.55, zz), (0.16, 1.1, 0.16), "wood", g, collider=0)
    box("Landing_Rail_E", (LANDING_X1 + 0.05, LANDING_Y + 1.0, STAIR_Z1 + ld / 2), (0.10, 0.10, ld), "wood", g, collider=0)
    box("Landing_Rail_S", ((LANDING_X0 + STAIR_X0) / 2 - 0.0, LANDING_Y + 1.0, STAIR_Z1 + 0.1), (STAIR_X0 - LANDING_X0 + 0.0, 0.10, 0.10), "wood", g, collider=0)
    # shelf, bottles, crate, door
    g = "Landing_Props"
    box("Landing_Shelf", (-4.4, LANDING_Y + 0.4, HZ - 0.3), (0.95, 0.8, 0.42), "wood", g)
    for i, x in enumerate((-4.7, -4.4, -4.1)):
        cyl(f"Landing_Bottle_{i}", (x, LANDING_Y + 0.8 + 0.16, HZ - 0.3), 0.13, 0.32, "glass", g, collider=0)
    box("Landing_Crate", (-3.65, LANDING_Y + 0.27, HZ - 0.38), (0.54, 0.54, 0.54), "wood", g)
    box("Door_Landing", (-HX + 0.06, LANDING_Y + 1.05, 2.45), (0.10, 2.1, 0.95), "wood", g, collider=0)

    # ------------------------------------------------------------ boilers
    for name, cx, cz, pr, sr, bh in BOILERS:
        g = f"Boiler_{name}"
        cyl(f"{g}_Plinth", (cx, PLINTH_H / 2, cz), pr * 2, PLINTH_H, "brick", g)
        cyl(f"{g}_Shell", (cx, PLINTH_H + bh / 2, cz), sr * 2, bh, "iron_teal", g)
        top = PLINTH_H + bh
        sph(f"{g}_Dome", (cx, top, cz), sr * 2, "iron_teal", g, yscale=0.70)
        neck_h = 0.34
        cyl(f"{g}_Neck", (cx, top + sr * 0.35 + neck_h / 2 - 0.03, cz), sr * 0.62, neck_h, "iron_teal", g)
        # front (south) fittings
        zf = cz - sr
        gy = top - 0.55 if name != "Left" else top - 0.42
        cyl(f"{g}_Gauge", (cx - 0.02, gy, zf - 0.03), 0.42 if name == "Center" else 0.34, 0.09, "cream", g, rot=(90, 0, 0), collider=0)
        # valve wheel on the east side, turned toward the room
        ang = math.radians(-28)
        wr = sr + 0.10
        wy = PLINTH_H + bh * 0.52
        wd = {"Left": 0.42, "Center": 0.52, "Right": 0.58}[name]
        cyl(f"{g}_ValveWheel", (cx + wr * math.cos(ang), wy, cz + wr * math.sin(ang)), wd, 0.07, "red", g, rot=(90, 0, 0), collider=0)
        cyl(f"{g}_ValveStem", (cx + (sr - 0.02) * math.cos(ang), wy, cz + (sr - 0.02) * math.sin(ang)), 0.14, 0.22, "iron_dark", g, rot=(90, 0, 0), collider=0)
    # firebox door + glow (centre boiler faces south) and small hatches on the others
    box("Boiler_Center_FireDoorFrame", (0.52, 1.05, 1.72 - 1.25 - 0.04), (0.95, 0.95, 0.14), "iron_dark", "Boiler_Center", collider=0)
    box("Boiler_Center_FireGlow", (0.52, 1.05, 1.72 - 1.25 - 0.13), (0.55, 0.46, 0.05), "glow", "Boiler_Center", collider=0)
    box("Boiler_Left_Hatch", (-1.96, 0.98, 1.54 - 0.85 - 0.03), (0.5, 0.36, 0.08), "iron_dark", "Boiler_Left", collider=0)
    box("Boiler_Right_Hatch", (3.24, 0.98, 1.58 - 1.05 - 0.03), (0.6, 0.42, 0.08), "iron_dark", "Boiler_Right", collider=0)
    # chimney through the ceiling
    pipe_y("Chimney", 3.9, H + 0.6, 0.52, 1.72, 0.56, "iron_teal", "Boiler_Center")

    # ------------------------------------------------------------ pipes
    g = "Pipes"
    pr = 0.38
    zr = 2.92      # run distance from the north wall
    yr = 3.5       # run height
    # west run, left riser, drop into centre boiler's left shoulder
    pipe_x("Run_N_West", -2.9, -0.75, yr, zr, pr, "iron_teal", g)
    pipe_y("Run_N_West_Up", yr, H + 0.5, -2.9, zr, pr, "iron_teal", g)
    pipe_y("LeftBoiler_Riser", 2.55, yr, -1.96, 1.54, 0.34, "iron_teal", g)
    pipe_z("LeftBoiler_Feed", 1.54, zr, -1.96, yr, 0.34, "iron_teal", g)
    pipe_z("CenterLeft_Feed", 1.72, zr, -0.75, yr, 0.34, "iron_teal", g)
    pipe_y("CenterLeft_Drop", 3.0, yr, -0.75, 1.72, 0.34, "iron_teal", g)
    # east run, right riser, drop into centre boiler's right shoulder
    pipe_x("Run_N_East", 1.77, 4.75, yr, zr, pr, "iron_teal", g)
    pipe_y("RightBoiler_Riser", 2.95, yr, 3.24, 1.58, 0.34, "iron_teal", g)
    pipe_z("RightBoiler_Feed", 1.58, zr, 3.24, yr, 0.34, "iron_teal", g)
    pipe_z("CenterRight_Feed", 1.72, zr, 1.77, yr, 0.34, "iron_teal", g)
    pipe_y("CenterRight_Drop", 3.0, yr, 1.77, 1.72, 0.34, "iron_teal", g)
    # east wall pipe: along the wall, then down to the floor beside the right boiler
    pipe_z("EastWall_Run", 0.75, zr, 4.75, yr, 0.46, "iron_teal", g)
    pipe_y("EastWall_Drop", 0.0, yr, 4.75, 0.75, 0.46, "iron_teal", g, collider=1)
    # corner pipes with valves
    cyl("SW_Pipe", (-4.5, H / 2, -2.0), 0.72, H, "iron_teal", g)
    cyl("SW_Pipe_Base", (-4.5, 0.18, -2.0), 0.98, 0.36, "iron_teal", g)
    cyl("SW_Pipe_CollarTop", (-4.5, 3.45, -2.0), 0.92, 0.3, "iron_teal", g, collider=0)
    cyl("SW_Pipe_Valve_Stub", (-4.15, 1.62, -2.0), 0.22, 0.4, "iron_dark", g, rot=(0, 0, 90), collider=0)
    cyl("SW_Pipe_ValveWheel", (-3.95, 1.62, -2.0), 0.58, 0.07, "red", g, rot=(0, 0, 90), collider=0)
    box("SE_Pipe_Footing", (4.5, 0.2, -2.3), (1.0, 0.4, 1.05), "brick", g)
    cyl("SE_Pipe", (4.55, H / 2, -2.4), 0.64, H, "iron_teal", g)
    cyl("SE_Pipe_Base", (4.55, 0.55, -2.4), 0.86, 0.3, "iron_teal", g, collider=0)
    cyl("SE_Pipe_CollarTop", (4.55, 3.4, -2.4), 0.84, 0.28, "iron_teal", g, collider=0)
    cyl("SE_Pipe_Valve_Stub", (4.22, 1.25, -2.4), 0.2, 0.36, "iron_dark", g, rot=(0, 0, 90), collider=0)
    cyl("SE_Pipe_ValveWheel", (4.0, 1.25, -2.4), 0.54, 0.07, "red", g, rot=(0, 0, 90), collider=0)

    # ------------------------------------------------------------ props (SW corner)
    g = "Props"
    box("Crate_Large_Back", (-3.85, 0.45, -1.65), (0.8, 0.9, 0.9), "wood_light", g)
    box("Crate_Large_Front", (-3.75, 0.35, -2.5), (1.05, 0.7, 0.7), "wood_light", g)
    box("Crate_Small", (-3.05, 0.2, -1.8), (0.55, 0.4, 0.5), "wood_light", g)
    cyl("Bucket", (-3.2, 0.225, -2.55), 0.5, 0.45, "iron_dark", g)
    cyl("Broom_Handle", (-3.4, 0.62, -2.1), 0.05, 1.35, "wood", g, rot=(-12, 0, 8), collider=0)

    # ------------------------------------------------------------ lantern markers
    g = "Lanterns"
    for name, p in (("Lantern_Landing", (-4.1, 3.05, HZ - 0.2)), ("Lantern_West", (-HX + 0.2, 2.5, -0.1)), ("Lantern_North", (2.6, 3.0, HZ - 0.2))):
        box(name, p, (0.24, 0.42, 0.24), "glow", g, collider=0)
    return items


def main(out):
    build()
    with open(out, "w", encoding="utf-8") as f:
        json.dump({"items": items}, f, indent=1)
    print(f"{len(items)} items -> {out}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "room_layout_blockout.json")
