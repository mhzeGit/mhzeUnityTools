---
name: make-video-content
description: Make marketing video and images from the game (social media clips, trailers, posters, thumbnails) with Content Studio - write a shot recipe, render it from an agent player as layers (clean picture, UI, sound, stems) at any size and aspect ratio, check it, export it fast or build an editable Blender video-editing project with reusable pieces such as the wishlist outro, and prepare per-platform files. Use when asked to film, capture, render or edit content, a clip, a short, a trailer shot, an outro/intro/end card, a poster or a thumbnail from the game. Not for test recordings (agent-playtest) and never for posting online.
---

# Make video content with Content Studio

Read `Docs/CONTENT_STUDIO.md` once (formats of recipes, pieces and edits; cameras; layers). Everything runs through
`python tools/studio/studio.py`. The project's own marketing notes (brand voice, ideas list) decide *what* to make; this skill
is *how*.

## Rules

1. **Recipes, not just files.** Every clip starts as a shot recipe in `<contentDir>/Shots/` (in Git). Never hand someone a
   video you cannot render again. Change the recipe and render again instead of patching a video.
2. **Never disturb the game or other agents.** Renders run in your own agent player (`--player` default `studio-<shot>`), never
   in the shared editor, never on a player you did not start. No scene, prefab or game-code edits to get a shot: hide things
   with `hide`/`hideUi`, move the camera with the recipe. A missing game hook (an NPC that should speak on command) is a small
   dev-only harness op in the project, reviewed like any code change.
3. **Builds follow the testing rules.** `python tools/agentplay/agentplay.py info` first; build only when it says STALE. If
   `render` says the build has no `studio` op, the build predates Content Studio: build once.
4. **Look at what you made.** After every render read `sheet.jpg` (the contact sheet) and judge framing, timing, what is in
   frame and what must not be (debug text, the crosshair, a floating arm in an outside shot). "No errors" is not done.
5. **Hand edits are the user's.** Never `edit build --force` over a project the user changed unless they say so. `edit render`
   renders their version.
6. **Never post, upload or send anything.** `publish` writes files; a person posts them.
7. Large files live in `outputDir` (outside Git). Only recipes, pieces (and their small source files) and edit plans go in a
   virtual commit.

## Steps

1. **Pick the idea** and the platforms. Vertical (`9:16`) for TikTok, Shorts, Reels; wide for X, YouTube, Steam; square or
   portrait for feeds. Short is better: most weird clips are 5-15 s.
2. **Find the moment.** Where in the game does it happen (a story stage, a scene), who is in it, which object names the camera
   and hide lists need (`agentplay.py send NAME '{"op":"find","name":"..."}'`, `observe`).
3. **Write the recipe**: `studio.py shot new NAME --idea "..."`, then fill `setup` (reach the moment; `attention {mode: off}` so
   the bot does not look around), `camera`, `length`, `actions` (timed with `at`, game time), `formats`, `hide`, `hideUi`.
4. **Draft render small and fast**: `studio.py render NAME --scale 0.5`. Read the sheet. Fix the camera (distance, fov, height,
   yaw, a path), timing and hides; render again. Keep each change small and look every time.
5. **Final render**: `studio.py render NAME` (full size; `--scale 2` for 4K; `--stems voice,music` when the edit needs separate
   sound levels).
6. **Quick file** (most social clips): `studio.py export NAME --platform tiktok` (outro from `defaults.outro`, `--outro none`
   for a raw loop, `--ui off` for a clean picture, `--audio none` for silent). Check duration and size in its answer.
7. **Editable project** when timing, text or several takes matter: `studio.py edit new EDIT --take NAME [--take NAME2]`, adjust
   the plan (in/out, pieces, captions, post text), `edit build EDIT`, `edit render EDIT`, look at the render's frames
   (`ffmpeg -ss T -i file -frames:v 1 frame.jpg`), then `publish EDIT`.
8. **New reusable part** (an end card, a sting, a lower third): `studio.py piece new NAME --role outro`, edit `piece.json`
   (brand words like `{gameName}` and `{cta}` come from `studio.json`), `studio.py piece render NAME --format vertical` and
   look at a frame of it in every format you use. The tools place pieces by role; never time them by hand in an edit plan.
9. **Report** to the user: what you made (paths of the preview or published folder), what it looks like (one honest line),
   what you could not do, and register a virtual commit with the recipe, pieces and edit plans you changed.

## When something is off

| Symptom | Fix |
| --- | --- |
| The take shows the wrong place | `setup` did not reach the moment: send the setup ops by hand to your player and `observe`. |
| The UI layer is empty | The game draws that UI some other way (world-space canvas, a camera-space canvas on a render texture). Use the player camera or hide it in the picture. |
| A first-person arm or the player body in an outside shot | Add its object name to `hide`. |
| Faces stare the wrong way in `face` mode | The character's root does not face forward: set `yaw` (degrees). |
| Sound and picture drift | Use fixed-step takes (the default); real-time takes trade sync for speed. |
| Stems do not add up exactly | Expected: each stem is its own pass. Mix levels with them; keep the master for sync. |
