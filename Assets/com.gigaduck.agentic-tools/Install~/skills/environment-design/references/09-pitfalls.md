# Pitfalls: symptom -> cause -> fix (all from the real build)

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

Look here BEFORE debugging. Columns: what you see | why | what to do.

## Shell and machine
| symptom | cause | fix |
| --- | --- | --- |
| `\\` becomes `\`, quotes/`'''` break a shell heredoc, "unexpected EOF" | the shell pre-processes heredocs | write files with the file Write tool; run them by name |
| `sleep 60 && ...` refused | some harnesses block foreground sleeps | run the job in the background, poll for an output file / use the agent's wait tool |
| files land in the wrong folder, relative paths fail | the shell tool's working directory drifts between calls | absolute paths, `cd` inside each command |
| a file written from Git Bash is not visible to Windows Python | `/tmp` is not the same folder for both | use `.utmp/<slug>/` or the scratchpad folder |
| `ModuleNotFoundError: PIL` inside a Blender script | Blender's Python has numpy but no PIL | do image work in system python scripts; Blender scripts only bpy/bmesh/numpy |
| `/c/Users/...` rejected by Python | Git Bash path style | use `C:/Users/...` (sheet.py converts a leading `/c/`) |
| `OSError: [Errno 22] Invalid argument` when a script rewrites an existing generated file (seen with the index generator on Windows) | the file cannot be re-opened for truncation by that process | delete the file and run the script again (appending or replacing via a temp file also works) |
| a long Graphmat job looks hung | AO bake of a hero group takes minutes | run in background with `--timeout 1500`, poll `batch_*.log`; inspect outputs before retrying a mutation |

## Blender / modelling
| symptom | cause | fix |
| --- | --- | --- |
| every box looks pillow-smoothed, flat faces and sharp angles look broken | `calc_face_angle()` is 0 until face normals exist, nothing was marked sharp; OBJ exporter ignores sharp edges | `bq.obj_from_bm` recalcs normals first; `gm_export.py` Edge Split before OBJ; `gm_obj.py` rebuilds normals from the recorded angle; verify with `gm_normals.py` and `NormalAudit` |
| bricks/solids dark or inside out | `recalc_face_normals` can leave one of many touching closed solids inverted | `bq.fix_closed_islands` (already in `obj_from_bm`) |
| black wedge slivers on a stepped lathe | profile doubles back (`STEP_IN == BEVEL`) | step-in 0.024 m with bevel 0.010 m |
| `author` aborts "Triangle N has zero area" | collinear vertices in an n-gon cap triangulated into slivers | build caps from fans/quads (`bq.extrude_stairs`), export triangulated (`export_triangulated_mesh=True` is set) |
| a part shows the colour of another region in the diagnostic view | Graphmat welds coincident vertices: touching parts become ONE component | 5 mm clearance or overlap between parts of different regions; `gm_check.py` names the offenders |
| model is rotated/mirrored in Unity | axis conversion | `export_fbx` bakes 180 deg about Z; run `test_marker.py` once and check markers land on +X/+Z/+Y |
| part names with `.001` | duplicate names while building a group in one scene | `export_fbx` renames; `gm_obj` strips the suffix |
| single-mesh FBX shows no child renderers in Unity | the renderer sits on the root, the part name survives as the mesh name | `EnvTools.MakePrefabs` reads the mesh name when the object name has no `__` |
| per-brick meshes (the user's rejection) | modelled the pattern instead of using texture | solid slab + tiling texture (reference 03 rule 1) |

## Graphmat
| symptom | cause | fix |
| --- | --- | --- |
| texture stretched on one axis | Mesh Triplanar tiles per (bbox extent x scale); bbox not a cube | group layout inside a cube with anchor triangles; `scale = tile / cube` (already in gm_pipeline) |
| brick courses vertical on +/-X faces | assumed Graphmat Y = up; the OBJ has no axis conversion, Y = depth, Z = up | do not rotate; courses are upright on +/-X, +/-Y faces |
| wood grain across the length | grain follows V = model Z | `grm` LONG_X [0,90,0] or `textureRotation` |
| cylinder projection wrong / seam | axis not Y, several assets in one group, cube not a multiple | `AXIS_Y` grm, `cylinder="Y"`, one asset, cube = 1 m + k x 0.25 m |
| AO muddy or invisible | radius/strength wrong | `ao_radius` ~ size/8, `apply` 0.9; rerun `textures` only |
| `author` cannot find a texture | paths in a job are relative to the job file | `gm_pipeline` writes relative paths from the group folder |
| CLI says unknown op `bakeAo` / `mode` | stock CLI | check installed capabilities first; review the historical patch only if an authorized compatible source change is needed |
| old project overwritten | reused output path | each phase writes a NEW project (`uv.gmat`, `diagnostic.gmat`, `textured.gmat`); `--overwrite` only on your own outputs |
| UVs changed but results stale | maps baked on the old UVs | re-run `setup` (re-bake) after any UV change |

## Textures and the generator
| symptom | cause | fix |
| --- | --- | --- |
| Codex ignores the prompt / error about files | `-i` is variadic and swallowed the positional prompt | prompt on stdin with `-` (gen_textures does) |
| 21 images against a budget of 16 | two generator runs overlapped, retries not counted | one process at a time (lock), count every call, ask before exceeding |
| seam visible when tiled | generated images are not seamless | `finalize_textures.py` cross-fade; open `_tiles/*_after.png` |
| texture shows border/vignette/object/shadow | prompt not strict enough | regenerate with the `common` rules, inside the budget |
| brick generation returns a wall of bricks | asked for "bricks" | ask for a SINGLE brick face and assemble the tile (`make_brick_tile.py`) |
| albedo too pale/saturated against the lit concept | generator normalises exposure | pick the closer candidate; correct in the Unity material tint, never grade the albedo |

## Unity bridge
| symptom | cause | fix |
| --- | --- | --- |
| `FAILED: Blocked pattern detected` | execute_code safety checks | inspect the rejected operation and use a narrower supported connector operation; do not disable safeguards to bypass a rejection |
| `COMPILE ERRORS ... defined multiple times / not referenced` | duplicate or missing assembly references | the bootstrap in `ucs.py` filters duplicates and honours netstandard; do not hand-edit |
| `Object is an ambiguous reference` | `System.Object` vs Unity | write `UnityEngine.Object` |
| `Newline in constant` | `\n` turned into a real newline by a heredoc | Write tool, or `chr(92)` in a Python patcher |
| unknown op | ops are found by reflection in EnvOps2..6 only | add the method to one of those classes |
| `FAILED: {"success":false,"message":null}` | editor busy (compiling, importing, Play Mode) | wait; `bo.retry`; `bo.py status` |
| `ECONNREFUSED 127.0.0.1:8080` | editor closed or MCP HTTP server not started (~95 s after launch); the session connector can show "failed" while HTTP works later | start the editor, wait, retry; or `ucs.py --emit` |
| JsonUtility returns empty arrays | arrays of custom classes in a dynamically loaded assembly | parse by hand / use string arrays |
| `volumeFrameworkUpdateMode` is protected | cannot be set from outside | call `VolumeManager.instance.Update(...)` instead |
| editor busy for minutes after copying FBX/PNG | import + domain reload | `AssetDatabase.Refresh` once, then poll `status` |
| `NewScene` replaced the user's scene | blockout/assemble use `NewSceneMode.Single` | adapt or replace legacy scene creation with additive mode, preserve open scenes, and refuse existing output paths |

## Capture, lighting, grade
| symptom | cause | fix |
| --- | --- | --- |
| profile edits change nothing in captures | stale volume stack (camera outside the scene / no refresh) | scene-resident capture camera + `VolumeManager.instance.Update` (the harness does it); `GradeProbe3` shows the real stack |
| scene far too dark with post (mean 8.6 vs 41 without) | ACES on a scene lit for linear + split toning with dark tints (soft light) + low exposure | tonemap None/Neutral + exposure; tints near 0.5 grey |
| crimson / pink cast | colour filter + blue shadow toning + high balance of the base profile | own profile tuned with `grade_sweep.py` using patch hues |
| big blob of glow on a wall | bloom threshold < 1 | threshold 1.0-1.3, intensity 0.4-0.8 |
| first capture after a change lacks static geometry | first render after a reload | render twice (harness does) |
| black boxes in a capture | camera inside a mesh | move the camera into free space (check with `Cast`) |
| captures from the wrong scene / lit by someone else's sun | other scene open at the origin | additive load + 5 km shift + other directional lights off (harness) |
| user sees flicker / loses their profile | capture swapped the scene's volume profile while they edit | capture with the scene's own volume; never run GradeSweep/Volume while the user is in the scene |

## Collision and walk tests
| symptom | cause | fix |
| --- | --- | --- |
| player stuck on the stairs | riser = step height, no ramp | stair wedge collider (`stairs` in colliders.json) |
| player falls between floor pieces | gaps between colliders | overlap slabs/colliders by 2-5 cm |
| boilers snag the player | riveted mesh used as collider | convex cylinder collider |
| invisible wall in an aisle | collider on a decorative part or oversized box | colliders only on structural/prop bodies; re-run `PrefabColliders`; `Cast` to find the blocker |

## Repository and shared editor
| symptom | cause | fix |
| --- | --- | --- |
| other people's files in `git status` | shared working tree | list only YOUR files in the virtual commit; never `git add/commit/stash/reset/checkout` |
| files appear staged (`A`) without you | an editor tool or another agent staged them | leave the index alone; mention it |
| hand-authored scene/prefab/profile overwritten | generator re-run | tools may only ADD or write NEW paths; prefab rebuild keeps names; check mtimes first |
| `generate_index.py --check` complains | new scripts without class summary / new models without look notes | add `/// <summary>`, run `visual_notes.py prepare`, cheap vision model, `merge`, regenerate |
