---
name: unity-gameplay-workflow
description: Design, implement, or polish playable Unity activities and scene content through spatial inspection, player decisions, event-based visual checks, and runtime validation. Use for gameplay and level work; exclude MCP setup and code-only maintenance with no player-facing behavior change.
---

# Unity gameplay workflow

Develop a playable experience whose cues, actions, consequences, and recovery work in the actual
level. Use the phases relevant to the request: a design request does not authorize implementation;
a small polish fix does not need a new design document or an exhaustive scene survey.

This is an agent-neutral procedure. Follow the user's current intent and repository instructions
first. Discover project APIs and tools rather than assuming the example project's names or paths.
Read [visual-and-runtime-verification.md](references/visual-and-runtime-verification.md) before
spatial rendering, runtime filming, or validating a substantial activity. MCP connection setup is
outside this skill.

## Establish the intended experience

Read the relevant design, gameplay, story, and authoring instructions before proposing mechanics.
Distinguish the user's established fiction and choices from an agent's previous suggestions.
Check implemented content when a document's build status may be stale.

Identify the player's main action, what they know, the decision they make, the feedback they get,
and what should make this moment memorable. For a new activity, briefly define:

- The normal activity and how it expresses this game's fiction.
- The cue the player can perceive, the inference it supports, and the available response.
- The competing demands on attention, movement, timing, or resources.
- The consequence, recovery, and payoff, including a naive player's likely behavior.
- How its main verb or emotional rhythm differs from neighboring activities.

Turn exposition into useful information where appropriate: a remark about an ingredient, a
character's habit, or a warning can teach an action the player later uses. In a trap game, let the
danger arise from the activity and the fiction. Keep these choices specific to the game; surprise,
deception, horror, and multiple endings are not requirements for every activity.

State concrete acceptance conditions before implementing. Include presentation conditions such as
“the player sees the object leave the shelf and reach the pot,” alongside state conditions such as
“the content changes exactly once.” A successful API call alone does not satisfy either condition.

## Inspect the space and available material

Check the loaded scene, edit/play state, compilation, unsaved changes, relevant hierarchy, and
existing story sequence before editing. Inspect the area involved and its connections to preceding
and following gameplay. Search names to locate candidates, then confirm with transforms, bounds,
components, and images.

When a layout is unfamiliar, obtain an overview and separate relevant floors with temporary
top-down renders. Use clipping or diagnostic visibility controls to expose the intended floor;
restore every temporary change. Narrow the view to the room or route that matters. Record floor
heights, doors, obstacles, actor positions, and likely player sightlines. A room name, a clipped
image, or a reflection-probe volume is a clue to corroborate, not proof of its contents.

Inspect existing props, animation clips, sockets, input prompts, interaction range, movement,
death/respawn, and checkpoint APIs before inventing replacements. Measure asset bounds and pivots;
imported props may have surprising scale or orientation. Preview candidate poses in isolation if
needed, and verify the preview actually changes pose. Repeated identical frames are an inconclusive
probe, even if sampling returned success.

Check reachability for player and NPC routes separately. NavMesh success does not prove the player
controller fits, that doors open, or that dynamic props will let the route work. Relate pacing to
measured travel and turn times: an attention window must suit the action the player can perform.

## Build the smallest complete playable loop

Integrate through the project's existing public contracts for movement, look, interaction,
pickup, prompts, dialog, accidents, and story. Follow package ownership rules for reusable system
behavior. Keep game-specific orchestration in the game and reusable capabilities in their owning
package. Avoid adding another controller or a second look system to solve an integration issue.

Keep editable content visible in the hierarchy and Inspector: actor spots, timings, references,
dialog, effects, and replaceable visual children. Group an activity's new content coherently, and
use local coordinates where the set should move together. Use clearly named temporary models to
test the real loop when final art is unavailable. A placeholder must still have usable size,
visibility, collisions, and interaction feedback.

Preserve hand-authored scene and prefab content. Prefer explicit additive authoring or direct
scoped edits. Never regenerate an existing hand-authored asset, reopen a dirty shared scene to
discard changes, or install automatic content-changing hooks. A repeated authoring operation
should refuse duplicates or add only verified missing objects under the project's rules.

Define state ownership and interruption behavior. Track object identity separately from its
current location when swapping or carrying objects. Remember an item's original home even after
pickup reparents it. Implement cancellation, retry, skip, and rewind where the surrounding story
requires them. With reloads disabled, inspect caches, statics, subscriptions, coroutines, and
collected child lists across repeated runs and script reloads.

Compile and confirm editor readiness before running the slice. Preserve a meaningful baseline of
existing warnings; do not equate a temporarily empty console with successful compilation.

## Make the experience observable

For temporal or branching behavior, add only the diagnostics needed to distinguish failures.
Reuse existing editor helpers first. A useful transition log includes game time, phase, actor
activity, attention/visibility, relevant item identity and contents, active dialog, and outcome.
Log changes and test actions so screenshots can be matched to actual events.

Keep diagnostics explicitly started, editor-only where appropriate, and separate from player
content. Build a reusable helper only when repeated scenarios justify it. Remove callbacks,
temporary cameras, textures, and test overrides when a run ends or is interrupted.

## Verify logic, presentation, and player access

Test the coherent loop and the meaningful branches: intended success, plausible failure or
refusal, recovery, and repeated entry. For a substantial story activity, exercise the real
checkpoint/death/skip path rather than directly setting its final state. Include a strategy that
could defeat the design, such as never looking away, acting early, or repeating an interaction.
Choose cases that threaten the actual mechanic rather than accumulating arbitrary tests.

Use public gameplay APIs for fast branch validation, but also check the actual player interaction
path for the changed action: approach, aim, reachable target, visible prompt, input/hold, action,
feedback. Directly calling `OnInteract` can bypass range, raycasts, bindings, and hold duration.
Test player travel with the project's supported movement tools or actual controls.

For visible changes, inspect the actual player view. Check whether cues, props, prompts, and
consequences can be seen from plausible positions while moving or holding an item. Then use a
separate diagnostic view when it explains animation or staging. A diagnostic camera must not
change the attention or visibility that drives the gameplay, and its picture must not be blocked
by test artifacts such as the first-person arm.

For motion, capture a short sequence around the relevant event: anticipation, action/contact,
reaction, and recovery. Trigger on the real state transition and schedule by game time, not by a
long shell sleep. Choose sampling density for the event; a fast impact needs denser frames than a
conversation. Read the saved images. Do not count an unviewed screenshot as a visual check.

Judge causality and readability: is the object in the hand, does the stream reach its destination,
does the body face its travel, does the head turn plausibly, do feet stay grounded, and does the
visible reaction agree with the period in which an action is allowed? Use numeric probes for
ambiguities a picture cannot settle. Return to the player view after changing staging.

Run time-critical polish checks at normal game speed. Accelerated branch testing is useful but
does not establish reaction fairness, dialogue rhythm, or animation quality. Observe audio where
the mechanic depends on it; checking that a clip is assigned does not establish audibility.

## Correct the cause and close the loop

Classify a failure before patching: game behavior, presentation, input/navigation, test fixture,
or capture setup. Fix an invalid camera or stale result first. For a game failure, compare the
observed transition with the acceptance condition and change the responsible layer. Do not make
assertions easier just to obtain a green report.

Review timing priorities as well as durations. A queue of harmless reactions can starve the main
mechanic. Batch or interrupt work only when it preserves the intended behavior. Check that the
state allowing an action and the visible cue for that opportunity agree.

After a correction, repeat the affected scenario. Re-run the relevant branch checks when control
flow, contents, reset behavior, or ownership changed. Passing an earlier snapshot does not validate
later edits. Finish when scoped acceptance conditions and appropriate project checks pass;
otherwise identify the concrete remaining limitation.

Restore test state and leave the editor in the agreed state. In BlahBlahFamilyV6 agents never enter
Play Mode in the editor: test once, at the end, in your own standalone player (`Docs/rules/TESTING.md`,
`Docs/AGENT_PLAY.md`) and stop it afterwards. Protect persistent saves and their in-memory caches from test deaths.

Document how the user can adjust and test the result. Report the checks performed, the player-view
moments inspected, known placeholders, and any untested conditions. Separate state verification,
visual inspection, and subjective design intent. Register the project's required handoff record
with exactly the files changed. This skill does not authorize publishing or Git actions beyond
the user's and repository's existing permissions.
