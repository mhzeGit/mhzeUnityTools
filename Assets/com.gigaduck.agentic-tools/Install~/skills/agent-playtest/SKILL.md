---
name: agent-playtest
description: Play-test a chapter, a stretch or the whole game with bots that play with real inputs and learn by trial and error (a shared knowledge base), record video and write a report; deep-test sections by trying every interactable, door, item combination and move to find bugs; record video of any agent test. Use when asked to playtest, play through, do a walkthrough, get a perfect run, test every door/interactable/feature of a chapter, find bugs by trying everything, master, speedrun-check or regression-check, or to record or show gameplay video. Not for checking one small function (use a scoped agentplay test).
---

# Agent playtest: learn, master, play, deep-test, record

## 0. Recipes: most requests are one or two commands

`python tools/agentplay/agentplay.py ...` Each command starts and stops its own standalone player (no editor claim needed),
records one video and prints a `VIEW` link. `NAME` is your own player name, `<tool>-<task>` (`claude-doors`). Run `info`
first; build only when it says STALE.

| The request | Run |
| --- | --- |
| Play through / walk through chapter X (recorded) | `kb brief --scope X`, then `playtest NAME --scope X` (`--learn` if brief says not mastered) |
| A perfect, clean play-through of X | `playtest NAME --scope X --until-perfect 6` |
| Test every door (or one kind of thing) in X | `deeptest NAME --scope X --only door` (regex on name, path or prompt) |
| Test everything in X / find bugs | `deeptest NAME --scope X` (`--states all` deeper, `--exhaustive` no skipping) |
| Walkthrough of X and test every door, recorded | `playtest NAME --scope X`, then `deeptest NAME --scope X --only door` |

Scope words: `segments --scope X` lists what X covers (ids, prefixes, `N`, `N-M`; the project adds chapter ids and titles,
see `Docs/AGENT_PLAY.md`, *This project*). Report exactly what the commands printed: the verdict, each finding with its
`@m:ss` video time, the section notes and the `VIEW` links. Do not open videos, report files or knowledge files to verify;
the printed summary is the result. Times: playtest 20 to 60 s per section; deep test 10 to 15 min per full section, about
1 min per section with `--only`; until-perfect one playtest per run. Run them in the background; every line prints as it
happens.

- If `kb brief` shows sections not mastered, a plain playtest still plays the scope and reports them (`unlearned`: no route,
  it waited for the goal; `fail` on a stale route): that is the answer to a walkthrough request. Learn (`--learn`,
  `--until-perfect`) only when the user wants the stretch played to its end.
- Every run writes test data: the knowledge base segments (`AgentPlayKnowledge/`) and deep-test `coverage/`. Expected in any
  test task: keep the files and list them in the report and commit.
- A deep test with `--only` reports `repeat` for an object already tried in the run (same prompt) and `skipped` for what is
  not worth trying; a section whose story has already moved on when it is reached is skipped with a note.

The rest of this skill explains the machinery for when something fails.

Two kinds of testing exist. A **function test** checks one change in a few commands (`agentplay.py send`, `sweep`, a shot).
A **playtest** plays a stretch of the game the way a player does, to see how it runs as a whole. This skill is the
playtest. It builds on the agent player (`Docs/AGENT_PLAY.md`); the testing rules (`Docs/rules/TESTING.md`) still apply:
ask the user once per task whether to test, keep the build current, use your own player names, stop what you start.

The bots learn. Every run feeds one shared **knowledge base** (`AgentPlayKnowledge/` unless `agentplay.json` says otherwise):
routes that play each segment, a win streak per route, values for actions in each story state (what moved the story on,
what did nothing, what killed the player), lessons and history. **Never learn again what is already mastered: start from it
and only improve it.** Commit the knowledge base with your work so every agent and machine shares it.

## 1. Read what is known (always first)

```
python tools/agentplay/agentplay.py info                 # build CURRENT? (build only when STALE; see AGENT_PLAY.md)
python tools/agentplay/agentplay.py kb brief --scope S   # mastery per segment, needs-agent list, lessons, the next command
python tools/agentplay/agentplay.py segments --scope S   # the segments a scope names
```

Pick the scope the user asked for. A whole-game scope only when they asked for the whole game.

## 2. Learn what is not mastered

```
python tools/agentplay/agentplay.py learn NAME --scope S --workers 2      # starts and stops its own players NAME-w1, NAME-w2
```

Each segment: explore with the autopilot if it has no route; replay routes with human-like noise until one wins `--streak`
times in a row (mastered); repair failures into variants; try faster variants. Mastered segments only get one check run.
Expect minutes per segment; `--workers` (about 0.7 GB RAM each) runs segments in parallel. The summary lists each segment's
mastery; read every **NEEDS AGENT** line.

## 3. Take over where the autopilot gave up

`kb show SEGMENT` has the diagnosis (what the story waits on, what is near, what was tried and how it scored).

```
python tools/agentplay/agentplay.py start NAME --require-current
python tools/agentplay/agentplay.py send NAME '{"op":"stage","n":N}'          # or the segment's own setup
python tools/agentplay/agentplay.py observe NAME                               # story hints, usable objects, text, suggestions
python tools/agentplay/agentplay.py step NAME '{"op":"interact","target":"X"}' --why "the hint says X" --segment SEG
python tools/agentplay/agentplay.py step NAME '{"op":"walk","pos":[x,y,z]}' --segment SEG
python tools/agentplay/agentplay.py learn NAME --segment SEG --from-draft      # verified route; failed steps become lessons
python tools/agentplay/agentplay.py learn NAME --segment SEG                   # make it robust (noise, repairs) until mastered
python tools/agentplay/agentplay.py kb lesson SEG "what a player must know here" --kind tip|pitfall|bug|fix
```

The player looks where it walks and at the hotspots (who speaks, live hazards, what a new story step points at) by itself:
keep `attention` on, and point it (`step NAME '{"op":"attention","focus":"Kettle","for":5}'`) at what you are checking.
Prefer real inputs (`interact`, `aim`, `walk`, `key`, `click`, `look`, `until`) over `teleport` or story jumps inside a route:
a route that cheats proves nothing about the game. Use `until` with a condition instead of fixed `wait`s where you can.
If the game itself is broken (an object never becomes usable, a beat never ends), that is a finding: record a `bug` lesson,
keep the segment needs-agent and tell the user; do not hide it with a jump.

## 4. Play the chunk and record it

```
python tools/agentplay/agentplay.py playtest NAME --scope S [--learn]   # --learn first learns what is not mastered
```

It plays every segment with its best route, chaining them without jumps where the game is already at the next one, records
one video, and writes `Builds/AgentPlay/playtests/<id>/report.html` + `report.json`: a verdict; completion, stability,
performance, pacing and friction scores; per segment outcome, time, deaths, new errors, worst frame, blocked walks, how much
was waiting; findings with their time in the video.

**Two kinds of play-testing.** A playtest *finishes* the game (or a stretch of it) the clean way: does it play through,
how does it feel. A **deep test** does not try to finish: in each section it tries what a player could, from the first
quiet moment, against a control (what happens with no input), and classifies what each action did: `deeptest NAME --scope S`
for everything (interactables, item combinations, the input map's moves, walking probes), `--only door` for every door
(every match is tried, once per prompt per run), `--states all` to also test at later story states. Outcomes, the smart
skipping and the shared coverage are in `Docs/AGENT_PLAY.md` (*Deep tests*). BUG findings (`error`, `fell`, `paused`) and
deaths are what to report first; `failed` on a usable object (never under the crosshair) is worth a look; `softlock` is a
warning to check in the video.

**A perfect play-through.** Segments mastered one at a time can still fail when played in one go (the run before leaves an
NPC a step further, a door already open). When the user wants to see a whole stretch played cleanly:

```
python tools/agentplay/agentplay.py playtest NAME --scope S --until-perfect 8 [--learn]
```

It plays the scope chained from its first segment, again and again, each run recorded: routes are picked by win-stay/UCB, a
failure in a run adds a lesson and repair variants (or explores on from where the run left the game) and the run carries on
by jumping; the next run tries the repairs. It stops at the first **perfect** run (every segment passed on a learned route,
chained from the one before, no deaths, no new errors: verdict `PERFECT`) and prints the run table. If no run is perfect
within the budget, the segments that keep failing are the ones to teach by hand (section 3).

## 5. Look at the evidence (cheaply)

Never watch the MP4 yourself. Read the report's findings, then look at the moments that matter:

```
python tools/agentplay/agentplay.py video sheet <video id> --from T-5 --to T+15   # keyframes with time and the action at that moment
python tools/agentplay/agentplay.py video events <video id> --kind result          # what each step did
python tools/agentplay/agentplay.py video frame <video id> --at T --exact          # one exact frame
```

A failed mastered route (segment **stale**) means the game changed. Decide which: a regression (report it as a bug, with the
video time and sheet) or a route the game outgrew (relearn: `learn NAME --segment SEG --force`).

## 6. Record video in any test

Any agent test can carry a video: `record NAME start --label "..."`, `record NAME mark "now X should happen"`,
`record NAME stop` (prints the MP4, captions, sheet). The HTTP API (`agentplay.py serve --detach`) serves the same videos,
reports and knowledge: `GET /api/videos/<id>`, `/api/playtests/<id>`, `/api/knowledge/<segment>`.

## 7. Report

Give the user: the verdict, segments that failed or need an agent (with the reason), the findings with their video times,
and the `VIEW` link each run printed (it opens the report and its video; on a phone too when the dashboard is shared). No
`VIEW` link means the dashboard is not running: `agentplay.py serve --detach` (local) or `serve --detach --tunnel` (a secret
view-only link for a phone; `serve --status` prints it again), then the report is `<link>#playtests/<id>`. Show a contact
sheet only for a finding that needs a picture. List the knowledge base files you changed in your commit (segments and
`coverage/`), and the lessons you added.
