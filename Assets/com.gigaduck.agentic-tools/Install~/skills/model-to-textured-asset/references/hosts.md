# Host routing

The same SKILL.md works in each host. Never load or invoke Computer Use. MCP requires an explicit human instruction for the current task; configured connectors alone do not authorize it. Inspect saved files with native image/file readers, not desktop automation.

- **Codex:** use the installed `imagegen` skill and native image tool. Inspect local reference images first. Copy returned images into the asset job; do not assume a destination argument. Use API/model fallback only when authorized.
- **Claude Code:** use an available native/API/CLI image generator that accepts reference images. Claude text output alone cannot generate a bitmap. If none exists, prepare prompts and report the missing generator; do not substitute procedural images or claim a completed texture gate.
- **Kilo Code:** use an available native/API/CLI reference-capable image generator. Inspect capabilities first. Do not assume the chat model supplies image generation or silently use an MCP generator.
- **Antigravity:** use native reference-based image generation when available. If delegated to an image tool/agent, await the actual output, preserve the real prompt and inspect the saved image.

Use Graphmat standalone background CLI in every host. No MCP adapter is supplied. Missing CLI capabilities are blockers, not permission to use the GUI or another texturing application. Do not install another provider, change credentials or bypass restrictions to complete a gate.

Skill discovery locations (copy the whole folder, including references/scripts):

| Host | Project | Personal |
| --- | --- | --- |
| Codex | `.agents/skills/` | `~/.codex/skills/` |
| Claude Code | `.claude/skills/` | `~/.claude/skills/` |
| Kilo Code | `.kilo/skills/` | `~/.kilo/skills/` |
| Antigravity | `.agents/skills/` (legacy `.agent/skills/` supported) | `~/.gemini/config/skills/` |

Keep installed copies synchronized when editing. A running host may require a new chat to discover a newly installed skill. Host-specific UI metadata in `agents/openai.yaml` is optional for other agents.
