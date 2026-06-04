# Unity Batch Renamer

## What This Tool Does
Batch Renamer lets you rename multiple Unity assets and GameObjects at once with real-time preview, search/replace with boolean logic, prefix/suffix insertion, text case transformation, and number preservation.

## Why It Helps
- Saves huge amounts of time when renaming many assets or objects.
- Preview every change before applying so you can avoid mistakes.
- Supports undo for GameObjects and asset database renaming for project files.

## Features
- **Search & Replace** — search by literal text with optional case sensitivity.
- **Boolean operators** — use `||` (OR), `&&` (AND), and `[]` for grouping.
- **`{Number}` token** — matches any digits in search; inserts the matched digits when used in replace.
- **Category filters** — filter by asset type: Prefab, Material, Texture, Model, Audio, Script, Animation, Folder, Scene.
- **Prefix / Suffix** — prepend or append text.
- **Text Case** — None, Lowercase, Uppercase, TitleCase, SentenceCase, CamelCase, PascalCase.
- **Number Preservation** — detect trailing numbers and re-append them with configurable format after rename.
- **Real-time preview** — highlighted match regions, diff display, and per-row old→new name comparison.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste `https://github.com/mhzeGit/mhzeUnityTools.git?path=Packages/com.mhze.unity-batch-renamer`

### Option B: Local package folder
1. Copy `com.mhze.unity-batch-renamer` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
1. Select assets in the Project window or GameObjects in the Hierarchy.
2. Open via **Assets/Batch Rename** or **GameObject/Batch Rename**.
3. Enter a search pattern:
   - Simple text: `player` matches names containing "player".
   - OR: `player||enemy` matches names containing either.
   - AND: `player&&armor` matches names containing both.
   - Grouped: `[player||enemy]&&boss` — complex boolean logic.
   - `{Number}` matches any digits in the name.
4. Toggle **Case Sensitive** to make matching exact.
5. Enter replace text, prefix, suffix, or choose a text case mode.
   - Use `{Number}` in replace to insert the matched digits (e.g. replace `{Number}` with `ID_{Number}` turns `_01` into `ID_01`).
6. Preview all changes in the scrollable list.
7. Click **Rename Selected** to apply.

## Search Examples
| Pattern | Matches |
|---|---|
| `hero` | any name containing "hero" |
| `hero||villain` | any name containing "hero" or "villain" (or both) |
| `hero&&sword` | any name containing both "hero" and "sword" |
| `[hero\|\|villain]&&boss` | any name containing "boss" and either "hero" or "villain" |
| `Hero` (case-sensitive ON) | only names with exactly "Hero" (not "hero" or "HERO") |
| `{Number}` | any name containing digits |
| `hero_{Number}` | any name containing "hero_" followed by digits |

## Notes
- GameObject renames support Undo (Ctrl+Z).
- Asset renames use `AssetDatabase.RenameAsset` and are reflected in the project immediately.
- Number preservation strips a trailing number (e.g. `_01`) before rename, then re-appends it with your chosen format.

## License
See `LICENSE.md` in this package.
