# Verified FBX delivery

Use before exporting and for the final acceptance check. Choose export settings for the actual target engine and installed Blender/exporter version; inspect supported options rather than copying an unverified preset.

## Prepare the asset

- Isolate exportable asset objects; exclude reference planes, cameras, lights, and helpers. Preserve required rig objects when the requested asset is animated. Avoid destructive cleanup of unrelated scene content.
- Set a useful pivot/origin, intended units, orientation, dimensions, and consistent object names. Account for transforms and axis conversion without applying scale/rotation blindly to a rig or changing the intended shape.
- Evaluate necessary modifiers and control final triangulation/normals. An FBX must contain the intended geometry, not unresolved procedural generators or the wrong editing cage. Preserve the editable authoring source separately.
- Export geometry without creating texture dependencies or embedding images. Use neutral clay inspection; preserve existing data when refining a supplied mesh. Named parts or empty surface-region slots can support the later texturing handoff.

## Verify the exported file

1. Export selected asset content to a new `.fbx` path. Verify the file exists and is nonempty; a format-correct file still needs visual validation.
2. Reimport that exact file into a fresh isolated scene/file without reference objects or authoring copies. Ensure screenshots and measurements come from the imported objects, not an accidentally visible original. Do not overwrite the authoring scene.
3. Compare expected parts, dimensions, pivot behavior, orientation and normals/smoothing. Verify part/region names survive. Measure imported vertices and triangles, noting format-induced splits or triangulation rather than demanding identical authoring counts.
4. Render/capture the imported asset from three-quarter, true side, and front or top views. Open and visually inspect all final views. A back or underside view is useful when it reveals distinctive construction. Use clear neutral lighting and enough resolution to assess silhouette and surface quality; dramatic lighting must not conceal defects.
5. When game-engine import is part of the authorized task, verify the imported asset there too, using a new asset path and existing project rules. Do not overwrite scenes/prefabs or change global import settings as a shortcut. If only Blender reimport was checked, say so; do not claim an engine test.

Export defects discovered here require correction and reinspection. Include model-changing export repairs in the same maximum of five revision passes; merely regenerating a screenshot or retrying unchanged export settings is not a model revision. If a defect persists at the limit, deliver any files as provisional with the failure explained, not as a finished game-ready asset.

## Final handoff

Provide actual paths/links to:

- The verified final FBX.
- At least three distinct screenshots of that reimported FBX: three-quarter, true side, and front or top. Identify their angles. An image-generation render or reference-image overlay cannot replace these screenshots.
- The concept reference and part-to-surface-region notes for `model-to-textured-asset`. Do not deliver texture maps, new UVs or appearance materials in this modeling task.

Retain an editable `.blend` or equivalent source and the modeling script when used; offer these as supporting files when useful. Report:

- Asset scale/dimensions and intended target/use.
- Measured final vertices and triangles, what those counts refer to, meshes and region labels.
- Number of completed revision passes (one to five for completed work), verification performed, and remaining deviations or inferred unseen surfaces.

Do not approve the model if reference match, geometry quality/budget, required revision, reimport check or deliverable files fail. Passing these checks means verified untextured geometry; UVs and final appearance remain a separate task.
