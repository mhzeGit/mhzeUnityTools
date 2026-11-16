# Content Studio: marketing video from the game, made by agents

Agents film the game for social media and trailers without touching scenes or the real game. You keep **recipes**, not just
videos: a shot recipe says where the game starts, which camera films it, what happens and for how long, so any agent can render
it again at another size, aspect ratio or frame rate. A render saves **layers** (clean picture, UI with alpha, game sound,
optional sound stems, events), so "with or without UI" and "with or without sound" are switches, never a new take. Edits are
plans that become a **Blender video-editing project** you can open and change by hand, with reusable **pieces** (outro,
sting, intro, lower third) placed by their role. Agent workflow: skill `make-video-content`.

Everything goes through `python tools/studio/studio.py` (Python 3.9+, ffmpeg via `pip install imageio-ffmpeg`, Blender 4.4+ for
pieces and edits). Renders use the agent player ([`AGENT_PLAY.md`](AGENT_PLAY.md)): the same build, your own player, the
testing rules for builds.

## Where things live

| What | Where | In Git |
| --- | --- | --- |
| Settings: output folder, brand (game name, call to action, font, colours), defaults, hide lists, stem rules | `tools/studio/studio.json` (the project's own file) | yes |
| Shot recipes | `<contentDir>/Shots/NAME.shot.json` (`contentDir` default `Marketing`) | yes |
| Pieces (outro, sting...) and their files (logo, sound) | `<contentDir>/Pieces/NAME/piece.json` | yes |
| Edit plans | `<contentDir>/Edits/NAME.edit.json` | yes |
| Takes, piece renders, Blender projects, exports, published files | `outputDir` (`takes/`, `pieces/`, `edits/`, `exports/`, `published/`) | no: large, rebuildable from the recipes |

`studio.py doctor` checks the setup; `studio.py init [--output DIR]` writes `studio.json` and the folders.

## The loop

```
python tools/studio/studio.py shot new stare --idea "The host stares into the lens"   # write the recipe
python tools/studio/studio.py render stare [--format vertical|wide|square|portrait|WxH] [--scale 2] [--stems voice,music]
python tools/studio/studio.py sheet stare          # contact sheet of the finished picture (look at it: the camera, the framing)
python tools/studio/studio.py export stare --platform tiktok [--ui off] [--audio none|master|voice+sfx] [--outro NAME|none]
python tools/studio/studio.py edit new stare-tiktok --take stare      # an edit plan with the default outro
python tools/studio/studio.py edit build stare-tiktok   # the Blender project; edit open opens it in Blender
python tools/studio/studio.py edit render stare-tiktok  # renders the project as it is, hand edits included
python tools/studio/studio.py publish stare-tiktok      # per-platform files, thumbnail, captions.srt, post text, checks
```

`export` is the fast path (ffmpeg only, seconds); the edit is the editable path. Nothing here posts anything online: publish
writes files and a README; a person uploads them.

## Shot recipes

```json
{
 "name": "stare", "idea": "...", "notes": "...",
 "setup": [{"op": "stage", "n": 3}, {"op": "attention", "mode": "off"}, {"wait": 1.5}],
 "camera": {"mode": "face", "target": "NPC_Host", "distance": 0.6, "fov": 32, "shake": {"amount": 0.3, "freq": 0.5}},
 "length": 6,
 "actions": [{"at": 0.8, "op": "...project op that makes her speak..."}, {"mark": "smile drops"}, {"at": 4, "camera": {"mode": "player"}}],
 "formats": ["vertical", "wide"], "fps": 30, "ui": true, "audio": true, "stems": [],
 "hide": ["PlayerArm"], "hideUi": ["Crosshair"]
}
```

- `setup` runs before the take (any agent player op: the project's story jumps, `teleport`, `attention {mode: off}`...).
- `actions` run during the take, in order: any op, plus `{"wait": s}` (game time), `{"at": s, ...op}` (wait until the take is
  `s` seconds old, then the op), `{"mark": "text"}` (a marker in the edit), `{"camera": {...}}` (a cut), `{"hide": [...]}`.
- `length` is the take's length in seconds (it stops then, or after the last action).
- `hide` turns renderers off by object name (part of the name, parents included), `hideUi` hides UI the same way; both also
  come from `studio.json` for every shot.

### Cameras

| `mode` | Fields | Shot |
| --- | --- | --- |
| `player` (default) | `fov` | The player's eye, with its camera stack (a first-person arm). |
| `pose` | `pos [x,y,z]`, `lookAt`, or `euler` | A fixed camera. |
| `face` | `target`, `bone` (default Head), `distance`, `height`, `yaw`, `eyes` | In front of a character's face, looking into it. Check the bone and facing with `probe` first. |
| `follow` | `target`, `offset [x,y,z]`, `local` (default true), `lookAt`, `smooth` | Follows an object. |
| `path` | `keys: [{t, pos, lookAt or euler, fov, ease}]` | Keyframed move; `ease`: `linear`, `smooth`, `in`, `out`. |

`lookAt` and key `pos` take `[x,y,z]`, an object name (its centre) or `{target, bone, offset, local}`. Any mode takes `fov`,
`shake {amount: degrees, freq: Hz}` (hand-held) and `listener: "camera"` (hear from the shot camera, not the player). A studio
camera copies the player camera's pipeline settings (post-processing, anti-aliasing, renderer) without its overlay stack.

## Takes and layers

A take renders on **fixed time steps** by default (`Time.captureDeltaTime` = 1/fps): every frame is complete at any size, even
4K or 9:16 on a 16:9 monitor, and sound is rendered offline (`AudioRenderer`) in step with the picture. `--realtime` records
in real time instead (sound from the listener). Folder `outputDir/takes/SHOT/SHOT_FORMAT_TIME/`:

| File | Layer |
| --- | --- |
| `clean.mp4` | The picture without overlay UI (H.264, CRF 14). |
| `ui/ui_00000.png...` | The overlay UI alone with straight alpha, laid out for this aspect ratio: overlay canvases move to a private UI camera during the take (put back after), which draws them over black and over white; the difference gives exact alpha whatever the render pipeline does with a camera's alpha. |
| `audio.wav` | The game's sound, 32-bit float. |
| `stems/voice.wav`... | With `--stems`: one extra pass per stem with every other audio source muted. |
| `events.jsonl` | On-screen text (captions), stage labels, marks and cuts, with times. Captions are cleaned when used: a line still typing out merges into its full text, and parts matching `studio.json` `captionDrop` regexes (prompts like "Click to continue") are dropped. |
| `take.json` | Settings, the recipe used, the commit, frames, errors. |
| `preview.mp4`, `sheet.jpg` | For people (UI and sound on) and for agents (up to 24 frames). |

**Stems** are separate passes of the same recipe: on fixed time steps the game replays closely, but anything random can
differ slightly between passes, so use stems for mixing levels, not for sample-exact editing. A source's stem is the project
hook `ContentStudio.StemOf`, else the first `stemRules` word (`studio.json`, e.g. `{"voice": ["dialog", "babble"]}`) found in
its mixer group, object path or clip name (defaults: voice, music, ambience), else `sfx`. The op
`{"op":"studio","action":"stems"}` (`agentplay.py send NAME ...`) lists every audio source with its stem.

`studio.py still SHOT [--at S]` renders one frame as PNG layers plus the combined image (posters, thumbnails, image posts).

## Pieces

A piece is a reusable part with a role. `piece new NAME --role outro|intro|sting|overlay|lowerthird` writes a template:

```json
{"name": "outro-wishlist", "role": "outro", "kind": "generated", "duration": 3.0,
 "place": {"at": "end", "fadeIn": 0.25},
 "background": "{background}",
 "elements": [
  {"type": "text", "text": "{gameName}", "size": 0.085, "y": 0.52, "color": "{foreground}", "bold": true, "anim": "pop"},
  {"type": "text", "text": "{cta}", "size": 0.05, "y": 0.42, "color": "{accent}", "anim": "fade", "start": 0.35},
  {"type": "image", "src": "logo.png", "width": 0.6, "x": 0.5, "y": 0.7, "anim": "fade"}],
 "audio": "sting.wav"}
```

- `{words}` are the brand values in `studio.json` (any string there, e.g. a `marketing` folder for shared art). Positions are
  fractions of the frame (`y` from the bottom), text `size` a fraction of the short side, image `width` a fraction of the
  frame width. `anim`: `fade`, `pop`, `slide`, `none`; `start` delays an element.
- `"formats": {"wide": {"elements": [{"width": 0.6, "y": 0.67}, ...]}}` adjusts the layout per format (elements merged by
  position; any other key replaced), so one piece lays out well in 9:16, 16:9, 1:1 and 4:5. Keep vertical text above the
  bottom fifth, where platform captions and buttons sit. Render each format you use and look at a frame of it.
- `kind`: `generated` (built from the elements), `blend` (your own `.blend`: the scene named like the format, e.g. `vertical`,
  else its first scene; `{words}` in its text strips are filled in) or `video` (`"files": {"vertical": "outro_9x16.mp4"}`).
- Placement by role, done by the tools: `intro` first, clips in order, `sting` then `outro` last (the outro fades in over the
  end of the last clip by `fadeIn`); `overlay` and `lowerthird` sit on top at `place.at` seconds with their transparent
  background (`"background": "none"`). An agent lists pieces in the edit; it never times them by hand.
- `piece render NAME --format F` renders it to a video (cached until the piece changes); `export` uses that for `--outro`.

## Edits: the Blender project

```json
{"name": "stare-tiktok", "format": "vertical", "fps": 30, "platforms": ["tiktok", "shorts", "reels"],
 "timeline": [{"take": "stare", "in": 0, "out": 6, "ui": true, "audio": "master", "fadeIn": 0, "fadeOut": 0},
              {"video": "path/to/other.mp4"}],
 "pieces": ["next-sunday-sting", "outro-wishlist"],
 "captions": {"kinds": ["text"], "show": false},
 "post": {"caption": "...", "hashtags": ["#indiegame"], "thumbnailAt": 1.2}}
```

A take is `SHOT` (its newest take), `SHOT:FORMAT`, a take id or a folder. `edit build` writes
`outputDir/edits/NAME/NAME.blend`, one scene with these channels (bottom to top):

| Channel | Strips |
| --- | --- |
| 1 | Clean picture of each take |
| 2 | UI layer (alpha over; muted when the plan says `"ui": false`, so you can switch it back on) |
| 3 | Other video files |
| 4 | Pieces in the sequence (scene strips: each piece is its own scene, editable) |
| 6 | Overlays and lower thirds |
| 8 | Game sound (muted when `audio` is `none` or names stems) |
| 9-12 | Stems (muted unless named, e.g. `"audio": "voice+sfx"`) |
| 14 | Captions from the events (muted unless `captions.show`) |

Markers mark the take's labels, marks and cuts. Colour management is Standard, so the video looks as rendered.
**Hand edits are safe:** `edit build` refuses to replace a project that changed after its last build (pass `--force`; the old
file is kept in `history/`), and `edit render` and `publish` render the project as it is. Change the plan and rebuild for
structural changes; open the project in Blender for timing, colour, extra text or music.

## Publish

`publish EDIT [--platform tiktok,shorts]` renders the project and writes `outputDir/published/EDIT_TIME/`: one MP4 per platform
(cropped to its format, cut at its length limit, with a warning), `thumbnail.jpg`, `captions.srt` (from the events),
`post.txt` (`post.caption` and hashtags) and a `README.md` with warnings (length outside the platform's usual range, a crop).
Platforms and formats are defaults in `studio.py`; `studio.json` `platforms` / `formats` override or add.

## Project hooks (C#, player side)

- `ContentStudio.StemOf = src => ...` returns a stem name for an `AudioSource` (or null for the rules).
- `ContentStudio.Staging += starting => ...` runs when a take or still begins (true) and ends (false): hide a crosshair, debug
  text, a watermark.
- The camera of `player` mode is `AgentPlayOps.ViewCamera` (else the main camera); captions come from
  `AgentPlayRecorder.TextProvider` and labels from `AgentPlayRecorder.LabelProvider`, which agent play already sets up.

The player side is the `studio` op of the agent harness (`ContentStudio`, compiled only into editor, Development and agent
builds), so nothing ships in the game and nothing runs unless an agent sends it. Ops: `start`, `stop`, `wait {s}` (game time),
`mark`, `camera`, `hide`, `still`, `stems`, `probe {target, bone}` (where a character and its bone are, which way it faces,
its size: frame a shot from numbers before rendering it), `status`.
