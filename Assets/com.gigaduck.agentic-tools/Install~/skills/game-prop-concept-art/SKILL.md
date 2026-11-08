---
name: game-prop-concept-art
description: Generate and refine game-prop concept art and multi-view reference sheets in the project's painted stylized art style. Use for prop images, item designs and turnarounds; exclude photographs, UI, logos and unrelated illustration.
---

# Game Prop Concept Art

Create recognizable, on-style prop art using the user's chosen generator and rendering treatment. Default output is one landscape sheet with three-quarter, true side, and front or top views.

## Establish the brief

- Preserve the requested item, setting, materials, state and explicit preferences. A flatter cartoon style is valid; skill defaults never override user choices.
- Read [style-bible.md](references/style-bible.md) and inspect actual project or supplied images. Do not infer style from filenames alone.
- Separate style references from shape references. A generated example can establish painting without establishing correct geometry. If the object is unfamiliar or the user rejects its identity, inspect real or user-supplied references before generating again. Do not keep editing a rejected shape.
- Preserve identity-defining parts. Do not replace the item with an ornament or remove essential features merely to simplify consistency. Invent only incidental details, not an arbitrary checklist of dimensions, mechanisms or numeric counts.

## Generate and review

Use [view-sheet-template.md](references/view-sheet-template.md) and [prompt-rules.md](references/prompt-rules.md). Keep one design in its natural resting orientation and state; change cameras, not geometry. For a tool without skill access, use [portable-prompt.md](references/portable-prompt.md).

Follow the requested host's capabilities in [hosts.md](references/hosts.md). Generate sequentially. Do not use Computer Use when prohibited, or switch providers without authorization. Save the actual native image prompt and reference roles beside the result under `Docs/Art/<StudyName>/`. Distinguish a parent agent brief from a rewritten image-tool prompt, and preserve the real file format.

Open the saved image and use [review-checklist.md](references/review-checklist.md). Labels, tool completion and an agent's success claim do not prove correct projections. Verify actual dimensions and paths.

## Refinement and stopping

For an ordinary request, generate, inspect and correct named defects. When comparison is requested, vary prompt approaches with the same item and layout, use the user's feedback to select a direction, then refine it. Do not require three trials for every simple request.

After two unsuccessful corrections of the same issue, reassess the reference or approach instead of repeating blindly. Ask for a user reference when useful. A request to stop or wrap up ends generation.

Report capability, quota, permission and safety failures accurately. Respect tool safety decisions; do not disguise a rejected request or silently substitute a different object. Obtain agreement for an allowed alternative that materially changes the deliverable. A long quota limit does not authorize an automation, account change or provider switch.

Deliver the selected actual image, saved prompt and remaining defects. Do not call a sheet perfect or construction-ready when its views disagree. Mark rejected and unfinished trials clearly. Follow repository handoff rules, including virtual commits where required. Concept-art work does not authorize changes to game scenes or prefabs.
