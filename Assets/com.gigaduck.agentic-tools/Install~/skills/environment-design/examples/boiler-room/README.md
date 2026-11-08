# Worked example: the boiler room

Real data of the first build this skill was extracted from. Use it to see HOW things are written, not to copy content.

| file | what it shows |
| --- | --- |
| `env.json` | an `env.json` for a real project (Unity paths of the Blah Blah Family game, tool overrides empty = auto-discovered, grade patches) |
| `assets/*.py` | 10 asset modules, 49 assets (`boilers`, `fittings`, `masonry`, `pipes`, `timber`, `doors`, `structure`, `stairs`, `props`, `props_misc`) written with `bq.py` |
| `layout/layout_blockout.py`, `layout/layout_room.py` | the primitive blockout and the 235 placements (helpers `put`, `put_ax`, `tube`, `elbow`, `band`) |
| `texture_groups.py` | 17 texture groups with tile sizes, cube sizes, rotations, cylinder groups |
| `materials.json` | the 11 generator prompts (style + common rules + one text per material) |
| `textures/selection.json` | the picked candidate per material with the reason; `generation-log.json` = every prompt/reference/size; `finalize-report.json`, `brick-wall-tile.json` |
| `unity/colliders.json`, `unity/materials.json` | collider and look-dev data for the Unity ops |
| `PROGRESS.md` | the working log as it stood at the end of modelling/texturing (the real, newer log is `Docs/Art/BoilerRoom/PROGRESS.md`) |

To reuse a pattern: open the file, copy the idea (a parametric shell, a stepped lathe plinth, an `extrude_stairs` staircase), change the numbers from YOUR concept's crops.
