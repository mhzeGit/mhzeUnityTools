# Implementation runbook

Use this with the parent `SKILL.md`. Read only the detailed guides needed for the current stage. Preserve the design gates, especially a finished representative section before producing the full kit. The numbered guides retain the original pipeline stage numbers for command lookup; those numbers do not require finishing all models before any texturing.

## Paths and compatibility

`<skill>` is the absolute folder containing `SKILL.md`. Scaffold from the repository root:

```text
python <skill>/scripts/new_environment.py --name <CamelName> --unity-base Assets/<GameRoot>
```

This creates `Docs/Art/<Name>/` with `env.json`, working notes, templates and copied tools. In detailed guides, `tools/...`, `reference/...`, `textures/...`, `layout/...` and `groups/...` are relative to that environment folder; change directory there first. Paths beginning `Docs/` or `tools/skills/` are repository-relative. Use shell-appropriate quoting for actual paths containing spaces.

Configure the installed tools, actual concept filenames, renderer/pipeline, approved generator, texture budget, controller dimensions and source assets. `env.json` starts with a provisional 16-image cap for the optional CLI adapter; reconcile it with the actual brief and texturing skill before generating. The 2048 atlas implementation is appropriate only when its density and pipeline fit the project.

Discover tools with `python tools/envcfg.py`. For a new or changed toolchain, `python <skill>/scripts/selftest.py` exercises Blender export/reimport and Graphmat in an isolated temporary project; `--unity` also checks the editor endpoint. This test writes temporary artifacts and takes time; it is not required for a documentation-only task or every subsequent build. It does not prove visual quality or runtime walkability. The image generator is reported, never called by the selftest.

The Unity helpers use an HTTP MCP endpoint and C# compiled in memory. Prefer the session's Unity connector when suitable. `tools/unity/ucs.py <source.cs> <Type.Method> <json> --emit` prints a bridge payload for a compatible `execute_code` tool. Inspect emitted code and respect the connector's safeguards; do not disable checks merely to force an operation through.

## Stage sequence and evidence

| Stage | Read / tools | Completion evidence |
| --- | --- | --- |
| Orient and brief | [01](01-orient-and-brief.md), `new_environment.py`, `envcfg.py`, optional `selftest.py` | Tool compatibility, editor baseline, actual request, assumptions and budgets |
| Analyze the reference | [02](02-reference-analysis-and-blockout.md), `crop.py`, `grid.py`, camera-fit helpers | Crops inspected; complete asset/surface inventory, dimensions, reuse and geometry/paint choices |
| Block out | [02](02-reference-analysis-and-blockout.md), new-path `unity/tools.py blockout` | Matching view, overview, eye-height views and actual controller route |
| Finish a representative section | [representation decisions](representation-decisions.md), [03](03-asset-kit-modelling.md), [04](04-textures-generation.md), [05](05-graphmat-texturing.md), [06](06-unity-import-and-scene.md), [07](07-lighting-and-post.md) | A corner/opening, repeated surface and focal prop textured and lit in Unity; close/grazing views; measured cost |
| Produce and replace in batches | [03](03-asset-kit-modelling.md), `run_asset.py`, `review.py`, `gm_angles.py` | Asset sheets vs crops, real FBX reimport, hard-edge checks, controlled placement and reuse |
| Generate and select surfaces | [04](04-textures-generation.md), optional `gen_textures.py`, `finalize_textures.py`, optional `make_brick_tile.py` | Logged attempts within agreed cap; selected textures and inspected tiling previews |
| UV, masks, texture and requested AO | [05](05-graphmat-texturing.md), `gm_groups.py`, `gm_run.py`, `gm_sheet.py`, `gm_check.py`, `gm_normals.py` | Diagnostic masks inspected before texture application; final sheets; UVs and normals on delivered FBX |
| Import and traverse | [06](06-unity-import-and-scene.md), compatible `tex.py` stages, collision tools | Correct materials and collisions; actual player route through entrances, corners, stairs and boundaries |
| Light and polish | [07](07-lighting-and-post.md), `cap.py`, `sbs.py`, `stats.py`, optional grade helpers | Reference comparison and neutral views; consistent lighting from reachable angles |
| Verify and deliver | [verification](verification.md), [08](08-verification.md), [10](10-shared-editor-and-repo-rules.md), `export_deliverables.py` | Opened screenshots, runtime and technical evidence, editable sources, validators and repository handoff |

Gates are agent verification checkpoints, not mandatory human approval pauses. Reuse valid completed work. When a stage fails, fix the cause and rerun affected checks; after repeated unsuccessful attempts change the approach and record unresolved limitations honestly. Do not continue dependent production while the underlying recipe fails.

## Toolkit contracts and boundaries

- **Modeling:** `templates/asset_module.py` defines `ASSETS` build functions and `<Region>__<Part>` names used by downstream masking. `bq.py`, `bl.py` and `run_asset.py` handle headless construction, real FBX export/reimport, review images and stats. Read the modeling skill for required review and revisions. Use measured runtime costs rather than copying the example's polygon counts.
- **Textures:** shared surface IDs, physical repeat size and texel density are planned before UVs. Apply [the UV/texture efficiency gate](uv-and-texture-efficiency.md) before full production and on final exports: inspect useful packing coverage, density, safe padding and material ownership; correct sparse atlases and duplicated surface data. Repeated brick, paving and other small surface detail is painted on simple forms. `gen_textures.py --prompt <material>` can prepare prompts for another generator; log those external attempts in the same budget record. Do not split jobs to evade limits. Tileable, trim or atlas strategies must be supported and proven by the selected tools.
- **Graphmat:** use the background CLI under `model-to-textured-asset` tool restrictions. Run `export setup masks review_diag`, inspect the diagnostic sheet, then `textures review_final import`. A batch must stop at the diagnostic gate. The legacy jobs include AO operations by default: use them only when AO is part of the brief; configure or adapt them otherwise. Check current CLI capabilities before considering the included historical extension patch; never apply an old patch blindly or modify another repository without authorization.
- **Unity creation:** `unity/tools.py blockout` and `assemble` are for brand-new scene paths only. Their legacy implementation uses `NewScene(Single)` and must be adapted or replaced for a shared editor: preserve open scenes and use additive creation. Never rerun a scene generator over an existing path. Create objects directly in the authorized edit area when revising an existing scene.
- **Unity updates:** `tex.py all` includes prefab regeneration; do not use it on a hand-authored prefab. Import maps/models separately and assign materials or collision through scoped edits. Review legacy scene/capture helpers before running against a scene already open by the user; they can change active scenes, render settings, temporary objects and save state. A file timestamp alone does not establish ownership: compare contents with your baseline and preserve external edits.
- **Traversal:** edit-mode `WalkTest` is a collision diagnostic. It never substitutes for the actual controller route. In BlahBlahFamilyV6 use your own standalone player (`walk`, `teleport`; `Docs/rules/TESTING.md`), then stop it. Keep the project's intended controller.
- **Lighting:** the included shader/material/volume code targets URP and the example's stylized look. Adapt to the installed pipeline and concept. Choose baked, mixed or realtime lighting deliberately; consider lightmap UVs and probes. Statistics and grade searches help diagnose differences; matching global means does not establish visual fidelity.

## Troubleshooting and example

[09-pitfalls.md](09-pitfalls.md) preserves practical failures and fixes, including region welding, projection stretch, normals and capture-volume issues. It is a symptom lookup, not a list of actions to execute. [11-case-study-boiler-room.md](11-case-study-boiler-room.md) and `examples/boiler-room/` preserve the original 49-asset build, layout, material prompts and texture-group data. Borrow patterns, not boiler-room content, fixed dimensions, lighting values, budgets or historical workarounds.

## Install or update the merged skill

Run `pwsh -NoProfile -File <skill>/scripts/install_skill.ps1` to install into personal Codex, Claude Code and Kilo Code roots. `-SkillRoots` selects explicit roots; `-ProjectRoot` adds project Claude/Kilo roots and `-Gemini` includes Gemini. The installer verifies file hashes, skips identical copies, and moves replaced/retired installations to each agent configuration folder's `skill-backups/` directory outside skill discovery. The single invocation is `$environment-design`.
