# Unity Fast Play Button

## What This Tool Does
Unity Fast Play Button adds a toolbar button that starts Play Mode with Domain Reload and Scene Reload disabled for faster iteration, then restores your original Enter Play Mode settings on exit.

## Why It Helps
- Cuts Play Mode startup time during rapid testing loops.
- Keeps the default Play button untouched for normal workflow.
- Automatically restores your original settings to avoid accidental project-wide changes.

## Features
- Fast Play toolbar button with play/stop state.
- Unity 6.3+ official `MainToolbarElement` support.
- Unity 6.0-6.2 toolbar injection fallback.
- Automatic settings restoration on stop, script reload, and editor quit.

## Installation
### Option A: Add from Git URL
1. Open Unity Package Manager.
2. Click + then Add package from git URL.
3. Paste this package repository URL.

### Option B: Local package folder
1. Copy `unity-fast-play-button` into your project's `Packages` folder.
2. Reopen Unity or wait for package refresh.

## How To Use
1. Locate the Fast Play control on the toolbar.
2. Click Fast to start Play Mode with both reloads disabled.
3. Click Stop to exit.
4. Original Enter Play Mode settings are restored automatically.

Behavior summary:

| Editor State | Fast Button State |
|---|---|
| Idle | Enabled (Fast) |
| Fast playing | Enabled (Stop) |
| Normal Play started elsewhere | Disabled |

## Example Workflow
1. Tweak a gameplay script.
2. Press Fast to test immediately.
3. Stop, adjust code, and repeat.
4. Use normal Play when you want full reload behavior.

## Notes
- Fast mode uses `DisableDomainReload` and `DisableSceneReload`.
- Restoration runs in multiple safety points to prevent stale editor settings.

## License
See `LICENSE.md` in this package.
