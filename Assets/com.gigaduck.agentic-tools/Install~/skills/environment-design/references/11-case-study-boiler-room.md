# Case study - "The House's Iron Heart" boiler room (2026-10-19), the build this skill was extracted from

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

Inputs: `reference/boiler-room-perspective.png` (1672 x 941, hero view) and `reference/boiler-room-map.png` (1536 x 1024, top-down plan), from `Docs/Art/UndergroundEnvironmentStudy`.
Style: dusty hand-painted, posterized colour patches, dark outlines, warm amber light, teal riveted iron, red valve wheels, orange brick.
Outcome: scene `Assets/_BlahBlahFamilyV6/Scenes/Environments/BoilerRoom/BoilerRoom.unity` (blockout kept as `BoilerRoom_Blockout.unity`), 10 x 7 x 4.8 m interior, first-person walkable, outline + volume profile integrated.
The user approved the result and the process. Files of the build: `Docs/Art/BoilerRoom/**` (this repo) and `examples/boiler-room/` (copy of the data).

## Numbers
| item | value |
| --- | --- |
| asset kit | 49 assets in 10 modules, 119,137 triangles (boilers 3 / 52.6k, fittings 3 / 7.7k, masonry 4 / 6.5k, pipes 7 / 8.9k, timber 4 / 2.1k, doors 3 / 5.8k, structure 9 / 8.4k, stairs 5 / 3.1k, props 9 / 18.2k, props_misc 2 / 5.7k) |
| placements | 235 in `layout/layout_room.py` (scene ~301k triangles; 8 lights: 6 wall lanterns, 1 furnace spot, 1 fill directional) |
| region names | 13: Teal, Iron, Red, Brick, Mortar, Flag, Oak, Pine, Plaster, Brass, Glow, Glass, Stoneware |
| materials generated | 11 distinct; 21 images generated against a 16 budget (two generator runs overlapped; disclosed); the brick-wall tile was assembled from the approved brick + mortar textures |
| texture groups | 17 (boiler_large, boiler_ms, plinth_large/medium/small, stairs_brick, stairs_wood, fittings, pipes, timber, doors, floor, wall_plaster, wall_brick, ceiling, crates, smallprops), 17 albedos of 2048 px, ~2.3 GB of Graphmat projects in `.utmp` |
| Graphmat changes | `bakeAo`, `applyAo`, `mode` (box/cylinder/sphere) added to the CLI; 2 regression tests; see `references/graphmat-cli-extensions.patch` |
| elapsed | about 9 hours including a ~2.5 h usage-limit pause; the first pass over the whole kit took ~30 min once `bq.py` existed (masonry and stairs were reworked later after the user's brick correction); Graphmat batches 3 groups in parallel |

## Order actually followed (and what each step produced)
1. Read rules/skills/memory; looked at both images; scripted a direct-HTTP bridge to the Unity MCP (`mcp.py`, `ucs.py`) because the session connector was down.
2. Asked the 3 questions (Codex CLI / up to 16 / Graphmat AO).
3. Grid-overlaid the plan, measured 10.0 x 7.0 m, 4.8 m ceiling, trench z -0.33..0.33, stairs 9 x 0.2 m rise to a 1.8 m landing (NW), boilers at x -1.96 / 0.52 / 3.24; blockout scene; 3 comparison rounds.
4. `bq.py` library + marker test for axes; 10 asset modules; review sheets v0 -> v1 -> final per asset (boiler: dome flatter, band bolder, neck taller, rivets chunkier after the first sheet).
5. Layout of 235 placements; clay scene; moved the SW/SE props 0.45 m north to reconcile plan and perspective.
6. Crops -> 11 prompts -> Codex one by one -> candidate sheets vs crops -> `selection.json` with reasons -> seamless 2048.
7. Graphmat: pilot group, then `gm_groups.py` for all groups, batch export/setup/masks/review_diag, inspected every diagnostic sheet, fixed welded regions, then textures + AO + review_final + import.
8. Unity: textures, URP/Lit materials with tints, models re-imported over the clay FBX, prefabs re-made, colliders, player rig copied from the house scene, WalkTest perimeter/stairs/door, outline entry, lighting rig, volume.
9. **User correction 1 (bricks)**: replaced all per-brick geometry by solid forms + an assembled tiling brick texture (cylinder mode for plinths), re-ran every affected group.
10. **User correction 2 (hard edges)**: fixed `mark_sharp`, Edge Split before OBJ, area-weighted normal rebuild, re-ran all groups, `NormalAudit` 4012 -> 542 large smoothed triangles (remaining ones legitimately curved).
11. Grade: found the stale volume-stack capture bug, built `grade_sweep/search`; the user then tuned the profile themselves (exposure 1.3, contrast -26.7, saturation +2, hue -2) and approved the look. Final verification set: 10 views.
12. Deliverables, index, virtual commits (game repo + Graphmat repo).

## Final look-dev values (starting point for a similar warm indoor scene)
* Ambient trilight sky 0.13/0.125/0.14, equator 0.10/0.095/0.105, ground 0.055/0.052/0.058; fog exp2 0.008 colour 0.05/0.045/0.05; fill dir 0.45 colour 1/0.78/0.58 euler 35/15/0.
* Lantern point light 1.7, range 5.5, colour 1/0.74/0.48 (no shadows); furnace spot 16, range 5, colour 1/0.62/0.30, angle 130/inner 60, pitch 50, soft shadows.
* Materials: iron tint 0.85/1.05/1.2, smoothness 0.12; floor tint 0.56/0.55/0.58, smoothness 0.08; glow emission g = 3.9 (furnace) / 3.0 (small props), colour (g, 0.44 g, 0.18 g).
* Grade at the time of the first complete pass: ACES, bloom 1.2/0.95/0.7, vignette 0.3, exposure 0.2, contrast 5, saturation -10 (too dark with the final lighting -> see reference 07; the user's tuning replaced it).
* Outline: colour 0.07/0.04/0.025, depth threshold/thickness 0.5/1.1, normal 1.3/1.0.

## What went wrong first time (so you skip it)
Per-brick meshes; smoothed box normals; two generator runs at once; volume stack stale in captures (grade "had no effect" for hours); a capture camera inside a boiler mistaken for a black block; split-toning tints darkening the whole picture;
profile overwritten risk after the user tuned it; zero-area triangles in stair caps; welded regions; wrong frame assumption for brick courses; shared editor in Play Mode while capturing.
