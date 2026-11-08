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
  its assembly with `[assembly: Gigaduck.AgenticTools.AgentPlay.CustomAgentPlayHarness]`, and the package harness stays off.

## Limits

- A player only contains what was built: `info` must show your files inside the build (not in its STALE list), or you are testing old code.
- Compile new scripts offline before they enter the shared tree: one broken file stops the shared editor for everybody.
- Editor-only test tools (validators that need Play Mode, authoring in Play Mode) need the editor: run them under the rule 1 claim
  or list them as **Not run**.
