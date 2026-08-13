# Folder Colorizer

## What This Tool Does
Unity Folder Colorizer applies custom colors to Project window folders so you can quickly identify important areas of your project.

## Why It Helps
- Speeds up navigation in large projects.
- Makes folder categories easier to scan visually.
- Stores project-specific color settings in `ProjectSettings` for team sharing.

## Features
- Color folders by folder name.
- Toggle colorization on/off from settings.
- Default color set loaded from package resources.
- Automatic persistence to `ProjectSettings/FolderColorSettings.json`.
- Handles legacy settings migration.

## How To Use
1. Open Project Settings.
2. Go to `Project/Folder Color Settings`.
3. Enable `Use Custom Folder Color`.
4. Add a folder name and pick a color.
5. Click Add / Modify.
6. Colors appear in Project view immediately.

For existing entries:
- Use Apply to rename or recolor.
- Use Remove to delete a mapping.

## Example Workflow
1. Set `Scripts` to green.
2. Set `Art` to orange.
3. Set `Audio` to yellow.
4. Commit `ProjectSettings/FolderColorSettings.json` so your team sees the same colors.

## Notes
- Matching is by folder name, not full path.
- The package resolves names to actual folder paths at load time.

## License
See `LICENSE.md` in this package.
