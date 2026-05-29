# Unity Editor Visual Tweaks

## What This Tool Does
Unity Editor Visual Tweaks adds readability improvements to Hierarchy and Project windows by drawing zebra row backgrounds and tree guide lines.

## Why It Helps
- Improves readability in long object and folder lists.
- Makes parent-child structure easier to scan.
- Lets each developer enable only the visual helpers they want.

## Features
- Zebra stripes for Hierarchy rows.
- Zebra stripes for Project rows (list view).
- Hierarchy guide lines for object depth.
- Project folder depth lines (list/tree style rows).
- User preferences under `Preferences/Editor Visuals`.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste this package repository URL.

### Option B: Local package folder
1. Copy `unity-editor-visual-tweaks` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
1. Open `Edit/Preferences`.
2. Go to `Editor Visuals`.
3. Toggle features independently:
   - Hierarchy Zebra Stripes
   - Hierarchy Lines
   - Project Zebra Stripes
   - Project Lines

## Example Workflow
1. Enable Hierarchy Zebra and Lines for scene organization.
2. Enable Project Zebra when browsing large asset folders.
3. Disable Project Lines if your team mostly uses grid view.

## Notes
- Visual lines and zebra behavior are designed for list/tree rows, not icon grid mode.
- Settings are stored per user via Unity EditorPrefs.

## License
See `LICENSE.md` in this package.
