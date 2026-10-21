# Lightmap UV Workbench

Open **Tools > Gigaduck > Lightmap UV Workbench**.

This is a central editor for **FBX assets under Assets/**, matching the model Import Settings' **Generate Lightmap UVs** section. It edits the source importer's `.fbx.meta`, so every submesh and scene/prefab instance using that FBX gets the imported result. It does not edit scene renderer overrides.

Search by filename or asset path. Hover a filename for its full path, or click to ping the project asset. Check one or more FBX files to edit them together. **Use Project selection** includes selected FBX files and the FBX files inside selected folders. **Select filtered** checks all search results. Checked assets remain selected when hidden by search; the selection count reports this.

The complete UV generation settings are available:

- Generate Lightmap UVs
- Hard Angle
- Angle Error
- Area Error
- Margin Method (Manual / Calculate)
- Pack Margin (Manual)
- Min Lightmap Resolution and Min Object Scale (Calculate)

A dash indicates mixed values across selected FBX files. Editing a field changes just that field on the selected drafts; other UV settings and unrelated import settings are preserved. If the selection mixes margin methods, both methods' controls are shown. UV settings can be prepared while generation is disabled.

Changes are staged in the window. An asterisk marks a changed FBX. **Apply & Reimport changed FBX files** writes all pending changes, including changes hidden by search, and reimports those assets. A cancellable progress bar shows which file is being processed; completed files stay applied and unprocessed files stay pending. Invalid values and read-only metadata stop the batch before any writes. **Revert all pending** discards unapplied work. Closing with pending work uses Unity's Save/Discard/Cancel prompt; Save applies and reimports. Drafts survive domain reload and inventory refresh. Missing assets with pending drafts remain visible rather than being silently dropped.

**Min Lightmap Resolution is an unwrap packing input, not the scene bake resolution.** Calculate uses this together with the smallest expected object scale. Pack Margin is specified in pixels assuming a full 1024 x 1024 lightmap. Re-bake affected scenes after changing UV generation. Applying importer changes follows Unity's reimport workflow; it is not a scene Undo operation. Package-cache models and non-FBX formats are outside the tool's scope.

Run **Tools > Gigaduck > Validate Lightmap UV Workbench** or use `-executeMethod Gigaduck.LightmapWorkbench.LightmapUVWorkbenchChecks.Run` in batch mode. Checks create a disposable copy of the smallest project FBX, test settings persistence and preservation, and remove that copy in a finally block. Original models and scenes are not modified.
