# Procedural animation rules: every one gets an editable curve profile

Read before writing or changing any motion that code computes (a fist or arm reaching, a camera move, a door swing, a UI slide, a bob, a recoil, a head turn). Entry point: [AGENTS.md]({{RULES_TO_ROOT}}/AGENTS.md).

## The rule

**Procedural motion is never hard-coded.** Its shape and timing live in an editable **curve profile**, a `ScriptableObject` holding an `AnimationCurve` and a duration, so the user can reshape it in the inspector and you can read the exact numbers back. Do not leave `k * k`, `Mathf.SmoothStep`, `Mathf.Lerp(..., t / 0.3f)`, `Mathf.Sin` bobs, hold timers or `internal const float ...Fraction` timings as the only definition of how something moves. Describing timing in words ("faster, snappier, hold a beat") does not work for the user; a curve they can drag does.

Applies to: motion whose path or timing you compute each frame or in a coroutine. Does not apply to: Animator clips and FBX animation (those are authored in a DCC tool), physics, or one-off waits that are not animation.

## The profile contract

1. **A `ScriptableObject` named `<Thing>Profile`**, with `[CreateAssetMenu(menuName = "<Project>/...")]`, next to the code that owns the motion. The asset lives in a `Resources/` folder as `<Thing>Profile.asset` and is loaded with `Resources.Load` (or is assigned in the inspector where the owner already has serialized references). The project part below names this project's menu root and Resources folder.
2. **One curve plus a `duration` in seconds.** The curve's x axis is normalized time 0 to 1 across the duration; its y axis is normalized progress with a stated meaning ("0 = stationary at rest, 1 = fully stretched into the surface"). Put that meaning in the field tooltip and the class summary. Values below 0 may be allowed for anticipation (a wind-up); say what happens above 1.
3. **A whole motion is one curve**, including its hold and return, unless its phases are genuinely independent. The user must be able to see the entire motion in one graph.
4. **Events come from the curve, never from a second set of constants.** Contact, release or footstep moments are derived (for example the first time the curve reaches 1) or are named normalized fields on the profile. A coroutine must not keep its own copy of the timing.
5. **The default equals the current behaviour.** When you convert existing hard-coded motion, build the default curve with explicit `Keyframe` tangents that reproduce it exactly (Hermite: ease-in `(x / L)^2` from 0 to 1 is keys `(0, 0, in 0, out 0)` and `(L, 1, in 2 / L, out 0)`; a smoothstep is two keys with zero tangents), and check it by sampling the curve against the old formula before replacing it. For new motion, the default is your best first shape. Provide a static `DefaultCurve()` and a fallback instance (`CreateInstance`, `HideFlags.HideAndDontSave`) so the motion works when the asset is missing.
6. **Read the profile when each motion starts**, not once in `Awake` and not every frame. Edits then apply from the next run (also in Play Mode), and a running motion keeps the values it started with.
7. **Authored clips follow the curve, not the other way round.** If an imported clip accompanies the motion, set its playback speed so its key frames land on the curve's events and its last frame on the end. Never retime the curve to the clip.
8. **Stay inside the gameplay rules.** The profile changes how a motion looks, never when a step of the game completes; skip, rewind and interruption still work as before.
9. **Document it** in the system's doc: the asset path, what the axes mean, what fires the events, and that the default equals the old behaviour.

## Creating the asset

Create the `.asset` through Unity (the create menu, or an editor script run from the menu) so the importer writes the `.meta`. If the editor is busy or in Play Mode and you must write the files by hand, use the script's own `.meta` GUID in `m_Script`, give the asset a fresh GUID in its own `.meta`, and compile-check afterwards. Serialized curve keys use `tangentMode: 0` for free tangents; keep the slopes you set.

**Never overwrite a profile asset that already exists.** The user tunes it. Check it (`git diff`, modified time) before touching it; change the code and the default curve, not their numbers. Only create the asset when it is missing.

## Letting the user shape it

Do not ask the user to describe timing or feel in text. Give them the profile asset to edit in the inspector (they can edit it during Play Mode and try again), or, when they ask for something interactive in the chat, a page with the curve editor that saves what they confirm. Apply nothing to the project until they say so. After they edit, read the asset (`duration`, and each `m_Curve` key's `time`, `value`, `inSlope`, `outSlope`) and work from those exact numbers.

## Checklist

- [ ] No hard-coded easing or timing is left as the definition of the motion.
- [ ] A `<Thing>Profile` asset exists, with a meaningful y axis and a default equal to the old behaviour (sampled).
- [ ] Events derive from the curve; any accompanying clip is synced to them.
- [ ] The motion reads the profile at its start; no existing asset was overwritten.
- [ ] The system's doc names the asset and its axes.
