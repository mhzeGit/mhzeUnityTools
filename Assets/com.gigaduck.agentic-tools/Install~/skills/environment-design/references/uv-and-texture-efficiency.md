# UV, texture-space and material efficiency

Read before surface grouping/unwrapping and final export. Minimize wasted texture space and duplicated data while preserving material ownership, useful detail and stable filtering. Evaluate the delivered UVs/maps and their imported runtime settings, not just source images or an auto-unwrap success message.

## Choose the representation first

- Broad repeated surfaces such as brick walls, paving, plaster and timber use shared tileable textures where supported. Keep their supporting faces simple; brick courses and mortar lines belong in the texture. Avoid baking the same pattern into a unique atlas for every wall module when a shared surface can meet the brief.
- Use trim sheets for repeated bands and edges, and atlases for compatible unique regions/small props. Give unique maps to actual unique artwork or hero features. Shared source images alone do not save runtime memory if every asset receives another full baked copy.
- Group by required texel density, shader/rendering compatibility and sensible loading/reuse. Neither one atlas per object nor one giant atlas for the whole scene is a default. Different-sized assets may share a map when density and packing work; objects from the same family may need separate groups when they do not.
- Record a compact map plan: surface/region IDs, consumers, tile/trim/unique strategy, intended density and repeat scale, output dimensions, material/shader settings, padding and permitted reuse. Inspect one finished section before allocating maps for the whole kit.

## Unwrap and pack deliberately

1. Establish texel density from the actual player camera, closest view and target output resolution. Allocate more area to readable focal detail, less to low-priority backsides. Keep density consistent across contiguous modules and equal-priority surfaces; do not silently stretch tiny pieces to fill gaps or shrink focal surfaces to make everything fit.
2. Use seams and island shapes that pack efficiently without visible distortion. Preserve hard-edge needs and directional grain, brick courses, lettering and trim orientation. Use checker/density previews and inspect corners and grazing angles. Many tiny slivers waste padding and can lose their material detail at lower mips; revise seams/grouping when warranted.
3. Pack islands compactly with rotation only where the surface allows it. Avoid large empty regions, unnecessary margins and oversized atlases containing only a few small parts. Adjust compatible group membership, island layout or output resolution rather than filling space with duplicate or invisible geometry.
4. Retain island gutters and edge dilation sufficient for the intended mip levels, compression and filtering. Padding is necessary space, not a defect to eliminate. An example `padding=8` setting is not evidence that seams remain safe in the final import. Check mip bleed at realistic distances and on both sides of boundaries between contrasting materials.
5. Stack or mirror UVs only for genuinely identical reusable appearance. Do not overlap unique lettering, asymmetric wear, incompatible materials or surfaces whose baked AO differs. Shared repeated UVs may be appropriate for a tileable base-color channel; separately baked data may need unique coordinates. Lightmap UVs have their own non-overlap and padding requirements.
6. Verify the final export retains the intended UV channel, transforms, island layout, material slots and mesh-region mapping. Never scale UVs blindly after baking: repack/rebake affected data, refresh diagnostic masks and reimport the delivered assets together.

## Preserve physical scale after placement

Never stretch a baked pattern by non-uniformly scaling its scene instance. Keep one measured physical repeat size per shared surface family across wide/narrow walls, landings/corridors, pipes and differently sized props. Use transform-aware tiling, world-metre projection, or properly sized/rebaked geometry as appropriate. Object scale must not change block proportions, grain width or wear size. Inspect differently scaled instances together with a fixed-metre checker and again with the final paint, including curved surfaces and grazing angles. Verify the actual Unity placement transforms; an unstretched source mesh or atlas alone is insufficient.

## Preserve material ownership

Every region must sample its intended texels and use the correct material settings after packing. Group compatible colors/surfaces into one atlas when that reduces cost without losing required density. Avoid creating a new material merely because a painted surface changes color.

Keep incompatible shader needs, transparency modes, UV sampling/wrap behavior and channel import settings separate where required. A normal map, sRGB base color and linear masks cannot be treated as interchangeable data. Individual atlas regions need correct mapping and filtering; tiling outside one region must not accidentally sample its neighbors. Preserve material boundaries and needed normals at mesh seams. An atlas does not automatically reduce draw calls if separate material slots/renderers remain.

Inspect diagnostic region colors, a UV-over-texture image and final material views. Reject missing/unassigned regions, unintended overlaps, colors on the wrong part, leakage across island edges and material duplication without a reason. When assigning an atlas to several slots, verify why each slot is still necessary and that every intended consumer shares the same map asset rather than another copied texture.

## Measure useful space and cost

For unique atlases, report covered texels using the union of rasterized UV faces at the actual output resolution, or clearly label a more approximate tool measurement. Summed triangle areas can double-count overlaps; island bounding boxes overstate useful coverage. Separate useful face coverage, required gutters and genuinely empty space. Do not count temporary anchor triangles or dummy faces as useful content. For a repeating surface, judge effective repeated detail and data reuse rather than forcing a unique-atlas occupancy metric onto it.

There is no universal occupancy percentage that guarantees a good map. Inspect the preview for avoidable holes and record why meaningful free space remains. Compare plausible alternatives: denser packing, another compatible group, a smaller map or a shared tile/trim strategy. Choose the lowest practical total texture cost that preserves the visual target; do not squeeze out gutters or distort islands to improve a percentage.

Record output dimensions, active density range/intentional exceptions, coverage method/results, padding, intentional overlaps, material-slot count, number of unique runtime maps and imported memory when available. Include all required channels and mipmaps in cost comparisons; identify estimates and their compression assumptions. Inspect lower mips and the closest/farthest relevant player views after an optimization. Do not call an atlas optimized based only on an attractive full-resolution layout.

## Graphmat implementation check

The bundled 2048-atlas pipeline enables thorough packing, but it does not measure useful coverage, prove adequate density or automatically choose an efficient material grouping. `gm_check.py` checks region ownership, not packing efficiency. Its cube/staircase layout and anchor triangles support projection math; the 3D cube is not a UV-utilization measurement and anchors must not consume meaningful delivered texture area.

Inspect the exported UV mesh and albedo for each group before repeating the recipe. If the current CLI or texturing skill requires a fixed output size, improve grouping/reuse within that constraint; do not silently change its export contract. If shared tile/trim sampling or a needed packing control is unsupported, resolve that capability limitation before multiplying inefficient unique maps and disclose what remains unverified.

## Gate evidence

- Actual exported UVs over delivered maps, plus checker/density and diagnostic material views.
- Coverage result or labeled estimate, empty-space assessment, density range, safe padding and any justified overlaps.
- Correct region/material mapping, retained UVs after Unity import, and no visible stretch or mip bleed at relevant player views.
- Unique map/material counts, dimensions and measured/estimated texture cost; before/after comparison for a material optimization.
- Named remaining inefficiencies/tool limitations; successful unwrapping alone does not pass.
