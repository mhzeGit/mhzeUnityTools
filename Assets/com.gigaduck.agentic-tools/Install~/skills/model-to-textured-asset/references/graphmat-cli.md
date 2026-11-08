# Graphmat background CLI

Use the actual standalone `graphmat-cli` executable. Do not open the desktop app, start an MCP listener or drive UI. A terminal wrapper around MCP is still MCP.

## Locate and verify

Inspect `graphmat-cli help` and command-specific help; an old executable may print generic help for an unknown command and still exit 0. Required authoring commands are **author, mesh-info, render-model, export-model**, plus the existing UV/inspection/extraction commands. Verify these exist in the binary, not just its source documentation.

On the validation PC, the upgraded build is:

```text
C:/Users/mhzeu/Projects/Softwares/Graphmat/tools/GraphMat.Cli/bin/Release/net8.0/graphmat-cli.exe
```

This is a local hint, not a portable dependency. The older Debug/dist binaries may lack authoring. Prefer the verified full build. If a required operation is absent, report it and stop dependent work; do not fall back to GUI, MCP, hand-crafted project containers or another texturing application.

## Phased jobs

Bind `$cli` to the verified executable. Paths inside a job are relative to that JSON file; use exact documented lower-camel-case properties. Save each phase to a new project path. **Do not copy every operation from help into one job: visual texture, UV and mask gates separate the phases.**

```powershell
& $cli help author
& $cli author --job setup.json --out imported.gmat
& $cli mesh-info imported.gmat
& $cli author imported.gmat --job unwrap.json --out uv.gmat
& $cli author uv.gmat --job bake.json --out baked.gmat
& $cli mesh-info baked.gmat
& $cli author baked.gmat --job masks.json --out diagnostic.gmat
& $cli render-model diagnostic.gmat --out diagnostic-front.png --yaw 0 --pitch 0
# Inspect several diagnostic views before the texture job.
& $cli author diagnostic.gmat --job textures.json --out textured.gmat
& $cli export-model textured.gmat --out asset-uv.obj
& $cli extract textured.gmat --channel basecolor --size 2048 --out albedo.png
```

A setup job creates the project and embeds approved images without applying them:

```json
{
  "create": {"mesh":"model.obj", "resolution":2048, "name":"Asset"},
  "operations": [
    {"op":"importTexture", "name":"wood", "path":"wood-2k.png"}
  ]
}
```

Other jobs contain `operations` arrays (discover full schemas in `help author`):

| Phase | Operations |
| --- | --- |
| UV | `autoUv` with resolution 2048, padding 8; `respectHardEdges` and `thoroughPacking` tune charting/packing; inspect the resulting copy before baking |
| Support bake | `bakeProjectionMaps` uses project resolution; provides projection data, not artistic PBR effects |
| Diagnostic masks | `addRegion` with name, distinct color and selection; then `evaluate` |
| Mask revision | `updateMask` replaces a folder's selection; `importMask` loads an exact-resolution mask; then `evaluate` |
| Approved textures | `assignTexture` with folder, embedded texture name, projection `triplanar`, scale and optional rotation/origin/textureRotation vectors; then `evaluate` |

Example region operation: `{"op":"addRegion","name":"Wood","color":"#17B8D4","selection":{"components":[0]}}`. Do not reuse example component 0 blindly. `mesh-info` exposes parts/materials, connected-component and UV-island IDs, bounds, seed faces, image inventory and baked-map sizes. Inspect it after UVs; select actual regions through `components`, `parts`, `materials`, `faces`, `islands` or `all:true`. Existing source material slots become separate material documents; use the documented `material` selector when needed. Verify each folder has an editable 2048 Paint mask connected to Alpha and a child contributing only albedo/alpha.

Example texture operation: `{"op":"assignTexture","folder":"Wood","texture":"wood","projection":"triplanar","scale":0.25,"rotation":[0,0,0]}`. Tune against actual rendered grain/detail scale; this number is not a universal preset. `rotation` turns the projection frame; `textureRotation:[x,y,z]` turns the image within the axis projections. Use the latter when brick courses or grain rotate incorrectly on an adjacent face, then inspect all affected faces. `evaluate` rebuilds native node caches in the background. Importing an image alone does not apply it.

## Inspect and export

`render-model` renders the **actual imported mesh**; use `--shading albedo` for diagnostic colors and several yaw/pitch views. Inspect saved images with a native image reader. Existing `render`/`sheet` use a material ball/sphere and cannot prove correct asset masks.

Use `info`, `layers` and `nodes` to verify saved state. Extract each material separately with `--material` when slots differ; one material's map cannot represent all slots. The job's `export.albedo` likewise requires `export.material` for multiple slots. Export the current applied UV mesh, not the original input. `export-model` writes OBJ geometry, UVs, normals and `usemtl` slot names; it does not bind images or write an MTL. Verify OBJ UV orientation, normals, slots, image assignments and dimensions in a clean headless reimport. Package separate maps and any required consumer material setup honestly.

Existing `uv-map` supports preview OBJ and confirmed project-copy application. Its 2048 packing target can yield a different internal atlas pixel size with normalized UVs; project/bake/export dimensions remain separate. Never assume independently regenerated candidates have identical UV correspondence. `set` invalidates caches; run author/evaluate before extraction after edits.

Use explicit paths, preserve sources and check exit codes/JSON errors. On Windows use shell pipes or `Start-Process -WindowStyle Hidden` with redirected output. Give long jobs bounded deadlines; inspect outputs before retrying a timed-out mutation. Authoring operates on a copy; overwriting an old output requires the CLI's explicit flag and never justifies overwriting an input.
