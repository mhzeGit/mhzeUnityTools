# Agent play: test the game without the Unity editor

Agents test in their own standalone player, so several can run at once while the shared editor stays free. The policy (editor
Play Mode only under the claim, few big tests, ask once, one shared build) is in [`TESTING.md`]({{DOCS_TO_RULES}}/TESTING.md).
Everything goes through `python tools/agentplay/agentplay.py` (Windows, Python 3.9+, Unity installed by the Hub). The player side
comes from the Agentic Tools package: `AgentPlayBuild` (builds the player with the `AGENT_PLAY` define), `AgentPlayShaders`
(drops shader variants a play-test does not need) and the `AgentPlayHarness` command server compiled into that player.

## Quick recipes (read this first)

One line of commands is the whole job: each command starts and stops its own player (the name you give), records one video
and ends with a `VIEW` link (the report and video on the dashboard; the phone link when it is shared). Run `info` first;
build only when it says STALE.

| The request | Run (`python tools/agentplay/agentplay.py ...`) |
| --- | --- |
| Play through chapter X (with video) | `kb brief --scope X`, then `playtest me1 --scope X` (`--learn` first learns what brief says is not mastered) |
| A perfect, clean play-through of X | `playtest me1 --scope X --until-perfect 6` (stops at the first perfect run) |
| Test every door (or any one kind of thing) in X | `deeptest me1 --scope X --only door` (a regex on name, path or prompt: `--only "drawer\|wardrobe"`) |
| Test everything in X, find bugs | `deeptest me1 --scope X` (`--states all`: also at later story states; `--exhaustive`: no smart skipping) |
| Walkthrough of X and test every door, recorded | the two recipes above, one after the other: two videos, two `VIEW` links |
| Record your own check | `record me1 start --label "..."`, your `send`s, `record me1 mark "..."`, `record me1 stop` |

- **Scope words** (`--scope`): segment ids or prefixes, numbers and ranges (`3`, `0-5`), comma separated; a project adds its
  own (chapters by id or title, `chapter/checkpoint`). `segments --scope X` lists what a scope covers; `kb brief --scope X`
  says what is mastered.
- **Report** what the command printed: the verdict line, the findings (severity, text, `@m:ss` in the video) and the `VIEW`
  link. That printed summary is the result: do not open videos, `report.json` or knowledge files to check it. Look at a
  moment only when a finding needs it: `video sheet <id> --from T-5 --to T+10` (a picture of keyframes).
- **Time**: a playtest about 20 to 60 s per section; `--until-perfect` one playtest per run; a deep test about 10 s per test
  point plus 5 to 20 s per action (a full section of 40 actions 10 to 15 min; `--only door` 1 to 3 min a section); learning
  minutes per section; a full build 2 to 3 min, a script-only patch seconds.
- If a build cannot resolve a package (another agent pinned an unpublished commit), `build --pin NAME=committed` builds that
  one package at the last commit's revision, in the build copy only (the record and `info` say so).

## Use

```
python tools/agentplay/agentplay.py info                  # ALWAYS first: age, commit, changes; CURRENT or STALE (exit 0 / 3 / 2 = none)
python tools/agentplay/agentplay.py build --by me         # only when info says STALE and your changes are not in the build; script-only changes patch in seconds
python tools/agentplay/agentplay.py start me1 --require-current [--headless] [--scene NAME] # unique name per agent, never another agent's
python tools/agentplay/agentplay.py send me1 '{"op":"status"}'
python tools/agentplay/agentplay.py shot me1 --as hall --pos=-6,2,-14 --look=-6,1,-8
python tools/agentplay/agentplay.py sheet me1               # pack the pictures into labelled contact sheets (needs Pillow)
python tools/agentplay/agentplay.py stop me1                # always stop your own player; never `stop --all` (it stops other agents')
python tools/agentplay/agentplay.py editor claim --by me1 --why "what you test"  # Unity not busy + claim free: test in editor Fast Play instead (TESTING.md rule 1); then: editor release --by me1
```

One build serves every agent: build only when `info` says it is stale, then everyone starts their **own instance** of it
(own name, own folder, own save). Never send to, stop or kill a player you did not start.

Shared `send` ops (the package's harness): `status`, `scene {name}`, `teleport {pos, yaw}` (the object tagged `Player`),
`key {key, s}`, `hold`/`release {key}`, `click {s}`, `look {dx, dy, frames}` (keys and mouse need the Input System package; `key`
uses Input System key names: `W`, `E`, `Space`), `shot {name, pos, look_at|euler, fov, w, h, hide, settle}`/`shots {list}`,
`find {name}`, `wait {s}`, `scale {value}`, `errors`, `summary`, `quit`. A project adds its own ops (see *Project extensions*).
Results live in `Builds/AgentPlay/runs/<name>/` (`shots/`, `sheets/`, `summary.txt`, `console.txt`, `player.log`, its own `save/`
folder for a game that supports a save override).

Players run in a real window (it does not take the keyboard focus), so the game draws and presents its frames exactly as
for a player: overlay UI, zooms, effects, frame pacing. `--batchmode` runs without a window (overlay UI is never drawn, so
pictures and videos miss it; only for quick logic checks); `--headless` has no GPU: lightest, for logic only; it cannot take
pictures. Recordings capture the finished screen (what the player sees, UI included); walking ops press the movement keys,
so the game's speed, collisions and movement locks apply. A walk result has `path` (the corners walked) and `trail`
(where the player was every half second), and stops with a reason when the player is moved away (a death, a teleport);
`walk {direct: true}` keeps to the straight line instead of the shortest path (a lane beside a hazard). Keys are pressed
relative to the player's body (what first-person controllers move along; `AgentPlayOps.MoveForward` changes it), and a
project's `AgentPlayOps.Dying` hook stops a walk when the player dies. Count deaths from the game's own death event in the
harness: a save's counter can be restored with the checkpoint on a death, so a status after the respawn would miss it.

## Play like a person (shared ops)

Every harness (the package's and a project's own) also answers these, from `AgentPlayOps`:

| Op | What it does |
| --- | --- |
| `observe {radius, max}` | What the player can see and use: status, view, the object under the crosshair, text on screen, objects near (with `usable`, `hold`, `prompt` from the project), and project parts such as `beats` (what the running story steps reference). `agentplay.py observe NAME` prints it with suggested actions. |
| `aim {target \| point, tolerance}` | Turns the view onto an object with real mouse movement like a hand: the mouse is calibrated once per session (a small nudge per axis), then the view is steered a little every frame (at most 220 degrees a second, gentler after an overshoot); a scripted look by the game is waited out, never fought. |
| `interact {target, key, hold, reach, approach}` | Walks close if needed, aims (stopping the moment the object is under the crosshair; the project's interactor decides), presses the interact key (held for the object's hold time). Fails with what is under the crosshair instead. A target can be a name or the end of a path (`Doors/Door01 (9) Hall1_Door01/Door`). |
| `approach {target \| pos, dist}` | Walks to a point near an object (the project's walker, e.g. NavMesh corners; default: a straight line), with the movement keys. |
| `until {cond, timeout, soft}` | Waits for a condition: `{"field":"stage","gt":4}` (eq ne gt gte lt lte contains in changed exists), `near`, `exists`/`active`/`gone`, `crosshair`, `text`, `log`, `all`/`any`/`not`, plus project keys. `soft` answers `met:false` instead of failing. |
| `record {action: start\|stop\|mark\|snap\|status}` | The recorder (below). |
| `studio {action: start\|stop\|wait\|mark\|camera\|hide\|still\|stems\|status}` | Content Studio takes and stills (marketing footage in layers, any size): see `CONTENT_STUDIO.md` and `tools/studio/studio.py`. |
| `screen`, `perf {reset}` | Visible UI text; frame times since the last reset (fps, worst, 95th percentile). |
| `point {target \| world:[x,y,z] \| x, y, click: n, interval: s}` | The mouse pointer, for menus and pointer UIs: glides it over an object as the camera sees it (a UI element where it is drawn; x, y are 0..1 of the screen from the bottom left) so hover fires, then clicks n times. Without a player it uses the camera that draws the menu. |
| `attention {mode: auto\|movement\|off}`, `{focus: name, for: s}`, `{clear: true}` | Where the player looks (below): its mode, a pinned focus, and the hotspots it sees with their scores. |

**Where agents look.** Every agent player looks like a person plays (`AgentPlayAttention`, on by default): along the way it
walks, and when something happens at the most important hotspot the project names (who speaks, a hazard that is live, what
a story step that just began points at), then back at the way. It turns the view with real mouse movement, holds a hotspot
long enough to see it, glances back at the way on long walks, ignores what is behind a wall or behind the walk unless it is
critical, and never fights the game: while the game drives the view (a scripted look, a look lock, look input off), while an
op aims or takes a picture itself, or when the view stops answering, it waits. `status` has `looking`, `observe` has
`attention`, and recordings caption each change ("looks at ..."). Keep it on when you test; point it with
`attention {focus: "Kettle", for: 5}` at what your check is about; `off` only for fixed-camera pictures. A project gives the
hotspots with `AgentPlayAttention.Hotspots` and says when the game has the view with `AgentPlayAttention.GameHasView`.

## Record video

```
python tools/agentplay/agentplay.py record me1 start --label "what this shows"   # --fps 15 --res 960x540 --max-minutes 60
python tools/agentplay/agentplay.py record me1 mark "the door should open now"     # a caption at this moment
python tools/agentplay/agentplay.py record me1 stop                                # encodes and stores it; prints the paths
python tools/agentplay/agentplay.py video sheet last --from 30 --to 60             # what an agent looks at instead of the video
python tools/agentplay/agentplay.py video list | info ID | frame ID --at 42.5 [--exact] | events ID | open ID | recaption ID | prune --keep 30
```

The player captures the finished screen at the end of each frame (every camera, overlay UI, zooms; `record start --source
camera` re-renders one camera instead), reads it back from the GPU without stalling and streams frames into ffmpeg on a worker
thread (H.264 at a constant frame rate: a hitch repeats the last frame, so the video keeps real time). It also
saves a JPEG keyframe every 2 s and logs events: every op the agent sends and its result, marks, the project's stage label and
on-screen text when they change. `stop` makes a clean fast-start MP4 (the picture only: nothing is drawn over it), the events
as `captions.vtt` beside it, chapters from the labels and an overview contact sheet, in `Builds/AgentPlay/videos/<id>/`
(`video.mp4`, `captions.vtt`,
`keyframes/`, `sheet_*.png`, `meta.json`). Videos survive `start` (a crashed player's recording is kept too). ffmpeg comes from
`AGENTPLAY_FFMPEG`, `agentplay.json` `"ffmpeg"`, `PATH`, or `python -m pip install imageio-ffmpeg`; without it frames are JPEGs
and the video is an MJPEG AVI (VLC, Windows Media Player; the dashboard plays the keyframes). The picture is the finished screen
(UI included); the dashboard, reports and phone show the section, the action, the text on screen and where the player looks
in a live panel beside the video, never over it. Agents never watch videos: they read `video sheet`/`video events` and look at
frames.

## Learning playtests and the knowledge base

Agents learn to play the game, keep what they learned in a shared knowledge base, and play whole chunks with it. Skill
`agent-playtest` has the workflow; the commands:

```
python tools/agentplay/agentplay.py kb brief [--scope S]               # START HERE: mastery, what needs an agent, lessons, next step
python tools/agentplay/agentplay.py learn me1 --scope S [--workers 2]  # learn segments until mastered (starts its own players)
python tools/agentplay/agentplay.py playtest me1 --scope S [--learn]   # play the scope with the learned routes, record, report
python tools/agentplay/agentplay.py playtest me1 --scope S --until-perfect 8   # chained runs, learning between them, until one is perfect
python tools/agentplay/agentplay.py deeptest me1 --scope S [--states all]        # try everything in each section: what breaks
python tools/agentplay/agentplay.py observe me1                        # what the bot sees, what the story waits for, suggestions
python tools/agentplay/agentplay.py step me1 '{"op":"interact","target":"Kettle"}' --why "..." --segment S   # explore by hand
python tools/agentplay/agentplay.py learn me1 --segment S --from-draft  # your draft -> a verified route
python tools/agentplay/agentplay.py kb status|show S|lesson S|global "text" --kind tip|pitfall|bug|fix|route S --file steps.json
```

- **Segments** (the project defines them): a stretch of play with setup commands that put the game at its start, a goal
  condition, a time budget and optionally `at` (the game is at its start already, so a playtest can chain segments).
- **Routes**: replayable step lists. A learned step has `when` (wait until the story is in the state the step was learned in),
  and may have `expect`, `optional`, `why`. **Explore**: the autopilot reads `observe` (what the running story steps point at,
  usable objects, text) and tries actions scored by a value table per story state; an action that moves the story on is kept, one
  that does nothing or kills the player is remembered as bad there, so a mistake is made once. **Replay** with human-like noise
  (reaction delays, look jitter), choosing among route variants by UCB1. **Repair** a failed step into variants (detours, the door
  in the way, approach first, wait until usable, longer timeouts, drop the step, wait out a hazard). **Mastered** = the best route
  won `--streak` (3) times in a row; then faster variants compete. A mastered route that fails later marks the segment **stale**
  (the game changed: a regression or an outdated route). What the autopilot cannot solve becomes **needs-agent** with a diagnosis.
- **Knowledge base**: `agentplay.json` `"knowledgeDir"` (default `AgentPlayKnowledge/`), one JSON per segment plus `global.json`
  and a README; commit it, so every agent and machine starts from it. Writes are locked and merged per segment, so parallel
  agents and `--workers` share it safely. Episodes (every run, step by step) are in `Builds/AgentPlay/episodes/`.
- **Playtest report** `Builds/AgentPlay/playtests/<id>/report.html` (+ `report.json`): verdict; completion, stability, performance,
  pacing and friction; per segment outcome, chained or jumped, time, deaths, new errors, worst frame, blocked walks, how much was
  waiting; findings with their time in the video, which plays embedded.
- **Chained runs** (`playtest --until-perfect N`): a proven route (3 or more wins, 70% or more) that fails once is played
  again before any repair (one failure can be chance); a segment that wins alone but fails chained is retried from where it
  starts alone (hand-off repair), and when it dies chained, the segment before gets a variant that ends there earlier (a
  hazard that already falls when its segment starts can only be escaped earlier). A click or key whose expected effect does
  not happen is pressed once more (the game was not listening yet). A death fails the attempt even when the story goes on.
- **Setup commands** may carry `"unless": <condition>` and are skipped when the game is already there (load the scene unless
  it is loaded).
- Learning takes real time (minutes per segment). Scope it like any test; a whole-game run only when the user asked for one.

## Deep tests: try everything a section offers

A playtest finishes the game; a deep test tries what a player could do in each section, to find what breaks:

```
python tools/agentplay/agentplay.py deeptest me1 --scope S                 # everything: interactables, item combinations, moves, walking probes
python tools/agentplay/agentplay.py deeptest me1 --scope S --only door     # every door (name, path or prompt matches), nothing else
python tools/agentplay/agentplay.py deeptest me1 --scope S --states all    # also at each story state the learned route goes through
```

In each section it goes to the start (the segment's setup), lets a scene that plays by itself finish (the first quiet moment),
and first watches for about 10 s with no input: the **control** (does the story move on by itself, does standing still kill).
Then it tries, from that point: every usable interactable within `--radius` (12 m; 30 m with `--only`): walk there, aim,
press or hold the interact key; with an item in hand, that item on the other interactables, then Drop and Attack; every move
of the project's input map (`Assets/**/*.inputactions`: a tap each; sprint and crouch held while walking; pause opened and
closed); and walking probes in 8 directions. After anything that changes the story, the item in hand or the player's life,
it resets to that point. A story change that the learned routes never go through gets a softlock check (the story must keep
moving and the section must still be finishable).

| Outcome | Meaning |
| --- | --- |
| `error` | New errors in the player log (BUG) |
| `fell` | The player dropped 3 m or more (BUG) |
| `paused` | The game was left paused afterwards (BUG) |
| `softlock` | The story stopped moving and the section could not be finished after it (a warning: check the video) |
| `death` | The player died, and doing nothing there does not kill |
| `failed` | It could not be done (e.g. never under the crosshair: a usable object out of reach) (WARN) |
| `story`, `held`, `changed`, `moved` | It did something: moved the story, picked up or dropped, changed a prompt, an object, the text on screen, the player |
| `nothing`, `blocked` | No effect; a walking probe stopped by geometry |
| `anyway` | What followed happens without the action too (the control showed it): not credited to it |
| `busy` | The game had the controls (a scene started on the way); tried once more from a clean start |
| `skipped` | Not worth trying (the reason is given): see below |

It is smart about what it tries (`--exhaustive` turns that off; `--only` turns it off for the matches): moves for an item
(Drop, Use, Next...) only with an item in hand; a move that did nothing twice in a context, or the same harmless thing in three
sections, is not repeated; of look-alike objects (a dresser's drawers: same name without numbers, same prompt verb) two are
tried and the rest only if those differ; kinds that behaved the same three times are not retried; objects the running story
points at go first; areas already walked around are not probed again. With `--only`, each matching object is tried once per
prompt in a run (a door locked in one section and open in a later one is tried twice). The memory behind it is
`<knowledgeDir>/coverage/_effects.json`; every action's outcome per section and build is in `<knowledgeDir>/coverage/<segment>.json`
(commit both): actions tested on the same build are reported as known, not repeated (`--retest` redoes them). A segment with
`"deep": false` is skipped (nothing to try there, e.g. a menu without a player). Moves come from
the input map; `agentplay.json` `"deepMoves"` adds others (`[{"action": "Lean", "binding": "<Keyboard>/q", "cmd": {"op": "key",
"key": "Q"}}]`). The report (`kind: deep`) is in the playtests list: the verdict counts bugs, deaths and warnings; every action
is listed with its outcome and video time (the phone filters Problems, Did something, Nothing, Skipped).

## Dashboard and HTTP API

`python tools/agentplay/agentplay.py serve --detach --open` (`--stop` ends it) serves a dashboard on `http://127.0.0.1:8765/`:
playtests and deep tests (report, embedded video, segment timeline, every deep-test action), videos (a clean player with a
live panel beside it: section, action, text on screen, where the player looks; clickable event timeline, keyframes),
knowledge (mastery, routes, lessons, values) and players (record, mark, stop, send). The same data is an API for agents and tools:
`GET /api/videos`, `/api/videos/<id>` (with `videoUrl`, `captionsUrl`, `keyframes[].url`), `/api/videos/<id>/frame?t=S`,
`/api/playtests[/<id>]`, `/api/knowledge[/<segment>]`, `/api/players`, `POST /api/players/<name>/send` (a command) and
`/api/players/<name>/record` (`{"action":"start|mark|stop"}`; stop returns the video). Localhost only, unless shared:
`serve --detach --tunnel` adds a Cloudflare quick tunnel (no account; cloudflared is downloaded once into `Builds/AgentPlay/_tools`)
and prints a secret share link that works from anywhere, a phone too, while the computer is on: remote viewers need its key
(then a cookie) and can only look (`--allow-control` lets them act). `serve --status` prints the link again; `serve --stop`
closes it. Phones get a phone app at the same address (also `/m`; `?desktop=1` for the desktop page): tabs at the bottom,
the playtest video pinned on top with a tappable segment strip and cards that jump to each segment. After a tools update,
`serve --restart` reloads the server and keeps the tunnel, so the link stays valid.

## Build records

Every build writes its own record, so an agent can judge in one read whether it needs a new build.

| File | What it holds |
| --- | --- |
| `Builds/AgentPlay/latest.json` | The newest build: name, time (`builtAt`, `builtAtEpoch`), duration, who asked (`--by`), attempts, Git commit, branch, number of uncommitted paths under `Assets`/`Packages`/`ProjectSettings`, `git diff --shortstat HEAD`, and `sinceBuild`: files added/modified/deleted against the previous build, by kind (code, scene, prefab, shader, assets, settings, packages), with the first 60 paths. Small; read it without running anything. |
| `Builds/AgentPlay/history.jsonl` | One short line per build, oldest first. |
| `<build folder>/build-info.json`, `source-manifest.json.gz` | The same record, and the file map the next build and `info` diff against. Pruned with the build folder. |

`agentplay.py info` reads that record and scans the working tree once (the same scan `build` does), so it also tells you whether the build is
still current: `CURRENT` (exit 0), or `STALE` (exit 3) with the files that changed since it was built (`--files` lists all). The change list
works on uncommitted work, because it compares file maps, not Git commits. The Git fields only say which commit the work was based on.

## How the build works

`build` mirrors `Assets`, `Packages` and `ProjectSettings` to `..\<project>_AgentBuild` (a private copy with its own Library, opened only
in batch mode), so the editor may stay open. Only one build runs at a time; others wait. It retries compile errors from another agent's edit
in progress and licensing timeouts. The first build seeds the copy's Library from the project's (once) and compiles every shader; later
builds only re-import what changed.

**Patch builds (code-only changes, no Unity).** When every file changed since the newest build is a script under `Assets/`
(`.cs`, `.cs.meta`), `build` does not start Unity. It recompiles `Assembly-CSharp` with Unity's bundled Roslyn, using the
response file the last full build used (saved in `<build>/_compile/`, taken from the build copy's
`Library/Bee/artifacts/<hash>P.dag`, the graph whose defines include `AGENT_PLAY`), the working tree's script list, and the
build's own `Managed/*.dll` as references. It then hardlinks the build into a new folder and replaces only
`Managed/Assembly-CSharp.dll` (and `RuntimeInitializeOnLoads.json` when the `[RuntimeInitializeOnLoadMethod]` list changed).
The base build is never written to, players running from it keep running, and `info`/`start` treat the result like any build
(its record has `patchedFrom`). `tools/agentplay/DllCheck.cs` (compiled once into `Builds/AgentPlay/_tools/`) refuses the patch,
so a full build runs instead, when a type's serialized fields changed (the built scenes and prefabs hold the old layout) or a
type that may be a Unity object was added or removed (the build has no MonoScript for it). Scenes, prefabs, assets, packages,
settings and `.asmdef` changes always need a full build. Compile errors fail the patch with the error list and do not fall back.
`build --full` skips patching.

The build compares the source and copied fingerprints before compiling. The fingerprint includes this tool, the build options and
locked package revisions. `start` reports `sourceMatches`. Use `--require-current` for verification and omit it when deliberately
inspecting an existing build.

It is a normal (non-Development) build with the `AGENT_PLAY` define, which compiles the harness in. A Development build opens a
player-connection port for the profiler: Windows Firewall then asks about every new build folder, and in Connect mode the player
attaches to the open editor. So the player opens no network port, and `player.log` stack traces have no line numbers. The player
uses DX12 only, and `AgentPlayShaders` drops some shader variants (non-cascade shadows, LOD crossfade, soft shadows off, cookies
off): LODs pop instead of fading. Real builds never set `AGENTPLAY_BUILD`, so they keep every variant.

## Project extensions

- **Settings**: `tools/agentplay/agentplay.json` (optional, the project's own file): `defaultScene` (scene `start` loads; empty =
  the first build scene), `buildMethod` (the `-executeMethod` that builds the player), `unityEditorRoot` (where the Hub installs
  editors). The `UNITY_EXE` environment variable overrides the editor path.
- **Commands**: `tools/agentplay/agentplay_project.py` (optional, the project's own file) defines `register(sub, ap)`: it adds
  subcommands to the argument parser `sub` and uses the tool's functions through `ap` (`ap.send(name, command, timeout)`,
  `ap.finish(answer)`, `ap.fail(text)`, `ap.ROOT`), for example scoped story sweeps.
- **Player ops**: a runtime script registers more harness ops with
  `Gigaduck.AgenticTools.AgentPlay.AgentPlayHarness.RegisterOp("name", (command, result, fail) => ...)` (an `IEnumerator`), and can
  add status fields with `AgentPlayHarness.StatusExtras += r => ...`. A project that ships its own complete harness instead marks
  its assembly with `[assembly: Gigaduck.AgenticTools.AgentPlay.CustomAgentPlayHarness]`, and the package harness stays off;
  it calls `AgentPlayOps.Init(outFolder)`, sends unknown ops to `AgentPlayOps.Run` (`Handles(op)`), reports each command to
  `AgentPlayOps.CommandStarted`/`CommandFinished` (recording captions), adds `AgentPlayOps.AddStatus(r)` to its status, and uses
  `AgentPlayInput` for keys and mouse (one virtual keyboard and mouse for everything).
- **The game's hands** (`AgentPlayOps` hooks): `Status`, `ViewCamera`, `Player`, `Walker` (e.g. NavMesh walking),
  `CrosshairTarget` (what an interact press would use), `HoldSeconds`, `Interactables`, `DescribeObject` (usable, prompt...),
  `ObserveExtras` (e.g. what the running story steps wait for: `AgentPlayOps.Fields(component)` turns serialized fields into
  hints), `AgentPlayRecorder.LabelProvider` (the stage label in captions), and `AgentPlayConditions.Register("key", ...)` for
  project conditions.
- **Segments**: `agentplay_project.py` calls `ap.learn.set_segment_provider(segments, scope, validate)` in `register()`
  (or the project writes `tools/agentplay/segments.json`), so `learn` and `playtest` know the game's segments.

## Limits

- A player only contains what was built: `info` must show your files inside the build (not in its STALE list), or you are testing old code.
- Compile new scripts offline before they enter the shared tree: one broken file stops the shared editor for everybody.
- Editor-only test tools (validators that need Play Mode, authoring in Play Mode) need the editor: run them under the rule 1 claim
  or list them as **Not run**.
