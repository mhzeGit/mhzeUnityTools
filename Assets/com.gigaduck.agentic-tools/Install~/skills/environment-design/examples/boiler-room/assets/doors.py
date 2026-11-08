"""Doors: heavy plank entrance door (hinged leaf), its timber frame with threshold, and the small landing door in a frame.
Regions: Oak__ (planks/frame), Iron__ (straps, hinges, latch), Mortar__ (threshold stone)."""
import math
import random
import bq
from mathutils import Vector, Matrix


def _planks(bm, W, H, n, t, seed, warp=0.006, xoff=0.0):
    rng = random.Random(seed)
    pw = W / n
    for i in range(n):
        x0 = xoff + i * pw
        w = pw - 0.006
        h = H - rng.uniform(0.0, 0.03)
        bq.beam(bm, w, t, h, chamfer=0.008, segs=6, crook=warp, twist=0.0, seed=seed + i, center=(x0 + pw / 2, 0, h / 2 + rng.uniform(0.0, 0.01)))


def _strap_hinge(bm, x0, x1, z, t, face_y):
    """Horizontal iron strap with rivets and a rolled hinge eye at x0."""
    bq.box(bm, (x1 - x0, 0.012, 0.11), center=((x0 + x1) / 2, face_y, z), bevel=0.004, bevel_segs=1)
    n = int((x1 - x0) / 0.18)
    for i in range(n + 1):
        x = x0 + 0.05 + i * (x1 - x0 - 0.1) / max(1, n)
        bq.rivet(bm, Vector((x, face_y - 0.006, z + 0.03)), Vector((0, -1, 0)), 0.014, 0.011, 8, 2)
        bq.rivet(bm, Vector((x, face_y - 0.006, z - 0.03)), Vector((0, -1, 0)), 0.014, 0.011, 8, 2)
    bq.cylinder(bm, 0.032, 0.115, segs=12, center=(x0 - 0.012, face_y, z), axis='Z', bevel=0.005)


def entrance_door():
    """Heavy plank door 1.45 x 2.2 x 0.08 m: 7 oak boards, three strap hinges, pull ring and bolt latch.  Origin at the hinge-side bottom corner (+X across the leaf)."""
    W, H, T = 1.45, 2.2, 0.08
    bm = bq.new_bm()
    _planks(bm, W, H, 7, T, 70)
    # cross ledges on the back (+Y)
    for z in (0.35, 1.85):
        bq.beam(bm, 0.16, 0.07, W - 0.1, chamfer=0.01, segs=3, crook=0.002, seed=77, axis='X', center=(W / 2, T / 2 + 0.03, z))
    wood = bq.obj_from_bm("Oak__Planks", bm, smooth_angle=35)
    ib = bq.new_bm()
    for z in (0.35, 1.1, 1.85):
        _strap_hinge(ib, 0.0, 1.0 if z != 1.1 else 0.75, z, T, -T / 2 - 0.004)
    # latch plate + bolt, pull ring
    bq.box(ib, (0.20, 0.014, 0.28), center=(1.28, -T / 2 - 0.006, 1.05), bevel=0.005, bevel_segs=1)
    bq.cylinder(ib, 0.018, 0.2, segs=10, center=(1.28, -T / 2 - 0.05, 1.05), axis='X', bevel=0.004)
    ring = bq.new_bm()
    bq.lathe(ring, [(0.095 + 0.012 * math.cos(math.tau * i / 8), 0.012 * math.sin(math.tau * i / 8)) for i in range(9)], 22,
             matrix=Matrix.Translation((1.08, -T / 2 - 0.04, 1.0)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    bq.add_bm(ib, ring)
    ring.free()
    iron = bq.obj_from_bm("Iron__Hardware", ib, smooth_angle=45)
    return [wood, iron], {"pivot_note": "origin at the bottom of the hinge edge; leaf extends +X (1.45 m) and up (2.2 m); face toward -Y"}


def entrance_frame():
    """Timber door frame for the 1.5 m opening: two jambs, lintel and a stone threshold.  Origin at the bottom centre of the opening, face toward -Y."""
    bm = bq.new_bm()
    for sx in (-1, 1):
        bq.beam(bm, 0.20, 0.30, 2.34, chamfer=0.02, segs=8, crook=0.004, twist=0.004, seed=51 + sx, center=(sx * 0.80, 0, 1.17))
    bq.beam(bm, 0.28, 0.30, 1.98, chamfer=0.024, segs=10, crook=0.005, twist=0.004, seed=55, center=(0, 0, 2.33), axis='X')
    wood = bq.obj_from_bm("Oak__Frame", bm, smooth_angle=35)
    sb = bq.new_bm()
    bq.slab(sb, 1.9, 0.5, 0.09, center=(0, 0, 0.045), bevel=0.03, bevel_segs=2, jit=0.003, seed=57)
    stone = bq.obj_from_bm("Mortar__Threshold", sb, smooth_angle=25)
    ib = bq.new_bm()
    for z in (0.35, 1.1, 1.85):
        bq.cylinder(ib, 0.014, 0.12, segs=10, center=(-0.72, 0.0, z), axis='Z', bevel=0.003)
    return [wood, stone, bq.obj_from_bm("Iron__Pintles", ib, smooth_angle=45)], {"pivot_note": "origin at the bottom centre of the opening (1.4 m clear x 2.2 m); face toward -Y"}


def landing_door():
    """Small plank door (0.9 x 2.05) in a timber frame, closed, with two straps and a ring pull.  Origin at the bottom centre of the opening."""
    W, H, T = 0.9, 2.05, 0.07
    bm = bq.new_bm()
    _planks(bm, W, H, 5, T, 90, xoff=-W / 2)
    wood = bq.obj_from_bm("Oak__Planks", bm, smooth_angle=35)
    fb = bq.new_bm()
    for sx in (-1, 1):
        bq.beam(fb, 0.16, 0.22, H + 0.2, chamfer=0.016, segs=8, crook=0.004, twist=0.003, seed=93 + sx, center=(sx * (W / 2 + 0.085), 0, (H + 0.2) / 2))
    bq.beam(fb, 0.2, 0.22, W + 0.5, chamfer=0.018, segs=6, crook=0.004, twist=0.003, seed=97, center=(0, 0, H + 0.1), axis='X')
    frame = bq.obj_from_bm("Oak__Frame", fb, smooth_angle=35)
    ib = bq.new_bm()
    for z in (0.45, 1.55):
        _strap_hinge(ib, -W / 2, W / 2 - 0.1, z, T, -T / 2 - 0.004)
    ring = bq.new_bm()
    bq.lathe(ring, [(0.06 + 0.009 * math.cos(math.tau * i / 8), 0.009 * math.sin(math.tau * i / 8)) for i in range(9)], 20,
             matrix=Matrix.Translation((W / 2 - 0.14, -T / 2 - 0.035, 1.0)) @ Matrix.Rotation(math.pi / 2, 4, 'X'))
    bq.add_bm(ib, ring)
    ring.free()
    iron = bq.obj_from_bm("Iron__Hardware", ib, smooth_angle=45)
    return [wood, frame, iron], {"pivot_note": "origin at the bottom centre of the opening; face toward -Y"}


ASSETS = {"Door_Entrance": entrance_door, "Doorframe_Entrance": entrance_frame, "Door_Landing": landing_door}
