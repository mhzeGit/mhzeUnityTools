"""Timber frame kit: post with stone pad and cap, beam, diagonal brace, stone entrance pillar.
Regions: Oak__ (dark oak), Mortar__ (pale stone), Iron__ (bolts)."""
import math
import random
import bq
from mathutils import Vector, Matrix


def _bolt(bm, pos, normal, r=0.028, h=0.02):
    bq.rivet(bm, Vector(pos), Vector(normal), r, h, 8, 2)


def post():
    """Hewn oak post 0.36 x 0.36 on a stone pad with a stone cap; 4.8 m floor to ceiling underside.  Origin on the floor under the post centre."""
    PAD, CAP, TOTAL = 0.20, 0.12, 4.80
    L = TOTAL - PAD - CAP
    bm = bq.new_bm()
    bq.beam(bm, 0.36, 0.36, L, chamfer=0.028, segs=10, crook=0.006, twist=0.012, seed=5, center=(0, 0, PAD + L / 2))
    # iron bolts where rails would be pegged (front/back faces)
    ib = bq.new_bm()
    for z in (1.15, 2.55, 3.55):
        for sy in (-1, 1):
            _bolt(ib, (0.06, sy * 0.185, z), (0, sy, 0))
            _bolt(ib, (-0.06, sy * 0.185, z + 0.02), (0, sy, 0))
    wood = bq.obj_from_bm("Oak__Post", bm, smooth_angle=35)
    iron = bq.obj_from_bm("Iron__PostBolts", ib, smooth_angle=45)
    sb = bq.new_bm()
    bq.slab(sb, 0.52, 0.52, PAD, center=(0, 0, PAD / 2), bevel=0.035, bevel_segs=2, jit=0.004, seed=3)
    bq.slab(sb, 0.50, 0.50, CAP, center=(0, 0, TOTAL - CAP / 2), bevel=0.03, bevel_segs=2, jit=0.003, seed=4)
    stone = bq.obj_from_bm("Mortar__PadCap", sb, smooth_angle=25)
    return [wood, iron, stone], {"pivot_note": "origin on the floor at the post centre; 4.8 m tall; pad 0.2 m, cap 0.12 m"}


def beam():
    """Hewn oak beam / rail 2.0 m along X, section 0.22 (Y) x 0.28 (Z), origin at its centre; two iron bolt heads on the -Y face."""
    bm = bq.new_bm()
    bq.beam(bm, 0.28, 0.22, 2.0, chamfer=0.024, segs=12, crook=0.007, twist=0.008, seed=8, axis='X')
    ib = bq.new_bm()
    for x in (-0.55, 0.55):
        _bolt(ib, (x, -0.11, 0.05), (0, -1, 0), 0.03, 0.022)
    return [bq.obj_from_bm("Oak__Beam", bm, smooth_angle=35), bq.obj_from_bm("Iron__BeamBolts", ib, smooth_angle=45)], {"pivot_note": "centre of the beam, axis X; bolts on -Y"}


def brace():
    """Diagonal brace 1.5 m long, 0.13 x 0.2, ends cut at 45 degrees; long axis X, origin at the centre."""
    bm = bq.new_bm()
    bq.beam(bm, 0.20, 0.13, 1.5, chamfer=0.018, segs=8, crook=0.005, twist=0.006, seed=9, axis='X', end_cut=(0.0, 0.0))
    # angled ends: shear the end rings in X by +-z of the section (vertical section dimension is Z after axis=X rotation)
    for v in bm.verts:
        if abs(v.co.x) > 0.74:
            v.co.x += math.copysign(1.0, v.co.x) * (-v.co.z) * 0.9
    ib = bq.new_bm()
    _bolt(ib, (0.62, -0.065, 0.0), (0, -1, 0), 0.026, 0.02)
    _bolt(ib, (-0.62, -0.065, 0.0), (0, -1, 0), 0.026, 0.02)
    return [bq.obj_from_bm("Oak__Brace", bm, smooth_angle=35), bq.obj_from_bm("Iron__BraceBolts", ib, smooth_angle=45)], {"pivot_note": "centre of the brace, axis X"}


def pillar():
    """Stone entrance pillar 0.55 x 0.55 x 2.6 m: five block courses with a projecting cap and plinth; origin on the floor at its centre."""
    rng = random.Random(41)
    bm = bq.new_bm()
    H = 2.6
    bq.slab(bm, 0.64, 0.64, 0.22, center=(0, 0, 0.11), bevel=0.03, bevel_segs=2, jit=0.003, seed=11)       # plinth
    courses = [0.40, 0.44, 0.42, 0.44, 0.40]
    z = 0.22
    for i, h in enumerate(courses):
        w = 0.55 + rng.uniform(-0.015, 0.015)
        bq.slab(bm, w, 0.55 + rng.uniform(-0.015, 0.015), h - 0.012, center=(rng.uniform(-0.006, 0.006), rng.uniform(-0.006, 0.006), z + h / 2), bevel=0.028, bevel_segs=2, jit=0.003, seed=20 + i)
        z += h
    bq.slab(bm, 0.66, 0.66, 0.16, center=(0, 0, H - 0.08 + 0.0), bevel=0.035, bevel_segs=2, jit=0.003, seed=31)    # cap
    stone = bq.obj_from_bm("Mortar__Blocks", bm, smooth_angle=25)
    ib = bq.new_bm()
    for sy in (-1, 1):
        bq.cylinder(ib, 0.035, 0.03, segs=10, center=(0.0, sy * 0.335, H - 0.08), axis='Y', bevel=0.006)
    return [stone, bq.obj_from_bm("Iron__CapBolts", ib, smooth_angle=45)], {"pivot_note": "origin on the floor at the pillar centre; 2.6 m tall"}


ASSETS = {"Post_Timber": post, "Beam_Timber": beam, "Brace_Timber": brace, "Pillar_Entrance": pillar}
