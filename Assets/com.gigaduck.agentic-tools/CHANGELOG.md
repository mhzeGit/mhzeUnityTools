# Changelog

## 1.5.1 - 2026-11-16

- The harness `wait` waits in real time: game time stops while a game is paused, so a wait with the pause menu open never
  ended and every command after it hung.
- Deep tests: an action that leaves the game paused is a finding (`paused`), and the menu is closed before going on; the
  pause test waits on the tool's side.

## 1.5.0 - 2026-11-16

- **Deep tests** (`agentplay.py deeptest NAME --scope S`): the second kind of play-testing. A playtest finishes the game; a
  deep test tries everything in each section with real inputs: every interactable in reach, an item in hand on the other
  interactables, every move of the project's input map (`Assets/**/*.inputactions`; held while walking for sprint and
  crouch; pause opened and closed), walking probes in 8 directions; `--states all` also at each story state of the learned
  route. Each action is classified (error, death, fell, story, held, changed, moved, blocked, failed, nothing); a story change
  gets a softlock check (the learned route must still finish the section). It resets to the section start after anything
  that changes the story, the item in hand or the player's life. Coverage is shared in `<knowledgeDir>/coverage/` (skipped on
  the same build, retested on a new one). Report with one video (a caption per action) in the Playtests list; the phone shows
  every action, filtered by problems, each one tap away in the video. `agentplay.json` `"deepMoves"` adds moves.

## 1.4.6 - 2026-11-16

- **Walking keys follow the body.** `KeyWalk` picks W/A/S/D relative to the player's body (first-person controllers move
  relative to it), not the camera: while the game turns the camera itself (a reveal, a scripted look) camera-relative keys
  walked the wrong way. `AgentPlayOps.MoveForward` overrides it for a game whose keys follow the camera.
- `AgentPlayOps.Dying` (project hook): a walk stops with "the player died" instead of walking on from the respawn; walk
  results carry `frozenSpan` (when, within the walk, the game held the player).
- **Learning "earlier".** Episodes record the story states after their last step (`states`); a playtest row says how its
  segment ended (`lastRunning`). When a chained segment dies, the segment before gets a variant that walks to where the
  dying segment starts when played alone, during that last story state: tried on the next run even over a winning route
  (`prior` 1.0). A hazard that is already falling when its segment starts can only be escaped earlier.
- Episodes keep the start and the end of each walk's trail.

## 1.4.5 - 2026-11-16

- An attempt in which the player died fails even when the story went on after the respawn (it used to count as a win, so
  a route that got the player killed could be mastered and nothing was learned from the death).

## 1.4.4 - 2026-11-16

- Walks measure progress as getting closer to the next corner, so jittering against a wall or a door frame reports
  "blocked" after 2 s instead of using up the timeout.
- Episodes keep the end of each walk's trail and its corners (they were cut off with the rest of long results).

## 1.4.3 - 2026-11-16

- A walk stops with a reason when the player is moved away (a death, a teleport) instead of walking on from the respawn.
- `walk {direct: true}`: the straight line instead of the shortest path (a project walker honours it): a player keeps to a
  lane beside a hazard; the shortest path can cut through it.
- Learning: a death on a walk makes a "walk the straight line" repair, tried first; when a segment's live routes are full,
  routes that never won after two runs retire to make room for new repairs (they used to block learning).

## 1.4.2 - 2026-11-16

- Walk results carry `path` (the corners walked) and `trail` (where the player was every half second), so a death or a
  block can be placed exactly.

## 1.4.1 - 2026-11-16

- **Agent players run in a real window by default** (opened without taking the keyboard focus): batch mode presents no
  screen, so overlay UI was never drawn and pictures and videos could not show it. `start --batchmode` keeps the old
  windowless mode for quick logic checks.
- Agent players keep the game's own frame pacing (its vSync and target frame rate) instead of a forced 30 fps;
  `start --fps N` caps it when an agent needs to (many players at once).
- `KeyWalk` keeps to the line between path corners (pure pursuit), so the 8 key directions never drift it off a narrow
  lane while the view looks elsewhere (a hammer beside the side lane).

## 1.4.0 - 2026-11-15

- **Recordings show what the player sees.** The recorder captures the finished screen at the end of each frame: every
  camera, the overlay UI (dialog boxes, prompts, menus, loading screens), zooms and effects. It used to re-render the player
  camera alone, so overlay UI never appeared and a view the game changed could differ. `record {source: "camera"}` keeps
  the old way for a picture of one camera; recording.json says which (`source`).
- **Agents walk with real keys.** `AgentPlayOps.KeyWalk`: W, A, S and D for the way to the next corner, relative to where
  the camera looks, so the game's own movement applies (speed, collisions, movement locks). While the game holds the player
  still (a hook says so) the walk waits like a person instead of moving them anyway; `frozen` in the result says how long.
  `run: true` holds Left Shift. The default walker uses it; the old mouse-steering walker is `MouseSteerWalk`.
- Attention looks along the way the walker is about to go (`AgentPlayAttention.Heading`), so the player faces where it
  walks before it moves.
- Learning: a segment that wins alone but fails or dies chained is first retried from where it starts when played alone
  (hand-off repair, tried before the other repairs); segments remember that start (`startPos`). No repairs are made from a
  failure right after a story jump in a run (the game may still be in a death screen).

## 1.3.8 - 2026-11-15

- Playtests: a segment the story already went through by itself (the segment before ended past its start, e.g. a checkpoint
  that resolves at once) counts as played and chained, instead of jumping the story back to replay it out of order.

## 1.3.7 - 2026-11-15

- **Perfect play-throughs**: `playtest --until-perfect N` plays the scope chained from its first segment, again and again,
  every run recorded; between runs it learns from what failed in the chain (win-stay/UCB route choice, lessons, repair
  variants, exploring on from where the run left the game) and stops at the first perfect run: every segment passed on a
  learned route, chained, no deaths, no new errors. Reports get `perfect`, `imperfect` (the reasons) and `series` (run n of
  N); the verdict reads `PERFECT: ...`; the phone app marks perfect runs and numbers the runs.
- **`point` op** for menus and pointer UIs: glides the virtual mouse over an object as the camera sees it (or a UI element,
  or a screen point), then clicks n times, aiming again before each click. Without a player, ops use the main camera, else
  the camera that draws last, and object lookups prefer what is nearest that camera.
- `explore(..., setup=False)` explores from wherever the game is (used by chained runs).

## 1.3.6 - 2026-11-15

- Attention captions read naturally: "looks where it walks", "the game has the view", "looks at Niko (speaking)".

## 1.3.5 - 2026-11-15

- **Agents look like players** (`AgentPlayAttention`, on by default in every agent player): along the way they walk, and when
  something happens at the most important hotspot (the project's `AgentPlayAttention.Hotspots`: who speaks, a hazard that is
  live, what a story step that just began points at), then back at the way. Real mouse movement, smooth turns, a minimum dwell,
  glances back at the way on long walks, nothing behind walls or behind the walk unless critical; it waits while the game drives
  the view (`AgentPlayAttention.GameHasView`), while an op aims or takes a picture, or when the view stops answering. New op
  `attention` (mode auto|movement|off, focus an object for a while, clear); `status.looking`, `observe.attention`, and a "look"
  caption in recordings ("looks at ..."). `-agent-attention off|movement|auto` on the player command line. Rule in TESTING.md.

## 1.3.4 - 2026-11-15

- **A phone app for the dashboard** (`agentplay_mobile.html`): phones opening the dashboard (or `/m`) get a phone-first app instead
  of the desktop page (`?desktop=1` still asks for it): bottom tab bar, list-to-detail screens, normal page scrolling, large tap
  targets, nothing wider than the screen. A playtest pins its video at the top with the segment "tape" under it (tap anywhere to
  jump) and a line that says what the bot does or what the screen shows at that moment (captions stay off on the small video);
  every segment is a card with a thumbnail of its start that plays from there and lights up while it plays; findings jump to
  their time. Learning shows mastery per chapter, each segment's route steps, the needs-agent diagnosis, lessons and runs.
  Checked as an iPhone 13 (Playwright): every screen, seeking from cards and the tape, no sideways overflow, no console errors.
- `serve --restart` restarts the background server (after a tools update) and keeps the tunnel, so a shared link stays valid.
  `/api/status` tells the page whether the viewer is local or remote.

## 1.3.3 - 2026-11-15

- **See it from a phone.** `agentplay.py serve --detach --tunnel` also opens a Cloudflare quick tunnel (no account; cloudflared
  is downloaded once into `Builds/AgentPlay/_tools`) and prints a secret share link. Requests from elsewhere need its key
  (once, then an HttpOnly cookie) and are view only (`--allow-control` lets them record and send commands); the computer
  itself needs no key. `serve --status` prints the link again; `serve --stop` closes the server and the tunnel. `--share`
  alone keys remote requests for your own tunnel or proxy.
- Dashboard and report fit a phone: the header wraps with scrollable tabs, segment blocks shrink to slivers.

## 1.3.2 - 2026-11-15

- Playtest pacing is judged only on segments the bot played (an unlearned segment's wait is not a pacing problem); the
  "mostly waiting" finding is no longer raised for them. `agentplay.py playtest-report ID` recomputes scores and findings.
- Captions leave out the learner's own zero-wait checks (`until ... timeout=0`); `agentplay.py video recaption ID` rebuilds an
  existing video's captions and swaps its subtitle track.

## 1.3.1 - 2026-11-15

Fixes from the first real learning runs (a whole game, 39 segments):
- Replays treat a route as story-state -> action pairs: a step waits for its learned state, but when the story is already in a
  later step's state it jumps there instead of waiting for the past. A step whose op reports failure while the story moves on
  (walking into the NPC it had to reach) counts as done.
- Win-stay, lose-shift route choice: a route on a winning streak is replayed until mastered; UCB only after a loss or while
  faster variants are tried. Every failure kind also offers a "drop the step" variant (ablation of steps that only looked
  useful); repairs stop at 6 live routes and the oldest retired routes are forgotten beyond 16.
- The explorer reads the screen ("Click LMB to continue" -> click, "Press E" -> key) and no longer counts interaction prompt
  text as progress; steps that only changed a detail or failed while the story moved are learned as optional.
- `learn --from-draft` keeps every deliberate, successful agent step (a sidestep out of a hazard shows no effect) and writes
  each mistake as a lesson with what the agent was trying.
- Dashboard: the needs-agent diagnosis on a segment's page; routes in the server's best-first order; no stray "null".

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
