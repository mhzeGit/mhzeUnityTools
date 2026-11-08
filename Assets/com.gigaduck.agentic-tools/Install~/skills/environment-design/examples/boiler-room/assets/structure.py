"""Room structure kit: flagstone floor panels, trench channel + drain grate, plaster and brick wall panels, ceiling bay.
Frames: floor pieces have their top surface at z = 0, origin at the piece centre; wall panels stand on z = 0 with the visible face toward -Y
(origin at the bottom centre); the ceiling bay hangs from z = 0 (the ceiling underside) with origin at its centre.
Regions: Flag__ (flagstone), Mortar__ (mortar / pale stone), Plaster__, Brick__, Oak__, Iron__."""
import math
import random
import bq
from mathutils import Vector, Matrix, noise

GAP = 0.05


def flag_slab(bm, cx, cy, sx, sy, th, seed, edge=0.05):
    """Chunky flagstone: irregular rounded outline, pillowed top edge, slightly uneven top (top surface at about z = 0)."""
    rng = random.Random(seed)
    hx, hy = sx / 2, sy / 2
    r = min(0.14, hx * 0.5, hy * 0.5)
    pts = []
    for (ccx, ccy, a0) in ((hx - r, hy - r, 0), (-hx + r, hy - r, 90), (-hx + r, -hy + r, 180), (hx - r, -hy + r, 270)):
        for k in range(5):
            a = math.radians(a0 + 90 * k / 4)
            rr = r * (1 + rng.uniform(-0.3, 0.3))
            pts.append(Vector((ccx + rr * math.cos(a), ccy + rr * math.sin(a))))
    # wobble the straight edges a little by displacing every outline point
    for i, p in enumerate(pts):
        pts[i] = p + Vector((rng.uniform(-0.012, 0.012), rng.uniform(-0.012, 0.012)))
    c0 = sum(pts, Vector((0, 0))) / len(pts)
    top_z = rng.uniform(-0.006, 0.010)

    def inset(p, d):
        v = p - c0
        return c0 + v * max(0.2, 1.0 - d / max(0.05, v.length))

    off = Vector((seed * 1.3, seed * 0.7, 0.0))

    def ring(d, z, noise_amp=0.0):
        vs = []
        for p in pts:
            q = inset(p, d)
            zz = z + (noise.noise(Vector((q.x * 2.2, q.y * 2.2, 0.0)) + off) * noise_amp if noise_amp else 0.0)
            vs.append(bm.verts.new((cx + q.x, cy + q.y, zz)))
        return vs
    r0 = ring(0.0, -th)
    r1 = ring(0.0, top_z - 0.035)
    r2 = ring(0.012, top_z - 0.012, 0.002)
    r3 = ring(edge, top_z, 0.003)
    r4 = ring(edge + 0.45 * min(hx, hy), top_z + 0.007, 0.006)
    n = len(pts)
    for a, b in ((r0, r1), (r1, r2), (r2, r3), (r3, r4)):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((a[i], a[j], b[j], b[i]))
    ctr = bm.verts.new((cx + c0.x, cy + c0.y, top_z + 0.011 + noise.noise(Vector((c0.x, c0.y, 1.0)) + off) * 0.004))
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((r4[i], r4[j], ctr))
    bm.faces.new(r0[::-1])
    return bm


def _flag_panel(w, d, rows, seed, name_slabs="Flag__Slabs"):
    slabs = bq.new_bm()
    y = -d / 2
    k = 0
    for (rd, widths) in rows:
        x = -w / 2
        for wi in widths:
            flag_slab(slabs, x + wi / 2, y + rd / 2, wi - GAP, rd - GAP, 0.16, seed * 10 + k)
            x += wi
            k += 1
        y += rd
    bed = bq.new_bm()
    bq.box(bed, (w, d, 0.12), center=(0, 0, -0.045 - 0.06))
    return [bq.obj_from_bm(name_slabs, slabs, smooth_angle=70), bq.obj_from_bm("Mortar__Bed", bed, smooth_angle=45)]


def floor_panel_a():
    return _flag_panel(2.5, 3.17, [(1.07, [1.15, 1.35]), (1.02, [1.4, 1.1]), (1.08, [0.9, 1.6])], 3), {"pivot_note": "2.5 x 3.17 m panel, top surface at z=0, origin at the centre"}


def floor_panel_b():
    return _flag_panel(2.5, 3.17, [(1.05, [1.45, 1.05]), (1.12, [1.0, 1.5]), (1.0, [1.3, 1.2])], 7), {"pivot_note": "2.5 x 3.17 m panel (variant B), top at z=0, origin at the centre"}


def floor_strip_w():
    return _flag_panel(1.75, 0.66, [(0.66, [1.0, 0.75])], 11), {"pivot_note": "1.75 x 0.66 m strip west of the drain trench"}


def floor_strip_e():
    return _flag_panel(0.2, 0.66, [(0.66, [0.2])], 13), {"pivot_note": "0.2 x 0.66 m strip east of the drain trench"}


def trench_channel():
    """Stone drain channel segment 1.34 x 0.66 x 0.5 m with a rebate for the grate; top at z = 0, origin at the centre of the top."""
    L = 1.34
    curb = bq.new_bm()
    prof_y_z = [(-0.33, 0.0), (-0.27, 0.0), (-0.27, -0.05), (-0.235, -0.05), (-0.235, -0.12), (0.235, -0.12), (0.235, -0.05), (0.27, -0.05), (0.27, 0.0), (0.33, 0.0), (0.33, -0.12)]
    prof_y_z += [(-0.33, -0.12)]
    # left and right curbs as two separate solids extruded along X
    for sgn in (-1, 1):
        pts = [(sgn * 0.33, 0.0), (sgn * 0.27, 0.0), (sgn * 0.27, -0.05), (sgn * 0.235, -0.05), (sgn * 0.235, -0.12), (sgn * 0.33, -0.12)]
        v0 = [curb.verts.new((-L / 2, y, z)) for (y, z) in pts]
        v1 = [curb.verts.new((L / 2, y, z)) for (y, z) in pts]
        n = len(pts)
        curb.faces.new(v0 if sgn > 0 else v0[::-1])
        curb.faces.new(v1[::-1] if sgn > 0 else v1)
        for i in range(n):
            j = (i + 1) % n
            f = (v0[i], v0[j], v1[j], v1[i]) if sgn > 0 else (v0[j], v0[i], v1[i], v1[j])
            curb.faces.new(f)
    bq.jitter(curb, 0.007, 2.2, 4)
    inner = bq.new_bm()
    # U channel below the curbs: two walls and a slightly curved invert
    prof = [(-0.33, -0.12), (-0.235, -0.12), (-0.235, -0.36), (-0.14, -0.44), (0.0, -0.46), (0.14, -0.44), (0.235, -0.36), (0.235, -0.12), (0.33, -0.12), (0.33, -0.5), (-0.33, -0.5)]
    v0 = [inner.verts.new((-L / 2, y, z)) for (y, z) in prof]
    v1 = [inner.verts.new((L / 2, y, z)) for (y, z) in prof]
    n = len(prof)
    inner.faces.new(v0)
    inner.faces.new(v1[::-1])
    for i in range(n):
        j = (i + 1) % n
        inner.faces.new((v0[j], v0[i], v1[i], v1[j]))
    return [bq.obj_from_bm("Flag__Curb", curb, smooth_angle=45), bq.obj_from_bm("Mortar__Channel", inner, smooth_angle=45)], {"pivot_note": "1.34 x 0.66 m, top at z=0, origin at the top centre; grate seats on the rebate"}


def drain_grate():
    """Cast-iron grate 1.34 x 0.60 m: two long angle bars, 10 cross bars with slots and end plates; top at z = 0 (origin at the top centre)."""
    L, Wd = 1.34, 0.54
    bm = bq.new_bm()
    for sy in (-1, 1):
        bq.box(bm, (L, 0.05, 0.06), center=(0, sy * (Wd / 2 - 0.025), -0.03), bevel=0.008, bevel_segs=1)
    n = 10
    pitch = (L - 0.1) / n
    gr = random.Random(5)
    for i in range(n):
        x = -L / 2 + 0.05 + pitch * (i + 0.5)
        bq.box(bm, (pitch * gr.uniform(0.48, 0.6), Wd - 0.08, 0.05), center=(x + gr.uniform(-0.006, 0.006), 0, -0.025), bevel=0.006, bevel_segs=1)
    for sy in (-0.16, 0.16):                                  # underside stiffeners
        bq.box(bm, (L - 0.1, 0.035, 0.05), center=(0, sy, -0.075), bevel=0.005, bevel_segs=1)
    for sx in (-1, 1):
        bq.box(bm, (0.04, Wd, 0.06), center=(sx * (L / 2 - 0.02), 0, -0.03), bevel=0.008, bevel_segs=1)
    bq.jitter(bm, 0.0015, 3.0, 5)
    return [bq.obj_from_bm("Iron__Grate", bm, smooth_angle=45)], {"pivot_note": "1.34 x 0.60 m, top at z=0, origin at the top centre"}


def plaster_panel():
    """Lime-washed plaster infill 2.4 x 4.0 x 0.14 m with undulating surface and three chipped patches that expose chunky bricks."""
    import bmesh
    W, H, T = 2.4, 4.8, 0.14
    patches = [(-0.55, 1.5, 0.78, 0.5), (0.55, 3.3, 0.62, 0.46), (0.2, 0.55, 0.5, 0.36), (-0.45, 4.2, 0.5, 0.34)]      # centre x, centre z, w, h

    def depth(x, z):
        n = noise.noise(Vector((x * 0.9 + 3.1, z * 0.7 + 1.7, 0.0))) * 0.022 + noise.noise(Vector((x * 2.6, z * 2.2, 5.0))) * 0.009 + noise.noise(Vector((x * 7.0, z * 6.0, 9.0))) * 0.004
        d = 0.04 + n
        for (cx, cz, pw, ph) in patches:
            ex = max(0.0, abs(x - cx) - pw / 2)
            ez = max(0.0, abs(z - cz) - ph / 2)
            e = math.hypot(ex, ez)
            if e < 0.14:
                d -= 0.034 * (1.0 - e / 0.14) ** 0.7
        return d

    nx, nz = 24, 48
    bm = bq.new_bm()
    xs = [-W / 2 + W * i / nx for i in range(nx + 1)]
    zs = [H * j / nz for j in range(nz + 1)]
    front = [[bm.verts.new((x, -depth(x, z), z)) for z in zs] for x in xs]
    for i in range(nx):
        for j in range(nz):
            bm.faces.new((front[i][j], front[i + 1][j], front[i + 1][j + 1], front[i][j + 1]))
    bb = [bm.verts.new((-W / 2, T, 0)), bm.verts.new((W / 2, T, 0)), bm.verts.new((W / 2, T, H)), bm.verts.new((-W / 2, T, H))]
    bm.faces.new(bb)
    bm.faces.new([front[0][j] for j in range(nz + 1)] + [bb[3], bb[0]])
    bm.faces.new([front[nx][j] for j in range(nz + 1)] + [bb[2], bb[1]])
    bm.faces.new([front[i][0] for i in range(nx + 1)] + [bb[1], bb[0]])
    bm.faces.new([front[i][nz] for i in range(nx + 1)] + [bb[2], bb[3]])
    plaster = bq.obj_from_bm("Plaster__Surface", bm, smooth_angle=60)
    # exposed brick patches: one flat slab per dent (the brick pattern is texture, not modelled brick by brick); front face sits 1.5 cm below the
    # surrounding plaster and 2 cm proud of the thinned plaster inside the dent
    patch = bq.new_bm()
    for (cx, cz, pw, ph) in patches:
        bq.box(patch, (pw - 0.04, 0.03, ph - 0.04), center=(cx, -0.010, cz), bevel=0.0)
    return [plaster, bq.obj_from_bm("Brick__Patch", patch, smooth_angle=35)], {"pivot_note": "2.4 x 4.0 m panel, visible face toward -Y, origin at the bottom centre"}


def brick_panel():
    """Brick infill wall panel 2.4 x 4.8 m: ONE flat slab (14.5 cm thick, face toward -Y). The brick pattern is a tiling texture
    (tools/make_brick_tile.py, 0.24 x 0.12 m bricks: exactly 10 bricks across and 40 courses high), not geometry."""
    W, H = 2.4, 4.8
    y0, y1 = -0.055, 0.09
    bm = bq.new_bm()
    bq.box(bm, (W, y1 - y0, H), center=(0, (y0 + y1) / 2, H / 2), bevel=0.0)
    return [bq.obj_from_bm("Brick__Wall", bm, smooth_angle=35)], {"pivot_note": "2.4 x 4.8 m panel, brick face toward -Y, origin at the bottom centre"}


def ceiling_bay():
    """Ceiling bay 2.5 (X) x 3.3 (Z north) m: main oak beam along Y through the centre, two cross joists, and a plank deck.
    Hangs from z = 0 (ceiling underside), origin at the bay centre."""
    rng = random.Random(95)
    bm = bq.new_bm()
    bq.beam(bm, 0.30, 0.36, 3.3, chamfer=0.026, segs=10, crook=0.008, twist=0.006, seed=14, center=(0, 0, -0.18), axis='Y')
    for y in (-1.0, 1.0):
        bq.beam(bm, 0.14, 0.22, 2.5, chamfer=0.016, segs=8, crook=0.005, twist=0.004, seed=int(20 + y), center=(0, y, -0.11), axis='X')
    for y in (-1.0, 1.0):                                      # wooden pegs where the joists meet the main beam
        for sx in (-1, 1):
            bq.cylinder(bm, 0.022, 0.045, segs=8, center=(sx * 0.152, y, -0.11), axis='X', bevel=0.004)
    beams = bq.obj_from_bm("Oak__Beams", bm, smooth_angle=35)
    deck = bq.new_bm()
    n = 17
    pw = 3.3 / n
    for i in range(n):
        planks = bq.new_bm()
        bq.beam(planks, 0.04, pw - 0.008, 2.5 - rng.uniform(0.0, 0.03), chamfer=0.004, segs=4, crook=0.002, seed=100 + i, axis='X')
        bq.add_bm(deck, planks, Matrix.Translation((rng.uniform(-0.01, 0.01), -1.65 + pw * (i + 0.5), 0.02)))
        planks.free()
    return [beams, bq.obj_from_bm("Oak__Deck", deck, smooth_angle=35)], {"pivot_note": "2.5 x 3.3 m bay hanging from the ceiling underside (z=0), origin at the centre; main beam runs along Y"}


ASSETS = {"Floor_Panel_A": floor_panel_a, "Floor_Panel_B": floor_panel_b, "Floor_Strip_W": floor_strip_w, "Floor_Strip_E": floor_strip_e,
          "Trench_Channel": trench_channel, "Drain_Grate": drain_grate, "Wall_Plaster_Panel": plaster_panel, "Wall_Brick_Panel": brick_panel, "Ceiling_Bay": ceiling_bay}
