# Editor Visual Tweaks

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
