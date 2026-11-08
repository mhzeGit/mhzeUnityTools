# Testing rules: how agents test (editor Play Mode when alone, else the standalone player)

Read before you test, play, build or move the player. Entry point: [AGENTS.md]({{RULES_TO_ROOT}}/AGENTS.md). Player commands:
[`AGENT_PLAY.md`]({{RULES_TO_DOCS}}/AGENT_PLAY.md).

This policy overrides any older line in a module doc, skill or package doc that tells an agent to "Fast Play" or to use an
editor-only test tool: an agent may do that only under rule 1 below, otherwise it uses the standalone-player route.

## Policy: four rules

### 1. Editor Play Mode when Unity is free; otherwise the standalone player

The shared editor's Play Mode (entered only with `fast_play`, see *Editor Play Mode* below, then the project's editor-only
test tools) is open to **one agent at a time**, and only when both checks below pass. Other agent sessions that are open,
editing or finished do not matter: only an agent that is testing in the editor right now does, and it holds the claim. Check,
right before you test:

1. **Unity is not busy.** `fast_play` with `action: "status"` (never call it bare: it starts Play Mode) says not
   playing; the editor is not compiling or importing (`mcpforunity://editor/state`); and no person is at the controls (no
   keyboard or mouse input in the last minute: [`UNITY_PITFALLS.md`](UNITY_PITFALLS.md); a person playing always wins).
2. **No other agent is testing in the editor: you get the claim.** `python tools/agentplay/agentplay.py editor claim --by <tool>-<task-slug> --why "<what you test>"`.
   It fails (exit 4) only while another agent is testing in the editor: then use the standalone player, do not wait for it.
   An agent that finished testing has released its claim, so it never blocks you; a claim nobody renews (a crashed or stopped
   agent) expires after 15 minutes by itself. Run the same command again to renew it during a long test.

If any condition fails, test in **your own standalone player** (rule 4). While you hold the claim: one batched test session, as
in rule 2; never save a scene or prefab from Play Mode; then stop Play Mode (`fast_play` `action: "stop"`), undo test side
effects (saves, counters, settings the test changed), and **always** run `agentplay.py editor release --by <same name>` as
soon as the test session ends, also when it failed, so you never block another agent. Hold the claim only while you test,
never while you implement or fix: a retest claims again (rule 1 checks again).

Interactive authoring that needs Play Mode (tuning poses or audio live) stays with the user. Name it under **Not run** in your
final message. Edit-mode checks (compile, validators, edit-mode tests) are fine and cheap at any time: do them before the test.

**No Unity on this machine** (a cloud session, or any machine without Windows and Unity): `agentplay.py` cannot build or run. Do not
ask whether to test and do not pretend: write the check list you would have run under **Not run** in the final message.

### 2. Few big tests, never none

- Implement the whole task first. While you implement, write the check list (what must be true in the game). Then build once, start
  one player and run every check in that session. Do not make a small feature, test it, make the next one, test again. A warm
  player is cheap (scene changes and teleports need no restart); a build is not.
- Tests are not optional. A task that changes what the player sees or does, or gameplay, story or player code, gets at least one
  verified player run, unless the user chose to test it (rule 3). A task with nothing to play (docs, index, tools, editor-only code,
  an asset move) needs no test.
- One exception: an early probe before you build a lot on an engine behaviour that reading the code cannot settle. One build, and
  say that it was a probe.
- A failed run: read all of it (summary, console, pictures), fix everything it showed, retest once. After two failed rounds on the
  same problem, stop and report with the evidence.
- **Scope every test to what your change can affect, and stop there.** Never run the whole game by default: test the levels,
  stages or scenes your change touches (plus the one just before when what happens on entry matters), and stop as soon as every
  check in your list has its answer. A whole-game run only when the user asked for it, or the change is a system every part of
  the game uses and you say so.
- Keep a test cheap in tokens: one summary, a contact `sheet` instead of many single pictures, and never poll a build in a
  loop (`build` is one blocking command that prints one result).

### 3. Ask the user once per task

- When the implementation is done and before you build, ask once whether to test: say what you would check and what it costs (the
  newest build is `CURRENT` so no wait, or a rebuild of about the time the last record shows). Offer: test everything, test only X,
  or "I test it myself". Many things the user can check by hand faster than an agent can.
- The answer holds for the whole task. Fixing what a failed run found, retesting, extra checks of the same work: no new question.
  Ask again only when you start fixing a **new, different issue** that the earlier answer did not cover.
- If the task text already says to test, or not to, that is the answer: do not ask.
- If the user tests it, say so in your final message and in the virtual commit description ("tests: user tests manually"). If there
  is nobody to ask, run the one batched test for player-visible or gameplay changes, and say that you did.

### 4. One shared build, one player each

1. Run `python tools/agentplay/agentplay.py info` first (or read `Builds/AgentPlay/latest.json`, which needs no scan). It prints when
   the newest build was made and how long it took, its Git commit, the uncommitted paths and what changed against the previous
   build, then `CURRENT` (it matches the working tree) or `STALE` with the exact files that changed since it was built.
2. `CURRENT`: do not build; start your player. `STALE` and a listed file is yours, or something you test depends on it:
   `build --by <your name>` (other agents queue on the build lock, then find the build done). `STALE`, but none of the listed
   files affects your check and none of yours is listed (so all of yours are inside the build): start it without
   `--require-current` and say so in your report. Never `build --force` or `--full` "to be sure".
   - `build` **patches** by itself when every change since the newest build is a script under `Assets/`: it recompiles only
     `Assembly-CSharp` (no Unity starts) and swaps that DLL into a hardlinked copy of the build, in seconds instead of minutes.
     Any other change (scene, prefab, asset, package, settings, `.asmdef`), a changed serialized field, or a new or removed
     MonoBehaviour/ScriptableObject type makes it print `full build needed: <reason>` and run the full build. That is correct;
     do not work around it.
   - Never start a full build per small change, never run several builds in parallel or poll `Builds/AgentPlay/build.lock`, and
     never kill a build or player you did not start. `build` blocks once and prints one result; while it waits on another
     agent's build it says so once.
3. Every agent runs **its own instance** of that build: `start <tool>-<task-slug>`, a name nobody else uses. Never `send` to,
   `stop` or kill another agent's player; never `stop --all`. Stop your own player when you finish.
4. Builds are tracked automatically ([`AGENT_PLAY.md`]({{RULES_TO_DOCS}}/AGENT_PLAY.md) "Build records"): time, duration, who asked,
   commit, changes since the previous build. You do not write them.

## Test runs always have a deadline

A hanging test wastes more time than a failing one, and a run that a deadline killed is **not** a failed test.

1. **Always impose a deadline.** Never start a test command without a hard timeout that will terminate it: set the shell tool's
   timeout *and* a matching in-command guard (`timeout 300 dotnet test ...` on POSIX, or PowerShell `Start-Process` +
   `WaitForExit(timeout)` / `Wait-Job -Timeout`). A few minutes is right for a normal suite; raise it deliberately for a heavy
   run, never leave it unlimited. `dotnet test --blame-hang-timeout 5m` also dumps the hung test's stack. Unity batch runs
   (`-runTests`, `-executeMethod`) get the same deadline.
2. **Never run watch/continuous modes** (`dotnet watch test`, `--watch`, `--serve`): they never exit by design. Prefer the narrowest
   run that proves the change (one test class or fixture, or a filter) over the whole suite.
3. **Give potentially blocking tests their own timeout.** Anything that waits on I/O, network, UI, a timer or another thread uses the
   framework's timeout attribute (`[Timeout(...)]` in NUnit / Unity Test Framework, `[Fact(Timeout = ...)]` in xUnit).
4. **Distinguish "timed out" from "failed".** Report a deadline-killed run as a timeout and investigate the cause (deadlock, waiting
   on input, a listener that never fires, an unbounded loop) before running again. Without a fix it will just hang again. If the
   deadline was your own limit rather than a real hang, say so.
5. **Clean up after a timeout.** Terminate the whole process tree, not just the parent, so the abandoned run stops using the
   machine while other agents work. Remove the test artifacts you generated once you have read them.

## Editor Play Mode: always Fast Play

The **Fast Play Button** package (mhzeUnityTools) enters Play Mode with **Domain Reload and Scene Reload
disabled**, which is dramatically faster, and restores the original Enter Play Mode settings on exit.

- **Never start Play Mode the default way**: not the Unity Play button (Ctrl/Cmd+P), not the MCP `manage_editor` `play` action,
  and not `EditorApplication.isPlaying = true` with default settings. That path does a full domain and scene reload.
- **Do it one of these ways instead:**
  - **An agent holding the rule 1 claim:** the MCP custom tool **`fast_play`** (from this package, `Gigaduck.AgenticTools.Mcp`,
    compiled in when MCP for Unity is installed) with `action: "play"` to enter, `action: "stop"` to exit and `action: "status"`
    to inspect. It sets the fast options, mirrors the Fast Play toolbar state, and restores the originals on exit.
  - In the editor, the toolbar **Fast** button (top toolbar, right side) enters and **Stop** exits.
  - From an agent script, set the fast options first, then start Play Mode, and restore them after Play Mode ends:

    ```csharp
    var origEnabled = EditorSettings.enterPlayModeOptionsEnabled;
    var origOptions = EditorSettings.enterPlayModeOptions;
    EditorSettings.enterPlayModeOptionsEnabled = true;
    EditorSettings.enterPlayModeOptions =
        EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload;
    EditorApplication.isPlaying = true;
    ```

- If the Fast button is unavailable (something already started Play Mode the normal way), stop Play Mode, return to Edit Mode,
  and re-enter with Fast Play before testing. Stop Fast Play and confirm the original Enter Play Mode settings are restored when
  testing is done.
- Play Mode only advances while the editor ticks: with the editor window unfocused the game barely advances unless
  `Application.runInBackground` is true. An agent testing through MCP may set it at runtime with `execute_code`; never change the
  project setting for this.
