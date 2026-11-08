# Changelog

## 1.0.1 - 2026-11-08

- Virtual commits: `agent-commit.ps1` builds file paths with the platform's separator, so `prepare` also registers new files
  on Linux and macOS (cloud sessions); Windows is unchanged.

## 1.0.0 - 2026-11-08

- First release, gathered from BlahBlahFamilyV6 (rules, virtual commits, agent play, MCP tools, skills) and Graphmat (Agent Task Board).
- Tools ▸ Agentic Tools window and `Install~/install.py`: modules, rule blocks injected next to a project's own rules, conflict
  detection for hand edits, automatic update on editor load when the package version changes, uninstall.
- Generic `AgentPlayHarness` with project ops (`RegisterOp`), status extras and a save-folder hook; `agentplay.py` reads
  `agentplay.json` and loads project commands from `agentplay_project.py`.
