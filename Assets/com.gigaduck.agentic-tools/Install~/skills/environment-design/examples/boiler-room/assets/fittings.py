"""Boiler fittings: pressure gauge, red valve wheel, valve body with drain pipe.
Frame: origin on the shell surface at the mounting point; fitting axis points -Y (toward the viewer); Z up.
Regions: Iron__ (dark iron), Brass__ (cream dial face / brass), Red__ (painted valve wheel)."""
import math
import bq
from mathutils import Vector, Matrix

NEG_Y = Matrix.Rotation(math.pi / 2, 4, 'X')      # maps local +Z to -Y (lathe axis -> viewer)


def _lathe_obj(name, profile, segs, mat=None, smooth=40):
    bm = bq.new_bm()
    bq.lathe(bm, profile, segs, matrix=mat)
    return bq.obj_from_bm(name, bm, smooth_angle=smooth)


def gauge():
    """Round pressure dial (no lettering): thick iron bezel, cream face with tick marks and a black needle,
    mounting boss and a pig-tail siphon nut underneath.  Bezel diameter 0.46 m, depth 0.12 m."""
    Ro, Ri = 0.23, 0.185
    # bezel ring: rounded chunky profile (z toward the viewer after rotation)
    ring = [(Ri, 0.035), (Ri, 0.09), (Ri + 0.012, 0.105), (Ro - 0.02, 0.112), (Ro, 0.092), (Ro, 0.045), (Ro - 0.012, 0.03), (Ri + 0.01, 0.022), (Ri, 0.035)]
    bezel = _lathe_obj("Iron__Bezel", bq.rounded_profile(ring, 0.006, 2), 40, NEG_Y @ Matrix.Translation((0, 0, 0)))
    # screws on the bezel (4)
    bm = bq.new_bm()
    for i in range(4):
        a = math.radians(45 + 90 * i)
        bq.rivet(bm, Vector((0.205 * math.cos(a), 0.0, 0.205 * math.sin(a))) , Vector((0, -1, 0)), 0.012, 0.01, 8, 2)
    # back plate / boss that meets the shell
    plate = bq.cylinder(bm, 0.205, 0.03, segs=36, center=(0, -0.015, 0), axis='Y', bevel=0.008)
    # siphon (pig-tail) below: short neck, hex nut and a fat acorn-shaped pot
    bq.cylinder(bm, 0.026, 0.07, segs=12, center=(0, -0.03, -0.245), axis='Z', bevel=0.004)
    bq.cylinder(bm, 0.04, 0.03, segs=6, center=(0, -0.03, -0.27), axis='Z')
    bq.lathe(bm, bq.rounded_profile([(0, -0.355), (0.035, -0.355), (0.058, -0.335), (0.062, -0.30), (0.044, -0.285), (0, -0.285)], 0.01, 2), 14,
             matrix=Matrix.Translation((0, -0.03, 0)))
    mount = bq.obj_from_bm("Iron__Mount", bm, smooth_angle=40)
    # dial face disc (cream), slightly domed, with tick marks
    face_prof = [(0, 0.058), (0.05, 0.059), (0.12, 0.062), (Ri - 0.004, 0.066), (Ri - 0.004, 0.05), (0, 0.05)]
    fb = bq.new_bm()
    bq.lathe(fb, face_prof[:4] + [(Ri - 0.004, 0.05), (0, 0.05)], 40, matrix=NEG_Y)
    for i in range(12):                                      # clock-style tick marks
        a = math.radians(30 * i)
        big = i % 3 == 0
        bq.box(fb, (0.016 if big else 0.012, 0.008, 0.045 if big else 0.03), center=(0.145 * math.cos(a), -0.068, 0.145 * math.sin(a)), bevel=0.002, bevel_segs=1,
               rot=(0, math.pi / 2 - a, 0))
    face = bq.obj_from_bm("Brass__Face", fb, smooth_angle=50)
    # needle: bold pointed black blade toward about 7 o'clock + hub
    nb = bq.new_bm()
    ang = math.radians(-112)
    L = 0.155
    tip = Vector((L * math.cos(ang), -0.078, L * math.sin(ang)))
    tail = Vector((-0.045 * math.cos(ang), -0.078, -0.045 * math.sin(ang)))
    perp = Vector((-math.sin(ang), 0, math.cos(ang))) * 0.016
    v0, v1, v2 = nb.verts.new(tail + perp), nb.verts.new(tail - perp), nb.verts.new(tip)
    v3 = nb.verts.new(Vector((0, -0.078, 0)) + perp * 1.15)
    v4 = nb.verts.new(Vector((0, -0.078, 0)) - perp * 1.15)
    nb.faces.new((v0, v3, v2, v4, v1))
    bmesh_extrude(nb, nb.faces[:], Vector((0, -0.01, 0)))
    bq.cylinder(nb, 0.03, 0.024, segs=14, center=(0, -0.088, 0), axis='Y', bevel=0.005)
    needle = bq.obj_from_bm("Iron__Needle", nb, smooth_angle=60)
    return [bezel, mount, face, needle], {"pivot_note": "origin on the shell surface behind the dial; dial faces -Y; siphon hangs below (-Z)"}


def bmesh_extrude(bm, faces, offset):
    import bmesh
    ret = bmesh.ops.extrude_face_region(bm, geom=faces)
    verts = [e for e in ret["geom"] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=offset, verts=verts)
    return ret


def _torus_ring(bm, big_r, small_r, segs_big=40, segs_small=10):
    prof = [(big_r + small_r * math.cos(math.tau * i / segs_small), small_r * math.sin(math.tau * i / segs_small)) for i in range(segs_small + 1)]
    bq.lathe(bm, prof, segs_big)
    return bm


def valve_wheel():
    """Cast red hand wheel: chunky rim, six tapered spokes, domed hub with nut.  Diameter 0.56 m, axis -Y."""
    Rw, t = 0.245, 0.031
    bm = bq.new_bm()
    rim = bq.new_bm()
    _torus_ring(rim, Rw, t, 44, 10)
    bq.xform(rim, rim.verts[:], NEG_Y)
    bq.add_bm(bm, rim)
    rim.free()
    for i in range(6):
        a = math.tau * i / 6 + 0.12
        sp = bq.new_bm()
        bq.box(sp, (Rw - 0.06, 0.036, 0.05), center=(0, 0, 0), bevel=0.007, bevel_segs=1)
        # paddle taper: wide at the hub, narrower at the rim
        for v in sp.verts:
            k = (v.co.x + (Rw - 0.06) / 2) / (Rw - 0.06)
            v.co.z *= 1.0 - 0.38 * k
            v.co.y *= 1.0 - 0.20 * k
        m = Matrix.Rotation(a, 4, 'Y') @ Matrix.Translation(((Rw - 0.06) / 2 + 0.045, 0, 0))
        bq.add_bm(bm, sp, m)
        sp.free()
    # hub: collar behind, domed front, nut
    hub = bq.new_bm()
    bq.lathe(hub, bq.rounded_profile([(0, -0.05), (0.085, -0.05), (0.085, 0.015), (0.06, 0.038), (0, 0.045)], 0.012, 2), 24, matrix=NEG_Y)
    bq.add_bm(bm, hub)
    hub.free()
    wheel = bq.obj_from_bm("Red__Wheel", bm, smooth_angle=45)
    nut = bq.new_bm()
    bq.cylinder(nut, 0.034, 0.034, segs=6, center=(0, -0.058, 0), axis='Y', bevel=0.005)
    nutobj = bq.obj_from_bm("Iron__Nut", nut, smooth_angle=45)
    return [wheel, nutobj], {"pivot_note": "origin at the wheel centre; axis -Y; wheel plane is X/Z"}


def valve_body():
    """Valve housing for the side of a boiler: round boss plate with bolts, flanged bonnet, stem and yoke, and a drain pipe with elbow.
    Axis -Y (stem points at the viewer where the wheel sits 0.24 m out).  Origin on the shell surface."""
    bm = bq.new_bm()
    bq.cylinder(bm, 0.19, 0.05, segs=28, center=(0, -0.025, 0), axis='Y', bevel=0.01)                  # boss plate
    for i in range(6):
        a = math.tau * i / 6
        bq.rivet(bm, Vector((0.15 * math.cos(a), -0.05, 0.15 * math.sin(a))), Vector((0, -1, 0)), 0.02, 0.016, 8, 2)
    bq.cylinder(bm, 0.125, 0.14, segs=24, center=(0, -0.12, 0), axis='Y', bevel=0.012)                 # bonnet
    bq.cylinder(bm, 0.158, 0.035, segs=24, center=(0, -0.083, 0), axis='Y', bevel=0.008)               # flange ring
    for i in range(8):
        a = math.tau * i / 8
        bq.rivet(bm, Vector((0.14 * math.cos(a), -0.1, 0.14 * math.sin(a))), Vector((0, -1, 0)), 0.014, 0.012, 6, 1)
    bq.cylinder(bm, 0.075, 0.07, segs=20, center=(0, -0.22, 0), axis='Y', bevel=0.01)                  # yoke cap
    bq.cylinder(bm, 0.024, 0.22, segs=12, center=(0, -0.30, 0), axis='Y', bevel=0.004)                 # stem
    for sx in (-1, 1):                                                                                     # yoke arms
        bq.box(bm, (0.03, 0.14, 0.03), center=(sx * 0.07, -0.17, 0.0), bevel=0.006, bevel_segs=1)
    body = bq.obj_from_bm("Iron__Valve", bm, smooth_angle=40)
    # drain pipe: nipple down from the bonnet, 90 degree elbow back toward the shell, tiny flange
    pm = bq.new_bm()
    bq.cylinder(pm, 0.034, 0.2, segs=14, center=(0, -0.12, -0.28), axis='Z', bevel=0.004)
    bq.cylinder(pm, 0.05, 0.03, segs=14, center=(0, -0.12, -0.19), axis='Z', bevel=0.005)
    # arc starts heading local +Z and bends toward local +X: map +Z -> world -Z (down) and +X -> world +Y (toward the shell)
    M = Matrix(((0, 1, 0, 0), (1, 0, 0, 0), (0, 0, -1, 0), (0, 0, 0, 1)))
    bq.torus_arc(pm, 0.034, 0.075, math.pi / 2, 12, 8, matrix=Matrix.Translation((0, -0.12, -0.38)) @ M)
    bq.cylinder(pm, 0.05, 0.03, segs=14, center=(0, -0.05, -0.455), axis='Y', bevel=0.005)
    drain = bq.obj_from_bm("Teal__Drain", pm, smooth_angle=40)
    return [body, drain], {"pivot_note": "origin on the shell surface; stem points -Y; drain pipe hangs below"}


ASSETS = {"Gauge": gauge, "ValveWheel": valve_wheel, "ValveBody": valve_body}
