---
name: agent-playtest
description: Play-test big chunks of the game or all of it with bots that learn to play by trial and error, share what they learned in the project's knowledge base, record video and write a quality report; also record video of any agent test. Use when asked to playtest, play through, master, speedrun-check or regression-check a level, chapter or the whole game, to record or show gameplay video, or to judge pacing, polish and stability by playing. Not for checking one small function (use a scoped agentplay test).
---

# Agent playtest: learn, master, play, record

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
python tools/agentplay/agentplay.py learn bot --scope S --workers 2      # starts and stops its own players bot-w1, bot-w2
```

Each segment: explore with the autopilot if it has no route; replay routes with human-like noise until one wins `--streak`
times in a row (mastered); repair failures into variants; try faster variants. Mastered segments only get one check run.
Expect minutes per segment; `--workers` (about 0.7 GB RAM each) runs segments in parallel. The summary lists each segment's
mastery; read every **NEEDS AGENT** line.

## 3. Take over where the autopilot gave up

`kb show SEGMENT` has the diagnosis (what the story waits on, what is near, what was tried and how it scored).

```
python tools/agentplay/agentplay.py start bot1 --require-current
python tools/agentplay/agentplay.py send bot1 '{"op":"stage","n":N}'          # or the segment's own setup
python tools/agentplay/agentplay.py observe bot1                               # story hints, usable objects, text, suggestions
python tools/agentplay/agentplay.py step bot1 '{"op":"interact","target":"X"}' --why "the hint says X" --segment SEG
python tools/agentplay/agentplay.py step bot1 '{"op":"walk","pos":[x,y,z]}' --segment SEG
python tools/agentplay/agentplay.py learn bot1 --segment SEG --from-draft      # verified route; failed steps become lessons
python tools/agentplay/agentplay.py learn bot1 --segment SEG                   # make it robust (noise, repairs) until mastered
python tools/agentplay/agentplay.py kb lesson SEG "what a player must know here" --kind tip|pitfall|bug|fix
```

The player looks where it walks and at the hotspots (who speaks, live hazards, what a new story step points at) by itself:
keep `attention` on, and point it (`step bot1 '{"op":"attention","focus":"Kettle","for":5}'`) at what you are checking.
Prefer real inputs (`interact`, `aim`, `walk`, `key`, `click`, `look`, `until`) over `teleport` or story jumps inside a route:
a route that cheats proves nothing about the game. Use `until` with a condition instead of fixed `wait`s where you can.
If the game itself is broken (an object never becomes usable, a beat never ends), that is a finding: record a `bug` lesson,
keep the segment needs-agent and tell the user; do not hide it with a jump.

## 4. Play the chunk and record it

```
python tools/agentplay/agentplay.py playtest bot --scope S [--learn]   # --learn first learns what is not mastered
```

It plays every segment with its best route, chaining them without jumps where the game is already at the next one, records
one video, and writes `Builds/AgentPlay/playtests/<id>/report.html` + `report.json`: a verdict; completion, stability,
performance, pacing and friction scores; per segment outcome, time, deaths, new errors, worst frame, blocked walks, how much
was waiting; findings with their time in the video.

**Two kinds of play-testing.** A playtest *finishes* the game (or a stretch of it) the clean way: does it play through,
how does it feel. A **deep test** does not try to finish: in each section it tries everything a player could
(`agentplay.py deeptest bot --scope S [--states all]`): every interactable in reach (walk there, aim, press or hold the
interact key), an item in hand on every other interactable, every move of the project's input map (a tap each; sprint and
crouch held while walking; pause in and out), and walking probes in 8 directions. Each action's effect is classified
(error in the log, death, fell through, story moved, item held, something changed, nothing, could not be used, blocked); a
story change out of order gets a softlock check (can the learned route still finish the section?). Coverage is shared in
`AgentPlayKnowledge/coverage/` (nothing is tested twice on the same build; a new build retests); the report (one video, a
caption per action, findings at their time) is on the dashboard and phone. Use it when asked to test everything, find
bugs, or check a section after a change; `--states all` also tests at each story state the learned route goes through.
It is smart about what it tries: item moves only with an item in hand, no repeats of moves that did nothing or the same
harmless thing before, two of each kind of look-alike object (the rest only if those differ), story objects first, areas
already walked around not probed again; every skip has its reason in the report. `--exhaustive` tries everything.

**A perfect play-through.** Segments mastered one at a time can still fail when played in one go (the run before leaves an
NPC a step further, a door already open). When the user wants to see a whole stretch played cleanly:

```
python tools/agentplay/agentplay.py playtest bot --scope S --until-perfect 8 [--learn]
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
video time and sheet) or a route the game outgrew (relearn: `learn bot --segment SEG --force`).

## 6. Record video in any test

Any agent test can carry a video: `record NAME start --label "..."`, `record NAME mark "now X should happen"`,
`record NAME stop` (prints the MP4, captions, sheet). The HTTP API (`agentplay.py serve --detach`) serves the same videos,
reports and knowledge: `GET /api/videos/<id>`, `/api/playtests/<id>`, `/api/knowledge/<segment>`.

## 7. Report

Give the user: the verdict and scores, segments that failed or need an agent (with the reason), new errors, hitches and
pacing notes with their video times, the paths of `report.html` and the video, and `agentplay.py serve --detach --open` to
browse it all. When the user is away from the computer (or reads you on a phone), `agentplay.py serve --detach --tunnel`
prints a secret view-only link that opens the dashboard, the report and the video anywhere; give them that link (the report
is `<link host>/media/playtests/<id>/report.html?key=<key>`). Show them a contact sheet of the important moments the way their client displays images. List the knowledge
base files you changed in your commit, and the lessons you added.
