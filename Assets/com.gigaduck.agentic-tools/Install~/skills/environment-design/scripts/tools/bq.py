"""bq - modeling helpers for an environment asset kit (Blender 4.2+, built on 5.2, headless).

Conventions
-----------
* 1 Blender unit = 1 metre. Blender Z is up. Assets are modelled so that, after export (see export_fbx),
  Unity sees the same shape with Unity +X = Blender +X, Unity +Y = Blender +Z, Unity +Z = Blender +Y
  (i.e. what you see looking along Blender +Y is what Unity looks at looking along +Z).
* An asset is a set of mesh objects whose names start with a region prefix:  Iron__Shell, Brick__Plinth ...
  The prefix is the future texturing region (see the environment README and references/03-asset-kit-modelling.md); no appearance is authored here.
* Geometry is built with bmesh; helpers return the bmesh so parts can be merged before conversion.
"""
import bpy
import bmesh
import math
import os
import random
import json
from mathutils import Vector, Matrix, noise

TAU = math.tau
PI = math.pi


# --------------------------------------------------------------------------- scene helpers
def clean_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    s = bpy.context.scene
    s.unit_settings.system = 'METRIC'
    s.unit_settings.scale_length = 1.0
    return s


def collection(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def fix_closed_islands(bm):
    """recalc_face_normals can leave one of many touching closed solids (e.g. a brick in a ring) inside out; flip every closed
    island whose signed volume is negative. Open shells are left alone (their orientation is judged by the recalc only)."""
    bm.faces.ensure_lookup_table()
    seen = set()
    flipped = 0
    for f in bm.faces:
        if f.index in seen:
            continue
        stack, comp = [f], []
        seen.add(f.index)
        while stack:
            cur = stack.pop()
            comp.append(cur)
            for e in cur.edges:
                for g in e.link_faces:
                    if g.index not in seen:
                        seen.add(g.index)
                        stack.append(g)
        if not all(len(e.link_faces) == 2 for fc in comp for e in fc.edges):
            continue
        n = sum(len(fc.verts) for fc in comp)
        c = sum((v.co for fc in comp for v in fc.verts), Vector()) / n
        vol = 0.0
        for fc in comp:
            vs = [v.co - c for v in fc.verts]
            for k in range(1, len(vs) - 1):
                vol += vs[0].dot(vs[k].cross(vs[k + 1])) / 6.0
        if vol < 0:
            for fc in comp:
                fc.normal_flip()
            flipped += 1
    return flipped


def obj_from_bm(name, bm, coll=None, smooth_angle=35.0, location=(0, 0, 0)):
    """Turn a bmesh into a mesh object. Faces are smooth-shaded; edges sharper than smooth_angle are marked sharp.
    The face normals must be correct BEFORE the angles are measured: a bmesh built with verts.new/faces.new has zero face normals until they are
    recalculated, calc_face_angle() then returns 0 for every edge, nothing is marked sharp and every hard edge renders smoothed."""
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    fix_closed_islands(bm)
    bm.normal_update()
    mark_sharp(bm, smooth_angle)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    o.location = location
    o["smooth_angle"] = float(smooth_angle)           # read by gm_angles.py: the final normals are rebuilt from it (see gm_obj.py)
    (coll or bpy.context.scene.collection).objects.link(o)
    return o


def mark_sharp(bm, angle_deg):
    lim = math.radians(angle_deg)
    for f in bm.faces:
        f.smooth = True
    for e in bm.edges:
        if len(e.link_faces) == 2:
            try:
                a = e.calc_face_angle()
            except ValueError:
                a = 0
            e.smooth = a < lim
        else:
            e.smooth = False


def new_bm():
    return bmesh.new()


# --------------------------------------------------------------------------- transforms on bmesh geometry
def xform(bm, verts, m):
    bmesh.ops.transform(bm, matrix=m, verts=verts)


def add_bm(dst, src, matrix=None):
    """Copy src bmesh geometry (with optional matrix) into dst bmesh."""
    vmap = {}
    for v in src.verts:
        co = (matrix @ v.co) if matrix is not None else v.co.copy()
        vmap[v] = dst.verts.new(co)
    for f in src.faces:
        try:
            nf = dst.faces.new([vmap[v] for v in f.verts])
            nf.smooth = f.smooth
        except ValueError:
            pass
    for e in src.edges:
        if e.verts[0] in vmap and e.verts[1] in vmap:
            de = dst.edges.get((vmap[e.verts[0]], vmap[e.verts[1]]))
            if de is not None:
                de.smooth = e.smooth
    return dst


def align_z_to(normal, up_hint=(0, 0, 1)):
    """Matrix rotating local +Z onto 'normal'."""
    n = Vector(normal).normalized()
    z = Vector((0, 0, 1))
    q = z.rotation_difference(n)
    return q.to_matrix().to_4x4()


# --------------------------------------------------------------------------- primitives
def lathe(bm, profile, segs=24, closed=False, matrix=None, start_angle=0.0, sweep=TAU):
    """Surface of revolution around local Z.  profile: [(radius, z), ...] bottom to top.
    radius==0 points collapse to a single pole vertex.  closed=True also closes top/bottom with the profile's own
    first/last points (profile should then start/end on the axis for a solid)."""
    full = abs(sweep - TAU) < 1e-6
    n_around = segs if full else segs + 1
    rings = []
    for (r, z) in profile:
        if r < 1e-7:
            rings.append([bm.verts.new((0, 0, z))])
        else:
            ring = []
            for i in range(n_around):
                a = start_angle + sweep * i / segs
                ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), z)))
            rings.append(ring)
    faces = []
    for k in range(len(rings) - 1):
        a, b = rings[k], rings[k + 1]
        if len(a) == 1 and len(b) == 1:
            continue
        if len(a) == 1:
            for i in range(segs):
                j = (i + 1) % len(b) if full else i + 1
                faces.append(bm.faces.new((a[0], b[j], b[i])))
        elif len(b) == 1:
            for i in range(segs):
                j = (i + 1) % len(a) if full else i + 1
                faces.append(bm.faces.new((a[i], a[j], b[0])))
        else:
            for i in range(segs):
                j = (i + 1) % len(a) if full else i + 1
                faces.append(bm.faces.new((a[i], a[j], b[j], b[i])))
    if matrix is not None:
        allv = [v for ring in rings for v in ring]
        bmesh.ops.transform(bm, matrix=matrix, verts=allv)
    return bm


def rounded_profile(points, radius, steps=3):
    """Round the corners of a polyline [(r,z)...] with the given radius (clamped per corner). Returns a new polyline."""
    pts = [Vector(p) for p in points]
    out = [pts[0].copy()]
    for i in range(1, len(pts) - 1):
        p0, p1, p2 = pts[i - 1], pts[i], pts[i + 1]
        d1 = (p0 - p1)
        d2 = (p2 - p1)
        l1, l2 = d1.length, d2.length
        if l1 < 1e-9 or l2 < 1e-9:
            out.append(p1.copy())
            continue
        d1n, d2n = d1 / l1, d2 / l2
        ang = math.acos(max(-1.0, min(1.0, d1n.dot(d2n))))
        if ang > math.pi - 1e-3 or ang < 1e-3:
            out.append(p1.copy())
            continue
        t = radius / math.tan(ang / 2)
        t = min(t, l1 * 0.49, l2 * 0.49)
        a = p1 + d1n * t
        b = p1 + d2n * t
        for s in range(steps + 1):
            u = s / steps
            # quadratic bezier a -> p1 -> b
            q = (1 - u) * (1 - u) * a + 2 * (1 - u) * u * p1 + u * u * b
            out.append(q)
    out.append(pts[-1].copy())
    return [(p.x, p.y) for p in out]


def box(bm, size, center=(0, 0, 0), bevel=0.0, bevel_segs=2, rot=None):
    """Axis-aligned box (size = full dims); bevel>0 rounds all edges."""
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    if bevel > 0:
        bmesh.ops.bevel(b, geom=b.edges[:] + b.verts[:], offset=min(bevel, min(size) * 0.49), segments=bevel_segs,
                        profile=0.5, affect='EDGES')
    m = Matrix.Translation(center)
    if rot is not None:
        m = m @ Matrix.Rotation(rot[0], 4, 'X') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[2], 4, 'Z') if isinstance(rot, (tuple, list)) else m @ rot
    add_bm(bm, b, m)
    b.free()
    return bm


def extrude_yz(bm, pts, x0, x1):
    """Prism: the polygon `pts` [(y, z), ...] (counter-clockwise seen from +X) extruded along X from x0 to x1. Flat caps are single n-gons,
    side walls are quads, so a staircase profile stays a few dozen faces. Returns bm."""
    n = len(pts)
    a = [bm.verts.new((x0, y, z)) for (y, z) in pts]
    b = [bm.verts.new((x1, y, z)) for (y, z) in pts]
    bm.faces.new(a[::-1])          # -X cap
    bm.faces.new(b)                # +X cap
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((a[i], a[j], b[j], b[i]))
    return bm


def extrude_stairs(bm, steps, rise, tread, extra, x0, x1):
    """Solid staircase prism: `steps` risers of height `rise` and treads of depth `tread`, every tread top `extra` metres higher, from x0 to x1;
    climbs toward +Y from y = 0 (z = 0 is the floor). Built from triangle fans and quads only: the side caps of a staircase are concave and
    their nose vertices are collinear, so a single n-gon would triangulate into zero-area triangles."""
    tops = [rise * (j + 1) + extra for j in range(steps)]
    ys = [tread * k for k in range(steps + 1)]
    V = {}

    def vert(x, y, z):
        key = (round(x, 6), round(y, 6), round(z, 6))
        if key not in V:
            V[key] = bm.verts.new((x, y, z))
        return V[key]

    def quad(a, b, c, d):
        bm.faces.new((a, b, c, d))

    for x, flip in ((x0, True), (x1, False)):
        for j in range(steps):
            y, y2, T = ys[j], ys[j + 1], tops[j]
            A, B, C, D = vert(x, y, 0.0), vert(x, y2, 0.0), vert(x, y2, T), vert(x, y, T)
            if j == 0:
                tris = [(A, B, C), (A, C, D)]
            else:
                E = vert(x, y, tops[j - 1])
                tris = [(A, B, E), (B, C, E), (C, D, E)]
            for t in tris:
                bm.faces.new(t[::-1] if flip else t)
    for j in range(steps):                                      # treads, bottom strip, risers
        y, y2, T = ys[j], ys[j + 1], tops[j]
        quad(vert(x0, y, T), vert(x1, y, T), vert(x1, y2, T), vert(x0, y2, T))
        quad(vert(x0, y, 0.0), vert(x0, y2, 0.0), vert(x1, y2, 0.0), vert(x1, y, 0.0))
        z0 = tops[j - 1] if j else 0.0
        quad(vert(x0, y, z0), vert(x0, y, T), vert(x1, y, T), vert(x1, y, z0))
    yb, tb = ys[steps], tops[steps - 1]
    quad(vert(x0, yb, 0.0), vert(x1, yb, 0.0), vert(x1, yb, tb), vert(x0, yb, tb))
    return bm


def sector_prism(bm, r0, r1, a0, a1, z0, z1, segs=12):
    """Annular sector (radii r0 < r1, angles a0..a1 in radians around Z) extruded from z0 to z1: a solid block of a ring."""
    out = [(r1 * math.cos(a0 + (a1 - a0) * i / segs), r1 * math.sin(a0 + (a1 - a0) * i / segs)) for i in range(segs + 1)]
    inn = [(r0 * math.cos(a0 + (a1 - a0) * i / segs), r0 * math.sin(a0 + (a1 - a0) * i / segs)) for i in range(segs, -1, -1)]
    ring = out + inn
    lo = [bm.verts.new((x, y, z0)) for (x, y) in ring]
    hi = [bm.verts.new((x, y, z1)) for (x, y) in ring]
    bm.faces.new(lo[::-1])
    bm.faces.new(hi)
    n = len(ring)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((lo[i], lo[j], hi[j], hi[i]))
    return bm


def cylinder(bm, radius, height, segs=20, center=(0, 0, 0), axis='Z', bevel=0.0, bevel_segs=2, cap=True):
    """Capped cylinder centred at 'center' with axis along X/Y/Z. bevel rounds the two rims."""
    h = height / 2
    prof = [(0, -h), (radius, -h), (radius, h), (0, h)] if cap else [(radius, -h), (radius, h)]
    if bevel > 0 and cap:
        prof = rounded_profile([(0, -h), (radius, -h), (radius, h), (0, h)], bevel, bevel_segs)
    m = Matrix.Translation(center)
    if axis == 'X':
        m = m @ Matrix.Rotation(math.pi / 2, 4, 'Y')
    elif axis == 'Y':
        m = m @ Matrix.Rotation(-math.pi / 2, 4, 'X')
    lathe(bm, prof, segs, matrix=m)
    return bm


def sphere(bm, radius, center=(0, 0, 0), segs=16, rings=8, scale=(1, 1, 1)):
    prof = [(0, -radius)] + [(radius * math.sin(math.pi * i / rings), -radius * math.cos(math.pi * i / rings)) for i in range(1, rings)] + [(0, radius)]
    m = Matrix.Translation(center) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    lathe(bm, prof, segs, matrix=m)
    return bm


def dome(bm, radius, height, center=(0, 0, 0), segs=16, rings=5, axis_dir=(0, 0, 1)):
    """Half-ellipsoid dome (base on the plane through center), pointing along axis_dir."""
    prof = [(radius, 0)] + [(radius * math.cos(math.pi / 2 * i / rings), height * math.sin(math.pi / 2 * i / rings)) for i in range(1, rings)] + [(0, height)]
    m = Matrix.Translation(center) @ align_z_to(axis_dir)
    lathe(bm, prof, segs, matrix=m)
    return bm


def rivet(bm, pos, normal, radius=0.02, height=0.014, segs=8, rings=2):
    dome(bm, radius, height, center=pos, segs=segs, rings=rings, axis_dir=normal)
    return bm


def rivets_ring(bm, center, axis_radius, z, count, radius=0.02, height=0.014, phase=0.0, segs=8, skip=None, out=True):
    """Rivets around a vertical cylinder of axis_radius at height z, domes pointing outwards."""
    for i in range(count):
        if skip and skip(i):
            continue
        a = phase + TAU * i / count
        n = Vector((math.cos(a), math.sin(a), 0))
        p = Vector(center) + n * axis_radius + Vector((0, 0, z))
        rivet(bm, p, n if out else -n, radius, height, segs)
    return bm


def torus_arc(bm, radius, bend_radius, angle, segs_around=14, segs_arc=10, matrix=None):
    """Pipe elbow: circle of 'radius' swept around Z axis centred at (bend_radius,0,0) in the XZ plane... Starts at origin
    heading +Z... Implemented by sweeping a circle (in the XY plane at the origin) about the pivot (bend_radius, 0, 0) around the Y axis."""
    ring = [bm.verts.new((radius * math.cos(TAU * i / segs_around), radius * math.sin(TAU * i / segs_around), 0)) for i in range(segs_around)]
    rings = [ring]
    for k in range(1, segs_arc + 1):
        a = angle * k / segs_arc
        # rotate circle about the pivot axis (parallel to Y through x = -bend_radius?) -- place pivot at x=+bend_radius
        piv = Vector((bend_radius, 0, 0))
        rot = Matrix.Rotation(a, 3, 'Y')
        nr = []
        for v in ring:
            p = rot @ (v.co - piv) + piv
            nr.append(bm.verts.new(p))
        rings.append(nr)
    for k in range(len(rings) - 1):
        for i in range(segs_around):
            j = (i + 1) % segs_around
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    if matrix is not None:
        allv = [v for r in rings for v in r]
        bmesh.ops.transform(bm, matrix=matrix, verts=allv)
    return bm


def jitter(bm, amp, scale=1.0, seed=0, along_normal=True, only_z=False):
    """Low-frequency hand-made wobble (deterministic)."""
    off = Vector((seed * 17.31, seed * 5.77, seed * 9.13))
    bm.normal_update()
    for v in bm.verts:
        n = noise.noise(v.co * scale + off)
        if along_normal:
            v.co += v.normal * (n * amp)
        else:
            d = Vector((noise.noise(v.co * scale + off + Vector((11, 0, 0))),
                        noise.noise(v.co * scale + off + Vector((0, 23, 0))),
                        0 if only_z else noise.noise(v.co * scale + off + Vector((0, 0, 37)))))
            v.co += d * amp


# --------------------------------------------------------------------------- counting / metadata
def mesh_stats(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    verts = tris = 0
    for o in objs:
        if o.type != 'MESH':
            continue
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        verts += len(me.vertices)
        tris += sum(len(p.vertices) - 2 for p in me.polygons)
        e.to_mesh_clear()
    return verts, tris


def bounds(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    mn = Vector((1e9, 1e9, 1e9))
    mx = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        if o.type != 'MESH':
            continue
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        for v in me.vertices:
            w = o.matrix_world @ v.co
            mn = Vector((min(mn.x, w.x), min(mn.y, w.y), min(mn.z, w.z)))
            mx = Vector((max(mx.x, w.x), max(mx.y, w.y), max(mx.z, w.z)))
        e.to_mesh_clear()
    return mn, mx


# --------------------------------------------------------------------------- export / re-import
EXPORT_ROT = Matrix.Rotation(PI, 4, 'Z')   # see module docstring; verified against Unity import


def export_fbx(path, objs, export_matrix=None):
    """Export objs as one FBX. Geometry is copied, transformed by export_matrix (default: 180deg about Z so that
    Unity sees +X/+Z like Blender +X/+Y) and exported with the project's usual axis settings. Originals untouched."""
    export_matrix = EXPORT_ROT if export_matrix is None else export_matrix
    bpy.ops.object.select_all(action='DESELECT')
    dups = []
    names = [(o, o.name) for o in objs]
    datanames = [(o, o.data.name) for o in objs]
    for o, n in names:
        o.name = n + '__src'
        o.data.name = n + '__src'
    for o, n in names:
        d = o.copy()
        d.data = o.data.copy()
        bpy.context.scene.collection.objects.link(d)
        d.matrix_world = export_matrix @ o.matrix_world
        d.data.transform(d.matrix_world)
        d.matrix_world = Matrix.Identity(4)
        d.name = n
        d.data.name = n
        dups.append(d)
    for d in dups:
        d.select_set(True)
    bpy.context.view_layer.objects.active = dups[0]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, global_scale=1.0, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
        axis_forward='-Z', axis_up='Y', bake_space_transform=True, object_types={'MESH'}, use_mesh_modifiers=True,
        mesh_smooth_type='EDGE', add_leaf_bones=False, path_mode='STRIP', embed_textures=False, bake_anim=False,
        use_tspace=False)
    for d in dups:
        me = d.data
        bpy.data.objects.remove(d)
        bpy.data.meshes.remove(me)
    for o, n in names:
        o.name = n
    for o, dn in datanames:
        o.data.name = dn
    return path


def import_fbx_clean(path, restore_authoring=True):
    """Fresh empty scene, import the FBX, return imported mesh objects.
    The exporter bakes a 180 deg Z rotation so Unity sees the intuitive axes; with restore_authoring the imported
    objects are rotated back for viewing so review renders use the authoring frame (front = -Y)."""
    clean_scene()
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=path, axis_forward='-Z', axis_up='Y')
    objs = [o for o in bpy.data.objects if o not in before and o.type == 'MESH']
    if restore_authoring:
        r = EXPORT_ROT.inverted()
        for o in objs:
            o.matrix_world = r @ o.matrix_world
        bpy.context.view_layer.update()
    return objs


# --------------------------------------------------------------------------- review renders
TEXTURED = False


def setup_render(w=900, h=700):
    s = bpy.context.scene
    s.render.engine = 'BLENDER_WORKBENCH'
    s.render.resolution_x, s.render.resolution_y = w, h
    s.render.resolution_percentage = 100
    s.render.film_transparent = False
    sh = s.display.shading
    if TEXTURED:
        sh.light = 'FLAT'
        sh.color_type = 'TEXTURE'
        sh.show_cavity = False
    else:
        sh.light = 'STUDIO'
        sh.color_type = 'SINGLE'
        sh.single_color = (0.74, 0.70, 0.64)
        sh.show_cavity = True
        sh.cavity_type = 'BOTH'
        sh.cavity_ridge_factor = 1.0
        sh.cavity_valley_factor = 1.0
    sh.show_object_outline = True
    sh.object_outline_color = (0.03, 0.03, 0.03)
    sh.show_specular_highlight = False
    wd = s.world or bpy.data.worlds.new('w')
    s.world = wd
    wd.color = (0.13, 0.14, 0.16)
    s.view_settings.view_transform = 'Standard'
    return s


def _camera():
    s = bpy.context.scene
    cam = bpy.data.objects.get('RV_Cam')
    if cam is None:
        cd = bpy.data.cameras.new('RV_Cam')
        cam = bpy.data.objects.new('RV_Cam', cd)
        s.collection.objects.link(cam)
    s.camera = cam
    return cam


def aim(cam, center, direction_from_center, dist):
    d = Vector(direction_from_center).normalized()
    cam.location = Vector(center) + d * dist
    cam.rotation_euler = (Vector(center) - cam.location).to_track_quat('-Z', 'Y').to_euler()


def render_view(path, mn, mx, kind, w=900, h=700, margin=1.12):
    """kind: 'q' three-quarter, 'qb' back three-quarter, 'front' (looking +Y), 'back', 'side' (looking -X), 'top'."""
    s = setup_render(w, h)
    cam = _camera()
    center = (mn + mx) / 2
    size = mx - mn
    cam.data.clip_start, cam.data.clip_end = 0.005, 1000
    if kind in ('q', 'qb'):
        cam.data.type = 'PERSP'
        cam.data.sensor_fit = 'AUTO'
        cam.data.angle = math.radians(30)
        az = math.radians(35 if kind == 'q' else 215)
        el = math.radians(22)
        d = Vector((math.cos(el) * math.sin(az), -math.cos(el) * math.cos(az), math.sin(el)))
        radius = size.length / 2
        vfov = 2 * math.atan(math.tan(cam.data.angle / 2) * min(1.0, h / w))
        dist = radius / math.sin(vfov / 2) * 1.04
        aim(cam, center, d, dist)
    else:
        cam.data.type = 'ORTHO'
        views = {'front': ((0, -1, 0), size.x, size.z), 'back': ((0, 1, 0), size.x, size.z),
                 'side': ((1, 0, 0), size.y, size.z), 'top': ((0, 0, 1), size.x, size.y)}
        dvec, eu, ev = views[kind]
        cam.data.ortho_scale = max(eu, ev * w / h) * margin
        aim(cam, center, dvec, max(size) * 3 + 5)
        if kind == 'top':
            cam.rotation_euler = (0, 0, 0)
            cam.location = Vector((center.x, center.y, mx.z + 10))
    s.render.filepath = path
    s.render.image_settings.file_format = 'PNG'
    bpy.ops.render.render(write_still=True)
    return path


def render_set(prefix, objs, views=('q', 'front', 'side', 'top', 'qb'), w=900, h=700):
    mn, mx = bounds(objs)
    out = {}
    for k in views:
        out[k] = render_view(f"{prefix}_{k}.png", mn, mx, k, w, h)
    return out


def render_wire_q(path, objs, w=900, h=700, az_deg=35, el_deg=22):
    """Three-quarter wireframe: clay render darkened, front-facing edges drawn on top (numpy)."""
    import numpy as np
    from bpy_extras.object_utils import world_to_camera_view
    mn, mx = bounds(objs)
    tmp = path + '.tmp.png'
    render_view(tmp, mn, mx, 'q', w, h)
    img = bpy.data.images.load(tmp)
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    px[..., :3] *= 0.30
    s = bpy.context.scene
    cam = s.camera
    dg = bpy.context.evaluated_depsgraph_get()
    campos = cam.matrix_world.translation
    segs = []
    for o in objs:
        e = o.evaluated_get(dg)
        me = e.to_mesh()
        mw = o.matrix_world
        co = np.array([(mw @ v.co)[:] for v in me.vertices], dtype=np.float32)
        ndc = np.array([world_to_camera_view(s, cam, Vector(c))[:] for c in co], dtype=np.float32)
        me.calc_loop_triangles()
        front = np.zeros(len(me.edges), dtype=bool)
        ekey = {tuple(sorted(ed.vertices)): i for i, ed in enumerate(me.edges)}
        for p in me.polygons:
            nrm = mw.to_3x3() @ p.normal
            cen = mw @ p.center
            if nrm.dot(campos - cen) > 0:
                vs = list(p.vertices)
                for a, b in zip(vs, vs[1:] + vs[:1]):
                    front[ekey[tuple(sorted((a, b)))]] = True
        for i, ed in enumerate(me.edges):
            if front[i]:
                a, b = ndc[ed.vertices[0]], ndc[ed.vertices[1]]
                segs.append((a[0] * w, (1 - a[1]) * h, b[0] * w, (1 - b[1]) * h))
        e.to_mesh_clear()
    if segs:
        arr = np.array(segs, dtype=np.float32)
        n = np.clip(np.max(np.abs(arr[:, [2, 3]] - arr[:, [0, 1]]), axis=1).astype(int) + 1, 2, 400)
        for (x0, y0, x1, y1), k in zip(arr, n):
            ts = np.linspace(0, 1, k)
            xs = (x0 + (x1 - x0) * ts).astype(int)
            ys = (y0 + (y1 - y0) * ts).astype(int)
            ok = (xs >= 0) & (xs < w) & (ys >= 0) & (ys < h)
            px[h - 1 - ys[ok], xs[ok], :3] = (1.0, 0.78, 0.25)   # image rows are bottom-up
    out = bpy.data.images.new('wire', w, h, alpha=False)
    out.pixels = px.ravel().tolist()
    out.filepath_raw = path
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)
    bpy.data.images.remove(img)
    os.remove(tmp)
    return path


# --------------------------------------------------------------------------- woodwork / masonry helpers
def beam(bm, sx, sy, length, chamfer=0.02, segs=8, crook=0.0, twist=0.0, seed=0, center=(0, 0, 0), axis='Z', end_cut=(0.0, 0.0)):
    """Hand-hewn timber: chamfered rectangular section swept along its axis with low-frequency crook and twist.
    sx, sy = section size; length along the chosen axis ('X','Y','Z') centred at 'center'.
    end_cut = (a0, a1): shear of each end along the length per unit of local x (angled cut for braces)."""
    hx, hy, c = sx / 2, sy / 2, min(chamfer, min(sx, sy) * 0.45)
    pts = [(-hx + c, -hy), (hx - c, -hy), (hx, -hy + c), (hx, hy - c), (hx - c, hy), (-hx + c, hy), (-hx, hy - c), (-hx, -hy + c)]
    off = Vector((seed * 3.7, seed * 1.3, seed * 5.9))
    rings = []
    for k in range(segs + 1):
        z = -length / 2 + length * k / segs
        t = k / segs
        ox = noise.noise(Vector((t * 2.1, 0.0, 0.0)) + off) * crook
        oy = noise.noise(Vector((0.0, t * 2.1, 0.0)) + off) * crook
        ang = noise.noise(Vector((0.0, 0.0, t * 1.7)) + off) * twist
        ca, sa = math.cos(ang), math.sin(ang)
        ring = []
        for (x, y) in pts:
            xr, yr = x * ca - y * sa + ox, x * sa + y * ca + oy
            zz = z
            if k == 0:
                zz += end_cut[0] * x
            if k == segs:
                zz += end_cut[1] * x
            ring.append(bm.verts.new((xr, yr, zz)))
        rings.append(ring)
    n = len(pts)
    for k in range(segs):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((rings[k][i], rings[k][j], rings[k + 1][j], rings[k + 1][i]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    allv = [v for r in rings for v in r]
    m = Matrix.Translation(center)
    if axis == 'X':
        m = m @ Matrix.Rotation(math.pi / 2, 4, 'Y')
    elif axis == 'Y':
        m = m @ Matrix.Rotation(-math.pi / 2, 4, 'X')
    bmesh.ops.transform(bm, matrix=m, verts=allv)
    return bm


def slab(bm, sx, sy, sz, center=(0, 0, 0), bevel=0.03, bevel_segs=2, dome=0.0, jit=0.0, seed=0):
    """Rounded stone slab / block with an optional dome on top and per-vertex jitter."""
    b = bmesh.new()
    bmesh.ops.create_cube(b, size=1.0)
    for v in b.verts:
        v.co = Vector((v.co.x * sx, v.co.y * sy, v.co.z * sz))
    if bevel > 0:
        bmesh.ops.bevel(b, geom=b.edges[:] + b.verts[:], offset=min(bevel, min(sx, sy, sz) * 0.45), segments=bevel_segs, profile=0.5, affect='EDGES')
    rng = random.Random(seed)
    off = Vector((seed * 1.7, seed * 2.3, seed * 0.9))
    for v in b.verts:
        if v.co.z > 0:
            if dome:
                r2 = (v.co.x / (sx / 2)) ** 2 + (v.co.y / (sy / 2)) ** 2
                v.co.z += dome * max(0.0, 1.0 - r2 * 0.5)
            if jit:
                v.co.z += noise.noise(Vector((v.co.x * 2.0, v.co.y * 2.0, 0.0)) + off) * jit
        if jit:
            v.co.x += rng.uniform(-jit, jit) * 0.4
            v.co.y += rng.uniform(-jit, jit) * 0.4
    add_bm(bm, b, Matrix.Translation(center))
    b.free()
    return bm
