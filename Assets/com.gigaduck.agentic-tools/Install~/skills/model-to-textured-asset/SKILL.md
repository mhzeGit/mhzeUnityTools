---
name: model-to-textured-asset
description: Texture an existing 3D model from concept art through Graphmat background CLI, with generated flat textures, UVs, 2K baking, painted masks and visual checks. Forbid Computer Use; MCP requires explicit user instruction. Exclude geometry creation and unsolicited PBR effects.
---

# Model to Textured Asset

Turn an existing mesh into a UV-mapped, textured asset that preserves the concept's palette and painting style. Use Graphmat for UVs, baking, masks and texture application. Keep the source mesh intact; work on a new asset/project path. Geometry redesign belongs to `concept-art-to-3d`.

**Execution boundary:** run the real Graphmat CLI in the background. **Never invoke a Computer Use skill, drive native/browser UI, send synthetic UI actions, or launch the GUI in this skill. Never use MCP unless the human user explicitly instructs you to use it for this task.** A terminal script forwarding requests to MCP is still MCP. Available tools, connectors, previous test permission and invoking this skill do not grant the exception. If MCP is explicitly authorized, read [references/mcp-opt-in.md](references/mcp-opt-in.md); Computer Use remains forbidden.

Follow the gates below in order. A command succeeding is not visual evidence. Keep a compact job log with input paths, material/region table, generated-image count, inspected images, gate results and final output paths.

## 1. Inspect both inputs

Obtain and open the actual **3D model and concept art**. If either is missing, ask for it and prepare only independent work. Inspect mesh parts, material slots, scale, normals and any existing UVs. Do not assume slot names identify the right surfaces.

Build a short table: material, concept location/crop, palette, painting/detail scale, matching mesh parts/faces and planned texture. Separate reusable surface texture from silhouette, hardware, motifs and lighting. Infer hidden surfaces conservatively. Do not paint a missing geometric feature into an unrelated surface to conceal a modeling defect.

## 2. Generate flat textures — before Graphmat authoring

Use the host's available image generator with the concept supplied as an actual image reference. Read [references/hosts.md](references/hosts.md) for host routing. Generate **one full flat base-color image per distinct material**, reusing it for regions with the same surface. Default to 2048 × 2048. Count every generated candidate/retry toward **five generated texture images per asset job**; a batch or atlas does not bypass the cap. If the plan already needs more than five, ask upfront; otherwise ask before generating image six, including retries of rejected candidates. Do not merge visibly different materials merely to fit the cap.

Prompt each image as a surface filling the whole square, viewed straight-on: same palette, brushwork, stylization, wear and detail density as its concept crop; no object silhouette, perspective, labels, borders, scene background or cast/specular lighting. Preserve painted surface variation, while removing scene lighting. Request seamless edges for repeating surfaces. A concept crop or procedural noise is not the generated full texture. Prefer a dedicated non-repeating map for unique markings; do not tile a logo across a model.

**Texture gate:** open every saved image beside the concept/crop. Check hue/value range, brush shapes, contrast, wear, motif/grain scale, flat coverage and unwanted shadows. Inspect repeated edges when tiling is needed. Correct named mismatches within the generation cap; otherwise report the gate blocked. Do not begin Graphmat authoring until every required image passes. Read-only CLI/version discovery is allowed beforehand. Save real image files and prompts; record any resampling separately from generation and verify actual dimensions.

## 3. Set up Graphmat, unwrap and bake at 2K

Read [references/graphmat-cli.md](references/graphmat-cli.md). Verify the installed executable's actual help/schema and required capabilities; do not invent commands or mistake an old/demo build for a capable CLI. Read-only discovery may happen before generation to expose blockers early. **If an operation is missing, stop dependent work and name the gap.** Never launch the app, silently switch to MCP, hand-edit undocumented project internals or replace Graphmat authoring with another application. Independent preparation is useful, but cannot prove a completed pipeline. Extending Graphmat itself requires a separate user instruction.

After the texture and capability gates pass, use verified background CLI commands to create a **new project**, set **project resolution 2048 × 2048** and import the model. Import approved images as **embedded project assets now, without connecting them to outputs**; inventory them before UV generation. Discover syntax from that CLI's help/schema. A required file picker is a missing CLI operation, not permission to use the UI.

Use **Graphmat CLI Auto UV Mapping**, targeting 2048 with an initial 8-pixel padding. Inspect exported checker/UV images and quality metrics for stretch, overlap, coverage and useful density before baking. Preview/apply separately when supported; an authoring command may apply into a new working copy, which you inspect and reject if unsuitable. Preserve the prior project. Explicit apply/confirm flags are authorized for this copy; no extra human confirmation is needed unless source data would be lost.

Configure the bake at **2048 × 2048** after UV application; check project, UV target, bake and export resolution separately. Bake the mesh support maps needed by projection/masking (such as position and world normals), wait for completion and inspect valid results. These support maps are not permission to add visible AO, normal, height or roughness effects. Save a checkpoint before masking. Re-bake after any later UV change.

## 4. Prove material separation with diagnostic colors

Through supported CLI authoring operations, create a named folder per conceptual surface where supported, with an **editable Paint-node mask connected to its Alpha input** and a child fill layer. Otherwise mask the layer directly and explain the limitation. Use 2048-resolution Paint buffers. White includes the region; black excludes it. Identify regions using mesh inspection and Graphmat CLI island/face/material selection and fill operations, then refine mask boundaries. A required click is a missing CLI capability. A graph frame is organizational only and cannot replace a masked folder.

Give each child layer a different random high-contrast flat color; record the color-to-material legend. Export background-rendered diagnostic images of the **actual asset** from front, back, sides and top/underside as needed. Inspect the saved images without desktop capture or Computer Use. Confirm full coverage, correct boundaries, no bleed/holes and matching region ownership. Inspect narrow parts and occluded surfaces; one front view is insufficient. Existing mesh slots can help select regions but do not replace the editable masks or this check.

**Mask gate:** correct the masks and re-inspect until separation matches the concept. Save the diagnostic views/checkpoint. Do not apply generated textures before this gate passes. Remove or disable diagnostic fills for final output.

## 5. Apply approved textures, then verify delivery

Place each approved image inside its verified masked folder/layer. **Use Graphmat's Triplanar layer projection or Mesh Triplanar node when projecting a flat repeating image onto custom/auto UVs**; discover the build's actual mechanism. Tune physical scale, axis orientation and blending against the concept. Inspect seam crossings, curved areas and grain direction. Triplanar projection does not fix incorrect masks or UV overlap. Unique art may need deliberate UV/decal placement instead of repeating triplanar projection.

Author base color only by default, with neutral channel defaults; enable Base Color and Alpha on color layers and disable unrequested channel contributions. Add normal-from-texture, blur, displacement, extra wear, AO styling, procedural detail, roughness/metallic maps or other effects **only when the user explicitly requests those additions**. Graphmat templates and generic "complete PBR" advice do not expand this scope. Report any neutral channels emitted by an export preset.

Save the editable `.gmat`, export the actual UV-mapped mesh through Graphmat's supported export route and export final textures at **2048 × 2048**. Preserve image dependencies in the project/package. If the build cannot export the applied mesh, use its verified UV-preview OBJ route for the same selected layout and verify correspondence, or report the missing capability; never deliver the original UV-less mesh as UV-mapped.

Reload the project through CLI and reimport the delivered mesh/maps into a clean headless viewer. Inspect matching-angle concept comparisons and at least three useful final asset renders. Check material ownership, style/palette, texture scale, seams, UV persistence, map dimensions and missing dependencies. A material-ball/sphere preview is not proof of the actual imported model. Deliver the project, mesh, maps, diagnostic/final views and short verification report, with any failed gates or unavailable capabilities explicit. Follow repository handoff rules.
