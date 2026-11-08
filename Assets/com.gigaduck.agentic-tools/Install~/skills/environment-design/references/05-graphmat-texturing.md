# Phase 7 - UV map, mask, texture and AO every asset with the Graphmat background CLI

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

Rules of the `model-to-textured-asset` skill that stay in force: Graphmat only through `graphmat-cli` (no GUI, no MCP, no Computer Use); the project resolution, UV target, bake and export are 2048;
masks are proven with diagnostic colours BEFORE textures are applied; base colour in this recipe (+ AO only when requested); verify the delivered mesh, not the intent. The scripts below implement exactly that.

## 7.1 Texture groups (`tools/texture_groups.py`, then `python tools/gm_groups.py`)
A **group** = assets that share one Graphmat project and one 2048 atlas. Rules:
* Plan groups with [uv-and-texture-efficiency.md](uv-and-texture-efficiency.md). Group compatible regions by required texel density, material settings and reuse; do not assign a separate atlas to every family automatically. Large structures and small fittings can share only when adequate density and compact packing are demonstrated. Prefer a shared tileable for repeated wall patterns when the pipeline supports it, rather than repeated full baked copies.
* `REGION[region] = (texture, tile_m, textureRotation)`: `tile_m` = metres of the model covered by one texture repeat (brick tile 0.96, flagstone 1.4, plaster 1.6, iron 0.6-1.2, wood 0.7-0.8, brass 0.5).
* When AO is part of the brief, `ao_radius` ~ typical feature size / 8 (hero shells 0.6, masonry 0.5, walls 0.4, floor/wood/pipes 0.3, fittings 0.2, small props 0.15). AO is baked with 64 rays, bias 0.01, and multiplied (`apply` 0.9) over the base colour of each region.
* `grm=[rx,ry,rz]` (Euler degrees) rotates an asset only while texturing (undone on import): `LONG_X=[0,90,0]` for beams (length -> Z because **wood grain follows the texture's V axis = model Z**),
  `AXIS_Y=[-90,0,0]` for round masonry (axis -> Y, the axis Graphmat's cylinder projection wraps).
* `cube=<m>` forces the bounding-cube side (see 7.3). `cylinder="Y"` = one round asset centred on its axis. `over={"Region": {"tile":..,"mode":"cylinder","textureRotation":[..],"texture":..}}` per-group overrides.
Real data: `examples/boiler-room/texture_groups.py` (17 groups for 49 assets).

## 7.2 The stages (per group; each stage is restartable)
```
python tools/gm_run.py <group> export          # Blender: build the group's assets, lay them out on a 3-axis staircase inside a CUBE (gap = max(1.5, 3 x AO radius)), anchor triangles, Edge Split on sharp edges -> model.obj + sidecar.json
python tools/gm_run.py <group> setup           # CLI: importTexture(s), autoUv 2048 pad 8 (respectHardEdges, thoroughPacking), bakeProjectionMaps, bakeAo -> uv.gmat, meshinfo.json
python tools/gm_run.py <group> masks           # CLI: one masked folder per region with a distinct diagnostic colour (addRegion by component ids) -> diagnostic.gmat, diag_*.png (8 views), legend.json
python tools/gm_run.py <group> review_diag     # Blender: unlit per-asset views of the real UV mesh with the diagnostic albedo -> review/<Asset>_diag_{q,qb,front,side,top}.png
python tools/gm_sheet.py <group> diag q,qb,top 560     # contact sheet review/_sheet_diag.png        <- OPEN IT: every part has the colour of its region? holes? bleed?
python tools/gm_run.py <group> textures        # CLI: assignTexture (triplanar, scale = tile/cube, mode/rotation overrides) + applyAo per region -> textured.gmat, albedo.png (2048), uv.obj, final_*.png
python tools/gm_run.py <group> review_final    # Blender: unlit per-asset views with the final albedo
python tools/gm_sheet.py <group> final q,qb,top 560    # <- OPEN IT: scale, orientation, seams, AO contact darkening, material ownership
python tools/gm_run.py <group> import          # Blender: rebuild per-asset objects from uv.obj, undo layout/rotation, rebuild normals -> .utmp/<slug>/fbx_uv/<Asset>_uv.fbx
python tools/gm_batch.py 3 export,setup,masks,review_diag <group> <group> ...     # parallel (3 at a time), logs in gmat/<group>/batch_<stage>.log
python tools/gm_check.py [group ...]           # every Graphmat component belongs to exactly one region (welding check)
python tools/gm_normals.py <group> [uv.obj]    # corner normals vs face normals: flat hard-edged parts must report ~0
```
Work folder per group: `.utmp/<slug>/gmat/<group>/` (`model.obj`, `uv.gmat`, `diagnostic.gmat`, `textured.gmat`, `albedo.png`, `uv.obj`, `review/`). 17 groups = ~2.3 GB: never commit.
Time: a hero group takes several minutes (AO bake dominates); small groups ~1-2 min. Always run commands with a generous timeout or in the background and poll the log.

## 7.3 Math you must respect (Graphmat Mesh Triplanar)
* Mesh Triplanar tiles the image every **(mesh bounding-box extent per axis x Scale)**. A non-cubic bounding box stretches the texture. `gm_export.py` therefore puts every group inside a **cube** (staircase layout + a 1 cm anchor
  triangle at the far corner, removed on import) and `gm_pipeline.py` sets `scale = tile / cube`.
* Brick course alignment: choose `cube` = one tile + k bricks (`5.04 = 0.96 + 17 x 0.24` for wall panels; `1 m + k x 0.25 m` for plinths so 0.125 m courses coincide with the modelled courses).
* Cylinder mode wraps round Y with integer repeats; give the group a single round asset (`cylinder="Y"`).
* **Frames**: the OBJ is written without axis conversion, so Graphmat's Y = the model's Y (depth) and Z = up. Box projection puts upright courses on +/-X and +/-Y faces and lays them flat on top faces. No texture rotation is needed for brick;
  wood boards that must run along X (deck, shelf top, ceiling planks) use `textureRotation` such as `[0,0,90]`, `[90,0,90]`, `[90,90,90]` (try, render, look).

## 7.4 Gates inside the phase
* **UV/texture efficiency gate (after `setup`, recheck after final export)**: inspect the actual UV layout and checker/density preview; measure useful face coverage or label the estimate, assess empty areas, and verify safe padding and any intentional overlaps. Regroup or repack sparse atlases without distorting islands, losing density or mixing incompatible material settings. The cube/anchor construction is for projection, not evidence of efficient UVs. `thoroughPacking` and `gm_check.py` do not establish utilization. Verify final UV-over-albedo mapping, unique map count and imported texture cost before approving the group for wider reuse.
* **Mask gate (after `masks`)**: open `_sheet_diag.png` and the `diag_*.png` views (front, back, sides, top, underside): every part is coloured by its own region, no holes, no bleed, narrow parts and occluded surfaces checked.
  If a part has the wrong colour: it is welded to another region (`gm_check.py` names it): separate the vertices in the asset module (5 mm), re-export.
* **Texture gate (after `textures`)**: `_sheet_final.png`: texture scale plausible at the concept's scale (bricks 0.24 x 0.12 m, planks ~0.15 m), grain direction right, no stretch, no visible tile seams on large faces, AO darkens crevices
  and contact edges but is not muddy (adjust `ao.apply` / `ao_radius` and rerun `textures`), regions keep their materials.
* **Normals gate**: `gm_normals.py` flat parts ~0 deviation; curved parts smooth. A group that fails goes back to the asset module (smooth_angle) - not to a post-fix.
* Re-run `setup` after ANY change that touches UVs (re-bake), `textures` after any change of tile/rotation/AO.

## 7.5 CLI facts (so you can debug a failing job)
`graphmat-cli author [input.gmat] --job job.json --out out.gmat [--timeout 1500] [--overwrite]` runs a JSON job `{"create": {"mesh","resolution","name"}, "operations": [...], "export": {"albedo","model"}}`.
Operations used: `importTexture {name,path}`, `autoUv {resolution,padding,respectHardEdges,thoroughPacking}`, `bakeProjectionMaps`, **`bakeAo {rays,radius,bias,strength}`**, `addRegion {name,color,selection:{components:[ids]}}`,
`assignTexture {folder,texture,projection,scale,rotation,origin,textureRotation,mode}` (`mode`: box | cylinder | sphere), **`applyAo {folder,strength}`**, `evaluate`.
Other commands: `mesh-info x.gmat` (components with seedFace/triangles, islands, bakes), `render-model x.gmat --out png --yaw --pitch --size --shading albedo`, `extract x.gmat --channel albedo --size 2048 --out png`, `export-model x.gmat --out uv.obj`.
Paths in a job are relative to the job file; the project copy is saved through a temp file; unknown fields are rejected. The original stock CLI lacked `bakeAo`, `applyAo` and `mode`; the included patch documents that historical extension. Query the installed version before deciding a source change is needed, and follow that repository's rules.
Zero-area triangles abort `author` ("Triangle N has zero area"); Graphmat welds coincident vertices (two touching parts become one component and take the seed face's region).

## GATE 7
- [ ] UV layout/coverage, density, padding and intentional overlaps inspected; avoidable empty space/duplicated maps corrected, material-region assignments verified and texture cost recorded
- [ ] for every group: `_sheet_diag.png` and `_sheet_final.png` opened and described; defects fixed and re-run
- [ ] `gm_check.py` OK for all groups, `gm_normals.py` flat parts ~0
- [ ] `fbx_uv/<Asset>_uv.fbx` exists for every asset of every group (`import_report.json` has_uv true)
