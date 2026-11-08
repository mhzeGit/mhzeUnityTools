## Shared agent rules

**Agentic tools ([`AGENTIC_TOOLS.md`]({{RULES_DIR}}/AGENTIC_TOOLS.md)).** These `agentic-tools` blocks (project rules go around them), pointer files, `tools/git`, `tools/agentplay` and skills are installed copies (`.agentic-tools.json`): never edit them here. A new or changed agentic rule, tool, skill or workflow goes into the package source (repo `mhzeUnityTools`, `Assets/com.gigaduck.agentic-tools`): change and push it there, then move this project's pin and reinstall.

**Packages ([`PACKAGES.md`]({{RULES_DIR}}/PACKAGES.md)).** Never fix, extend or vendor package code here (not in `Library/PackageCache`, not copied into `Assets/`): change the package's source repo, push, then move the pin in `Packages/manifest.json`. Committing and pushing in a package source repo needs no user request (the one exception to the virtual-commit rule): stage only your package files, never force-push. No class exists in both places: prototype here, migrate before your commit.

**Docs are part of the change**: a changed system or tool updates its doc in the same task.

**Scenes and prefabs: never regenerate.** A scene or prefab a person edited is hand-authored: no tool rebuilds, migrates or "cleans up" an existing `.unity` or `.prefab`; tools only add objects to an open scene or write to a new path. No `[InitializeOnLoad]`, `delayCall`, asset postprocessor or scene/build hook may change scene, prefab or asset content on its own. Restore a wrong one from Git (`git show <sha>:<path>`) or a pending record; never rebuild it.

**Every test command has a deadline** (shell timeout plus an in-command guard; no watch modes). A killed run is a timeout, not a failure: find the cause, kill the process tree, never rerun it unchanged.

**Unity or MCP misbehaves:** [`UNITY_PITFALLS.md`]({{RULES_DIR}}/UNITY_PITFALLS.md); add each new lesson there.
