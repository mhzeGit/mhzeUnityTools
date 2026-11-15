# Changelog

## 1.3.0 - 2026-11-15

- **Gameplay video.** `agentplay.py record NAME start|mark|snap|status|stop`: the new `AgentPlayRecorder` renders the player's
  camera, reads it back from the GPU without a stall and streams it into ffmpeg on a worker thread (constant frame rate, so a
  hitch keeps real time), with a JPEG keyframe every 2 s and an event log (every op, its result, marks, stage labels, on-screen
  text). `stop` stores a fast-start MP4 with those events as a captions track, WebVTT, chapters and a contact sheet in
  `Builds/AgentPlay/videos/<id>/`. `agentplay.py video list|info|sheet|frame|events|open|prune`. Without ffmpeg: JPEG frames and
  an MJPEG AVI. A crashed player's recording is kept when its folder is reused.
- **Shared ops for every harness** (`AgentPlayOps`): `observe` (objects near, crosshair target, screen text, project hints),
  `aim` (closed-loop mouse aim), `interact` (walk close, aim, check the crosshair target, press or hold), `approach`, `until`
  (condition language `AgentPlayConditions`, extensible), `record`, `screen`, `perf`. `AgentPlayInput` is the one virtual keyboard
  and mouse. A project with its own harness plugs them in with a few calls and gives them its hands (walker, interactor,
  interactables, hints).
- **Learning playtests** (`agentplay.py learn`, `playtest`, `observe`, `step`, `draft`, `kb`, `segments`): bots learn segments
  by exploring (hint-driven autopilot with a value table per story state, so a mistake is made once), replaying with
  human-like noise, repairing failed steps into route variants chosen by UCB1, and optimizing mastered routes. Everything goes
  into a shared, committed knowledge base (`AgentPlayKnowledge/`, locked and merged per segment) that later agents start from.
  `playtest` chains the learned routes through a scope, records one video and writes `report.html`/`report.json` with a verdict,
  quality scores and findings at their video times. Parallel worker players (`--workers`).
- **Dashboard and HTTP API**: `agentplay.py serve [--detach] [--open]` on localhost: playtests, videos (captions, event timeline,
  keyframes), knowledge and players (record, send); JSON API for agents and tools.
- New skill `agent-playtest`; `AGENT_PLAY.md` documents video, shared ops, learning and the API.

## 1.2.1 - 2026-11-12

- **Virtual commits no longer keep their patch files forever.** Each record stored a full binary patch of every file in it under
  `.git/agent-commits/patches/`, and nothing ever deleted it; one project's store grew to 14 GB. The patch is only read while a
  record is pending, so it is now deleted when the record is committed (finalize) or discarded (command or picker), and every
  `prepare` also sweeps the patches of already applied or discarded records (leftovers from older versions or a crash). Pending
  records are never touched. Records, commits and history are unchanged.

## 1.2.0 - 2026-11-12

- New opt-in module **Production planner skill** (`planner`): the `do-task` skill. Give an agent a card id or title from the
  production plan and it reads the card (definition of done, steps, why, estimate), moves it to its board's Now or Doing
  column (`pm begin`, planner 2026-11-12 or later), does and verifies the work under the project's rules, ticks its steps,
  reports, and ticks the card off with `pm done` once you confirm. Installed
  into the same skill folders as the other skills; enable it with `install.py --enable planner` or in Tools ▸ Agentic Tools.

## 1.1.0 - 2026-11-09

- Tools ▸ Agent Task Board ▸ Open Board: one click opens the task board in the browser, starting the board server hidden in
  the background if it is not running (a server already open from a terminal, `task-board.bat` or another project is reused).
  Also **Stop Board Server** (stops a server started from the menu) and **Show Task Board Folder**. Needs the Agent Task Board
  module installed and Python 3.9+; without the module it offers to open Tools ▸ Agentic Tools.

## 1.0.1 - 2026-11-08

- Virtual commits: `agent-commit.ps1` builds file paths with the platform's separator, so `prepare` also registers new files
  on Linux and macOS (cloud sessions); Windows is unchanged.

## 1.0.0 - 2026-11-08

- First release, gathered from BlahBlahFamilyV6 (rules, virtual commits, agent play, MCP tools, skills) and Graphmat (Agent Task Board).
- Tools ▸ Agentic Tools window and `Install~/install.py`: modules, rule blocks injected next to a project's own rules, conflict
  detection for hand edits, automatic update on editor load when the package version changes, uninstall.
- Generic `AgentPlayHarness` with project ops (`RegisterOp`), status extras and a save-folder hook; `agentplay.py` reads
  `agentplay.json` and loads project commands from `agentplay_project.py`.
