"""Brick stair, landing block, landing deck, balustrades.
Frame: +Y = climbing direction (north); steps rise 0.2 m with a 0.27 m tread.  Regions: Brick__, Mortar__, Oak__ (timber), Pine__ (deck boards), Iron__."""
import math
import random
import bq
from mathutils import Vector, Matrix

STEPS, RISE, TREAD, WIDTH = 9, 0.20, 0.27, 1.15
LANDING_W, LANDING_D, LANDING_H = 1.75, 1.61, 1.8


def stair():
    """Nine solid brick steps, 1.15 m wide, with a brick stringer wall on the east side and a plain stone fill on the west.
    Solid forms with the brick pattern as texture (tools/make_brick_tile.py), not brick by brick.
    Origin at the bottom centre of the first riser; climbs toward +Y."""
    bricks = bq.new_bm()
    bq.extrude_stairs(bricks, STEPS, RISE, TREAD, 0.0, -WIDTH / 2, WIDTH / 2)
    bq.extrude_stairs(bricks, STEPS, RISE, TREAD, 0.08, WIDTH / 2, WIDTH / 2 + 0.20)   # east stringer wall, 8 cm proud of every tread
    fill = bq.new_bm()
    bq.extrude_stairs(fill, STEPS, RISE, TREAD, 0.02, -WIDTH / 2 - 0.30, -WIDTH / 2 + 0.005)    # overlaps the brick body by 5 mm: no vertex shared with it
    return [bq.obj_from_bm("Brick__Bricks", bricks, smooth_angle=35), bq.obj_from_bm("Mortar__Fill", fill, smooth_angle=35)],            {"pivot_note": "origin at the bottom centre of the first riser, climbs +Y; 1.15 m wide; top step at z=1.8, y=2.16..2.43"}


def landing_block():
    """Brick mass under the landing (1.75 x 1.61 x 1.74 m): one solid, the brick pattern is texture. Origin at the bottom centre."""
    H = LANDING_H - 0.06
    bm = bq.new_bm()
    bq.box(bm, (LANDING_W, LANDING_D, H), center=(0, 0, H / 2), bevel=0.01, bevel_segs=1)
    return [bq.obj_from_bm("Brick__Bricks", bm, smooth_angle=35)],            {"pivot_note": "origin at the bottom centre; 1.75 x 1.61 x 1.74 m; brick faces on +X (east) and -Y (south)"}


def landing_deck():
    """Plank deck 1.75 x 1.41 m (7 boards along X) with an oak fascia on the east and south edges; top of the boards at z = 0.05, origin at the bottom centre."""
    rng = random.Random(23)
    deck = bq.new_bm()
    n = 7
    pw = LANDING_D / n
    for i in range(n):
        b = bq.new_bm()
        bq.beam(b, 0.05, pw - 0.007, LANDING_W - rng.uniform(0.0, 0.02), chamfer=0.006, segs=5, crook=0.0025, seed=30 + i, axis='X')
        bq.add_bm(deck, b, Matrix.Translation((rng.uniform(-0.008, 0.008), -LANDING_D / 2 + pw * (i + 0.5), 0.025)))
        b.free()
        for sx in (-1, 1):
            bq.rivet(deck, Vector((sx * (LANDING_W / 2 - 0.12), -LANDING_D / 2 + pw * (i + 0.5), 0.05)), Vector((0, 0, 1)), 0.011, 0.006, 6, 1)
    fas = bq.new_bm()
    bq.beam(fas, 0.04, 0.16, LANDING_D + 0.02, chamfer=0.008, segs=6, crook=0.003, seed=40, axis='Y', center=(LANDING_W / 2 + 0.01, 0, -0.06))
    bq.beam(fas, 0.04, 0.16, LANDING_W + 0.02, chamfer=0.008, segs=6, crook=0.003, seed=41, axis='X', center=(0, -LANDING_D / 2 - 0.01, -0.06))
    return [bq.obj_from_bm("Pine__Deck", deck, smooth_angle=40), bq.obj_from_bm("Oak__Fascia", fas, smooth_angle=40)], \
           {"pivot_note": "origin at the bottom centre of the boards; top at z=0.05; fascia drops 0.14 m on +X and -Y edges"}


def _newel(bm, x, y, h=1.12, w=0.15, cap=0.20, seed=0):
    bq.beam(bm, w, w, h, chamfer=0.014, segs=5, crook=0.002, seed=seed, center=(x, y, h / 2))
    bq.slab(bm, cap, cap, 0.06, center=(x, y, h + 0.03), bevel=0.012, bevel_segs=1)
    bq.slab(bm, cap * 0.8, cap * 0.8, 0.05, center=(x, y, h + 0.085), bevel=0.012, bevel_segs=1)


def landing_rail():
    """Balustrade for the landing's east edge: two newel posts with caps, top rail, lower rail and six square balusters. Along +Y, 1.41 m; origin at the bottom of the first post."""
    L = LANDING_D - 0.15
    bm = bq.new_bm()
    _newel(bm, 0.0, 0.0, seed=2)
    _newel(bm, 0.0, L, seed=3)
    bq.beam(bm, 0.07, 0.10, L, chamfer=0.01, segs=6, crook=0.002, seed=4, axis='Y', center=(0, L / 2, 0.98))
    bq.beam(bm, 0.05, 0.08, L, chamfer=0.008, segs=6, crook=0.002, seed=5, axis='Y', center=(0, L / 2, 0.16))
    for i in range(1, 7):
        y = L * i / 7
        bq.beam(bm, 0.04, 0.04, 0.82 - 0.2, chamfer=0.004, segs=2, seed=10 + i, center=(0, y, 0.20 + (0.82 - 0.2) / 2 + 0.0))
    return [bq.obj_from_bm("Oak__Balustrade", bm, smooth_angle=40)], {"pivot_note": "origin at the foot of the first newel; runs +Y (1.26 m); newels 1.2 m tall"}


def stair_handrail():
    """Sloped balustrade for the stairs' west side: newels at both ends, handrail, lower rail, nine balusters. Origin at the floor under the lower newel; climbs +Y."""
    y0, y1 = 0.15, STEPS * TREAD - 0.15
    slope = RISE / TREAD
    ang = math.atan(slope)
    bm = bq.new_bm()
    _newel(bm, 0.0, y0, h=1.12, seed=21)
    _newel(bm, 0.0, y1, h=1.12 + RISE * STEPS - RISE, seed=22)
    # rails follow the nosing line: z(y) = RISE + (y - y0) * slope (relative to the lower newel foot at floor level)
    L = (y1 - y0) / math.cos(ang)
    mid_y, mid_z = (y0 + y1) / 2, RISE + ((y0 + y1) / 2 - y0) * slope
    rail = bq.new_bm()
    bq.beam(rail, 0.07, 0.10, L, chamfer=0.01, segs=8, crook=0.002, seed=23, axis='Y')
    bq.add_bm(bm, rail, Matrix.Translation((0, mid_y, mid_z + 0.92)) @ Matrix.Rotation(ang, 4, 'X'))
    rail.free()
    low = bq.new_bm()
    bq.beam(low, 0.05, 0.08, L, chamfer=0.008, segs=8, crook=0.002, seed=24, axis='Y')
    bq.add_bm(bm, low, Matrix.Translation((0, mid_y, mid_z + 0.14)) @ Matrix.Rotation(ang, 4, 'X'))
    low.free()
    for i in range(STEPS):
        y = TREAD * i + TREAD / 2
        if y < y0 + 0.1 or y > y1 - 0.1:
            continue
        zb = RISE + (y - y0) * slope + 0.14
        zt = RISE + (y - y0) * slope + 0.92
        bq.beam(bm, 0.04, 0.04, zt - zb, chamfer=0.004, segs=2, seed=30 + i, center=(0, y, (zb + zt) / 2))
    return [bq.obj_from_bm("Oak__Balustrade", bm, smooth_angle=40)], {"pivot_note": "origin at the floor under the lower newel; climbs +Y at 36.5 degrees; upper newel foot at z=1.6"}


ASSETS = {"Stair_Brick_9": stair, "Landing_Block": landing_block, "Landing_Deck": landing_deck, "Landing_Rail": landing_rail, "Stair_Handrail": stair_handrail}
