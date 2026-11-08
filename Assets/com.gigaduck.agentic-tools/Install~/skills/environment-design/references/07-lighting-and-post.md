# Phase 10 - lighting and post-processing so the colours and vibe equal the concept

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

Order matters: **materials -> light -> grade**. Get the lit scene right WITHOUT post-processing first (pp off), then add the grade. Tune with numbers (`unity/stats.py`) and then finish by eye next to the concept.

## 10.1 Read the concept's light (write it down before touching a light)
Dominant light colour and direction, where the light sources are (lanterns, furnace window, skylight), how dark the shadows are and what colour they are (the boiler room: brown, with teal in the iron), ambient level, bloom/glow
around emissives, vignette, saturation (muted/dusty vs vivid), contrast. Take the hero-view statistics of the concept once: `python tools/unity/stats.py` prints mean RGB, mean luminance, percentiles (5/25/50/75/95/99) and saturation
(boiler room concept: mean RGB 49.5/27/14.9, luminance 30.9, percentiles 3/15/25/42/72/117, saturation 0.60).

## 10.2 The lighting rig (op `Lighting`, args in `tune.py`)
`python tools/unity/tune.py "<lighting args>" "<volume args>" <shot name> "0 1.65 -3.0 0 1.5 3.0 80 1672 941 pp"` applies the rig, the volume and a capture in one call (the capture spec is `px py pz tx ty tz fov w h [pp]`).
* **Ambient**: Trilight (`sky`, `equator`, `ground` colours). Very dark and slightly cool-neutral so shadows are brown-teal, not black: case study `sky=0.13,0.125,0.14; equator=0.10,0.095,0.105; ground=0.055,0.052,0.058`.
  Reflection intensity 0 (custom default reflection) so rooms do not mirror a skybox.
* **Fog**: exponential squared, density 0.008, colour (0.05, 0.045, 0.05): depth and a cheap haze that hides the far wall's detail.
* **Local lights live in the PREFABS** (so every instance has its own): wall lanterns = a Point light in the lantern prefab at the glass centre (case study intensity 1.7, range 5.5, colour 1,0.74,0.48, shadows OFF);
  the hero furnace = a Spot light in the boiler prefab just in front of the window (intensity 16, range 5, colour 1,0.62,0.30, angle 130, inner 60, pitch 50 down, soft shadows strength 0.9, position 0,0.75,-1.45 in prefab space) so
  the hearth bricks and floor glow but the door frame is only grazed. Args: `lanternPrefab/lanternPos/lantern/lanternRange/lanternColor/lanternShadows`, `furnacePrefab/furnacePos/furnace/furnaceRange/furnaceColor/furnacePitch/furnaceAngle/furnaceInner/furnaceShadows`.
* **Fill**: one Directional light "Fill_Bounce" without shadows (case study intensity 0.45, colour 1,0.78,0.58, euler 35,15,0): fakes the light the lit floor/walls throw back onto the props.
* The case study used realtime lights and `ForcePixel`; choose baked/mixed/realtime for the actual project and verify probes/lightmap UVs where needed; check the URP asset's additional-lights limit per object and shadow atlas size if you add many lights (`Audit` lists every light).
* **Emissive materials** (lantern glass, furnace window) are HDR emission with an orange emission colour: the glow comes from emission + bloom threshold >= 0.95, not from a bright albedo.
* Per-instance fixes: `bo.py op LightOverride "text=Name:range[,intensity]"` (a lantern in a tight vestibule that over-lights its pillar).
Tune order: ambient -> fill -> lanterns -> furnace -> fog -> emission multipliers -> material tints (`unity/materials.json`: iron tint (0.85,1.05,1.2) kept the teal alive under orange light; floor tint (0.56,0.55,0.58) stopped the flagstones blowing out).

## 10.3 The grade (volume profile) - and the traps that cost the case study hours
* `Volume` op = copy of the project's base profile into `<room_profile>` (if missing) + overrides + a global `PostProcess` Volume (priority 10, weight 1) in the scene. Keys:
  `bloomI bloomT bloomS bloomTint vig vigS exposure contrast sat hue filter temp tint shadowTone highlightTone balance tonemap(0 none, 1 neutral, 2 aces)`.
* **Capture path trap (the biggest one)**: URP render requests do NOT refresh the volume stack. A camera that is not part of the scene (or a hidden one) renders with whatever stack the last editor repaint left behind, so every
  profile edit appears to have no effect. The capture harness (`EnvOps.Capture`) therefore creates the capture camera INSIDE the scene and calls `VolumeManager.instance.Update(cam.transform, additionalCameraData.volumeLayerMask)` right before `SubmitRenderRequest`.
  If your own captures ignore grade changes: that is the cause. `bo.py op GradeProbe3` prints the blended stack (tonemapping mode, exposure, contrast, saturation, split toning...) and renders it for each profile: use it to see what the camera really gets.
* **Tonemapping**: a scene lit for linear output renders much darker with a filmic tonemapper (case study hero view: mean 8.6 with ACES + dark split-toning tints + exposure 0.2, against 41.4 with no post at all). Either light for the tonemapper or choose None/Neutral and put the exposure in `postExposure`. ACES also boosts orange saturation.
* **Split Toning trap**: the shadow/highlight tints are applied as *soft light* over gamma-space colour, weighted by luminance + balance. A dark tint colour such as (0.1, 0.16, 0.2) is not "a bit of cyan": it darkens all shadows heavily even at balance 0.
  Keep tints close to 0.5 grey with a small chroma (e.g. shadows (0.40, 0.51, 0.50), highlights (0.48, 0.52, 0.50)).
* **Bloom**: threshold >= 1.0-1.3, intensity 0.4-0.8, scatter 0.5, so only emissives glow; a low threshold turns walls into a hazy salmon blob. Vignette 0.3, smoothness 0.45.
* **Hue**: compare patches, not only means (`env.json grade_patches`: wall/floor/brick/iron/ceiling boxes in the concept vs the render; `grade_search.py` includes them in its loss): the house profile (colour filter 1,0.89,0.90 + blue shadows + balance 61) made brick crimson and walls pink while the concept's are orange-brown.
* **Automated starting point** (does not replace your eyes): `python tools/unity/grade_sweep.py <tag> "<view>" name="k=v;k=v" ...` renders candidate grades on a COPY of the room profile (nothing saved) and prints statistics; `TONE=1 python tools/unity/grade_search.py <rounds>` does a random local search against the concept statistics.
  Best result of the case study: Neutral tonemap, exposure ~0.8, contrast ~7, saturation ~-9, hue +5, bloom 0.5 at threshold 1.2, vignette 0.33, near-neutral split toning -> loss 2.26, looked far closer to the concept than the house profile.
* **The user owns the grade once they touch it.** After the first complete grade the user opened the scene and tuned `postExposure 1.3 / contrast -26.7 / saturation +2 / hue shift -2` on the room profile asset themselves and approved that look. Rules that follow: (1) before writing a volume
  profile or the scene again, compare the file's modification time with your last write and read its current values; (2) never overwrite values you did not set; (3) swapping the PostProcess profile for a capture is visible in the user's editor for a second: avoid it while they are in the scene
  (capture with the scene's own volume instead); (4) hand the grade over with the numbers and say it is theirs.

## 10.4 Verification of the look
* `python tools/unity/bo.py shots look "0 1.65 -3.0 0 1.5 3.0 80 1672 941 pp"` then `python tools/unity/sbs.py <shot> <out>` (concept left, render right) and `stats.py <shot>`. Targets: mean colour and percentiles within ~10 %, saturation within ~0.05,
  hue of the main patches within ~10 degrees, glow only where the concept glows, shadows brown/teal not black, no banding, no blown walls.
* Look from the concept's view AND from 3 other views (lights behave differently): the stats are a guide, the picture is the judge.
* Keep one neutral (pp off) capture per view in the verification set: lighting must not hide shape or texture defects.

## GATE 10
- [ ] side-by-side concept vs scene at the concept view opened and described; stats printed; patch hues compared
- [ ] other views checked for hot spots (over-lit pillars), black voids, flat areas
- [ ] profile/scene writes were checked against file times (user edits respected); final grade values written in PROGRESS.md
