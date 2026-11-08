---
name: environment-design
description: Design and build modular, textured, lit, walkable Unity environments from concept art, with Blender and Graphmat tooling, staged visual checks, and runtime verification. Use for complete environments or substantial environment revisions; individual prop modeling and texturing use their dedicated skills.
---

# Environment Design

Translate concept art into a coherent playable space. Match composition, shapes, palette, material language and atmosphere while making the environment hold up from every accessible player position. High quality means convincing results at the intended viewing distance; it does not mean modeling every visible mark.

This skill coordinates an environment job. Read and use the available `concept-art-to-3d` and `model-to-textured-asset` skills for their respective implementation stages. Discover their installed paths rather than assuming a machine-specific location. Follow project authoring rules and use `unity-gameplay-workflow` and `unity-mcp-orchestrator` when relevant to scene work and available tools. Do not silently replace an unavailable required capability. Do not build an environment when the request is only for planning or a skill update.

## Execution resources and scope

This skill combines the environment design workflow with the former concept-art-to-unity-environment toolkit. Use judgment to adapt the build to the actual reference, project, tools and authorized scope. For a revision, run only affected stages and recheck their dependencies; do not scaffold or rebuild an existing environment unnecessarily.

For a full build, read [the implementation runbook](references/runbook.md) to select commands and record gates. Its detailed phase guides, scripts, templates and boiler-room example are supporting resources, not a second skill. Resolve `scripts/`, `templates/`, `references/` and `examples/` relative to this skill folder. Discover sibling skill paths rather than assuming a particular agent installation.

The bundled tools implement a particular Blender/Graphmat/URP pipeline. Inspect capabilities and project compatibility before invoking them. The runbook explains which commands create new assets, update generated assets or edit a live scene. Existing hand-authored scenes/prefabs must never be regenerated. Reference dimensions, lighting values, texture counts and example costs are not universal targets.

## Establish the target

Open the actual concept images. If missing, request them before reference-dependent design; independent project/tool discovery may continue. Inspect repository instructions, asset indexes, render pipeline, scene state and reusable content before editing. Preserve hand-authored scenes/prefabs. A new environment uses a new scene path; existing content gets scoped edits, never generator-based replacement.

Ask only questions that materially change the build and cannot be inferred: playable area versus backdrop, target hardware/performance, reference ambiguities, or permission to reuse existing assets. State provisional assumptions for unspecified scale, controller dimensions, texture density and performance targets. Verify required modeling, image generation, Graphmat and Unity capabilities early through read-only discovery. This coordinator does not authorize a restricted tool path in another skill.

Establish:

- Reference camera: projection, approximate focal length/FOV, horizon and useful landmarks. Do not distort the world merely to fit a painted perspective inconsistency.
- Art brief: shape ratios, palette/value groups, focal area, bevel language, brush/detail scale, light direction, shadow softness and atmosphere. Separate painted lighting from surface color.
- Player space: actual eye height/capsule, entrances, route, closest viewing distances, headroom, door widths, stairs/slopes and accessible backsides. Infer unseen construction consistently and label assumptions.
- Acceptance conditions and provisional platform-appropriate budgets: visible triangles, renderer/material counts, texture memory, lights/shadows and frame time. Do not invent universal polygon limits or claim unmeasured performance.

Keep a compact working plan with asset/surface inventory, decisions, paths and gate status. Follow project documentation conventions; do not create a competing project-rule file.

## Plan geometry and surfaces before production

Read [representation-decisions.md](references/representation-decisions.md) before assigning modeling work. Inventory every reference element, including architecture, terrain, props, foliage and surface treatments. Completeness means every element has an intentional representation, not a separate mesh or texture file for each element.

For each reusable family record: reference location, dimensions, instances, closest viewing distance, silhouette/occlusion/collision needs, geometry-versus-surface decision, shared texture/material strategy, modular connections and provisional cost. Identify focal/interactive assets separately from background dressing.

Use geometry for silhouette, real openings, substantial depth, meaningful parallax, cast shadows and physical interaction. Use paint for appearance-only detail. Use selective geometry plus reusable textures where both matter. Bricks, mortar, grain, cracks, stains and painted edging are not automatically mesh parts. A planar wall surface is valid finished geometry when construction and shading are correct; accessible boundaries still need appropriate thickness, corners, returns and collision.

**Small surface details belong in textures by default.** A normal brick wall is a flat wall face with a shared brick-and-mortar texture, not individually modeled bricks. Apply the same rule to paving patterns, roof-tile patterns, plank seams, shallow chips, grain and small decorative fasteners. Add geometry only for a demonstrated silhouette, opening, depth or interaction need; model selected exposed/broken edges rather than the entire repeated pattern. High quality means the right appearance at player distance, not more mesh pieces.

Decide before invoking the modeling skill. Give each asset job explicit modeled features and explicit texture-only features, intended distance, dimensions, pivot/snap rules and shared surface IDs. Example: “Model wall volume, doorway and broken top contour; keep continuous faces simple; brick pattern and mortar are painted texture. Only exposed broken-edge stones need individual form.” Do not send “high detail, model everything” as a brief.

Prefer reusable modular kits, tileables, trim sheets, small atlases and localized decals where appropriate. Give unique maps to unique artwork or defining hero detail. Reuse approved surfaces across matching modules; do not generate a brick texture per wall or unique-map every repeated prop. Vary repetition without adding an unnecessary material per instance.

## Optimize UVs, texture space and material ownership

Read [uv-and-texture-efficiency.md](references/uv-and-texture-efficiency.md) before grouping surfaces or unwrapping, and again before final export. Choose shared tileables/trim sheets for repeated surfaces and compact atlases for compatible unique regions. Plan density by actual player distance and feature importance. Pack islands to minimize unused space while preserving correct material assignment, orientation, proportions and mip-safe padding. Avoid oversized maps, duplicated surface data, tiny islands stranded in large atlases and unnecessary materials.

**UV and texture efficiency gate:** inspect the actual exported UV layout over the delivered maps; record useful coverage, texel density, padding, intentional overlaps, material-to-region mapping and runtime texture cost. Repack, regroup, reuse or reduce resolution where it preserves the required detail. Recheck masks, seams and materials after changing the layout. A completed auto-unwrap or a fixed 2K export does not pass this gate. Prove the shared recipe in the representative section before applying it across the environment.

## Resolve the blockout in Unity

Build major masses and traversal with primitives in the intended new scene or authorized edit area. Establish scale and reference camera early. Match proportions, skyline, negative spaces, slopes, openings, focal placement and spacing before fine detail.

Inspect the matching reference view, a top/oblique overview and actual eye-height route views. Walk the main route with the intended controller. Correct compressed spaces, unreachable areas, impossible stairs and camera-scale mismatches now. Reduced-detail backdrops belong outside accessible and consequential sightlines. A walkable environment cannot be a single-view projection or facade with missing reachable backsides.

**Blockout gate:** major forms/composition match, the route fits, and unseen-space assumptions are coherent. Record observed mismatches and corrections. Do not produce fine detail while large forms are wrong.

## Finish one representative section before multiplying assets

Choose a small section that exercises the real risks: a wall corner/opening, repeated surface, focal prop, floor contact and representative lighting. Use planned geometry, final UV/texturing pipeline, Unity materials and target-scale lighting.

Inspect it from the reference camera, normal player distance and closest reachable grazing angle. Check texture scale in world units, corner continuity, silhouette, bevels, seams, material separation, AO strength and repetition. Measure actual asset cost. An isolated prop or shader ball does not prove the scene pipeline.

**Section gate (before full-kit production):** the geometry/texture split works in engine, style matches, and cost is plausible. Correct the shared recipe before replicating it. This is an agent verification gate, not a mandatory human approval pause.

## Produce modular assets and painted surfaces

Use `concept-art-to-3d` for geometry with the approved representation plan. Its geometry-only boundary applies: provide surface-region labels and a texturing handoff, not paint/unwrap instructions. Retain its required revisions, real export and reimport checks. Keep dimensions, pivots, orientation, snap increments, corner/end treatments and bevels consistent. Preserve hard edges on flat forms and smooth only intended curves; inspect shading and audit normals again on exported, textured and Unity-imported meshes. High-detail bake sources may be dense; delivered runtime meshes need only perceptible form.

Use `model-to-textured-asset` on approved meshes for UVs and Graphmat authoring. Preserve its CLI execution boundary, generation limits, diagnostic masks, checkpoints and delivered-mesh verification. Plan shared texture ownership up front; do not split jobs to evade generation caps. If its UV/projection capabilities cannot implement the chosen tile/trim/atlas strategy, resolve that limitation before producing dependent assets.

Choose an available reference-capable image generator according to the session's tools and user preference. The bundled Codex CLI generator is an optional adapter. Confirm a generation budget before using it, obey the texturing skill's applicable limits, count all attempts and retries across the environment, and generate sequentially. Do not silently exceed an agreed cap. Keep prompts, reference paths, selections and rejection reasons; inspect each chosen flat texture beside its concept crop and in a 2x2 tiling preview. Verify actual world-space repeat size in engine.

Match the supplied style. Hand-painted stylization needs readable color shapes, controlled variation and coherent brush/detail scale. Do not add noisy photoreal scans, reflective PBR response or arbitrary grime merely to seem finished. Inspect seams and repeats on actual modules, not just flat images. Inspect asset families together to catch incompatible palettes, bevel scales or surface detail density.

When the user requests AO, route that explicitly into the texturing task. Use restrained contact/cavity shading suited to the concept. Avoid broad directional lighting in reusable base color and avoid multiplying baked, painted and screen-space AO into black seams. Preserve editable color/AO separation where supported. Normal, height, parallax and displacement require an explicit style/performance decision and the subordinate skill's required authorization. Displacement adds geometry cost; it is not a free texture trick.

Replace blockout families in controlled batches while preserving placement, dimensions and clearance. Inspect each family in context before propagating. Simplify unnecessary modeled detail when the validated surface solution preserves the appearance; time already spent is not a reason to keep it.

## Light and polish the scene

Use the installed render pipeline and project conventions. Match broad light direction, warm/cool relationship, exposure, shadow softness and focal contrast before local accents or post-processing. Choose baked/mixed/realtime lighting for the actual needs. Check lightmap UVs where baking is used, leaks, probe coverage for moving objects, shadows and material response.

Inspect neutral material views as well as the final grade so lighting cannot conceal shape or texture defects. Use fog, fill, grading and restrained effects where they support the reference. Keep traversal readable. Inspect floor contact, floating props, repeated patterns, modular seams, z-fighting and edge shadows under final lighting.

## Verify and deliver

Read [verification.md](references/verification.md) before final review. Open actual in-engine images and run the real controller route. Include the reference view, overview, reverse route view, close surface at a grazing angle and weakest accessible angle; add views for scene-specific risks. Check all reachable spaces. A flythrough does not prove collision or access.

Compare side by side with the concept. Correct major shape/scale/layout first, then traversal, material palette/detail scale, lighting/value structure, and small dressing. Recheck affected views and shared instances after changes. Set a reasonable iteration/time budget for the scope; if passes stall, reassess the representation or missing input and report the concrete defect. Do not claim exact fidelity to unseen surfaces or “polished” completion while required gates fail.

Deliver the real scene, modular assets/prefabs, editable sources/texture projects, reusable maps/materials and concise verification report with inspected screenshot paths. Report tested routes, measured costs/conditions, assumptions, provisional items and blockers honestly. Clearly name useful reference/diagnostic cameras without altering the gameplay camera. Remove temporary diagnostics and restore test state/settings. Follow project validation, index and virtual-commit rules for exactly the files changed.
