# Agentic tools: one source, installed into every project

Read before you add or change an agent rule, a skill, the virtual-commit tool, the agent player or any other agent workflow.
Entry point: [AGENTS.md]({{RULES_TO_ROOT}}/AGENTS.md).

## Where they live

Every agentic rule, tool and workflow that is not about one project's content is maintained in **one place**: the Unity package
**Agentic Tools** (`com.gigaduck.agentic-tools`), folder `Assets/com.gigaduck.agentic-tools` of the **`mhzeUnityTools`** repository
(`https://github.com/mhzeGit/mhzeUnityTools`). Projects import it by Git URL and install copies from it:

| Installed copy | Package source (in `Assets/com.gigaduck.agentic-tools/`) |
| --- | --- |
| `agentic-tools` blocks in `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `.github/copilot-instructions.md`, `.cursor/rules/agents.mdc`, `.agent/rules/project-rules.md` | `Install~/blocks/`, `Install~/headers/` |
| `{{RULES_DIR}}/*.md` blocks (GIT, TESTING, PACKAGES, UNITY_PITFALLS, PROCEDURAL_ANIMATION, AGENTIC_TOOLS), `{{DOCS_DIR}}/AGENT_PLAY.md` block | `Install~/blocks/rules-*.md`, `Install~/blocks/docs-*.md` |
| `tools/git/` (virtual commits), `virtual-commit.bat` | `Install~/files/tools/git/`, `Install~/files/virtual-commit.bat` |
| `tools/agentplay/agentplay.py`, `DllCheck.cs` | `Install~/files/tools/agentplay/` |
| `tools/taskboard/`, `task-board.bat` (if enabled) | `Install~/files/tools/taskboard/`, `Install~/files/task-board.bat` |
| `.claude/skills/`, `.agents/skills/`, `.kilo/skills/`, `.agent/skills/` | `Install~/skills/` |
| `.mcp.json` (merged keys), `.codex/config.toml` block | `Install~/files/mcp.json`, `Install~/blocks/codex-mcp.toml` |
| Editor and player code: `fast_play` MCP tool, MCP auto-start, agent player build, agent player harness, the installer window | `Editor/`, `Runtime/` (compiled from the package, not copied) |

What is installed where, with a hash of each copy, is recorded in `.agentic-tools.json` in the project root. `Install~/manifest.json`
lists the modules and their items.

## The rule: change the source, then reinstall

1. **Never edit an installed copy.** A copy edited in place is a *conflict*: the next update leaves it alone and reports it, so the
   project silently falls behind, and every other project never gets the change.
2. **A new or changed agentic rule, skill, tool or workflow goes into the package source**: edit `Assets/com.gigaduck.agentic-tools`
   in a `mhzeUnityTools` checkout (find it with `git remote -v`; clone `https://github.com/mhzeGit/mhzeUnityTools` if there is none).
   A new file to install goes under `Install~/` and gets an item in `Install~/manifest.json`; a rule goes in the matching block.
   Keep it free of any one project's content: name no project's scenes, chapters, characters or paths. Project-specific detail
   stays in that project, outside the blocks.
3. **Bump `version` in the package's `package.json`** (patch for a fix or wording, minor for a new rule, tool or module) and add a
   line to its `CHANGELOG.md`. Run the installer's tests (`python Install~/test_install.py`, with a deadline).
4. **Commit and push in `mhzeUnityTools`.** Like any package source repo, this needs no user request (the exception in
   [PACKAGES.md](PACKAGES.md)); stage only the package files you changed, never force-push.
5. **Reimport it in this project**: set the pin in `Packages/manifest.json` to the pushed commit
   (`...mhzeUnityTools.git?path=/Assets/com.gigaduck.agentic-tools#<full sha>`), then install the new version:
   - with Unity: let the Package Manager resolve it; the editor installs the update by itself on the next load (unchanged copies
     are replaced, conflicts are reported in the Console), or open **Tools ▸ Agentic Tools** and press **Install / Update**;
   - without Unity (cloud): `python <mhzeUnityTools checkout>/Assets/com.gigaduck.agentic-tools/Install~/install.py --project .`
     (the installer from the version you just pushed).
6. **List the updated copies in your virtual commit** together with `Packages/manifest.json`, `Packages/packages-lock.json` (if
   it changed) and `.agentic-tools.json`.

A project-only rule (about this project's scenes, story, assets or conventions) is written in this project, outside the blocks: in
`AGENTS.md` around the blocks, or in its own part of a `{{RULES_DIR}}/` file below the block.

## The installer

- **Tools ▸ Agentic Tools** in Unity: choose the modules, set the rules and docs folders, see what an update would change
  (**Check**), **Install / Update**, replace conflicts on purpose (**Force**), or copy the skills to your user folders too.
- `python <package>/Install~/install.py --help` does the same without Unity (`--status`, `--enable`, `--disable`, `--set`, `--force`,
  `--dry-run`, `--personal-skills`, `--uninstall`).
- It never overrides a project's own rules: rules are injected as blocks between `<!-- agentic-tools:begin ID -->` and
  `<!-- agentic-tools:end ID -->`, everything outside a block stays as it is, and `.mcp.json` only gains missing keys.
