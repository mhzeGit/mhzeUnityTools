# Unity and MCP pitfalls (learned the hard way)

Read when the editor or the Unity MCP bridge misbehaves, before a QA run, or before a package test. Each item cost an agent
time once. Entry point: [AGENTS.md]({{RULES_TO_ROOT}}/AGENTS.md).
Add new lessons (one line each, with the symptom) to this file, not to a private agent memory: a lesson about Unity, MCP or
the agent tools in general belongs in the package source (see [AGENTIC_TOOLS.md](AGENTIC_TOOLS.md)); a lesson about this
project's own scenes and code goes below the shared part.

## The bridge

- **No tools / `ECONNREFUSED`.** The MCP server lives only while Unity is open with this project (`http://127.0.0.1:8080/mcp`,
  streamable HTTP). First check Unity is running. If the tools vanished mid-session but port 8080 listens again, call it from
  Python: POST `initialize`, then `notifications/initialized`, then `tools/call` (headers `Accept: application/json, text/event-stream`,
  keep the `Mcp-Session-Id` response header). `execute_code`, `read_console`, `manage_scene`, `fast_play`, `refresh_unity` work.
- **The server does not start with the editor.** This package's `McpAutoStartOnLoad` turns on MCP for Unity's own
  *Auto-Start on Editor Load* preference and starts the local HTTP server on the load where that preference flips.
- **Compile errors without MCP:** `Logs/Editor.log` shows them. An open editor recompiles on its own; `AssetDatabase.Refresh()` plus
  `CompilationPipeline.RequestScriptCompilation()` from `execute_code` forces it.
- **No editor at all:** batch mode works (`Unity -batchmode -nographics -quit -projectPath . -executeMethod <Type.Method> -logFile <file>`),
  only when no editor has the project open (`Temp/UnityLockfile`). Give it a deadline like any test run.

## Compile / reload stuck

- **Symptom:** `refresh_unity` says idle but `EditorApplication.isCompiling` stays true for minutes, new types never appear, `Library/Bee` and
  `Library/ScriptAssemblies` timestamps do not move, `EditorCompilationInterface.IsCompilationPending()` is true while `IsCompilationInProgress()` is
  false. The editor window is unfocused and does not tick its compile pipeline (typical after another agent's automated play run).
- **Fix 1:** from `execute_code` call the internal tick yourself (reflection on
  `UnityEditor.Scripting.ScriptCompilation.EditorCompilationInterface.TickCompilationPipeline(options=BuildingForEditor, activeBuildTarget, 0, new string[0], false)`)
  in a loop with `Thread.Sleep(500)` until it stops returning `Compiling`/`CompilationStarted`. `CompilationFailed` = real errors: read the console.
  `CompilationComplete` still needs a domain reload.
- **Fix 2:** the reload did not follow because a stale lock is held (`CanReloadAssemblies()` false, `IsScriptReloadRequested()` true). Check
  `tests.is_running` is false (`mcpforunity://editor/state`), then call `EditorApplication.UnlockReloadAssemblies()` once.

## Fast Play

These traps matter for the user's editor runs and for an agent holding the editor Play Mode claim ([`TESTING.md`](TESTING.md) rule 1).

- The `fast_play` MCP tool defaults to **starting** Fast Play. Use explicit `action: "status"` to inspect or
  `action: "stop"` to restore Enter Play Mode settings. If an older wrapper shows an empty schema,
  refresh tools or call `FastPlayTool.HandleCommand` from `execute_code` with a JSON action.
- Domain **and** scene reload are off: serialized state is restored on exit but **non-serialized fields of scene components persist into the next
  session**. Reset runtime fields in `Awake`/`OnEnable`; never trust a `_prepared`-style flag across runs.
- Editing any script during Play Mode recompiles and ends the run. Finish or poll the run's log first.
- C# events on scene objects survive Fast Play too: a subscription made in `Awake` piles up each session, so one click runs the handler twice after two runs. Subscribe in `OnEnable`, unsubscribe in `OnDisable`.
- Static fields survive as well (no domain reload): reset them in a `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]` method.
- `?.` does not see Unity's fake null: in the editor an unassigned serialized `GameObject` field is not C# null, so `field?.SetActive(...)` throws `UnassignedReferenceException` (builds are fine).
- Other agents share this editor: never abort a peer's run; wait on its run id or use your own standalone player.

## `execute_code`

- It compiles with codedom against **stale assemblies**: a type added in the last compile may not resolve directly. Use
  `Type.GetType("Namespace.Type, Assembly-CSharp-Editor")` and reflection. Local functions after `return` do not compile. `Object` is ambiguous (write
  `UnityEngine.Object`); `EditorUtility.InstanceIDToObject` is obsolete. A class defining its own method hides `using static` overloads of that name.
- It blocks `AssetDatabase.DeleteAsset` and `File.Delete`: delete your own generated assets from the shell, then `AssetDatabase.Refresh`.
- `GameObject.Find` skips inactive objects: walk `Transform.Find` paths.
- Sample what the renderer sees with `Application.onBeforeRender`, not `EditorApplication.update`.

## Captures

- `manage_camera screenshot` writes into `Assets/Screenshots` (inside the project, picked up by Git tooling). Capture with
  `ScreenCapture.CaptureScreenshot(<scratchpad path>)` instead, or take pictures in your standalone player (`agentplay.py shot`).
- Validations that drive input should refuse to start while a person used the keyboard or mouse in the last seconds, and must never
  call `InputSystem.DisableDevice` on real hardware: swap in virtual devices for the run.

## Testing a package change from this project

- Point `Packages/manifest.json` temporarily at `file:<source checkout>/<package folder>` (Unity then writes new `.meta` files into the
  source repo). Back up `manifest.json` and `packages-lock.json` first; after `git push` restore them and set `#<full sha>` in both (and the lock's `hash`).
- EditMode tests of a `file:` package are not discovered: add `"testables": ["<pkg>"]` to the manifest temporarily, then `run_tests` with
  `assembly_names: ["<pkg>.Tests"]`. The first run after a refresh can fail to initialise: retry.
- A package source repo may use a Unity version that is not installed on this machine, so a batch compile there can be impossible:
  compile it in this project instead (the `file:` route above).
