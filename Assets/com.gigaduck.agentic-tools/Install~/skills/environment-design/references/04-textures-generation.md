# Phase 6 - generate the flat base-colour textures (hand painted, simple, stylized)

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

One **full flat base-colour image per distinct material**, generated from the concept (the generator must SEE crops of the concept), reused by every region with that surface.
Appearance only: no lighting, no objects, no perspective. AO is added later by Graphmat. Budget: use the current agreed cap and the texturing skill's applicable limits. Sixteen images was the boiler-room allowance; count all calls, including retries, against the actual cap.

## 6.1 Prepare `textures/materials.json`
```
{"style": "<one sentence in the painting style of THIS concept>", "common": "<the no-nonsense rules>", "materials": {"<name>": {"refs": ["crop.png", ...], "text": "<what this surface is>"}}}
```
* **style** - look at the concept first and describe its painting language: the boiler room's was *"Hand-painted stylized game-art surface in the exact painting style of the supplied environment
  concept: broad posterized color patches, angular brush shapes, restrained wear, sparse bold detail, no photorealism."* Adapt it (soft gouache? thick outlines? flat cel colours?). For this stylized recipe avoid incompatible photorealistic prompts. For another brief, follow its actual style and permitted material channels.
* **common** - keep: *the WHOLE square is filled by the flat surface, viewed perfectly straight-on; no object, silhouette, outline, border, frame, perspective, vignette, gradient from lighting, cast shadow,
  specular highlight, text, labels, characters; even flat albedo only; seamless tiling (left/right and top/bottom edges match); generate exactly one square image and save nothing else.*
* **per material text**: "ONE seamless square base-color texture of the <colour/material> of the <where> in the concept: <palette words>, broad irregular painted patches, <wear>, <what NOT to include (no rivets, no seams, no plates, no gauge ...)>."
  Brick = a SINGLE brick face (no mortar lines, no courses); stone/mortar = uniform stone surface (no blocks, no joints); flagstone = one continuous slab (no joints); wood = grain direction stated ("grain runs vertically"); plaster = no bricks, no timber.
* **refs**: 1-2 crops per material from `reference/crops/` (the crop that shows the surface best, plus a plan crop when the colour is clearer there). Real example: `examples/boiler-room/materials.json` (11 materials).
Typical material set for an indoor stylized room: teal iron, red paint, brick, mortar/pale stone, flagstone, dark oak, pine planks, plaster, black iron, brass/cream, amber glass (11).

## 6.2 Generate (ONE AT A TIME)
```
python tools/gen_textures.py --list                      # materials, refs, budget used / left
python tools/gen_textures.py --prompt teal_iron          # the exact prompt (also usable with another image tool)
python tools/gen_textures.py teal_iron                   # generate -> .utmp/<slug>/tex/raw/teal_iron_gen.png (+ log entry)
python tools/gen_textures.py red_paint brick mortar_stone ...   # several, strictly sequential in ONE process
```
How it works (so you can fix it when it breaks): `codex exec --skip-git-repo-check -s read-only -C .utmp/<slug>/tex/codex -i <crop> [-i <crop2>] -` with the prompt on **stdin** (`-i` is variadic and would
swallow a positional prompt); Codex writes the picture to `~/.codex/generated_images/<session>/`; the script copies the newest new file to `tex/raw/<name>_gen[_n].png` and appends
`{material, prompt, references, source, size, seconds}` to `textures/generation-log.json`. Typical output is 1254 x 1254 px. Each call took 42-93 s (average 58 s) in the case study.
**Never run two generator processes at once** (a lock file refuses it; the first build ran two and produced 21 images against a budget of 16). The guard stops before exceeding `env.json`'s configured cap. Use it only when the CLI generator is available and selected; otherwise use the session generator and keep the same attempt log.
Failed calls (no image) still count as used. If a generator is unavailable: stop here, hand the prompts to the user, report.

## 6.3 Gate every picture (texture gate of `model-to-textured-asset`)
For each material build a comparison and OPEN it: concept crop | candidate | candidate tiled 2x2. Check and write down: hue/value range (mean RGB close to the crop's lit colour), brush shapes and contrast,
wear amount, motif/grain scale (will it read at the tile size?), flat coverage (no vignette, no object), no baked shadows. Numbers: compare `mean RGB` of crop vs candidate; edge mismatch left/right
and top/bottom vs the typical neighbour-pixel difference (generated images are NOT seamless: edge mismatch 2-5 grey levels vs 1-3 inside).
Reject by named mismatch ("too pale", "has a border", "rust too heavy") and regenerate only inside the budget. Record the pick and the reason in `textures/selection.json` (see the example: every pick has a `why`).

## 6.4 Finalize: seamless + 2048
`textures/selection.json`: `{"materials": {"<material>": {"candidate": "<file stem in tex/raw or tex/cands>", "why": "..."}}}`
```
python tools/finalize_textures.py        # cross-fade with a half-offset copy (band 0.34 of half-size), Lanczos resample to 2048, NO colour grading
```
Outputs `.utmp/<slug>/tex/<material>_2k.png`, `tex/_tiles/<material>_before.png/_after.png` (2x2 tiling previews: OPEN them), `textures/finalize-report.json` (edge metrics before/after). Gate: after-metrics at the level of interior neighbour differences, no visible repeat seam in `_after.png`.

## 6.5 Optional masonry tile assembly (brick patterns stay in the texture)
Either generate a complete seamless masonry surface or, when independent brick/mortar control helps, generate a single brick face and mortar and assemble a tile with the helper:
```
python tools/make_brick_tile.py [--brick <candidate>] [--mortar <candidate>]     # -> tex/brick_wall_2k.png + _tiles/brick_wall_after.png + textures/brick-wall-tile.json
```
Layout 4 bricks x 8 courses per 0.96 m tile, running bond (odd courses shifted half a brick), brick pitch 0.24 x 0.12 m, joint ~10 mm, rounded bricks with a dark painted rim, light top edge and shaded
underside, random crops/flips/tints of the brick texture, soot specks, mortar luminance from the stone texture. A 2.4 m panel holds exactly 10 bricks, a 4.8 m wall exactly 40 courses. No new generation needed (the case study built it from the two approved textures).
Register it in `texture_groups.py` as `brick_wall` (region `Brick`, tile 0.96). Use the cylinder projection for round masonry (plinths) so courses wrap the curve.

## GATE 6
- [ ] `generation-log.json` count <= budget (increase the cap only with authorization; report any accidental overrun honestly, but disclosure does not make it a passed gate)
- [ ] every picked texture opened beside its crop; `selection.json` has a reason per pick; `finalize-report.json` + 2x2 previews inspected
- [ ] `<material>_2k.png` exists for every entry of `TEXTURE_FILE` in `texture_groups.py`
