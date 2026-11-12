# Changelog

## 1.2.0 - 2026-11-12

- New opt-in module **Production planner skill** (`planner`): the `do-task` skill. Give an agent a card id or title from the
  production plan and it reads the card (definition of done, steps, why, estimate), moves it to Now, does and verifies the
  work under the project's rules, ticks its steps, reports, and ticks the card off with `pm done` once you confirm. Installed
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
