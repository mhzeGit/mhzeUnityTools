"""Boiler kit: three riveted iron boilers (large centre, medium right, small left) built from one parametric shell.

Asset frame: origin = centre of the boiler on the plinth's top plane (z = 0); front faces -Y; Z up.
The brick plinths, gauges, valve wheels and pipes are separate assets placed by the room layout.
Region prefixes (future texture regions): Teal__ (riveted iron), Iron__ (black iron hardware), Glow__ (furnace glow).
"""
import math
import bq
from mathutils import Vector, Matrix

FRONT = -math.pi / 2          # azimuth of the front (-Y)

# name: (R, base_h, body_h, band_h, dome_h, neck_r, neck_h, collar_r, seams)
SIZES = {
    "Large":  dict(R=1.25, base_h=0.22, body_h=1.90, band_h=0.38, dome_h=0.33, neck_r=0.38, neck_h=0.46, collar_r=0.62, rv=0.056),
    "Medium": dict(R=1.00, base_h=0.18, body_h=1.50, band_h=0.31, dome_h=0.27, neck_r=0.30, neck_h=0.34, collar_r=0.50, rv=0.047),
    "Small":  dict(R=0.75, base_h=0.15, body_h=1.05, band_h=0.24, dome_h=0.21, neck_r=0.24, neck_h=0.28, collar_r=0.40, rv=0.040),
}


def _cap_arc(r0, r1, dh, n):
    """Spherical-cap arc from (r0, 0) up to (r1, dh): list of (r, z)."""
    c = (dh * dh + r1 * r1 - r0 * r0) / (2 * dh)
    rho = math.hypot(r0, c)
    a0, a1 = math.asin(min(1.0, r0 / rho)), math.asin(min(1.0, r1 / rho))
    pts = []
    for i in range(n + 1):
        a = a0 + (a1 - a0) * i / n
        pts.append((rho * math.sin(a), c + rho * math.cos(a)))
    return pts


def shell_profile(p):
    R = p["R"]
    zb = p["base_h"]
    zt = zb + p["body_h"]
    prof = [(0.0, 0.0), (R + 0.03, 0.0)]
    # base ring (thick flange band around the foot)
    prof += [(R + 0.075, 0.025), (R + 0.075, zb - 0.045), (R + 0.045, zb - 0.005)]
    # body wall
    prof += [(R, zb + 0.01), (R, zt - 0.02)]
    # shoulder band: overhanging lip, slanted riveted face, rolled top edge
    bh = p["band_h"]
    lip = 0.075 + 0.025 * R
    prof += [(R + lip * 0.8, zt - 0.01), (R + lip, zt + 0.03), (R + lip, zt + 0.11), (R + 0.045, zt + bh - 0.07), (R + 0.0, zt + bh - 0.03), (R - 0.03, zt + bh)]
    # dome
    z0 = zt + bh
    prof += [(r, z0 + z) for (r, z) in _cap_arc(R - 0.04, p["neck_r"] + 0.10, p["dome_h"], 12)[1:]]
    # collar flange + neck
    zc = z0 + p["dome_h"]
    cr, nr = p["collar_r"], p["neck_r"]
    ch = 0.13 + 0.03 * R                                   # collar flange thickness
    prof += [(cr, zc - 0.03), (cr + 0.015, zc), (cr + 0.015, zc + ch), (cr - 0.03, zc + ch + 0.03), (nr + 0.07, zc + ch + 0.03), (nr + 0.055, zc + ch + 0.055)]
    zn = zc + ch + 0.075
    prof += [(nr, zn), (nr, zn + p["neck_h"] - 0.07), (nr + 0.04, zn + p["neck_h"] - 0.05), (nr + 0.04, zn + p["neck_h"]), (0.0, zn + p["neck_h"])]
    return prof, dict(z_body_top=zt, z_band_top=z0, z_collar=zc, z_collar_top=zc + ch + 0.03, z_top=zn + p["neck_h"], lip=lip)


def rivet_at_profile(bm, R_at, z_at, nrm, count, size, phase, front_only=None, skip=None):
    """Rivets around a rotationally symmetric surface: R_at/z_at = radius/height of the rivet row, nrm = (nr, nz) surface normal."""
    for i in range(count):
        a = phase + math.tau * i / count
        if skip and skip(a):
            continue
        n = Vector((math.cos(a) * nrm[0], math.sin(a) * nrm[0], nrm[1])).normalized()
        pos = Vector((R_at * math.cos(a), R_at * math.sin(a), z_at))
        bq.rivet(bm, pos, n, radius=size, height=size * 0.7, segs=8, rings=2)


def shell(kind):
    p = SIZES[kind]
    R = p["R"]
    prof, k = shell_profile(p)
    bm = bq.new_bm()
    bq.lathe(bm, bq.rounded_profile(prof, 0.018, 2), segs=72)
    # hand-made wobble on the whole shell (very low amplitude)
    bq.jitter(bm, amp=0.016 * R, scale=0.75, seed={"Large": 3, "Medium": 7, "Small": 11}[kind])
    rv = p["rv"]
    zt, bh, lip = k["z_body_top"], p["band_h"], k["lip"]
    # rivets: slanted face of the shoulder band, base ring, collar flange
    za, zb_ = zt + 0.11, zt + bh - 0.07                           # slanted face runs (R+lip, za) -> (R+0.045, zb_)
    tmid = 0.42
    r_mid, z_mid = (R + lip) + ((R + 0.045) - (R + lip)) * tmid, za + (zb_ - za) * tmid
    nrm = Vector((zb_ - za, (R + lip) - (R + 0.045))).normalized()  # outward normal of the slanted face in (r, z)
    rivet_at_profile(bm, r_mid + 0.004, z_mid, (nrm.x, nrm.y), 18 if kind == "Large" else 14 if kind == "Medium" else 11, rv, 0.0)
    rivet_at_profile(bm, R + 0.075, p["base_h"] * 0.5, (1.0, 0.0), 22 if kind == "Large" else 17 if kind == "Medium" else 13, rv * 0.9, 0.1)
    rivet_at_profile(bm, p["collar_r"] - 0.075, k["z_collar_top"], (0.0, 1.0), 10 if kind == "Large" else 8 if kind == "Medium" else 7, rv * 1.2, 0.2)
    # vertical butt-strap seams on the sides/back only (the front stays clean like the concept)
    for ang_deg in (15, 165, 195, 345):
        a = math.radians(ang_deg)
        strap = bq.new_bm()
        bq.box(strap, (0.11, 0.03, p["body_h"] - 0.12), center=(0, 0, 0), bevel=0.008, bevel_segs=1)
        n_riv = max(4, int(p["body_h"] / 0.3))
        for j in range(n_riv):
            for sx in (-0.036, 0.036):
                bq.rivet(strap, Vector((sx, -0.015, -p["body_h"] / 2 + 0.1 + j * (p["body_h"] - 0.2) / (n_riv - 1))), Vector((0, -1, 0)), rv * 0.55, rv * 0.4, 6, 1)
        m = Matrix.Translation((R * math.cos(a), R * math.sin(a), p["base_h"] + p["body_h"] / 2)) @ Matrix.Rotation(a + math.pi / 2, 4, 'Z')
        # strap faces +Y locally: rotate so the local -Y (rivets) points outward
        m = Matrix.Translation((R * math.cos(a), R * math.sin(a), p["base_h"] + p["body_h"] / 2)) @ Matrix.Rotation(a - math.pi / 2, 4, 'Z')
        bq.add_bm(bm, strap, m)
        strap.free()
    return bq.obj_from_bm("Teal__Shell", bm, smooth_angle=38), p, k




def _plate_with_hole(bm, outer, hole, y0, y1):
    """Extruded plate with a hole: front face at y0, back face at y1, boundary walls round the outline and the hole.
    outer / hole are (x, z) polylines; normals are fixed by bq.obj_from_bm (closed manifold)."""
    from mathutils import geometry
    pts = list(outer) + list(hole)
    tris = geometry.tessellate_polygon([[Vector((x, z, 0.0)) for (x, z) in outer], [Vector((x, z, 0.0)) for (x, z) in hole]])
    vf = [bm.verts.new((x, y0, z)) for (x, z) in pts]
    vb = [bm.verts.new((x, y1, z)) for (x, z) in pts]
    for (a, b, c) in tris:
        bm.faces.new((vf[a], vf[b], vf[c]))
        bm.faces.new((vb[c], vb[b], vb[a]))
    no, nh = len(outer), len(hole)
    for base, n in ((0, no), (no, nh)):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((vf[base + i], vf[base + j], vb[base + j], vb[base + i]))


# ------------------------------------------------------------------------- firebox (large) and hatches (medium / small)
def fire_door_parts(p, k):
    """Centre boiler furnace mouth: flat-faced iron housing proud of the shell, recessed opening, arched door with barred glowing window.
    Returns (housing_obj, door_obj, bars_obj, glow_obj)."""
    R = p["R"]
    W, Hh, D = 1.40, 0.98, 0.30           # housing width / height / depth (front face plane at y = -(R + 0.10))
    y_front = -(R + 0.10)
    zc = 0.03 + Hh / 2                                         # frame sits right on the plinth top like the concept (interrupts the base ring)
    bm = bq.new_bm()
    # outer plate (frame): hollow box = four bars so the recess is real geometry
    t = 0.11                                                   # frame bar thickness
    bar_d = D
    for (cx, cz, sx, sz) in ((0, Hh / 2 - t / 2, W, t), (0, -Hh / 2 + t / 2, W, t), (-W / 2 + t / 2, 0, t, Hh - 2 * t), (W / 2 - t / 2, 0, t, Hh - 2 * t)):
        bq.box(bm, (sx, bar_d, sz), center=(cx, y_front + bar_d / 2, zc + cz), bevel=0.018, bevel_segs=2)
    # recessed flat panel filling the frame opening (the round shell would otherwise show through and swallow the door and the glow)
    y_face = y_front + 0.05
    bq.box(bm, (W - 2 * t + 0.02, 0.25, Hh - 2 * t + 0.02), center=(0, y_face + 0.125, zc), bevel=0.0)
    # rivets on the frame face (every ~0.17 m)
    for i in range(9):
        x = -W / 2 + 0.055 + i * (W - 0.11) / 8
        for sz in (Hh / 2 - t / 2, -Hh / 2 + t / 2):
            bq.rivet(bm, Vector((x, y_front, zc + sz)), Vector((0, -1, 0)), 0.026, 0.018, 8, 2)
    for i in range(1, 5):
        z = -Hh / 2 + t / 2 + i * (Hh - t) / 5
        for sx in (-W / 2 + t / 2, W / 2 - t / 2):
            bq.rivet(bm, Vector((sx, y_front, zc + z)), Vector((0, -1, 0)), 0.026, 0.018, 8, 2)
    # lip bracket under the frame (hearth rail) and side gussets
    bq.box(bm, (W + 0.10, 0.10, 0.06), center=(0, y_front + 0.06, 0.03), bevel=0.014)
    housing = bq.obj_from_bm("Teal__FireHousing", bm, smooth_angle=35)

    # door: arched plate recessed behind the frame face, with a real arched window opening
    dm = bq.new_bm()
    dw, dh = 0.78, 0.74
    t_door = 0.05
    yd = y_face - 0.035                                        # plate front: 3.5 cm proud of the panel, 1.5 cm behind the frame face
    arch_r = dw / 2
    n_arch = 14
    z0 = zc - 0.02
    outer = [(-dw / 2, z0 - dh / 2), (dw / 2, z0 - dh / 2)]
    for i in range(n_arch + 1):
        a = math.pi * i / n_arch
        outer.append((arch_r * math.cos(a), z0 + dh / 2 - arch_r + arch_r * math.sin(a) * 0.9))
    # window opening (arched): straight sides up to zs, semi-elliptic top rising `rise`
    ww = 0.46
    zb, zs, rise = zc - 0.15, zc + 0.08, 0.17
    hole = [(-ww / 2, zb), (ww / 2, zb), (ww / 2, zs)]
    for i in range(1, 12):
        a = math.pi * i / 12
        hole.append((ww / 2 * math.cos(a), zs + rise * math.sin(a)))
    hole.append((-ww / 2, zs))
    _plate_with_hole(dm, outer, hole, yd, yd + t_door)
    # hinge knuckles / latch
    for sz in (0.22, -0.22):
        bq.cylinder(dm, 0.032, 0.14, segs=10, center=(dw / 2 + 0.01, yd - 0.01, zc + sz), axis='Z')
        bq.box(dm, (0.16, 0.035, 0.07), center=(dw / 2 - 0.06, yd - 0.01, zc + sz), bevel=0.01)
    bq.box(dm, (0.07, 0.05, 0.12), center=(-dw / 2 + 0.05, yd - 0.02, zc + 0.02), bevel=0.012)
    bq.cylinder(dm, 0.02, 0.07, segs=8, center=(-dw / 2 + 0.05, yd - 0.055, zc + 0.02), axis='Y')
    door = bq.obj_from_bm("Iron__Door", dm, smooth_angle=35)

    # window bars (5 vertical bars, bottom rail, arched top ring) in front of the opening; the glow plane sits inside the door thickness
    bmb = bq.new_bm()
    for i in range(5):
        x = -ww / 2 + i * ww / 4
        top = zs + rise * math.sqrt(max(0.0, 1 - (2 * x / ww) ** 2))
        bq.box(bmb, (0.035, 0.04, top - zb), center=(x, yd + 0.005, (top + zb) / 2), bevel=0.008, bevel_segs=1)
    bq.box(bmb, (ww + 0.05, 0.04, 0.035), center=(0, yd + 0.005, zb), bevel=0.008, bevel_segs=1)
    bq.box(bmb, (ww + 0.05, 0.04, 0.035), center=(0, yd + 0.005, zs - 0.01), bevel=0.008, bevel_segs=1)
    prev = None
    for i in range(13):
        a = math.pi * i / 12
        pt = (ww / 2 * math.cos(a), zs + rise * math.sin(a))
        if prev is not None:
            mid = ((pt[0] + prev[0]) / 2, (pt[1] + prev[1]) / 2)
            ln = math.hypot(pt[0] - prev[0], pt[1] - prev[1])
            ang = math.atan2(pt[1] - prev[1], pt[0] - prev[0])
            bq.box(bmb, (ln + 0.004, 0.04, 0.035), center=(mid[0], yd + 0.005, mid[1]), bevel=0.008, bevel_segs=1, rot=(0, -ang, 0))
        prev = pt
    bars = bq.obj_from_bm("Iron__Bars", bmb, smooth_angle=35)
    gm = bq.new_bm()
    vs = [gm.verts.new((x, y_face - 0.012, z)) for (x, z) in hole]
    gm.faces.new(vs)                                           # counter-clockwise in (x, z) -> normal -Y (toward the player)
    glow = bq.obj_from_bm("Glow__Fire", gm, smooth_angle=80)
    return housing, door, bars, glow


def hatch_parts(p, k, w, h):
    """Small rectangular iron hatch plate with two hinge brackets, bolted to the lower front of the shell."""
    R = p["R"]
    y_front = -(R + 0.035)
    zc = p["base_h"] + 0.30 + h / 2
    bm = bq.new_bm()
    bq.box(bm, (w + 0.10, 0.07, h + 0.08), center=(0, y_front, zc), bevel=0.012, bevel_segs=2)         # raised frame
    for i in range(5):
        x = -w / 2 - 0.02 + i * (w + 0.04) / 4
        bq.rivet(bm, Vector((x, y_front - 0.035, zc + h / 2 + 0.01)), Vector((0, -1, 0)), 0.018, 0.012, 8, 2)
        bq.rivet(bm, Vector((x, y_front - 0.035, zc - h / 2 - 0.01)), Vector((0, -1, 0)), 0.018, 0.012, 8, 2)
    housing = bq.obj_from_bm("Teal__HatchFrame", bm, smooth_angle=35)
    dm = bq.new_bm()
    bq.box(dm, (w, 0.05, h), center=(0, y_front - 0.05, zc), bevel=0.01, bevel_segs=2)
    for sx in (-1, 1):
        bq.box(dm, (0.07, 0.05, 0.14), center=(sx * (w / 2 + 0.04), y_front - 0.055, zc + h * 0.22), bevel=0.01)
        bq.box(dm, (0.07, 0.05, 0.14), center=(sx * (w / 2 + 0.04), y_front - 0.055, zc - h * 0.22), bevel=0.01)
    bq.cylinder(dm, 0.022, 0.07, segs=8, center=(0, y_front - 0.105, zc), axis='Y')
    door = bq.obj_from_bm("Iron__HatchDoor", dm, smooth_angle=35)
    return housing, door


def build_boiler(kind):
    sh, p, k = shell(kind)
    objs = [sh]
    if kind == "Large":
        objs += list(fire_door_parts(p, k))
    else:
        w, h = (0.70, 0.46) if kind == "Medium" else (0.50, 0.34)
        objs += list(hatch_parts(p, k, w, h))
    meta = {"pivot_note": "origin at the shell centre on the plinth top plane; front faces -Y",
            "params": {kk: vv for kk, vv in p.items()}, "z_top_of_neck": k["z_top"], "regions": sorted({o.name.split('__')[0] for o in objs})}
    return objs, meta


ASSETS = {"Boiler_Large": lambda: build_boiler("Large"), "Boiler_Medium": lambda: build_boiler("Medium"), "Boiler_Small": lambda: build_boiler("Small")}
