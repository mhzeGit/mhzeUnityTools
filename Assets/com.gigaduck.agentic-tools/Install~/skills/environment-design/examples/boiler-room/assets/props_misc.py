"""Small props: bucket, broom handle, bottles, shelf bench.  Origin = centre of the base on the floor (z = 0)."""
import math
import bq
from mathutils import Vector, Matrix


def bucket():
    """Dark sheet-metal pail with a rolled rim, two embossed hoops, bottom bead and a drop-down bail.
    Height 0.46 m, rim diameter 0.50 m, base diameter 0.38 m (read from the concept: wide mouth, slight taper)."""
    H, R_TOP, R_BOT, T = 0.46, 0.25, 0.19, 0.012
    # outer wall: bottom bead, tapered wall with two hoops, rolled rim
    def wall_r(z):          # tapered wall radius at height z
        return R_BOT + (R_TOP - R_BOT) * (z / H)
    outer = [(0.0, 0.0), (R_BOT - 0.012, 0.0), (R_BOT + 0.006, 0.006), (R_BOT + 0.008, 0.026), (wall_r(0.03), 0.034)]
    for hz in (0.13, 0.30):                           # embossed hoops
        outer += [(wall_r(hz - 0.03), hz - 0.03), (wall_r(hz - 0.012) + 0.008, hz - 0.012), (wall_r(hz + 0.012) + 0.008, hz + 0.012), (wall_r(hz + 0.03), hz + 0.03)]
    outer += [(wall_r(H - 0.035), H - 0.035), (R_TOP + 0.004, H - 0.02), (R_TOP + 0.014, H - 0.008), (R_TOP + 0.014, H + 0.006),
              (R_TOP + 0.004, H + 0.016), (R_TOP - 0.008, H + 0.012)]
    # inner wall back down to the floor (thickness T), floor raised 0.02 from the bottom
    inner = [(R_TOP - T - 0.004, H - 0.01), (wall_r(H - 0.03) - T, H - 0.03), (wall_r(0.05) - T, 0.05), (R_BOT - T - 0.004, 0.026), (0.0, 0.026)]
    prof = bq.rounded_profile(outer + inner, 0.006, 2)
    bm = bq.new_bm()
    bq.lathe(bm, prof, segs=32)
    body = bq.obj_from_bm("Iron__Body", bm, smooth_angle=40)

    # bail ear lugs (small riveted plates) and the wire bail resting down against the +X side
    bm = bq.new_bm()
    for sgn in (-1, 1):
        x = sgn * (wall_r(H - 0.06) + 0.012)
        bq.box(bm, (0.012, 0.05, 0.05), center=(x, 0, H - 0.06), bevel=0.004, bevel_segs=2)
        bq.box(bm, (0.02, 0.016, 0.016), center=(x + sgn * 0.012, 0, H - 0.06), bevel=0.004, bevel_segs=2)
        bq.sphere(bm, 0.007, center=(x + sgn * 0.007, 0.016, H - 0.075), segs=8, rings=4)
        bq.sphere(bm, 0.007, center=(x + sgn * 0.007, -0.016, H - 0.045), segs=8, rings=4)
    lugs = bq.obj_from_bm("Iron__Ears", bm, smooth_angle=40)

    # bail: wire arc (semicircle) hinged at the two ears, swung down to lie against the +Y side
    bm = bq.new_bm()
    r_wire, r_arc = 0.0075, wall_r(H - 0.06) + 0.012
    n_arc, n_wire = 18, 8
    ring_pts = []
    for i in range(n_arc + 1):
        a = math.pi * i / n_arc
        c = Vector((r_arc * math.cos(a), 0.0, r_arc * math.sin(a)))            # arc in the XZ plane, apex up
        t = Vector((-math.sin(a), 0.0, math.cos(a)))                           # tangent
        ring_pts.append((c, t))
    rings = []
    for c, t in ring_pts:
        t = t.normalized()
        ax = Vector((0, 1, 0))
        bn = t.cross(ax).normalized()
        ring = []
        for k in range(n_wire):
            ang = math.tau * k / n_wire
            ring.append(bm.verts.new(c + ax * (math.cos(ang) * r_wire) + bn * (math.sin(ang) * r_wire)))
        rings.append(ring)
    for k in range(len(rings) - 1):
        for i in range(n_wire):
            j = (i + 1) % n_wire
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    # swing the bail down toward +Y by 62 degrees about the ear axis (X axis through the ears), then place at ear height
    m = Matrix.Translation((0, 0, H - 0.06)) @ Matrix.Rotation(math.radians(62), 4, 'X')
    bq.xform(bm, bm.verts[:], m)
    bail = bq.obj_from_bm("Iron__Bail", bm, smooth_angle=60)
    return [body, lugs, bail], {"pivot_note": "origin at base centre, z up; mouth faces +Z"}


def broom_handle():
    """Plain turned wooden handle (stick) 1.25 m long, 0.032 m diameter, rounded ends and a hang-hole boss near the top."""
    L, R = 1.25, 0.016
    prof = [(0.0, 0.0), (R * 0.55, 0.0), (R * 0.9, 0.012), (R, 0.04)] + [(R, L - 0.05)] + [(R * 0.95, L - 0.02), (R * 0.7, L - 0.006), (0.0, L)]
    bm = bq.new_bm()
    bq.lathe(bm, bq.rounded_profile(prof, 0.01, 2), segs=14)
    # a tiny collar ring where the broom head would sit (concept shows a bare stick leaning in the bucket)
    bq.cylinder(bm, R + 0.004, 0.012, segs=14, center=(0, 0, L - 0.07))
    o = bq.obj_from_bm("Pine__Handle", bm, smooth_angle=40)
    return [o], {"pivot_note": "origin at the foot of the stick, long axis +Z"}


ASSETS = {"Bucket": bucket, "BroomHandle": broom_handle}
