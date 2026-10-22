# Changelog

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
