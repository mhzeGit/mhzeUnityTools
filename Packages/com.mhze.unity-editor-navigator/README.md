# Unity Editor Navigator

## What This Tool Does
Unity Editor Navigator adds keyboard-first workflows to the Unity editor so you can reorganize Hierarchy objects and move Project assets without relying on drag-and-drop.

## Why It Helps
- Reduces repetitive mouse work during scene and project cleanup.
- Makes large refactors faster when moving many objects or assets.
- Keeps your flow focused when you prefer keyboard navigation.

## Features
- Hierarchy object move up/down by sibling index.
- Hierarchy reparenting (move in) and unparenting (move out).
- Project asset Move Mode with highlighted target folder.
- Multi-selection support.
- Undo support for Hierarchy operations.
- Settings/help window with shortcut reference.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste this package repository URL.

### Option B: Local package folder
1. Copy `unity-editor-navigator` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
### Hierarchy shortcuts
Use these while Hierarchy is focused:

| Shortcut | Action |
|---|---|
| Ctrl + Shift + Up | Move selected object(s) up |
| Ctrl + Shift + Down | Move selected object(s) down |
| Ctrl + Shift + Right | Parent under sibling above |
| Ctrl + Shift + Left | Unparent to parent level |

Menu path: `Tools/Hierarchy Navigator/...`

### Project Move Mode
1. Select one or more assets in Project window.
2. Press Ctrl + Shift + Up or Down to enter Move Mode.
3. Navigate target folder:
   - Ctrl + Shift + Up/Down: sibling folders
   - Ctrl + Shift + Right: enter highlighted folder
   - Ctrl + Shift + Left: move up one level
4. Press Enter to confirm move.
5. Press Escape to cancel.

Menu path for guide window: `Tools/Hierarchy Navigator/Settings && Help`

## Example Workflow
1. Reorganize scene root objects with the Hierarchy shortcuts.
2. Select textures or scripts in Project and move them into a feature folder using Move Mode.
3. Use the help window to check shortcuts while onboarding teammates.

## Notes
- Project asset moves are file operations and are not covered by Unity Undo.
- Hierarchy moves and reparenting use Undo.

## License
See `LICENSE.md` in this package.
