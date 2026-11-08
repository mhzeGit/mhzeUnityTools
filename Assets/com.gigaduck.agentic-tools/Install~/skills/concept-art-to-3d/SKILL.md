---
name: concept-art-to-3d
description: Build faithful untextured 3D geometry from concept art, with visual revision and verified FBX delivery. Use for modeling and mesh refinement; UVs, textures and material authoring belong to model-to-textured-asset.
---

# Concept Art to 3D

Create custom untextured geometry that faithfully reproduces the supplied reference, uses geometry efficiently, and survives FBX export. Reference fidelity, geometry quality, visual revision and verified delivery are required.

**Geometry only.** Do not generate, extract, project, paint or bake textures; create UV layouts; paint vertex colors; or author appearance materials, procedural shaders or PBR maps. Use a neutral clay display for inspection. Preserve existing UVs/materials on supplied meshes without extending them. Meaningful part names and empty named material slots may identify future surface regions; they are labels, not finished materials. UVs and texturing are a separate task using `model-to-textured-asset`; do not invoke it automatically.

## Inspect and establish the target

- Open and visually inspect the actual reference images before modeling. Record the defining silhouette, relative dimensions, parts, negative spaces, asymmetry, colors, materials, and level of stylization. Use measurable landmarks and ratios rather than invented dimensions.
- Treat the supplied design as authoritative, not as inspiration or a recommendation. Do not replace it with a familiar template, redesign its construction, or add realism or ornament that changes its style. In this game's context, preserve medieval styling and the user's flatter cartoon preference when applicable; other supplied styles remain valid.
- Distinguish geometry references from style-only examples. Use only the user-approved aspects of generated art; rejected trials do not become construction references.
- Use all supplied views of the same object. If they contradict each other, resolve consequential ambiguity with the user rather than averaging them into a different design. Infer unseen surfaces conservatively and document those assumptions; do not claim exact matching of unseen geometry.
- Establish intended scale, game camera distance, engine and polygon budget. Record surface-region names for a later texturing handoff, without implementing appearance. If unspecified, state reasonable assumptions and a provisional geometry budget suited to this asset.

## Build a precise custom model

Use scripting, mesh operations, curves, sculpting, retopology, or a combination appropriate to the shape. Blender MCP or a headless Blender script can perform the work; scripts are a method, not evidence of quality. Inspect available tool/API capabilities before using them. Respect a prohibition on Computer Use and preserve existing scenes and unrelated objects.

Primitives are acceptable starting geometry or genuinely appropriate simple components. A finished model must not be a cheap assembly of stock cubes, cylinders, and spheres that approximates the reference. Shape contours, thicknesses, joints, bevels and distinctive details deliberately. Custom topology must serve the actual design. Do not add polygons or surface noise merely to make the asset seem sophisticated.

Keep an editable source and the modeling script when used. Build under a dedicated asset collection or isolated working file. Save meaningful versions so revisions and optimization do not destroy the best matching form.

Read [geometry-quality.md](references/geometry-quality.md) while planning topology and before approving the mesh. Match the reference first, then remove geometry that makes no visible contribution. Judge reductions at intended game viewing distance and in close views of defining features. Keep the least geometry that preserves those qualities; blindly decimating to meet a count fails this skill.

## Inspect, revise, and inspect again

Render or capture the actual 3D model and open the images for visual inspection. Compare with the reference at matching orientations and projections; include a true side view and front or top view as appropriate. Also inspect a three-quarter view that exposes depth. Neutral shape renders or wireframe views help distinguish geometry defects from lighting.

The required loop is **baseline review, then at least one and at most five revision passes**. Count a pass as an actual, evidence-based model change followed by new visual inspection; changing only the camera or writing a favorable review does not count.

1. Save and inspect baseline v0. Name specific mismatches with reference landmarks or visible defects.
2. Revise the largest mismatch first, then render and inspect again. If baseline already matches closely, use the mandatory revision for a justified topology, normal/smoothing or geometry-efficiency improvement and verify preserved fidelity. Appearance changes do not count as model revisions. Never invent damage just to repair it.
3. Keep a short revision log: issue, change, views inspected, result, and remaining defects. Repeated unsuccessful corrections require reassessing the modeling approach or ambiguous reference within the same five-pass limit.
4. Stop successfully only after at least one revision and all acceptance gates pass. Do not stop merely because the first render looks fine. Do not exceed five revisions without a new user instruction; after pass five, report unresolved defects and mark the asset provisional/failed instead of claiming success. A user request to stop takes precedence.

A missing reference or unavailable modeling/export capability is a blocker, not an excuse to deliver an imagined substitute or fabricate visual checks. Complete independent preparation and report the specific missing input/capability.

## Export and deliver

Read [fbx-delivery.md](references/fbx-delivery.md) before export. Export a real FBX, reimport it into an isolated scene, and verify geometry, orientation, scale, normals and part names. Inspect multiple clay views of that reimported asset. An export success message alone is insufficient.

Deliver the untextured `.fbx`, editable source and **at least three screenshots from distinct useful angles**, including three-quarter, true side, and front or top. Screenshots must show the reimported FBX. Report vertices, triangles, parts, dimensions, revision count and limitations. Include the concept-art path and part-to-surface-region notes for the separate texturing skill. Label this a verified untextured model, not a finished textured asset. Link actual files; never substitute image-generated previews or claim checks not performed.

Follow the host and repository handoff rules. Creating a model does not authorize overwriting existing Unity scenes/prefabs or changing unrelated game content.
