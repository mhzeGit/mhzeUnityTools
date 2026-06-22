# Unity Quick Texture Tools

## What This Tool Does
Unity Quick Texture Tools provides fast, repeatable texture operations directly in Unity: invert RGB, generate mask maps, flip normal Y channel, and whiten textures while keeping alpha.

## Why It Helps
- Removes repetitive external image editor steps.
- Generates consistent outputs for common material workflows.
- Works on selected textures in batches.

## Features
- Invert RGB channels and keep alpha untouched.
- Build mask map with channels:
	- R = metallic
	- G = ambient occlusion
	- B = detail mask
	- A = smoothness
- Flip normal map green channel for DirectX/OpenGL conversion workflows.
- Whiten RGB while preserving alpha.
- Context menu support on selected textures.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste this package repository URL.

### Option B: Local package folder
1. Copy `unity-quick-texture-tools` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
Window entry: `Tools/Unity Quick Texture Tools`

Context menu entries on selected textures:
- `Assets/Unity Quick Texture Tools/Invert`
- `Assets/Unity Quick Texture Tools/Normal Y-Flip`
- `Assets/Unity Quick Texture Tools/Whiten`
- `Assets/Unity Quick Texture Tools/Mask Map...`

Typical process:
1. Select texture(s).
2. Choose operation from the tool window or context menu.
3. New PNG files are generated with suffixes like `_Inverted`, `_YFlipped`, `_Whitened`, or custom mask map name.

## Example Workflow
1. Select metallic, AO, detail mask, and roughness textures.
2. Open Mask Map tab.
3. Assign channels and enable/disable roughness inversion for smoothness.
4. Generate and use output in your material.

## Notes
- Textures may be temporarily switched to readable during processing and restored.
- Source files are not overwritten.

## License
See `LICENSE.md` in this package.
