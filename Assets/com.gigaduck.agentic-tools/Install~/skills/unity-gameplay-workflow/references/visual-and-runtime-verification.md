# Visual and runtime verification

Use the relevant sections when investigating a level, filming temporal gameplay, or validating an
activity. The quantities below are examples to adapt after inspecting the scene; they are not
fixed budgets or coordinates. This reference assumes Unity access already works.

## Keep the observation from changing the experiment

Record scene/edit/play state, dirty state, and the temporary settings you will change. In a shared
editor, check for recent human input before driving the player. Input-device timestamps are one
signal; corroborate with unexpected movement or the user's activity. If a person is playing, use
passive logging and a separate camera, and do independent documentation work while they finish.

Prefer a temporary disabled, untagged camera rendered explicitly to a texture. Do not replace
`Camera.main`, the player rig camera, or the camera used by gameplay visibility tests. Put helper
objects outside the activity's authoring hierarchy, mark them temporary/non-saving, and destroy
them after the run. `HideAndDontSave` does not by itself guarantee that a component has no runtime
effects; disable automatic rendering and avoid audio listeners or gameplay components.

A player's arm or held-item model may be rendered as ordinary world geometry. Exclude viewmodel
layers from a diagnostic camera if the project already separates them. Otherwise inspect a
scoped, reversible diagnostic option and restore it after the synchronous render. Never let
visibility changes alter the runtime test. Keep the unmodified player-view capture as the evidence
for what the player actually sees.

Use cleanup that also runs after cancellation or exceptions. Restore camera target textures,
`RenderTexture.active`, visibility/culling overrides, temporary poses, and any timing overrides;
release textures and unsubscribe update callbacks. Do not save a scene containing capture setup.

## Reconstruct a floor or room

1. Identify the relevant renderers and aggregate their bounds. Inspect vertical levels, room
   markers, doors, stair connections, and outlier geometry rather than framing the entire world
   blindly.
2. Position a temporary orthographic camera above the floor, looking down. Select its height and
   near/far depth range to exclude the roof or upper floor while including the floor's walls and
   useful props. These distances run from the camera along its view direction.
3. Fit the view to the useful bounds. For a landscape view with aspect ratio `a`, an initial
   half-height is `max(depth / 2, width / (2 * a))`, plus a margin. Confirm what the resulting image
   contains. Use a consistent orientation and describe which world directions correspond to its
   edges if orientation matters.
4. Render and inspect each relevant level separately, then capture a closer room view. Check that
   the chosen depth range did not also remove tall furniture or lower steps. A blank area in a
   clipped render is not evidence that a room or connection is absent.
5. Cross-check uncertain dimensions with directional raycasts and renderer/collider bounds.
   Cross-check traversable space with NavMesh samples and complete path status, preserving floor
   height in samples so nearby floors are not mistaken for the target floor.
6. Confirm routes with actual movement where needed. A baked path can still pass through a closed
   door, newly added obstacle, or a place the player controller cannot fit.

Use a rendering/culling alternative if depth clipping cannot isolate the floor cleanly. Prefer
existing reversible scene-visibility controls or an isolated preview. Never delete or save-disabled
roof/wall objects to obtain a floor plan.

## Check a screenshot before interpreting it

A successful capture proves that pixels were saved. Verify that:

- The requested scene, camera, floor, phase, and target appear.
- Camera position is not inside geometry and the target is within its frustum.
- Walls, lighting, and materials are present; clipping or a stale render has not removed them.
- Important labels, hands, props, and faces are not hidden by an obstacle or a diagnostic artifact.
- The file belongs to this run and is readable, with a matching state/timestamp where relevant.

The audited project sometimes returned a first render missing static geometry after reload.
If the picture is inconsistent with the hierarchy, allow the editor/render pipeline to settle and
render again; investigate culling, clipping, and pipeline configuration if the problem persists.
Do not move level geometry in response to an unvalidated picture. Do not turn a project-specific
“render twice” workaround into a guarantee for every Unity project.

For asset previews, compare at least two clips or times expected to differ. If all poses are idle,
check sampling target, clip bindings, Animator evaluation, and preview isolation. Render a clone
or preview scene when it avoids disturbing the live character. Restore any sampled transforms.

## Film the event instead of waiting for a picture

Use the actual phase, beat, collision, or animation event as the capture trigger. Record its game
time and the test action that caused it. Capture enough of the sequence to answer the question:

| Question | Useful evidence |
| --- | --- |
| Can the player notice and respond? | Player view before the cue, at cue onset, through the response deadline, and after the response |
| Does the movement look right? | Fixed diagnostic view across start, travel, turn, stop, and settling |
| Does a hand action make contact? | Hand/socket and prop before contact, at contact, and during recovery; world positions if ambiguous |
| Does a distraction allow the advertised action? | Visible reaction and logged attention/action availability throughout the window |
| Is the consequence understandable? | Action, changed world/object, actor reaction, outcome, and resulting story state |

For example, the cooking session captured 0.35, 1.7, and 4.6 seconds after turning to catch the
hostess, and filmed other events at roughly 0.25–0.5 second game-time intervals. These timings
suited seconds-long actions. They would miss a fast blade impact. Use a denser interval or
consecutive frames near short contacts. Use a rolling pre-event buffer, a preceding state trigger,
or a reproducible replay when you need frames before an unpredictable event.

Schedule captures from the editor/player update loop at game-time offsets. This supports an
unfocused editor and distinguishes slow editor ticking from long in-game delays. It does not
guarantee deterministic simulation: random seeds, physics, and event ordering may still differ.
If you change `Time.captureFramerate` or `timeScale`, record and restore it, and label the resulting
evidence. Normal-speed player-view observation is still needed for pacing and fairness.

Contact sheets should keep frame order obvious, with time/state labels or a matching timeline.
Inspect the saved image, including the recovery. A middle pose alone can hide sliding, popping,
snapping, duplicated effects, or a reaction that never ends. A film strip without sound cannot
verify an audio-only warning; inspect its playback through an available audio capture or live test.

Poll the run's state or result rather than blocking on a long arbitrary shell sleep. Use bounded
waits and report meaningful progress. Reject old result files: identify the run, clear or move its
previous output before starting, and confirm fresh completion, not merely file existence.

## Relate state to perceptible behavior

Log the small set of values that separates plausible causes. Useful examples are body yaw versus
velocity direction, head angle/target versus body rotation, hand socket versus prop position,
camera viewport/occlusion versus “seen,” and current side versus pot identity and contents.

Use both observation and state. A passing “caught” flag cannot prove that the player could see the
pour. An expressive screenshot cannot prove that poisoning stopped or that a swap was allowed.
An object tagged “held” may be behind the camera's near plane. Check camera-space position, item
bounds, socket transform, offsets, and clipping rather than randomly changing offsets.

For procedural character motion, inspect bone axes and pivot ownership before adding rotations.
Check at rest, peak, and recovery. Avoid rotating the whole model from its feet when the action
requires a bend at the waist. Use the existing presentation contract, or add a reusable capability
in its owning package under project rules. A one-scene procedural patch is not an exemption from
package ownership.

## Validate branches without overstating input coverage

Choose scenarios around player behavior. For an attention-based cooking activity, useful cases
were getting fooled, catching a pour, trying a watched swap, using a reaction window, eating,
dropping, refusing to look away, death/return, skip, and rewind. Another mechanic needs different
cases. Check observable outcomes and cross-system invariants, not every internal method.

Fast API-driven checks can cover the real gameplay components and story path, while still
bypassing player-facing constraints. Record those limits. At least one representative check of a
changed interaction should verify target acquisition, prompt, binding/hold, and outcome through
the actual input path. Check normal approach routes and timing rather than teleporting between
every action and calling that a travel test.

When a scenario fails, distinguish a behavioral failure from a bad fixture. A cumulative death
counter may need a per-scenario baseline; a timeout may expire before several queued lines finish;
an ingredient collection may lose items that pickup reparented. Inspect the timeline, preserve
the intended acceptance condition, fix the responsible cause, and repeat the affected scenario.

Test re-entry with the project's actual reload configuration. Include relevant repeated runs (re-enter the stage in the same player; in BlahBlahFamilyV6 agents never use editor Fast Play)
if cached hierarchy or static state is involved. Test recovery after objects
have been moved, consumed, hidden, reparented, or broken. If the story supports skip/rewind, verify
their resulting world state and that the activity can run again afterwards.

## Finish and report the verified snapshot

Before testing, preserve the current save state and runtime settings. Restore persistent data and
its in-memory caches as appropriate to the project; replacing save bytes alone may leave a static
cache dirty. Restore runtime overrides such as background ticking, speed, death-screen delays,
NPC commands, and forced views. Stop your own standalone player (`agentplay.py stop NAME`); agents never start editor Play Mode in BlahBlahFamilyV6,
so there are no Enter Play Mode settings to restore.

Report which branches passed, which player-view moments were inspected, whether timing/audio were
observed at normal speed, and which interactions were API-driven. Associate the result with the
version tested. If changes follow a green run, perform the relevant fresh checks before using that
green result as a completion claim. If interrupted, record what still needs verification and
cleanup without claiming it happened.
