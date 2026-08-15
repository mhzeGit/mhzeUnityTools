# Advanced Batch Renamer

## What This Tool Does
Advanced Batch Renamer lets you rename multiple Unity assets and GameObjects at once with real-time preview, search/replace with boolean logic, prefix/suffix insertion, text case transformation, and number preservation.

## Why It Helps
- Saves huge amounts of time when renaming many assets or objects.
- Preview every change before applying so you can avoid mistakes.
- Supports undo for GameObjects and asset database renaming for project files.

## Features
- **Search & Replace** — search by literal text or **standard .NET regex** with optional case sensitivity.
- **Boolean operators** — use `||` (OR), `&&` (AND), and `[]` for grouping.
- **`{Number}` token** — matches any digits in search; inserts the matched digits when used in replace.
- **Regex mode** — toggle **Regex** to use patterns like `\d+`, `[A-Za-z]+`, capture groups `(\w+)`, alternation `a|b`, and anchors `^...$`; replace with `$1` / `${name}` / `$&` group references.
- **Category filters** — filter by asset type: Prefab, Material, Texture, Model, Audio, Script, Animation, Folder, Scene.
- **Prefix / Suffix** — prepend or append text.
- **Text Case** — None, Lowercase, Uppercase, TitleCase, SentenceCase, CamelCase, PascalCase.
- **Number Preservation** — detect trailing numbers and re-append them with configurable format after rename.
- **Real-time preview** — highlighted match regions, diff display, and per-row old→new name comparison.
- **Per-item include/exclude** — checkbox on every preview row; uncheck to skip an item, or check an item the search/filter excluded to force-include it (applies prefix/suffix/case transforms). Overrides search results.

## How To Use
1. Select assets in the Project window or GameObjects in the Hierarchy.
2. Open via **Assets/Advanced Batch Rename** or **GameObject/Advanced Batch Rename**.
3. Enter a search pattern:
   - Simple text: `player` matches names containing "player".
   - OR: `player||enemy` matches names containing either.
   - AND: `player&&armor` matches names containing both.
   - Grouped: `[player||enemy]&&boss` — complex boolean logic.
   - `{Number}` matches any digits in the name.
4. Toggle **Case Sensitive** to make matching exact.
5. Enter replace text, prefix, suffix, or choose a text case mode.
   - Use `{Number}` in replace to insert the matched digits (e.g. replace `{Number}` with `ID_{Number}` turns `_01` into `ID_01`).
6. Preview all changes in the scrollable list. Use each row's checkbox to include/exclude items individually — excluded items are skipped, and items the search missed can be force-included.
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
| `^\d+_` (Regex ON) | any name starting with digits followed by underscore |
| `_v(\d+)$` → replace `_v$1` (Regex ON) | renames `hero_v2` to `hero_v2` with the version captured and re-inserted |
| `[A-Z]` (Regex ON) | any name containing a capital letter |

## Regex Mode
- Toggle **Regex** next to **Case Sensitive** to treat the Search field as a .NET regular expression.
- The **Case Sensitive** toggle adds/removes `RegexOptions.IgnoreCase`.
- Replace supports standard .NET substitution syntax: `$1` (first capture group), `${name}` (named group), `$&` (whole match), `$$` (literal `$`).
- The `{Number}` and `{Index}` tokens still work in regex mode.
- An invalid regex is reported in the status bar and renaming is disabled until fixed.

## Notes
- GameObject renames support Undo (Ctrl+Z).
- Asset renames use `AssetDatabase.RenameAsset` and are reflected in the project immediately.
- Number preservation strips a trailing number (e.g. `_01`) before rename, then re-appends it with your chosen format.

## License
See `LICENSE.md` in this package.
