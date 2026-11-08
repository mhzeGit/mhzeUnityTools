# Geometry versus surface detail

Read during asset planning, before modeling. Judge at first-person eye height, closest reachable distance and grazing angles, not only the reference overview.

**Default to painted detail on simple forms.** A brick wall is a flat face with a shared brick-and-mortar texture; do not build a grid of individual bricks. Add wall thickness, openings and returns only where construction or accessible views require them. Small seams, grain, chips and repeated surface motifs use the same texture-first approach. Any exception needs a specific visible or interactive purpose.

## Decision test

1. Does the feature alter a visible outline, make an opening, reveal a backside, block sight, need collision/interaction, or cast an important depth-dependent shadow? Model enough form for that function.
2. Is it color, a painted edge, shallow seam, grain, mortar, wear or motif? Start with texture paint on the supporting form.
3. Does close movement reveal necessary relief/parallax that paint cannot preserve? Add only justified depth: a bevel, inset, edge strip or a few protrusions. Consider an allowed surface shader after checking style, tooling and cost.
4. Does repeated geometry contribute nothing at gameplay distance? Remove it and compare from the same cameras. Preserve defining silhouettes.

Screen-space contribution is a diagnostic, not a rigid cutoff. A tiny opening can matter against a bright background; thousands of interior brick faces may contribute nothing. Triangle counts also miss hundreds of renderers, material slots and colliders.

## Typical choices

| Feature | Starting representation | Reason to add geometry |
| --- | --- | --- |
| Continuous brick wall | Simple wall module, shared painted brick/mortar surface | Broken contour, deep recess, projecting course, exposed wall end |
| Wall corner | Real volume/return; consistent texture scale | Quoins substantially change contour or shadows |
| Stone paving | Walkable slab/terrain plus painted pattern | Steps, holes, important raised/displaced stones |
| Timber beam | Shaped silhouette/bevels; painted grain/checks | Large split, carved end, joint or profile curvature |
| Roof | Roof planes/volumes plus painted tile pattern | Eaves, ridge caps, broken edge tiles, substantial overlap |
| Door/window | Real opening, frame, reveal and door as needed | Structural form generally needs depth; grain/shallow seams stay painted |
| Barrel/crate | Shaped form, readable rim/frame; shared wood surface | Projecting hoops, chunky boards, open top or interaction |
| Stain/crack/moss | Painted surface or localized decal | Hanging clump, broken opening or plant silhouette |
| Foliage | Style-appropriate cards, volumes or meshes | Close views expose cards or require branching/depth |
| Sign/mural | Dedicated artwork on its support | Raised lettering/ornament is visibly volumetric |

Defaults are not bans. Sculptural masonry or chunky foreground tiles may need modeled relief. Fully 3D means a coherent navigable world, not a mesh per painted stroke. A projected photograph fails wherever the player sees missing form.

## Surface ownership and modular construction

- Assign shared surface IDs such as `stone-wall`, `painted-timber`, `roof-tile` and `sign-art`. Several mesh families can share a surface; one mesh can use several surfaces.
- Choose physical repeat size before UV production. Keep scale consistent across walls, corners and small modules. A fixed 2K map does not guarantee adequate density on an arbitrarily large surface.
- Use trim sheets for repeated bands/edges where supported, tileables for broad surfaces, atlases for compatible small unique parts, and dedicated maps for artwork/hero details. Choose by need and actual tooling, not a technique quota.
- Test corner alignment, UV orientation/stretch, padding and mip bleed. Verify projection does not tile non-repeating artwork.
- Break repetition with placement, compatible variants, decals or supported instance variation. Avoid rotations that contradict grain or brick courses.
- Use simple colliders where appropriate. Do not assign a collider per painted brick or reproduce tiny depressions in collision.
- Distinguish high-detail authoring/bake sources from runtime meshes in filenames and reports. Do not ship the dense source as the optimized prefab.

Before UV production, apply [uv-and-texture-efficiency.md](uv-and-texture-efficiency.md): plan reusable surfaces, pack compatible unique regions with safe padding, verify material ownership and inspect delivered UV utilization. Baking the same brick pattern into many sparse, unique maps defeats surface reuse even if the source image is shared.

Example inventory row:

| Family | Instances / closest view | Modeled form | Surface detail | Reuse | Required evidence |
| --- | --- | --- | --- | --- | --- |
| Brick wall kit | Repeated; player can touch | Volume, opening reveals, end/corner caps, broken upper contour | Brick color, mortar, shallow chips, requested cavity AO | Shared tileable, consistent repeat scale, corner/doorway modules | Reference and eye-height grazing views, doorway passage, renderer/triangle counts |
