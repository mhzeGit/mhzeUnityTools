"""Pipe kit (nominal diameter 0.40 m, radius 0.20).  Scale the cross-section (X/Y in Unity = X/Z here, see each asset) to
get other diameters; every detail (bands, rivets, flanges) scales with it like the thicker pipes in the concept.
Frame: tube axis = local +Z, origin at the start of the part.  Regions: Teal__ (pipe iron), Iron__ (bolts, clamps)."""
import math
import random
import bq
from mathutils import Vector, Matrix

R = 0.20


def _wobbly_tube(bm, length, r, segs=28, rings=6, seed=1, amp=0.006):
    prof = [(0, 0), (r - 0.005, 0), (r + 0.014, 0.008), (r + 0.014, 0.03), (r, 0.045)] + [(r, length * i / rings) for i in range(1, rings)] + [(r, length - 0.045), (r + 0.014, length - 0.03), (r + 0.014, length - 0.008), (r - 0.005, length), (0, length)]
    bq.lathe(bm, prof, segs)
    bq.jitter(bm, amp, 0.8, seed)


def tube():
    """Plain pipe, 1.0 m long, closed ends (stretch Z freely), with a riveted longitudinal seam strap on the back (+Y)."""
    bm = bq.new_bm()
    _wobbly_tube(bm, 1.0, R, 28, 8, seed=2)
    strap = bq.new_bm()
    bq.box(strap, (0.07, 0.018, 0.9), center=(0, 0, 0.5), bevel=0.005, bevel_segs=1)
    for i in range(9):
        z = 0.1 + i * 0.1
        for sx in (-0.024, 0.024):
            bq.rivet(strap, Vector((sx, 0.009, z)), Vector((0, 1, 0)), 0.011, 0.008, 6, 1)
    bq.add_bm(bm, strap, Matrix.Translation((0, R + 0.006, 0)))
    strap.free()
    return [bq.obj_from_bm("Teal__Tube", bm, smooth_angle=40)], {"pivot_note": "axis +Z from origin, length 1 m, diameter 0.4 m; seam strap on +Y"}


def elbow():
    """90 degree elbow with straight 0.14 m stubs and a collar band at each end: bend radius 0.40 m.
    Starts at the origin heading +Z, ends at (0.54, 0, 0.54) heading +X."""
    b, stub = 0.40, 0.14
    bm = bq.new_bm()
    # stub 1 (open towards the start), arc, stub 2
    s1 = bq.new_bm()
    bq.lathe(s1, [(R, 0.0), (R, stub + 0.005)], 28)
    bq.add_bm(bm, s1)
    s1.free()
    arc = bq.new_bm()
    bq.torus_arc(arc, R, b, math.pi / 2, 24, 14, matrix=Matrix.Translation((0, 0, stub)))
    bq.add_bm(bm, arc)
    arc.free()
    s2 = bq.new_bm()
    bq.lathe(s2, [(R, -0.005), (R, stub)], 28)
    bq.add_bm(bm, s2, Matrix.Translation((b, 0, stub + b)) @ Matrix.Rotation(math.pi / 2, 4, 'Y'))
    s2.free()
    bq.jitter(bm, 0.005, 0.9, 4)
    bands = bq.new_bm()
    prof = bq.rounded_profile([(R - 0.01, 0.0), (R + 0.045, 0.0), (R + 0.054, 0.012), (R + 0.054, 0.118), (R + 0.045, 0.13), (R - 0.01, 0.13)], 0.008, 1)

    def one_band(m):
        tmp = bq.new_bm()
        bq.lathe(tmp, prof, 28)
        for i in range(8):
            a = math.tau * i / 8 + 0.2
            bq.rivet(tmp, Vector(((R + 0.054) * math.cos(a), (R + 0.054) * math.sin(a), 0.065)), Vector((math.cos(a), math.sin(a), 0)), 0.022, 0.016, 8, 2)
        bq.add_bm(bands, tmp, m)
        tmp.free()
    one_band(Matrix.Translation((0, 0, -0.018)))
    one_band(Matrix.Translation((b + stub + 0.018, 0, stub + b)) @ Matrix.Rotation(-math.pi / 2, 4, 'Y'))
    shell = bq.obj_from_bm("Teal__Elbow", bm, smooth_angle=40)
    band_obj = bq.obj_from_bm("Teal__ElbowBands", bands, smooth_angle=40)
    return [shell, band_obj], {"pivot_note": "starts at origin heading +Z; ends at (0.54, 0, 0.54) heading +X"}


def band():
    """Slide-over collar band: 0.15 m long ring with rolled edges and 8 big rivets; fits a 0.40 m pipe."""
    bm = bq.new_bm()
    prof = bq.rounded_profile([(R - 0.004, -0.09), (R + 0.04, -0.09), (R + 0.06, -0.073), (R + 0.06, 0.073), (R + 0.04, 0.09), (R - 0.004, 0.09)], 0.014, 2)
    bq.lathe(bm, prof, 32)
    bq.jitter(bm, 0.003, 1.0, 9)
    for i in range(8):
        a = math.tau * i / 8 + 0.15
        bq.rivet(bm, Vector(((R + 0.058) * math.cos(a), (R + 0.058) * math.sin(a), 0.0)), Vector((math.cos(a), math.sin(a), 0)), 0.024, 0.017, 8, 2)
    # a thicker lap joint where the band closes
    bq.box(bm, (0.12, 0.012, 0.18), center=(0, -(R + 0.064), 0), bevel=0.004, bevel_segs=1)
    for sx in (-0.04, 0.04):
        bq.rivet(bm, Vector((sx, -(R + 0.07), 0.0)), Vector((0, -1, 0)), 0.02, 0.014, 8, 2)
    return [bq.obj_from_bm("Teal__Band", bm, smooth_angle=40)], {"pivot_note": "centred on the axis (+Z)"}


def ceiling_collar():
    """Flanged collar where a pipe passes through the ceiling.  Origin at the ceiling plane centre; hangs 0.2 m below (-Z)."""
    bm = bq.new_bm()
    prof = bq.rounded_profile([(R + 0.01, -0.22), (R + 0.075, -0.22), (R + 0.075, -0.07), (R + 0.17, -0.06), (R + 0.17, 0.0), (R + 0.01, 0.0)], 0.01, 2)
    bq.lathe(bm, prof, 36)
    for i in range(10):
        a = math.tau * i / 10
        bq.rivet(bm, Vector(((R + 0.12) * math.cos(a), (R + 0.12) * math.sin(a), -0.06)), Vector((0, 0, -1)), 0.03, 0.024, 8, 2)
    bq.jitter(bm, 0.004, 0.9, 12)
    return [bq.obj_from_bm("Iron__CeilingCollar", bm, smooth_angle=40)], {"pivot_note": "origin at the ceiling plane, pipe axis +Z, hangs below"}


def bracket():
    """Wall clamp for a horizontal pipe along X: strap around the pipe, two ears and a wall plate behind (+Y)."""
    bm = bq.new_bm()
    ring = [(R + 0.002, -0.04), (R + 0.022, -0.04), (R + 0.022, 0.04), (R + 0.002, 0.04)]
    # half-strap (front half) as a lathe sweep around the axis X
    tmp = bq.new_bm()
    bq.lathe(tmp, bq.rounded_profile(ring + [ring[0]], 0.004, 1), 24, start_angle=math.radians(-30), sweep=math.radians(240))
    bq.add_bm(bm, tmp, Matrix.Rotation(math.pi / 2, 4, 'Y') @ Matrix.Rotation(math.radians(-90 + 90), 4, 'Z'))
    tmp.free()
    for sz in (-0.17, 0.17):
        bq.box(bm, (0.06, 0.05, 0.04), center=(sz, R + 0.03, 0.0), bevel=0.008, bevel_segs=1)
    bq.box(bm, (0.40, 0.025, 0.14), center=(0, R + 0.09, 0), bevel=0.01, bevel_segs=1)       # wall plate
    for sz in (-0.17, 0.17):
        bq.rivet(bm, Vector((sz, R + 0.1, 0.0)), Vector((0, 1, 0)), 0.02, 0.015, 8, 2)
    return [bq.obj_from_bm("Iron__Bracket", bm, smooth_angle=40)], {"pivot_note": "pipe axis along X through the origin; wall is at +Y"}


def cap():
    """Domed end cap that slides over a 0.40 m pipe end (axis +Z, opening at z=0)."""
    bm = bq.new_bm()
    prof = bq.rounded_profile([(R - 0.004, 0.0), (R + 0.045, 0.0), (R + 0.052, 0.012), (R + 0.052, 0.10), (R + 0.03, 0.15), (R * 0.6, 0.215), (0.0, 0.235)], 0.015, 2)
    bq.lathe(bm, prof, 32)
    for i in range(8):
        a = math.tau * i / 8
        bq.rivet(bm, Vector(((R + 0.052) * math.cos(a), (R + 0.052) * math.sin(a), 0.055)), Vector((math.cos(a), math.sin(a), 0)), 0.021, 0.015, 8, 2)
    bq.jitter(bm, 0.004, 0.9, 15)
    return [bq.obj_from_bm("Teal__Cap", bm, smooth_angle=40)], {"pivot_note": "opening at the origin facing -Z, dome toward +Z"}


def base_flange():
    """Floor collar for a vertical pipe: a tall riveted band with a flared foot (outer radius 1.7 x the pipe radius), 10 big rivets.
    Origin on the floor, axis +Z, 0.30 m tall."""
    bm = bq.new_bm()
    prof = bq.rounded_profile([(R + 0.01, 0.30), (R + 0.07, 0.30), (R + 0.088, 0.27), (R + 0.088, 0.09), (R + 0.135, 0.06), (R + 0.145, 0.0), (R + 0.01, 0.0)], 0.014, 2)
    bq.lathe(bm, prof, 40)
    bq.jitter(bm, 0.004, 0.9, 17)
    for i in range(10):
        a = math.tau * i / 10 + 0.2
        bq.rivet(bm, Vector(((R + 0.088) * math.cos(a), (R + 0.088) * math.sin(a), 0.19)), Vector((math.cos(a), math.sin(a), 0)), 0.028, 0.022, 8, 2)
    for i in range(10):
        a = math.tau * i / 10
        bq.rivet(bm, Vector(((R + 0.12) * math.cos(a), (R + 0.12) * math.sin(a), 0.062)), Vector((0, 0, 1)), 0.022, 0.016, 8, 2)
    return [bq.obj_from_bm("Teal__BaseFlange", bm, smooth_angle=40)], {"pivot_note": "origin on the floor, pipe axis +Z, 0.30 m tall"}


ASSETS = {"Pipe_Tube_1m": tube, "Pipe_Elbow90": elbow, "Pipe_Band": band, "Pipe_CeilingCollar": ceiling_collar, "Pipe_Bracket": bracket, "Pipe_Cap": cap, "Pipe_BaseFlange": base_flange}
