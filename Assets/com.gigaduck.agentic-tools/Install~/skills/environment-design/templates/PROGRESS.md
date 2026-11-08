# @NAME@ - working log

Skill: `environment-design` (read its SKILL.md first). Update this file at the END of every phase: it is the only memory a
fresh agent (or you after a context reset) has. One line per fact, with paths. Never delete a gate row; mark it DONE with the evidence.

## Request (the user's words, numbered)
1. (paste every requirement from the user's prompt here, literally, then add what each one means in measurable terms)

## Decisions and answers
| question | answer | date |
| --- | --- | --- |
| image generator | (Codex CLI / user supplies textures / other) | |
| texture budget (all generation attempts across the environment) | (confirm or state provisional cap; obey texturing skill limits) | |
| ambient occlusion | (requested? CLI bake/apply settings, or jobs adapted to omit it) | |

## Frame, scale and layout facts
- Unity metres, +X east, +Z north, floor top y = 0, origin = floor centre. Interior W x D x H = ... , eye height from actual controller ..., door width ... , stair rise/tread ...
- Concept contradictions and how they were resolved: ...

## Phase gates
| # | phase | gate artifact (path) | status |
| --- | --- | --- | --- |
| 0 | orient, tools, selftest | tool table + selftest PASS + baseline `git status` | |
| 1 | brief + questions | this file: request, decisions | |
| 2 | reference analysis | reference/crops/*, asset inventory, material table, dimension table | |
| 3 | blockout | Verification/blockout-views.png + side-by-side with concept (plan and perspective) | |
| 3a | finished representative section BEFORE full kit | Unity textured/lit corner + repeated surface + focal prop; close/grazing views, measured cost | |
| 4 | asset kit (Blender) | one review sheet per asset, assets.json, kit triangle total | |
| 5 | clay swap in Unity | clay views vs concept, layout_room.py | |
| 6 | textures generated | textures/materials.json, generation-log.json, selection.json, finalize-report.json, count <= budget | |
| 7 | Graphmat per group | groups/*.json, review sheets diag + final per group, gm_check OK, gm_normals OK | |
| 7a | UV/texture efficiency | exported UV-over-map, useful coverage/empty-space review, density, safe padding, material mapping and runtime texture cost | |
| 8 | textured import | tex.py all <groups>, Unity materials, prefab materials, colliders | |
| 9 | player + walk | WalkTest logs, perimeter + stairs + door checks | |
| 10 | lighting + post | side-by-side, stats.py numbers | |
| 11 | final verification | coverage matrix incl. weakest angle, normals audit, fixes list | |
| 12 | deliver | README, Verification/, index, virtual commit | |

## Numbers
- assets: ... , kit triangles: ... , scene triangles: ... , textures generated: ... of ...
- AO radius per group: ...

## Surface and UV efficiency plan / evidence

- Repeated small details represented by paint (brick/mortar, paving pattern, seams, grain): ...
- Geometry exceptions and their visible/interactive purpose: ...

| map/group | consumers and tile/trim/unique strategy | dimensions / density | useful coverage + method / remaining waste | padding / intentional overlaps | material-region mapping evidence | runtime cost / estimate |
| --- | --- | --- | --- | --- | --- | --- |

- UV/density/material preview paths inspected: ...
- Repacking/reuse/downsize comparison and closest/distant-view result: ...

## Open issues / honest disclosures
- (budget overruns, assumptions about unseen surfaces, things only checked by numbers, not seen)
