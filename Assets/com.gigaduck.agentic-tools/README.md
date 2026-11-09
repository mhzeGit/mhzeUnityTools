# Agentic Tools

Rules, tools and workflows for AI coding agents (Claude Code, Codex, Cursor, Copilot, Gemini/Antigravity, Kilo), installed into
any Unity project with one button. They grew up in BlahBlahFamilyV6 and live here so every project gets the same, current set.

## Install

1. **Window ▸ Package Manager ▸ + ▸ Add package from git URL**:
   `https://github.com/mhzeGit/mhzeUnityTools.git?path=/Assets/com.gigaduck.agentic-tools`
   (pin a commit with `#<sha>` at the end).
2. **Tools ▸ Agentic Tools ▸ Open Window**, pick the modules, press **Install / Update**.
3. Commit what it added (the window lists every file; `.agentic-tools.json` records them).

Without Unity (a cloud agent, CI, or a repository that is not a Unity project):

```
python <package>/Install~/install.py --project <repo> [--status] [--enable taskboard] [--set RULES_DIR=Docs/rules]
```

`<package>` is the package folder: `Library/PackageCache/com.gigaduck.agentic-tools@<hash>` in a project that imported it, or
`Assets/com.gigaduck.agentic-tools` in a `mhzeUnityTools` checkout. `--help` lists every option.

## Modules

| Module | What the project gets |
| --- | --- |
| **Agent rules** (always) | Shared rules injected into `AGENTS.md`; pointer files for Claude (`CLAUDE.md`, imports `AGENTS.md`), Gemini/Antigravity (`GEMINI.md`, `.agent/rules/project-rules.md`), Copilot (`.github/copilot-instructions.md`) and Cursor (`.cursor/rules/agents.mdc`); rule docs `AGENTIC_TOOLS.md` (how these tools are maintained), `PACKAGES.md` (fix packages at the source), `UNITY_PITFALLS.md` (Unity and MCP lessons). |
| **Virtual commits** | Agents never commit: they register a record with `tools/git/agent-commit.ps1 prepare`; you review, commit and push the records you pick with `virtual-commit.bat` (one commit per record, only its files, with its version bump). Includes the worktree guard (a `post-checkout` hook that keeps the shared folder off detached HEADs), `repair-index.ps1`, `restore-worktree.ps1` and the `GIT.md` rules. |
| **Agent play testing** | `tools/agentplay/agentplay.py`: one shared standalone build that agents start their own players from (script-only changes patched in seconds without Unity), commands sent as JSON, pictures, contact sheets, build records that say whether a build is current, and the editor Play Mode claim. The `TESTING.md` policy and `AGENT_PLAY.md` guide. The player side is this package's `AgentPlayBuild`, `AgentPlayShaders` and `AgentPlayHarness`. |
| **Unity MCP setup** | `.mcp.json` and `.codex/config.toml` entries for MCP for Unity on `http://127.0.0.1:8080/mcp`. With `com.coplaydev.unity-mcp` installed, the package also compiles the `fast_play` MCP tool and starts the MCP server with the editor. |
| **Agent skills** | `unity-gameplay-workflow`, `environment-design`, `concept-art-to-3d`, `model-to-textured-asset`, `game-prop-concept-art` in `.claude/skills`, `.agents/skills` (Codex), `.kilo/skills` and `.agent/skills` (Antigravity). **Skills for my user** in the window (or `install.py --personal-skills`) also copies them to your home folders. |
| **Procedural animation rule** | `PROCEDURAL_ANIMATION.md`: motion computed in code always gets an editable curve profile. |
| **Agent Task Board** (off by default) | `tools/taskboard` and `task-board.bat`: a local kanban that agents claim tasks from; finished tasks wait in Review until you commit their virtual commit. Open it from **Tools ▸ Agent Task Board ▸ Open Board** in Unity. |

## How it injects without overriding

- **Rules are blocks.** Each rule is written between `<!-- agentic-tools:begin ID -->` and `<!-- agentic-tools:end ID -->`
  (`# agentic-tools:...` lines in TOML and `.gitattributes`). A file that does not exist is created; an existing `AGENTS.md`,
  `CLAUDE.md` and so on keeps all of its own text and gets the block appended (or placed after a related block). Updates rewrite
  only the inside of a block. Put project-only rules anywhere outside the blocks.
- **Tools are copies at fixed paths.** A path that already holds a different file nobody installed, or an installed file that was
  edited by hand, is a **conflict**: it is left alone and reported. **Force** (or `--force`) replaces conflicts on purpose.
- **Settings are merged.** `.mcp.json` only gains missing servers; a server the project defines is never changed.
- **Updates are automatic.** `.agentic-tools.json` records the version, the chosen modules and folders, and a hash of every file
  and block written. When the package is updated (the pin moves), the editor installs the new version on its next load, replacing
  only untouched copies; conflicts are listed in the Console. Disabling a module or uninstalling removes only untouched copies.
- Text is compared with LF line endings (Git may check a file out as CRLF on Windows), and an updated file keeps its own endings.

## Changing the tools (the rule every project gets)

The installed copies are never edited in a project. To add or change an agentic rule, tool, skill or workflow:

1. Edit this package in a `mhzeUnityTools` checkout: a rule in `Install~/blocks/`, a tool in `Install~/files/`, a skill in
   `Install~/skills/`, a new installed file as an item in `Install~/manifest.json`, editor or player code in `Editor/` or `Runtime/`.
   Keep it free of any one project's content; use `{{RULES_DIR}}`, `{{DOCS_DIR}}`, `{{RULES_TO_ROOT}}`, `{{RULES_TO_DOCS}}`,
   `{{DOCS_TO_RULES}}` and `{{PROJECT}}` for paths in blocks.
2. Bump `version` in `package.json`, add a `CHANGELOG.md` line, run `timeout 600 python Install~/test_install.py`.
3. Commit and push `mhzeUnityTools`.
4. In each project: move the pin in `Packages/manifest.json` to the new commit; the editor installs the update (or run
   `install.py`). Commit the updated copies with the manifest change.

## Layout

| Path | Contents |
| --- | --- |
| `Editor/AgenticToolsWindow.cs` | Tools ▸ Agentic Tools window. |
| `Editor/AgenticToolsPackage.cs` | Finds the package, auto-update on editor load, the Install or Update menu item. |
| `Editor/Installer/` | The installer (no Unity dependency) and its JSON reader/writer. |
| `Editor/AgentPlay/` | `AgentPlayBuild` (batch build of the agent player with `AGENT_PLAY`), `AgentPlayShaders` (shader stripping for that build). |
| `Editor/Mcp/` | `fast_play` MCP tool and MCP auto-start; compiled only with MCP for Unity installed. |
| `Runtime/` | `AgentPlayHarness`, the command server in agent players (ops, status extras, save folder hook), `CustomAgentPlayHarness` attribute. |
| `Install~/manifest.json` | Modules and their items (copy, block, json-merge). |
| `Install~/blocks/`, `Install~/headers/` | Rule blocks and the headings of files the installer creates. |
| `Install~/files/` | Tools copied into projects. |
| `Install~/skills/` | Agent skills. |
| `Install~/install.py`, `Install~/test_install.py` | Command-line installer and its tests (both installers, byte-identical results). |
| `Install~/tests/cs/` | Console wrapper that runs the C# installer for the tests. |

## Agent play in a new project

The package harness works without project code: `agentplay.py build`, then `start`, `send`, `shot`, `sheet`, `stop`. Optional:
`tools/agentplay/agentplay.json` (`defaultScene`, `buildMethod`, `unityEditorRoot`), `tools/agentplay/agentplay_project.py` for
project commands, and in game code (inside `#if AGENT_PLAY || UNITY_EDITOR || DEVELOPMENT_BUILD`):

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void RegisterAgentOps()
{
    AgentPlayHarness.Starting += folder => SaveSystem.Folder = Path.Combine(folder, "save");
    AgentPlayHarness.StatusExtras += r => r["level"] = LevelManager.CurrentName;
    AgentPlayHarness.RegisterOp("level", (c, r, fail) => LoadLevel(AgentPlayHarness.S(c, "name", ""), r, fail));
}
```

`agentplay.py` needs Windows, Python 3.9+ and the project's Unity version installed by the Hub (or `UNITY_EXE`).
