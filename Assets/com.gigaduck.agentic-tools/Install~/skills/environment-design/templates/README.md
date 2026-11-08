# @NAME@

Walkable, textured and lit Unity environment built from concept art with the `environment-design` skill.

- Scene: `@UNITY_BASE@/Scenes/Environments/@NAME@/@NAME@.unity` (blockout kept as `@NAME@_Blockout.unity`)
- Prefabs: `@UNITY_BASE@/Content/Environments/@NAME@/Prefabs/@PREFIX@<Asset>.prefab`   Models/Textures/Materials: `@UNITY_BASE@/Content/Environments/@NAME@/`
- Concept art: `reference/` (the two images the environment was built from)

## Folders
| folder | content |
| --- | --- |
| `assets/` | one python module per asset family (Blender, `tools/bq.py`); `ASSETS = {name: build_fn}`; parts named `<Region>__<Part>` |
| `layout/` | `layout_blockout.py` (primitives) and `layout_room.py` (placement of every prefab instance) |
| `groups/` | texture group specs written by `tools/gm_groups.py` from `tools/texture_groups.py` |
| `textures/` | `materials.json` (prompts), `generation-log.json`, `selection.json`, `finalize-report.json` |
| `unity/` | per-environment data for the Unity ops: `colliders.json`, `materials.json` |
| `tools/` | every script (see the skill's `references/` for what each does); `tools/unity/` drives the editor |
| `Models/blend/`, `Verification/` | editable Blender sources and the review images (written by `tools/export_deliverables.py`) |
| `PROGRESS.md` | the working log |

## Regenerate build intermediates

The external work area is recorded in `env.json` as `work_root`, normally
`../Work/@SLUG@/` beside this `Production/` folder. `project_root` identifies the
Unity project receiving the final exports.

These commands are for generated, untouched assets. Existing hand-authored scenes and prefabs require scoped edits. Inspect diagnostic masks before applying textures.
1. `blender -b -P tools/run_asset.py -- <module> ALL final` for every module in `assets/`
2. `python tools/gm_groups.py`, run `export,setup,masks,review_diag`, inspect diagnostic sheets, then run `textures,review_final,import` and inspect final sheets
3. Import maps/models and assign materials/collision. Use `tex.py all` and `PrefabColliders` only for generated, untouched prefabs; make scoped edits otherwise.
The scene itself is hand-authored; never regenerate it.
