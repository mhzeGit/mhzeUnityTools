"""TEMPLATE asset module: one file per asset FAMILY (boilers, pipes, masonry, timber, doors, structure, stairs, props ...).

Copy to <env>/assets/<family>.py and replace the example assets. The three examples below are also what selftest.py builds.

Contract (run_asset.py, gm_export.py, gm_angles.py, export_deliverables.py and the Unity importers all rely on it)
  * the module exposes   ASSETS = {"Asset_Name": build_function, ...}
  * build_function() returns either  [objects]  or  ([objects], {"pivot_note": "...", "regions": {...}})
  * every object is a Blender mesh created with bq.obj_from_bm(name, bm, smooth_angle=...) and is NAMED  <Region>__<Part>
      Region = the future texturing region (Pine, Iron, Teal, Brick, Mortar, Plaster ...). One region = one surface material.
      Part   = free text. Only the text before the first "__" is read by the texturing pipeline.
  * units: 1 Blender unit = 1 metre, Z up, the visible/front side of the asset faces -Y, origin at the floor contact point
    (modular pieces: at the snapping corner or centre, document it in pivot_note). The FBX export bakes the axis conversion.
  * modular dimensions: choose round numbers so pieces snap (panels 2.4 x 4.8 m, floor slabs 2.5 m, pipes 1 m ...) and reuse a piece
    by placing it several times in layout/layout_room.py instead of modelling variants.

Non-negotiable modelling rules (from the user's corrections, see references/03-asset-kit-modelling.md)
  1. NO per-brick / per-plank / per-stone geometry for repeating surfaces. A brick wall is ONE slab (flat plane with real thickness) that carries
     a tiling brick TEXTURE. Model relief only where it changes the SILHOUETTE (a stepped plinth, a chamfered corner, a doorway reveal).
  2. Flat faces keep hard edges. Pass an honest smooth_angle to obj_from_bm: ~25-35 for boxy parts, 40-60 for rounded/lathed parts.
     bq.obj_from_bm marks edges sharper than that angle as sharp; gm_obj.py later rebuilds the final normals from the same angle.
  3. Closed, watertight solids; no zero-area or collinear n-gon faces (Graphmat aborts on them); parts of DIFFERENT regions must not share
     an exact vertex (Graphmat welds them into one component): leave 5 mm of clearance or overlap.
"""
import math

import bq


def crate():
    """Small pine crate 0.5 x 0.5 x 0.4 m with four iron corner straps. Region Pine = boards (texture), Iron = straps."""
    bm = bq.new_bm()
    bq.box(bm, (0.5, 0.5, 0.4), center=(0, 0, 0.2), bevel=0.012)
    body = bq.obj_from_bm("Pine__Body", bm, smooth_angle=30)
    bm = bq.new_bm()
    for sx in (-1, 1):
        for sy in (-1, 1):
            bq.box(bm, (0.06, 0.06, 0.42), center=(sx * 0.24, sy * 0.24, 0.2), bevel=0.004)
    straps = bq.obj_from_bm("Iron__Straps", bm, smooth_angle=30)
    return [body, straps], {"pivot_note": "origin at the centre of the base on the floor"}


def pipe_segment():
    """1 m straight pipe, radius 0.18 m, with a flange at each end (lathe = surface of revolution around local Z)."""
    r, L = 0.18, 1.0
    prof = [(0.0, 0.0), (r + 0.04, 0.0), (r + 0.04, 0.05), (r, 0.06), (r, L - 0.06), (r + 0.04, L - 0.05), (r + 0.04, L), (0.0, L)]
    bm = bq.new_bm()
    bq.lathe(bm, bq.rounded_profile(prof, 0.01, 2), segs=32)
    pipe = bq.obj_from_bm("Teal__Tube", bm, smooth_angle=50)
    return [pipe], {"pivot_note": "origin at the start of the pipe, axis +Z, length 1 m (scale Y in the layout for other lengths)"}


def brick_panel():
    """Wall panel 2.4 m wide x 4.8 m high x 0.145 m thick. ONE slab: the bricks are a tiling texture (region Brick), never geometry."""
    bm = bq.new_bm()
    bq.box(bm, (2.4, 0.145, 4.8), center=(0, 0, 2.4), bevel=0.0)
    wall = bq.obj_from_bm("Brick__Wall", bm, smooth_angle=30)
    return [wall], {"pivot_note": "origin at the floor, centred on the panel width; front face toward -Y"}


ASSETS = {"Crate_Test": crate, "Pipe_Test": pipe_segment, "Wall_Brick_Panel": brick_panel}
