# Unity Git Integration

## What This Tool Does
Unity Git Integration brings core Git workflows into the Unity editor: commit history graph, working tree changes, per-asset history, inline diffs, and repository configuration.

## Why It Helps
- Lets artists and designers inspect Git state without leaving Unity.
- Speeds up code and asset review with in-editor diffs.
- Reduces context switching between Unity and external Git tools.

## Features
- Main Git window with history, working tree, and configuration views.
- Commit graph rendering with branch lanes and merge connections.
- Commit detail panel with changed files and inline diff.
- Asset context menu actions:
	- `Assets/Git/View History` (Ctrl + H)
	- `Assets/Git/View Working Diff` (Ctrl + D)
- Main window shortcut: `Tools/Git Integration` (Ctrl + Alt + G).
- Setup/config tools for user identity, remotes, `.gitignore`, and LFS.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste this package repository URL.

### Option B: Local package folder
1. Copy `unity-git-integration` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
1. Open `Tools/Git Integration`.
2. Use History view to browse commits and select one for details.
3. Open Working Tree view to inspect current modified files.
4. Select a changed file to see inline diff.
5. Open Config to edit Git name/email and remote URL.

Asset-level workflow:
1. Right-click an asset in Project window.
2. Choose `Git/View History` for commit history of that file.
3. Choose `Git/View Working Diff` for current unstaged/staged diff.

## Example Workflow
1. Make scene or prefab changes.
2. Open Working Tree to inspect changed files.
3. Check exact line changes in inline diff.
4. Switch to History and compare with recent commits.
5. Use Config view to fix missing Git identity if needed.

## Notes
- Requires Git installed and available in PATH.
- Git commands run from your project repository root.

## License
See `LICENSE.md` in this package.
