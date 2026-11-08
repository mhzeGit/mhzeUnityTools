# Phases 2 and 3 - analyse the concept, build the primitive blockout

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

Goal: derive EVERY number from the images (not from taste), list every element, then prove the primitive blockout has the right shape before any
detail work. "Do not distort the world to fit a painted perspective inconsistency": the plan decides the layout, the perspective decides heights and silhouettes.

## 2.1 Reference images -> measurements
1. Open both images. Write what each is good for: *plan* = footprint, wall lines, stairs, openings, where every prop stands; *perspective* = heights, proportions, materials, light direction, mood.
2. **Grid overlay** to read pixel coordinates exactly:
   `python tools/grid.py <src.png> <out.png> x0 y0 x1 y1 scale step`   e.g. `python tools/grid.py reference/plan.png .utmp/<slug>/analysis/map_NW.png 0 0 768 512 2 50`
   Do the four quadrants (NW, NE, SW, SE) and open them. Write the pixel coordinates of every wall corner, door, stair start/end, trench, boiler centres, pipes, crates.
3. **Scale anchors** (convert pixels to metres): choose 2-3 elements whose real size is known from convention and measure them in the plan:
   door width 1.4-1.5 m (height 2.1-2.2 m), stair rise 0.17-0.20 m and tread 0.25-0.30 m, handrail 0.90-1.0 m, standard brick 0.24 x 0.12 m, human eye 1.65 m,
   floor-to-ceiling 4.8 m for a boiler hall. Use two anchors and check they agree within 5 %. Record `metres per pixel`.
4. **Heights from the perspective**: ratios against a known anchor in the same image (boiler height / door height, ceiling beam spacing, stair count).
5. **Contradictions** between plan and perspective (they always exist): prefer the plan for x/z, the perspective for y; if props disagree, move the smaller prop;
   write each contradiction + decision in PROGRESS.md (boiler room: SW/SE corner props moved 0.45 m north).
6. **Frame** (fix once, never change): Unity metres, +X east (right on the plan), +Z north (up on the plan), +Y up, origin = floor centre, floor top y = 0.
7. **Camera fit (optional)**: `tools/unity/fitcam.py` + `layout/landmarks.json` (3D point -> pixel pairs) or `camsearch.py` finds a camera that reproduces the concept view for the
   side-by-sides. A fit that lands outside the room means the concept perspective is inconsistent: keep the room right, the camera approximate.

## 2.2 Crops (one per asset family and per material)
`python tools/crop.py <reference> x0 y0 x1 y1 <name> [scale]` -> `reference/crops/<name>.png` (upscaled to ~900 px). Take crops for: each hero prop (one per size/variant),
fittings (gauge, valve wheel, bracket), masonry (plinth, stairs, footing), floor (perspective + plan), ceiling/beams, doors/frames, pipes, small props, lanterns, grate/trench,
every distinct material. **Open every crop** and write what you see (shape language, rivet pitch, bevel size, colour, wear). These crops are also the references handed to the texture generator.

## 2.3 The asset inventory (the "do not miss anything" table) - write it in PROGRESS.md
Scan the concept in this fixed order and list every element, also small ones:
structure (floor slabs, trench, grate, walls, wall panels, ceiling deck, beams, posts, braces, lintels) -> openings (doors, frames, pillars, thresholds) -> stairs and landing (steps, stringers,
deck, rails, handrail, posts) -> masonry (plinths, footings, hearth bricks) -> hero props (boilers, tanks) -> fittings (gauges, valves, wheels, brackets, collars, bands) -> pipes
(straight, elbow, band, cap, flange, ceiling collar) -> small props (crates, bucket, broom, bottles, shelf) -> light sources (lanterns, furnace window, lamps) -> decals/dressing.

| id | element | instances | size (m) | closest view | geometry or texture? | region names | module/asset | shared with |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |

Representation rule (from the user's corrections and `environment-design`):
* **Geometry** for silhouette, real openings, substantial depth, parallax and shadow, physical interaction (a stepped plinth, a chamfered beam, an iron frame around a furnace opening, bolt heads that break the outline).
* **Texture** for appearance-only detail: bricks and mortar, plank seams and grain, flagstone pattern, plaster mottling, rust, stains, painted edging, tiny rivets seen from > 3 m.
* A brick wall / stair side / plinth core / footing = a **solid form with a tiling brick texture**, never one mesh per brick (the user rejected exactly that).
* Reuse: floor slab A/B, wall panel 2.4 x 4.8 m, pipe tube 1 m (scale Y), elbow, band... one asset, many placements. Reuse transforms when they preserve proportions, texture density and collision; author a distinct variant when silhouette or construction requires it.
* Rough budget from the case study: 49 assets / 119k triangles for a 10 x 7 x 4.8 m room; hero boilers 17-19k each, small pieces 0.1-5k, whole scene ~301k triangles. State your own provisional budget in PROGRESS.md.

## 2.4 Material table (feeds phase 6)
| material | where in the concept (crop) | palette (mean RGB) | detail scale | tile size (m per repeat) | planned texture |
Typical for a stylized room: iron (teal), red paint, brick, mortar/pale stone, flagstone, dark wood, light wood, plaster, black iron, brass/cream, glowing glass.
One texture per **distinct** material, reused for every region with the same surface. Do not merge visibly different materials to fit a cap.

## GATE 2
- [ ] pixel coordinates + metres-per-pixel + anchors + plan/perspective decisions in PROGRESS.md
- [ ] crops opened (observations written), inventory complete, material table complete

## 3. Blockout with primitives (in Unity, new scene)
1. Fill `layout/layout_blockout.py` (template has helpers `box`, `cyl`, `sph`, groups). One item per mass: floor, walls with openings (as separate boxes around the opening), ceiling, trench,
   stairs (one box per step), landing, boilers (plinth cylinder + shell cylinder + dome sphere), pipes (cylinders), crates, lanterns (glow boxes). Group by family (`parent="Pipes/East"`).
2. `python tools/unity/tools.py blockout` builds `<Name>_Blockout.unity`. **It replaces the open scene (NewScene Single)**: only run it when the active scene is clean and not someone else's;
   it aborts when the active scene is dirty. In a shared editor, adapt it to additive creation or use the session connector to create a new scene without replacing existing open scenes. Never rerun it over an existing scene path.
3. Captures (editor must be idle, Play Mode off) with `tools/unity/cap.py`:
   * top-down orthographic matching the plan: `python tools/unity/cap.py out.png 0 20 -0.6  0 0 0  40 1536 1024 --ortho 3.9 --hide Ceiling` (ortho size = half the vertical extent of the plan in metres; shift z until walls coincide)
   * perspective close to the concept view: e.g. `... -1.0 1.65 -2.7  0.3 1.6 1.5  62` (hide the near wall pieces with `--hide Wall_South_*,Entrance*` to look in from outside)
   * eye-level entrance view and a stairs view.
4. Side-by-side with the concept: `python tools/blend.py reference/plan.png <shot.png> <out.png> side` (also `overlay`). `python tools/sheet.py out.png 3 600 a.png::caption b.png::caption ...` builds a contact sheet.
5. Compare in this order and fix largest first: footprint and wall lines -> heights (ceiling, boilers, landing) -> stairs run/rise -> openings -> prop positions and spacing -> pipe routes. Re-capture. Typically 3 iterations.
6. Traverse the blockout with the intended controller using project-approved Play Mode tools. Measure headroom, steps and clearances against its actual capsule and movement settings; the boiler-room dimensions are only examples.

## GATE 3
- [ ] `Verification/blockout-views.png` (top, hero, entrance, stairs, ne corner...) saved
- [ ] side-by-sides inspected; list of corrections made; remaining differences named
- [ ] numbers frozen in `layout_blockout.py` (they become the base of `layout_room.py`)
