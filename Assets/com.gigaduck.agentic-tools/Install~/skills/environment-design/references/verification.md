# Environment verification

Read before final review. Evidence comes from the delivered assets and scene. Concept previews, successful commands and unviewed captures are not verification.

## Visual evidence

Capture and open useful views at intended output resolution:

- Reference-matching camera with projection/framing recorded. Compare landmarks, negative spaces, skyline, value groups, focal contrast and palette against the actual concept.
- Overview exposing layout, connections, omissions and repeated-module patterns.
- Eye-height route views in both directions, including accessible backsides and module transitions.
- Close/grazing views of the most repeated surface, a corner/opening and representative finished section. Check insufficient depth, seams, stretch, tiling artifacts, excessive AO and texture scale.
- Weakest accessible view found during traversal. Do not curate evidence to exclude defects.

Record files actually opened and observed issues. Recapture affected views after changes; an old screenshot cannot validate a new state. Judge unseen areas for coherent construction/style rather than claiming exact matching.

## Runtime checks

Use the existing intended controller and repository-approved testing tools. In BlahBlahFamilyV6 agents do not use editor Play Mode: walk it in your own standalone player (`walk`, `teleport`; `Docs/rules/TESTING.md`) and stop it afterward. Do not add a replacement controller for easier testing.

Traverse the route, doorways, stairs/slopes, corners and narrow spaces. Test floor/wall collision, headroom, camera clipping, fall-throughs, unintended escape routes and relevant moving doors/props. Teleportation or a NavMesh path does not prove the controller can walk there.

Observe the actual gameplay-camera lighting/materials during movement. Screenshots can miss pop-in, sorting, shadow transitions, exposure changes and streaming problems. Check relevant dynamic objects under probes and shadows.

## Technical evidence

Verify imports: scale/orientation, transforms, bounds, pivots, normals, material ownership, UV persistence and texture references. Check missing assets/materials, compilation, collision and final lighting state. Verify saved deliverables through safe reload procedures where needed; never discard dirty user scenes to do so.

Apply the [UV/texture efficiency gate](uv-and-texture-efficiency.md) to the delivered maps and mesh UVs: inspect packing and material-region ownership, measure or label useful coverage, check density and mip-safe padding, and justify overlaps and remaining empty areas. Check for duplicated baked surfaces, oversized sparse atlases and unnecessary material slots. Record actual runtime map counts/dimensions and texture memory where available. Compare close and distant views after repacking or reducing cost.

Inspect a wireframe or mesh breakdown alongside the repeated surfaces: brick/mortar patterns and other small decorative detail should be texture work on simple geometry. Flag unnecessary individual bricks, paving joints or tiny fasteners and compare a simplified textured version from the same player views.

Report measured complexity: asset triangles after modifiers/export, scene versus visible counts, renderer/material counts, texture dimensions/memory where available, and expensive light/shadow settings. If performance is an acceptance condition, profile the relevant route. Record hardware, resolution, quality settings, Editor versus build and whether loading/baking/shader compilation had settled. Do not extrapolate Editor timing into an unsupported target-device claim.

Optimize demonstrated bottlenecks: unnecessary renderers/material duplication or geometry, poor texture reuse, collisions or lighting. Introduce LOD/culling when warranted. Keep before/after views so efficiency does not excuse lost fidelity.

## Completion record

Use concrete evidence rather than invented scores:

| Gate | Evidence | Verdict / remaining issue |
| --- | --- | --- |
| Reference/blockout | Inspected reference/scene views and route | Specific match or mismatch |
| Finished section | Textured/lit section, close views, measured cost | Whether the shared recipe works |
| Assets | Modeling/texturing handoffs, reimport views | Coverage and technical/style issues |
| UV/texture efficiency | Exported UV-over-map and density previews, useful coverage, padding, material mapping and texture cost | Avoidable waste corrected; remaining constraints justified |
| Final scene | Matching, reverse, close and weakest views | Composition/material/lighting findings |
| Walkability | Actual controller route and boundaries | Collision/reachability findings |
| Performance/project checks | Measurements and required validators | Tested conditions, untested targets, failures |

Link the scene, editable sources, reusable assets, texture projects/maps and evidence. Distinguish passed checks, assumptions and limitations. If a gate/capability is blocked, deliver useful completed work with the precise limitation; do not substitute a weaker pipeline silently or claim completion.
