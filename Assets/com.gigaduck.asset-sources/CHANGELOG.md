# Changelog

## 1.5.4

- Show source and folder shortcuts only on selected Project rows, including multiple selected assets. On Unity 6, selecting a subasset does not show shortcuts on its unselected parent row.
- Skip source-record reads for unselected rows.

## 1.5.3

- Import the application PNGs as full-color, uncompressed RGBA textures. Incomplete importer metadata previously triggered Unity's legacy alpha-only migration and displayed black silhouettes.
- Verify the imported RGBA format for every bundled icon in regression tests.

## 1.5.2

- Replace text badges with actual Blender, Graphmat and Affinity application icons, and a native source-file icon for applications without bundled artwork.
- Draw icon-only shortcuts at 16 pixels with a hover highlight, preserving right alignment, tooltips and live-sync actions.
- Verify that row contents contain images and no initials, bundled artwork loads in the source project and package, and the image fits its button.

## 1.5.1

- Reuse the folder icon content while repainting Project rows.
- Verify every application badge, the native folder icon and the shared source-choice action that enables only the clicked link before launching, without controlling desktop applications.

## 1.5.0

- Add compact application badges at the far right of linked assets in Project list rows. Clicking a badge opens the source and enables its live sync; multiple sources for an application show a chooser.
- Add a folder button at the rightmost edge to reveal linked sources without changing sync settings.
- Cache visible-row source availability, disable unavailable sources, and keep unlinked assets and thumbnail grid tiles clear.

## 1.4.4

- Keep automatic Blender exports working when a confirmed single-mesh master has a different object or mesh name from the linked Unity mesh.
- Use the same mapping for live edits and save exports, preserving the Unity mesh name and restoring the Blender master names after export.
- Continue rejecting ambiguous multi-mesh mappings; cover renamed edits, repeated saves and exported mesh identity in native Blender checks.

## 1.4.3

- Make Open Blender Source and Open + Live Link explicitly enable the selected connection before launching, without enabling other disabled targets.
- Export every linked mesh on each Blender save, including unchanged saves, and force Unity to import the result even when its bytes match.
- Bake static FBX axis conversion into mesh data and store unit scaling in FBX metadata, avoiding the -90-degree root correction and 100x object scale when meshes are used directly by prefabs.
- Isolate the revised bridge protocol from older running Blender connections.
- Cover unchanged saves, forced-import messages, explicit connection enabling and asymmetric FBX orientation/scale in regression checks.

## 1.4.2

- Use Windows extended filesystem paths for live handshake files and staged exports in deeply nested external resource libraries.
- Add a regression check writing handshake files beyond Windows' legacy 260-character limit.

## 1.4.1

- Publish the initial Blender handshake before its asynchronous target scan completes, preventing a startup race on fast launches.
- Allow a disconnected source to reopen and give stopped connections a fresh session token.

## 1.4.0

- Connect Blender when opening a source and update linked FBX/OBJ/GLB meshes from unsaved edits, after a short editing pause.
- Preserve Blender selection, visibility, active object, frame and edit mode during export; never save the master.
- Keep the connection across Unity script reloads, reuse an already connected Blender session and stop it with Stop Live Links.
- Respect per-target Live sync off both when finding shared-master outputs and immediately before publication.
- Retain atomic publication, GUID/importer preservation, conflict checks and save-based export for other source types.
- Add Blender integration checks for transforms, edit-mode geometry, feedback loops, failed mappings and source preservation.

## 1.3.0

- Add navigation-only source links for shared masters that must not auto-export into a generated asset.
- Add a per-source Live sync toggle in the Asset Sources window.
- Log the resolved source path, chosen executable and launch result when opening a source, and report missing executables or OS launch failures as errors.

## 1.2.2

- Disable Open and Show actions for links whose source file is missing, and mark missing entries in the source chooser.

## 1.2.1

- Add a status provider so project pipelines can report their own sync state in the Asset Sources window.

## 1.2.0

- Add a public `SourceSaved` event so project pipelines can react when an armed master changes.
- Add `ApplicationExecutable` to resolve an application without launching it.

## 1.1.1

- Report navigation-only links immediately instead of claiming they will update on save.

## 1.1.0

- Add live links: opening a source arms it, and saving it exports and updates every linked Unity asset in the background.
- Support Blender mesh/embedded-image export, Graphmat single-channel PNG export, Painter remote export, Photoshop image export, Designer `.sbsar`/PNG export and same-extension copies.
- Keep Unity GUIDs and importer settings by replacing the imported file atomically, and leave the current asset untouched when an export fails or is ambiguous.
- Keep the source open fast by finding linked assets and exporting off the editor thread.
- Add Edit Mode tests for channel mapping, material ambiguity, shared masters, atomic publication and failure handling.

## 1.0.0

- Add portable per-asset source records and per-machine library/application locations.
- Add explicit setup/relink window and context actions for Blender, Graphmat, Painter and other sources.
- Add public editor automation API, JSON CLI and isolated Unity/Python tests.
