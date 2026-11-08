# The House's Iron Heart - boiler room environment: working log (final)

Source concept: `reference/boiler-room-perspective.png` (hero view) and `reference/boiler-room-map.png` (plan), both from
`Docs/Art/UndergroundEnvironmentStudy`. Result: a walkable, textured, lit Unity scene that looks like the concept:
`Assets/_BlahBlahFamilyV6/Scenes/Environments/BoilerRoom/BoilerRoom.unity` (the primitive blockout is kept as `BoilerRoom_Blockout.unity`).
The reusable method is the skill `tools/skills/environment-design` (this folder is its worked example).

## Decisions (user answers, 2026-10-19)
- Texture generator: Codex CLI image tool (`codex exec -i <ref> ... -`, prompt on stdin), budget **up to 16** generated textures for the whole room.
  **21 were generated** (two generator runs overlapped: 11 materials + 10 duplicates/retries), i.e. 5 over budget; nothing more was generated. Picks and reasons: `textures/selection.json`, every prompt: `textures/generation-log.json`.
- AO: Graphmat's own AO through the Graphmat CLI (bake first), multiplied over the base colour of every region. The stock CLI only baked position + world normal, so `bakeAo`, `applyAo` and a projection `mode` option were added to the Graphmat repo (separate virtual commit there; patch kept in the skill).
- Corrections by the user after the first result, now hard rules: (1) brick walls must be texture on solid forms, not one mesh per brick; (2) flat faces keep hard edges (sharp normals).
- The scene's PostProcess volume uses `GlobalVolumes/BoilerRoomGlobalVolumeProfile.asset`. **The user tuned that profile live in the editor** (exposure 1.3, contrast -26.7, saturation +2, hue shift -2) and approved the look; it was not overwritten.

## Frame and scale
Unity metres, +X east, +Z north, floor top y = 0, origin = floor centre. Interior 10.0 x 7.0 m, ceiling underside 4.8 m.
Blender assets: Z up, front faces -Y. `bq.export_fbx` bakes a 180 deg Z rotation so Unity sees (x, y, z)_unity = (x, z, y)_blender (verified with `tools/test_marker.py`: markers land on +X/+Z/+Y, 1 unit = 1 m, root rotation identity).

## Layout (blockout, verified against both references)
`layout/layout_blockout.py` -> Unity blockout builder -> `BoilerRoom_Blockout.unity`; final placement `layout/layout_room.py` (235 items).
Boilers: left small (x -1.96), centre large (x 0.52), right medium (x 3.24), trench z -0.33..0.33, stairs on the west wall (9 x 0.2 m rise) to a raised landing (1.8 m) in the NW corner, SW crates/bucket/pipe, SE pipe on a brick footing,
entrance door in the south wall with a small vestibule, pipe runs along the north and east walls. The perspective and plan images disagree about the depth of the corner props; the SW/SE props were moved 0.45 m north.

## Numbers
- Kit: 49 assets in 10 modules (`assets/*.py`), 119,137 triangles (`Verification/assets.json`); placements 235; scene ~301k triangles; lights: 6 wall lanterns, 1 furnace spot, 1 fill directional.
- Textures: 11 materials, 17 texture groups (`groups/*.json`), 17 albedo PNGs of 2048 px, brick-wall tile assembled procedurally from the approved brick + mortar textures (`tools/make_brick_tile.py`, no extra generation).
- Normals: Unity `NormalAudit` large smoothed triangles 4012 -> 542 after the hard-edge fix (the remaining ones are legitimately curved parts).

## Tools (this folder)
- `tools/bq.py` modelling library; `tools/run_asset.py` build -> .blend -> export FBX -> RE-IMPORT -> clay renders -> JSON stats (`blender -b -P tools/run_asset.py -- <module> <Asset|ALL> <tag>`); outputs in `.utmp/boiler-room/{blender,fbx,renders,logs}`.
- Asset scripts in `assets/*.py`. Region prefixes `<Region>__<Part>` (Teal, Iron, Red, Brick, Mortar, Flag, Oak, Pine, Plaster, Brass, Glow, Glass, Stoneware) name every surface for texturing.
- Texturing (Graphmat CLI, no GUI, no MCP): `tools/gm_groups.py` (group specs -> `groups/*.json`), `tools/gm_run.py <group> <stages>` (export, setup, masks, review_diag, textures, review_final, import), `tools/gm_batch.py` (parallel), `tools/gm_export.py` / `gm_obj.py` (Blender side),
  `tools/gm_pipeline.py` (CLI side), `tools/gm_review.py` + `tools/gm_sheet.py` (unlit per-asset review renders), `tools/gm_check.py`, `tools/gm_normals.py`, `tools/gm_angles.py`, `tools/finalize_textures.py`, `tools/make_brick_tile.py`, `tools/gen_textures.py`.
- `tools/unity/` records the Unity-side scratch tooling (in-memory C# through the Unity MCP HTTP endpoint; see its README). `tools/export_deliverables.py` writes `Models/blend/*.blend` and `Verification/*`.

## Texture pipeline facts worth remembering
- Graphmat Mesh Triplanar tiles the image every (mesh AABB extent per axis x Scale): a non-cubic AABB stretches the texture. `gm_export.py` therefore lays a group's assets out on a 3-axis staircase and adds a 1 cm anchor triangle at the far corner of a cube; `tile` (metres per repeat) in the group spec becomes scale = tile / cube. The anchor is dropped on import.
- The OBJ is written without axis conversion: Graphmat's Y = model depth, Z = up. Round masonry is rotated (`grm` AXIS_Y) so its axis is Y for the cylinder projection; cube sides are 1 m + k x 0.25 m so the 0.125 m courses line up.
- Graphmat merges all OBJ objects into one mesh and keeps triangle order; `gm_obj.py` rebuilds per-part objects from the triangle ranges (verified as a pure translation), undoes the layout/`grm` rotation and rebuilds the normals from the authored smooth angles. Welded vertices merge regions: parts of different regions keep 5 mm of clearance.
- Wood grain follows the texture's V axis (= model Z): beams are rotated while texturing; crates and the landing deck use `textureRotation`.
- The generated flat textures are not seamless; `finalize_textures.py` cross-fades each with a half-offset copy and resamples 1254 -> 2048 (Lanczos); no colour grading.

## Verification (2026-10-19/20)
- Ten in-editor views of the finished scene with its own volume (`Verification/unity_*.jpg`): hero, SE corner, SW corner, east-to-stairs, landing, furnace close-up, floor/trench, NE trench, entrance door, ceiling - opened and compared with the concept.
- Walk tests (edit-mode CharacterController legs: perimeter, aisles, stairs, door) and a normal audit were run earlier in the build; **no Play Mode / Fast Play run was made** (the editor was shared and in use), so first-person feel with the real controller was not re-checked after the last model changes.
- Not checked: performance on target hardware (counts only), lightmapping (everything is realtime).

## Reproduce
`Docs/Art/BoilerRoom/README.md`. Everything generated lives in the git-ignored `.utmp/boiler-room/` (~4.6 GB incl. 17 Graphmat projects); the Unity assets, Blender sources (`Models/blend`) and all scripts are committed.
