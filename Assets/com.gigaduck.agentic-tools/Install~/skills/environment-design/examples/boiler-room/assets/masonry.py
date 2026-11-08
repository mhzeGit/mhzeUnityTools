"""Brick masonry assets: boiler plinths and the SE pipe footing block.  Frame: origin at the centre of the plinth on the floor (z = 0); the front
(hearth) faces -Y.

The bricks are NOT modelled one by one: every masonry form is a solid with clean faces and the brick pattern comes from the tiling brick texture
(see tools/make_brick_tile.py), applied and ambient-occlusion-baked through Graphmat. Modelled relief is kept only where it changes the silhouette
(the stepped courses of the plinth rings and the hearth apron).
Regions: Brick__ (brick-faced solids), Mortar__Core (the stone core inside a plinth ring)."""
import math
import bq
from mathutils import Vector

COURSE = 0.125          # modelled course height of the plinth rings
STEP_IN = 0.024         # each course sits this much further in than the one below (must exceed BEVEL, or the profile doubles back)
BEVEL = 0.010           # chamfer on every course edge, catches the light and the outline


def plinth(kind):
    spec = {"Large": dict(Ro=1.40, H=0.50, apron=True), "Medium": dict(Ro=1.17, H=0.50, apron=False), "Small": dict(Ro=0.90, H=0.45, apron=False)}[kind]
    Ro, H = spec["Ro"], spec["H"]
    courses = int(round(H / COURSE))
    ch = H / courses
    Ri = Ro - 0.20                                     # inner edge of the brick ring: inside it the stone core shows
    # profile (radius, z) of the ring: bottom centre -> out along the floor -> up the stepped courses -> over the top -> down inside -> axis
    prof = [(0.0, 0.0), (Ro, 0.0)]
    r = Ro
    for k in range(courses):
        z1 = ch * (k + 1)
        prof.append((r, z1 - BEVEL))
        prof.append((r - BEVEL, z1))
        if k < courses - 1:
            r -= STEP_IN
            prof.append((r, z1))
    prof.append((Ri, H))
    prof.append((Ri, H - 0.02))
    prof.append((0.0, H - 0.02))
    bricks = bq.new_bm()
    bq.lathe(bricks, prof, {"Large": 80, "Medium": 64, "Small": 56}[kind])
    apron_info = {}
    if spec["apron"]:
        # hearth apron in front of the firebox: two stepped blocks over +-40 / +-34 degrees around the front (-Y = 270 degrees)
        for (reach, height, half) in ((0.17, 0.14, 40.0), (0.07, 0.27, 34.0)):
            a0, a1 = math.radians(270 - half), math.radians(270 + half)
            bq.sector_prism(bricks, Ro - 0.12, Ro + reach, a0, a1, 0.0, height, segs=16)
        apron_info = {"apron_reach_m": 0.17}
    brick_obj = bq.obj_from_bm("Brick__Bricks", bricks, smooth_angle=40)
    core = bq.new_bm()
    bq.lathe(core, [(0.0, 0.01), (Ri + 0.02, 0.01), (Ri + 0.02, H - 0.012), (0.0, H - 0.012)], 48)      # bottom 1 cm up: no vertex shared with the brick ring
    core_obj = bq.obj_from_bm("Mortar__Core", core, smooth_angle=50)
    return [brick_obj, core_obj], {"pivot_note": "origin at the plinth centre on the floor; hearth faces -Y; solid stepped courses, brick pattern is texture",
                                    "height_m": H, "outer_radius_m": Ro, **apron_info}


def footing():
    """Brick footing block under the SE corner pipe: 1.0 x 1.05 x 0.46 m, a body and a slightly wider cap, both brick (the pattern is texture)."""
    W, D, H, CAP = 1.0, 1.05, 0.46, 0.04
    body = bq.new_bm()
    bq.box(body, (W, D, H - CAP), center=(0, 0, (H - CAP) / 2), bevel=0.008, bevel_segs=1)
    cap = bq.new_bm()
    bq.box(cap, (W + 0.04, D + 0.04, CAP), center=(0, 0, H - CAP / 2), bevel=0.008, bevel_segs=1)
    return [bq.obj_from_bm("Brick__Bricks", body, smooth_angle=40), bq.obj_from_bm("Brick__Cap", cap, smooth_angle=40)], \
           {"pivot_note": "origin at the centre of the base on the floor; brick body with a slightly wider brick cap, top at z=0.46"}


ASSETS = {"Plinth_Large": lambda: plinth("Large"), "Plinth_Medium": lambda: plinth("Medium"), "Plinth_Small": lambda: plinth("Small"), "Brick_Footing": footing}
