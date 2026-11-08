# Generator Routing

Use the requested provider. Inspect available capabilities rather than assuming them. Failure does not authorize switching providers, installing tools, collecting credentials or UI automation.

## Antigravity / Gemini

- Prefer native image generation. If it uses an image-generator subagent, wait for an actual image or error, not the controller's submitted message.
- For an installed CLI such as agy, inspect current help and models; use documented flags and an existing conversation when continuing. Headless shell permission may be denied even while native file viewing and image generation work. Do not disable broad permissions to bypass a denial.
- Generate sequentially. Respect quota/reset information and retry restrictions. A brief transient limit may justify one authorized retry after reset; report long capacity limits rather than creating an unrequested monitor or switching accounts/providers.
- Controller and image models can differ. Report the image model only when exposed; otherwise say unknown.
- Copy actual selected outputs into the workspace with their real format. Preserve the native prompt as well as a parent brief when they differ.
- Workspace skill: `.agent/skills/game-prop-concept-art/`.

## Kilo Code

Use a configured, available image generator. A Unity MCP image tool is usable only when present and configured; its name alone proves neither. If generation is unavailable, deliver the prepared prompt and intended path with a clear limitation. Do not call an SVG/procedural drawing an AI-generated image. Workspace skill: `.kilo/skills/game-prop-concept-art/`.

## Codex

Use the session's installed imagegen skill and built-in tool, following its reference/generation/edit rules. Do not assume the skill lives in this game repository. Inspect local edit targets, preserve invariants, save sibling versions and copy project-bound output from its returned path into the study. Use API/CLI fallback or another model only when explicitly chosen. Built-in generation does not require asking for an API key.

The installed personal skill lives in the user's Codex skills directory; repository copies are the portable source.

## ChatGPT without filesystem access

Use [portable-prompt.md](portable-prompt.md) with fields replaced. Supply reference images with roles explained. Do not assume ChatGPT can read local paths or skill folders.

## Handoff

Use `Docs/Art/<StudyName>/<item>-<view>-<round>.<actual extension>` plus a sibling prompt. Verify existence and dimensions. Report the provider, exposed image model or unknown status, paths and remaining defects. Follow repository virtual-commit rules. Provider changes require authorization, and a stop request ends generation.
