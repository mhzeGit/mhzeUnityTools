"""Props: plank crates, shelf table, bottles, cage lantern with wall bracket.
Origin at the centre of the base on the floor (z = 0) unless stated; fronts face -Y.
Regions: Pine__ (light pine boards), Oak__ (dark oak), Iron__ (nails, frame), Glow__ (lantern glass), Glass__ / Stoneware__ (bottles)."""
import math
import random
import bq
from mathutils import Vector, Matrix


def _board(bm, size, center, seed, axis_dir=None, chamfer=0.004, crook=0.002):
    """Flat board: size = (thickness_x, width_y, length_z) before orientation, oriented so that local Z follows axis_dir
    and local X (thickness) follows the face normal passed as the 2nd vector of axis_dir=(dir, normal)."""
    b = bq.new_bm()
    bq.beam(b, size[0], size[1], size[2], chamfer=chamfer, segs=3, crook=crook, seed=seed)
    if axis_dir is not None:
        d, n = Vector(axis_dir[0]).normalized(), Vector(axis_dir[1]).normalized()
        n = (n - d * n.dot(d)).normalized()
        yv = d.cross(n)
        m = Matrix(((n.x, yv.x, d.x, 0), (n.y, yv.y, d.y, 0), (n.z, yv.z, d.z, 0), (0, 0, 0, 1)))
    else:
        m = Matrix.Identity(4)
    bq.add_bm(bm, b, Matrix.Translation(center) @ m)
    b.free()


def _nail(bm, p, n):
    bq.rivet(bm, Vector(p), Vector(n), 0.010, 0.006, 6, 1)


def slatted_crate(W, D, H, name, seed, slat_h=0.15, xbrace_faces=(), lid=True, open_top=False):
    """General plank crate: corner posts, horizontal slats on the four sides, top boards, optional X braces on chosen faces
    ('-Y','+Y','-X','+X','top')."""
    rng = random.Random(seed)
    wood = bq.new_bm()
    nails = bq.new_bm()
    pt = 0.065
    for sx in (-1, 1):
        for sy in (-1, 1):
            _board(wood, (pt, pt, H), (sx * (W / 2 - pt / 2), sy * (D / 2 - pt / 2), H / 2), seed + sx * 3 + sy, chamfer=0.006)
    n = max(2, int((H - 0.06) / slat_h))
    sh = (H - 0.06) / n
    for k in range(n):
        z = 0.03 + sh * (k + 0.5)
        for sy in (-1, 1):                  # front/back slats run along X
            ln = W - 2 * pt + 0.01
            _board(wood, (0.022, sh - 0.012, ln), (0, sy * (D / 2 - 0.011), z), seed + 10 * k + sy, axis_dir=((1, 0, 0), (0, sy, 0)))
            for sx in (-1, 1):
                _nail(nails, (sx * (W / 2 - pt / 2), sy * (D / 2 + 0.0), z), (0, sy, 0))
        for sx in (-1, 1):                  # side slats run along Y
            ln = D - 2 * pt + 0.01
            _board(wood, (0.022, sh - 0.012, ln), (sx * (W / 2 - 0.011), 0, z), seed + 20 * k + sx, axis_dir=((0, 1, 0), (sx, 0, 0)))
    if lid and not open_top:
        n_l = max(3, int(D / 0.2))
        for k in range(n_l):
            y = -D / 2 + (k + 0.5) * D / n_l
            _board(wood, (0.026, D / n_l - 0.012, W - 0.02), (0, y, H - 0.013 + rng.uniform(-0.003, 0.003)), seed + 40 + k, axis_dir=((1, 0, 0), (0, 0, 1)))
        for sx in (-1, 1):
            _board(wood, (0.03, 0.07, D - 0.01), (sx * (W / 2 - 0.1), 0, H + 0.012), seed + 50 + sx, axis_dir=((0, 1, 0), (0, 0, 1)))
    for face in xbrace_faces:
        for sgn in (-1, 1):
            if face in ('-Y', '+Y'):
                yy = (-1 if face == '-Y' else 1) * (D / 2 + 0.011)
                p0, p1 = Vector((-W / 2 + pt, yy, 0.05)), Vector((W / 2 - pt, yy, H - 0.05))
                if sgn < 0:
                    p0, p1 = Vector((-W / 2 + pt, yy, H - 0.05)), Vector((W / 2 - pt, yy, 0.05))
                nrm = (0, 1 if face == '+Y' else -1, 0)
            elif face in ('-X', '+X'):
                xx = (-1 if face == '-X' else 1) * (W / 2 + 0.011)
                p0, p1 = Vector((xx, -D / 2 + pt, 0.05)), Vector((xx, D / 2 - pt, H - 0.05))
                if sgn < 0:
                    p0, p1 = Vector((xx, -D / 2 + pt, H - 0.05)), Vector((xx, D / 2 - pt, 0.05))
                nrm = (1 if face == '+X' else -1, 0, 0)
            else:       # top
                zz = H + 0.045
                p0, p1 = Vector((-W / 2 + pt, -D / 2 + pt, zz)), Vector((W / 2 - pt, D / 2 - pt, zz))
                if sgn < 0:
                    p0, p1 = Vector((-W / 2 + pt, D / 2 - pt, zz)), Vector((W / 2 - pt, -D / 2 + pt, zz))
                nrm = (0, 0, 1)
            d = p1 - p0
            _board(wood, (0.024, 0.075, d.length), (p0 + p1) / 2, seed + 70 + sgn, axis_dir=(tuple(d), nrm), chamfer=0.005)
            _nail(nails, tuple(p0 + d.normalized() * 0.04), nrm)
            _nail(nails, tuple(p1 - d.normalized() * 0.04), nrm)
    return [bq.obj_from_bm("Pine__Boards", wood, smooth_angle=40), bq.obj_from_bm("Iron__Nails", nails, smooth_angle=60)], {"pivot_note": f"origin at the centre of the base; {W} x {D} x {H} m"}


def crate_large():
    return slatted_crate(1.05, 0.70, 0.70, "Crate_Large", 3, slat_h=0.16)


def crate_xbrace():
    return slatted_crate(0.80, 0.90, 0.90, "Crate_Xbrace", 5, slat_h=0.18, xbrace_faces=('-Y', '+Y', '-X', '+X', 'top'))


def crate_small():
    return slatted_crate(0.55, 0.50, 0.40, "Crate_Small", 8, slat_h=0.12, xbrace_faces=('-Y', 'top'))


def crate_lattice():
    """Open lattice crate 0.54 m cube for the landing: corner posts, three rails per side and X braces."""
    return slatted_crate(0.54, 0.54, 0.54, "Crate_Lattice", 12, slat_h=0.26, xbrace_faces=('-Y', '+Y', '-X', '+X', 'top'), lid=True)


def shelf_table():
    """Small plank table/shelf 0.95 x 0.42 x 0.78 m: top boards, two end panels with cut-out feet, a rear stretcher and a lower shelf."""
    wood = bq.new_bm()
    W, D, H = 0.95, 0.42, 0.78
    for k in range(3):
        y = -D / 2 + (k + 0.5) * D / 3
        _board(wood, (0.035, D / 3 - 0.008, W), (0, y, H - 0.018), 60 + k, axis_dir=((1, 0, 0), (0, 0, 1)))
    for sx in (-1, 1):
        # end panel with a notch between the feet
        bq.box(wood, (0.04, D - 0.04, H - 0.2), center=(sx * (W / 2 - 0.1), 0, 0.1 + (H - 0.2 - 0.036) / 2 + 0.1), bevel=0.006, bevel_segs=1)
        for sy in (-1, 1):
            bq.box(wood, (0.05, 0.07, 0.2), center=(sx * (W / 2 - 0.1), sy * (D / 2 - 0.05), 0.1), bevel=0.006, bevel_segs=1)
    _board(wood, (0.04, 0.09, W - 0.2), (0, D / 2 - 0.06, H - 0.12), 70, axis_dir=((1, 0, 0), (0, -1, 0)))
    _board(wood, (0.03, D - 0.1, W - 0.22), (0, 0, 0.25), 71, axis_dir=((1, 0, 0), (0, 0, 1)))
    nails = bq.new_bm()
    for x in (-0.4, 0.4):
        for y in (-0.15, 0.15):
            _nail(nails, (x, y, H + 0.0), (0, 0, 1))
    return [bq.obj_from_bm("Oak__Boards", wood, smooth_angle=40), bq.obj_from_bm("Iron__Nails", nails, smooth_angle=60)], {"pivot_note": "origin at the centre of the base on the floor; top at 0.78 m"}


def _bottle(name, prof, handle=False, cork=None, seed=0, region="Glass"):
    bm = bq.new_bm()
    bq.lathe(bm, bq.rounded_profile(prof, 0.03, 5), 28)
    bq.jitter(bm, 0.0025, 3.0, seed)
    objs = [bq.obj_from_bm(f"{region}__Body", bm, smooth_angle=50)]
    if handle:
        hb = bq.new_bm()
        bq.lathe(hb, [(0.045 + 0.011 * math.cos(math.tau * i / 8), 0.011 * math.sin(math.tau * i / 8)) for i in range(9)], 14, start_angle=math.radians(-80), sweep=math.radians(160),
                 matrix=Matrix.Translation((0.05, 0, 0.17)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
        objs.append(bq.obj_from_bm("Stoneware__Handle", hb, smooth_angle=60))
    if cork:
        cb = bq.new_bm()
        bq.lathe(cb, bq.rounded_profile([(0, cork[0]), (cork[1] * 0.85, cork[0]), (cork[1], cork[0] + 0.018), (cork[1] * 0.9, cork[0] + 0.045), (0, cork[0] + 0.045)], 0.004, 1), 14)
        objs.append(bq.obj_from_bm("Pine__Cork", cb, smooth_angle=60))
    return objs, {"pivot_note": "origin at the centre of the base on the floor"}


def bottle_jug():
    return _bottle("Bottle_Jug", [(0, 0), (0.060, 0), (0.078, 0.03), (0.082, 0.10), (0.060, 0.17), (0.034, 0.21), (0.030, 0.26), (0.036, 0.275), (0.036, 0.29), (0.026, 0.29), (0.0, 0.29)], cork=(0.29, 0.03), seed=3)


def bottle_tall():
    return _bottle("Bottle_Tall", [(0, 0), (0.050, 0), (0.062, 0.03), (0.064, 0.16), (0.046, 0.24), (0.026, 0.285), (0.024, 0.34), (0.03, 0.352), (0.03, 0.364), (0.022, 0.364), (0.0, 0.364)], cork=(0.364, 0.026), seed=5)


def bottle_stone():
    return _bottle("Bottle_Stone", [(0, 0), (0.062, 0), (0.082, 0.03), (0.086, 0.10), (0.066, 0.17), (0.044, 0.20), (0.034, 0.22), (0.036, 0.235), (0.0, 0.235)], handle=True, seed=7, region="Stoneware")


def lantern_wall():
    """Hanging cage lantern on a curved wall bracket.  Wall plane is y = 0 (wall at +Y); the lantern hangs 0.30 m out toward -Y, its top at z = 0.13 and base at z = -0.40.
    Origin on the wall at the bracket plate centre."""
    iron = bq.new_bm()
    # wall plate and curved arm
    bq.box(iron, (0.12, 0.03, 0.2), center=(0, -0.015, 0.0), bevel=0.008, bevel_segs=1)
    for z in (-0.07, 0.07):
        bq.rivet(iron, Vector((0, -0.03, z)), Vector((0, -1, 0)), 0.014, 0.01, 8, 2)
    pts = []
    for i in range(13):
        a = math.pi * 0.5 * i / 12
        pts.append(Vector((0, -0.30 * math.sin(a) - 0.02, 0.10 + 0.10 * (1 - math.cos(a)) * 0.0 + 0.0 * a)))
    arm = bq.new_bm()
    prev = None
    for i in range(13):
        t = i / 12
        p = Vector((0, -0.02 - 0.30 * t, 0.07 + 0.06 * math.sin(math.pi * t)))
        if prev is not None:
            d = p - prev
            _board(arm, (0.016, 0.016, d.length + 0.004), (p + prev) / 2, 1, axis_dir=(tuple(d), (1, 0, 0)), chamfer=0.003, crook=0.0)
        prev = p
    bq.add_bm(iron, arm)
    arm.free()
    # lantern: base, four posts, hood and hanging ring (axis through x=0, y=-0.30)
    cx, cy = 0.0, -0.30
    bq.lathe(iron, bq.rounded_profile([(0, -0.40), (0.085, -0.40), (0.095, -0.385), (0.095, -0.36), (0.075, -0.35), (0, -0.35)], 0.006, 1), 20, matrix=Matrix.Translation((cx, cy, 0)))
    for i in range(4):
        a = math.tau * i / 4 + math.pi / 4
        bq.beam(iron, 0.014, 0.014, 0.36, chamfer=0.002, segs=2, seed=i, center=(cx + 0.075 * math.cos(a), cy + 0.075 * math.sin(a), -0.20))
    bq.lathe(iron, bq.rounded_profile([(0.082, -0.035), (0.095, -0.03), (0.095, -0.015), (0.075, 0.015), (0.04, 0.035), (0.04, 0.06), (0.052, 0.07), (0.052, 0.085), (0.02, 0.095), (0.0, 0.095)], 0.005, 1), 20,
             matrix=Matrix.Translation((cx, cy, 0)))
    ring = bq.new_bm()
    bq.lathe(ring, [(0.03 + 0.007 * math.cos(math.tau * i / 8), 0.007 * math.sin(math.tau * i / 8)) for i in range(9)], 14, matrix=Matrix.Translation((cx, cy, 0.125)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    bq.add_bm(iron, ring)
    ring.free()
    # glass chimney
    glass = bq.new_bm()
    # 5 mm clear of the frame's base and hood: coincident vertices would weld glass and frame into one Graphmat component (one region)
    bq.lathe(glass, [(0.0, -0.345), (0.062, -0.345), (0.072, -0.30), (0.072, -0.10), (0.06, -0.045), (0.0, -0.045)], 18, matrix=Matrix.Translation((cx, cy, 0)))
    return [bq.obj_from_bm("Iron__Frame", iron, smooth_angle=50), bq.obj_from_bm("Glow__Glass", glass, smooth_angle=60)], {"pivot_note": "origin on the wall (wall at +Y) at the bracket plate centre; lantern hangs 0.30 m out toward -Y"}


ASSETS = {"Crate_Large": crate_large, "Crate_Xbrace": crate_xbrace, "Crate_Small": crate_small, "Crate_Lattice": crate_lattice, "Shelf_Table": shelf_table,
          "Bottle_Jug": bottle_jug, "Bottle_Tall": bottle_tall, "Bottle_Stone": bottle_stone, "Lantern_Wall": lantern_wall}
