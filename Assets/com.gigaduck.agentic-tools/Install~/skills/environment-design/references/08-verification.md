# Phase 11 - verify like a player, not like a compiler

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

"0 errors" and "the command succeeded" prove nothing about how an environment looks or plays (the user once found a visible bug right after a "0 errors" test). Verify with your eyes at the player's positions,
with numbers where a picture hides things, and report which was which.

## 11.1 The capture harness (`bo.py shots`)
`python tools/unity/bo.py shots <name> "px py pz tx ty tz fov [w h] [pp]" ["..." ...] [hide=a,b,c*]` -> `.utmp/<slug>/shots/<name>_<n>.png`. What it does and why:
* loads the scene additively, shifts it 5 km away if another scene is open (so other scenes' colliders/lights at the origin cannot interfere) and disables other scenes' directional lights for the duration;
* creates `__CaptureCam` INSIDE the scene (hidden/scene-less cameras reuse a stale volume stack), refreshes the volume stack explicitly, renders through `SubmitRenderRequest` twice (the first render after a change can come out
  without static geometry), reads the pixels back and deletes the camera;
* hides the player rig by default (`FPC_Character`) and anything you list (`hide=Ceiling,Wall_South*`: exact names or `prefix*`);
* afterwards restores everything and re-saves the scene ONLY to clear a dirty flag it caused itself (content unchanged).
`pp` at the end of a spec turns post-processing on. Use 1280x720 for review images, 1672x941 for side-by-sides with the concept.

## 11.2 Suggested coverage matrix (adapt view count, eye height and FOV to the actual space/controller)
1. concept hero view (matched camera) + side-by-side + stats
2. reverse view (from the far end back toward the entrance)
3. each corner looking inward (4 views)
4. along each long aisle (both directions)
5. stairs: from the bottom looking up, from the landing looking down, the underside/side of the stairs
6. landing and everything reachable from it (rail, deck, walls seen from above)
7. entrance/door from inside and the vestibule
8. ceiling and beams (look up), pipes where they pass the ceiling
9. close-ups at the nearest play distance (0.5-1.0 m) of: hero prop front, hearth/plinth bricks, floor and grate, a wall with its texture, a pillar/frame, a valve/gauge
10. grazing angles on large flat surfaces (texture stretch, seams, z-fighting) and dark corners behind props
Place cameras in FREE space: a camera inside a boiler or a wall renders black boxes and back faces (a false alarm that cost time: "black block in the middle of the room" was a camera 0.9 m inside the boiler shell).
Build a contact sheet: `python tools/sheet.py out.png 2 640 a.png::V1 b.png::V2 ...` and open it, then open single views at full size where something looks odd.

## 11.3 What to look for (tick each)
- [ ] shapes/silhouettes match the concept; proportions and heights right; nothing missing from the inventory
- [ ] floating or sunken props; gaps between modules; z-fighting; interpenetrating pieces that show
- [ ] texture: scale at the concept's scale, orientation (courses horizontal, grain along length), no stretch, seams at module borders, brick pattern continuity across panels, AO contact darkening present but not muddy
- [ ] **flat faces are flat and edges are crisp** (no pillow shading on boxes/stairs/walls), curved parts smooth: look at stairs, walls, pillars, plinth steps, door frames; then `bo.py op NormalAudit` (numbers: large flat triangles with deviating normals; list the offenders with `NormalDetail text=<Asset>`)
- [ ] materials: no clay material left (`bo.py op Audit`), glow parts glow, metal not mirror-like, floor not blown out
- [ ] light: no hot spots on pillars next to lanterns, no pitch-black voids, shadows brown/teal, furnace glow on hearth and floor
- [ ] outline (if the project has it) present and not doubled; no outline on glass
- [ ] collision: no invisible walls in aisles, no gaps to fall through, stairs walkable, door passable
- [ ] performance sanity: `Audit` (renderers, lights, scene triangles); the case study ran ~301k triangles with 8 lights (6 wall lanterns, 1 furnace spot, 1 fill directional)

## 11.4 Walk it
Run the WalkTest legs of reference 06 again after every fix that touches colliders or layout. In BlahBlahFamilyV6 walk with the real controller in your own standalone player (`Docs/rules/TESTING.md`), once, after the fixes are batched; film stills at fixed steps for things a single image hides.

## 11.5 Fix loop (do not give up)
List every visible defect with the view that shows it -> fix at the right level (layout < prefab/asset < texture group < material look-dev < lighting) -> re-run only the affected stage -> re-capture the same views -> compare to the previous capture.
Large errors first (shape, scale, layout), then traversal, then palette/texture scale, then lighting/value structure, then small dressing. If a defect survives three attempts, change the representation (different projection, different group, geometry instead of texture or vice versa).
Keep a fix log in PROGRESS.md: defect, view, cause, change, result.

## GATE 11
- [ ] Views covering the matching camera, overview, both route directions, close/grazing surfaces and weakest accessible angle saved at useful resolution as `Verification/unity_<name>.jpg` (via `export_deliverables.py --unity-shots`), each inspected and described
- [ ] `Audit` and `NormalAudit` numbers recorded; WalkTest logs recorded; fix log closed
- [ ] the report says which items were SEEN, which only MEASURED, which not checked
