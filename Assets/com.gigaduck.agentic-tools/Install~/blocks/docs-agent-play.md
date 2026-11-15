# Agent play: test the game without the Unity editor

Agents test in their own standalone player, so several can run at once while the shared editor stays free. The policy (editor
Play Mode only under the claim, few big tests, ask once, one shared build) is in [`TESTING.md`]({{DOCS_TO_RULES}}/TESTING.md).
Everything goes through `python tools/agentplay/agentplay.py` (Windows, Python 3.9+, Unity installed by the Hub). The player side
comes from the Agentic Tools package: `AgentPlayBuild` (builds the player with the `AGENT_PLAY` define), `AgentPlayShaders`
(drops shader variants a play-test does not need) and the `AgentPlayHarness` command server compiled into that player.

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

`--headless` has no GPU: lightest, for logic only; it cannot take pictures.

## Play like a person (shared ops)

Every harness (the package's and a project's own) also answers these, from `AgentPlayOps`:

| Op | What it does |
| --- | --- |
| `observe {radius, max}` | What the player can see and use: status, view, the object under the crosshair, text on screen, objects near (with `usable`, `hold`, `prompt` from the project), and project parts such as `beats` (what the running story steps reference). `agentplay.py observe NAME` prints it with suggested actions. |
| `aim {target \| point, tolerance}` | Turns the view onto an object with real mouse movement, closed loop: it learns how far the view turns per mouse unit. |
| `interact {target, key, hold, reach, approach}` | Walks close if needed, aims, checks the object is under the crosshair (the project's interactor decides), presses the interact key (held for the object's hold time). Fails with what is under the crosshair instead. |
| `approach {target \| pos, dist}` | Walks to a point near an object (the project's walker, e.g. NavMesh; default: W and mouse steering). |
| `until {cond, timeout, soft}` | Waits for a condition: `{"field":"stage","gt":4}` (eq ne gt gte lt lte contains in changed exists), `near`, `exists`/`active`/`gone`, `crosshair`, `text`, `log`, `all`/`any`/`not`, plus project keys. `soft` answers `met:false` instead of failing. |
| `record {action: start\|stop\|mark\|snap\|status}` | The recorder (below). |
| `screen`, `perf {reset}` | Visible UI text; frame times since the last reset (fps, worst, 95th percentile). |
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

The player renders its camera into a small render texture, reads it back from the GPU without stalling and streams frames into
ffmpeg on a worker thread (H.264 at a constant frame rate: a hitch repeats the last frame, so the video keeps real time). It also
saves a JPEG keyframe every 2 s and logs events: every op the agent sends and its result, marks, the project's stage label and
on-screen text when they change. `stop` makes a fast-start MP4 with those events as a captions track (and `captions.vtt`),
chapters from the labels and an overview contact sheet, in `Builds/AgentPlay/videos/<id>/` (`video.mp4`, `captions.vtt`,
`keyframes/`, `sheet_*.png`, `meta.json`). Videos survive `start` (a crashed player's recording is kept too). ffmpeg comes from
`AGENTPLAY_FFMPEG`, `agentplay.json` `"ffmpeg"`, `PATH`, or `python -m pip install imageio-ffmpeg`; without it frames are JPEGs
and the video is an MJPEG AVI (VLC, Windows Media Player; the dashboard plays the keyframes). The camera picture has no
screen-space UI; its text is in the captions. Agents never watch videos: they read `video sheet`/`video events` and look at frames.

## Learning playtests and the knowledge base

Agents learn to play the game, keep what they learned in a shared knowledge base, and play whole chunks with it. Skill
`agent-playtest` has the workflow; the commands:

```
python tools/agentplay/agentplay.py kb brief [--scope S]               # START HERE: mastery, what needs an agent, lessons, next step
python tools/agentplay/agentplay.py learn me1 --scope S [--workers 2]  # learn segments until mastered (starts its own players)
python tools/agentplay/agentplay.py playtest me1 --scope S [--learn]   # play the scope with the learned routes, record, report
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
- Learning takes real time (minutes per segment). Scope it like any test; a whole-game run only when the user asked for one.

## Dashboard and HTTP API

`python tools/agentplay/agentplay.py serve --detach --open` (`--stop` ends it) serves a dashboard on `http://127.0.0.1:8765/`:
playtests (report, embedded video, segment timeline), videos (player with captions, clickable event timeline, keyframes),
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
