# Unity Folder Structure Generator

## What This Tool Does
Quickly generate folder structures from built-in presets or your own custom templates. Supports nested folder hierarchies and batch creation with one click.

## Why It Helps
- Standardizes project folder layout across your team.
- Saves time setting up new projects or reorganizing messy ones.
- Nested folder trees are visually edited and generated in one pass.

## Features
- **Presets**: Built-in templates (Standard Unity, Clean Architecture, Modular Game).
- **Custom Presets**: Save any tree you build as a reusable `.asset` preset.
- **Nested Folders**: Add child folders to any depth (up to 5 levels).
- **One-Click Setup**: Press "Setup Folders" to create the entire tree under `Assets` or any target folder.
- **Live Editing**: Rename, add, or remove folders directly in the window.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste this package repository URL.

### Option B: Local package folder
1. Copy `com.mhze.unity-folder-structure-generator` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
1. Open **Tools > MHZE > Folder Structure Generator**.
2. Select a preset from the dropdown.
3. (Optional) Edit the folder tree: rename, add child folders (`+`), remove (`x`).
4. (Optional) Pick a **Target Folder** in the Project window to create folders under a sub-folder.
5. Click **Setup Folders** — all folders are created immediately with `.meta` files.

To save your current tree as a reusable preset:
1. Click **Save as Preset**.
2. Choose a name and location.
3. The preset appears in the dropdown on next open.

## Presets

### Standard Unity Project
Organises by asset type: `_Scripts`, `_Art`, `_Audio`, `_Prefabs`, `_Scenes`, `_Animations`, `_Resources`, `_ThirdParty` — each with common sub-folders.

### Clean Architecture
Follows Domain / Application / Infrastructure / Presentation layers under `_Scripts`.

### Modular Game
Groups by feature: `Core`, `Gameplay`, `UI`, `Audio`, `Art`, `Config`, `Tests`.

## Notes
- Folder names are sanitised (invalid characters replaced with `_`).
- Existing folders are skipped (no overwrite).
- Window state resets on domain reload.

## License
See `LICENSE.md` in this package.
