# Phases 4 and 5 - model every asset in Blender, then swap the blockout for the models

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

After the blockout, finish one representative section through texturing, lighting and in-engine review before multiplying the kit. Produce the rest in controlled batches. This recipe uses scripted, headless Blender with `tools/bq.py` (bmesh helpers). There is no GUI and no Blender MCP. Each asset family is one python module in
`Docs/Art/<Name>/assets/<family>.py`; read `templates/asset_module.py` (the contract) and skim `examples/boiler-room/assets/*.py` for real patterns.

## 4.1 Conventions (never break them: the texturing and Unity importers rely on them)
* 1 Blender unit = 1 m, **Z up**, the visible/front side of an asset faces **-Y**, origin at the floor contact point (modules: at a snapping corner or centre - write it in `pivot_note`).
  `bq.export_fbx` bakes a 180 degree rotation about Z so Unity sees (x, y, z)_unity = (x, z, y)_blender (front toward Unity -Z). Verified with `python tools/bl.py test_marker.py` (+ `tools/unity/tools.py import`).
* A module exposes `ASSETS = {"Asset_Name": build_fn}`; `build_fn()` returns `[objects]` or `([objects], {"pivot_note": ...})`.
* Every mesh object is named **`<Region>__<Part>`** (`Teal__Shell`, `Iron__Door`, `Brick__Wall`, `Mortar__Cap`, `Glow__Fire`). The region = surface material = what the texturing pipeline masks.
  Reuse region names across assets (Teal, Iron, Red, Brick, Mortar, Flag, Oak, Pine, Plaster, Brass, Glow, Glass, Stoneware were the 13 of the boiler room).
* Create meshes only with `bq.obj_from_bm(name, bm, smooth_angle=...)` (it recalculates normals, fixes inverted closed islands, marks sharp edges, records `smooth_angle`).
* Round numbers for modular pieces: wall panel 2.4 x 4.8 m, floor slab 2.5 m, pipe tube 1 m (scaled in the layout), stair run of 9 x (0.2 rise, 0.27 tread).

## 4.2 The two rules the user imposed (apply from the first asset)
1. **Brick is texture, not geometry.** Walls, stair sides, landing masonry, hearth/footing and plinth cores are solid forms:
   * wall = ONE slab (`box 2.4 x 0.145 x 4.8`, region `Brick__Wall`); exposed-brick patches in a plaster wall = flat box slabs `Brick__Patch` set into the plaster (front face 1.5 cm below the surrounding plaster);
   * stairs = `bq.extrude_stairs` (one solid prism: body + stringers), landing = one box;
   * plinths = stepped lathe: course height 0.125 m, step-in 0.024 m, bevel 0.010 m (the stepping is silhouette; the brick pattern is texture), core disc, apron `sector_prism`.
   Choose relief by silhouette, parallax, shadows and closest viewing distance; neither a fixed centimetre cutoff nor a repetition count decides representation. See `representation-decisions.md`.
2. **Hard edges stay hard.** Pass an honest `smooth_angle`: 25-35 for boxy parts, 40-60 for lathed/rounded parts. Mechanism and traps:
   * `bmesh` `edge.calc_face_angle()` returns 0 until face normals exist -> `obj_from_bm` runs `recalc_face_normals` first (otherwise nothing is marked sharp and every box renders smoothed: the bug the user saw);
   * Blender's OBJ exporter ignores sharp edges (writes per-vertex normals) -> `gm_export.py` adds an Edge Split (sharp) modifier before the OBJ export;
   * the final FBX normals are rebuilt in `gm_obj.py` from the recorded angle with area-weighted corner normals, so a big flat face keeps its own normal next to a small bevel.
   * **Check it numerically**: `python tools/gm_normals.py <group>` must report ~0 deviation on flat parts; in Unity run `bo.py op NormalAudit` (large flat triangles whose vertex normals deviate from the face normal: the case study went 4012 -> 542, all remaining were legitimately curved).

## 4.3 `bq.py` cheat sheet (all return a bmesh unless noted; `matrix=` places the result)
`new_bm()`, `add_bm(dst, src, matrix)`, `xform(bm, verts, m)` | solids: `box(bm, size, center, bevel, bevel_segs, rot)`, `cylinder(bm, r, h, segs, center, axis, bevel)`,
`sphere(...)`, `dome(...)`, `slab(...)` (rounded stone slab with pillowing/jitter), `beam(...)` (hand-hewn timber: chamfered section, crook, twist, end cuts) |
revolution: `lathe(bm, [(radius, z), ...], segs)`, `rounded_profile(points, radius, steps)` (round the corners of a profile) | prisms: `extrude_yz(bm, pts, x0, x1)`,
`extrude_stairs(bm, steps, rise, tread, extra, x0, x1)`, `sector_prism(bm, r0, r1, a0, a1, z0, z1)` | details: `rivet`, `rivets_ring`, `torus_arc` (pipe elbow), `jitter` (deterministic hand-made wobble) |
pipeline: `obj_from_bm`, `fix_closed_islands`, `mesh_stats`, `bounds`, `export_fbx(path, objs)`, `import_fbx_clean(path)`, `render_set(prefix, objs)`, `render_view`, `render_wire_q`.
Build parametrically: one function with a parameter dict per size produced three boilers (Large/Medium/Small); one `elbow()` produced all pipe bends.

## 4.4 Quality bar (the `concept-art-to-3d` skill's, plus the user's "not simple")
* Not a cheap assembly of stock primitives: shape contours, thickness, joints, bevels, chamfers, rivets where the concept shows them. But do not add polygons to look sophisticated.
* Each asset: silhouette matches its crop in front/side/top; proportions measured from the crop (landmarks, ratios), not invented.
* Keep geometry where it is visible at the closest play distance; delete geometry that no view shows.
* Triangle guide from the case study: hero shell 17-19k, plinth 1.8-2.7k, wall slab 12, pipe tube 1k, gauge 3.8k, bucket 5k, whole kit 119k, scene ~301k.
* Closed, watertight solids for everything that has volume; no zero-area faces; no collinear n-gons on caps (Graphmat aborts: "Triangle N has zero area").
* Different regions must not share an exact vertex (Graphmat welds coincident vertices -> one component -> one region): clearances/overlaps of 5 mm (core disc bottom z=0.01, lantern glass 5 mm clear of its frame, stair fill overlapping the body by 5 mm).

## 4.5 The per-asset loop (at least one revision, at most five)
```
python tools/bl.py run_asset.py -- <module> <Asset> v0 wire              # build -> .blend -> FBX -> RE-IMPORT -> clay renders (q, qb, front, side, top, wire) -> JSON stats
python tools/review.py <Asset> v0 reference/crops/<crop>.png             # sheet: [reference | 3/4 | back 3/4] over [front | side | top | wire]
```
Open `.utmp/<slug>/renders/<Asset>_v0_sheet.png`. Write the mismatches against the crop (proportions, missing parts, wrong silhouette, thickness, facets). Patch the module, run `v1`, compare again.
Finish with tag `final` for every module (`python tools/bl.py run_asset.py -- <module> ALL final`), then `python tools/bl.py gm_angles.py` (records each part's smooth angle in `groups/smooth_angles.json`).
Outputs per asset in `.utmp/<slug>/`: `blender/<Asset>_<tag>.blend`, `fbx/<Asset>_<tag>.fbx`, `renders/*.png`, `logs/<Asset>_<tag>.json` (verts, tris, dimensions from the RE-IMPORTED FBX).
Per-family hints distilled from the build:
* hero shells: lathe with a rounded profile + rivet rings + bands + dome + neck + collar; frame = four bars so the recess is real; gauge = bezel + 12 ticks + needle + siphon.
* pipes: lathe tube with end lips; elbow = torus arc with stubs and collars (bend radius 0.40 m); bands/brackets/flanges as separate assets so the layout can combine them.
* stairs: `extrude_stairs`; fans/quads for caps so no collinear n-gon; handrail posts and rail as one asset; landing deck planks.
* timber: chamfered beams with slight crook/twist; end cuts for braces; stone caps as separate Mortar region.
* doors: plank door + iron straps + frame; threshold smooth_angle 25.
* floor: use a slab with a shared painted paving pattern by default; model only meaningful raised edges, holes, steps or contour changes. Chunky separate flagstones were an example-specific choice, not a requirement.

## GATE 4
- [ ] every inventory row is modelled or consciously texture-only (tick it in PROGRESS.md)
- [ ] a review sheet per asset was opened; mismatches/fixes listed; >= 1 revision each
- [ ] `final` build of every module OK; `Verification/assets.json` totals; marker test done once

## 5. Replace the blockout with the models (clay swap)
1. `python tools/unity/tools.py import` (copies `*_final.fbx` into `<base>/Content/Environments/<Name>/Models/<Asset>.fbx`), `python tools/unity/tools.py prefabs`
   (prefab `<prefix><Asset>` with a flat clay material per region colour), then fill `layout/layout_room.py` (one `put()` per instance; `put_ax()`/`tube()`/`elbow()` for pipes and beams;
   groups mirror the families; 235 placements in the case study) and `python tools/unity/tools.py assemble` (writes the NEW scene `<Name>.unity`; keep the blockout scene). Check the target does not exist; in a shared editor adapt creation to additive mode. Subsequent batches use scoped object additions/edits, never scene regeneration.
2. Capture the hero, overview (ceiling hidden), stairs, corners, entrance, ceiling with `unity/cap.py`; compare with the concept and with the blockout; fix the LAYOUT first (positions, rotations, scales),
   assets second. A prop that "does not fit" means the blockout number was wrong: fix the number in both layout files.
3. Do not continue while anything is missing, floating, intersecting or the wrong scale.

## GATE 5
- [ ] clay hero + overview + stairs + corners + entrance + ceiling captured and inspected; side-by-side with the concept; fix list closed
