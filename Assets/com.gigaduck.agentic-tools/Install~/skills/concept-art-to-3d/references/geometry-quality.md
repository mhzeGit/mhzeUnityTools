# Geometry quality and reference acceptance

Use these checks when choosing topology and at each visual review. An asset passes only when its defining features match the supplied art and it meets the technical needs of its intended use. A low triangle count or beautiful lighting cannot excuse a mismatched shape.

## Reference comparison

Compare the actual reference and model views side by side. Match the camera angle and orthographic/perspective treatment where possible; do not confuse perspective distortion with geometry. Use a neutral background and framing that exposes the entire object without clipping or hiding difficult features.

Check:

- Outer contour and proportions: overall width/height/depth, landmark positions, curvature, taper, thickness, and distinctive angles.
- Parts and negative spaces: correct count and arrangement of visible components, openings, spacing, attachment points, and intentional asymmetry.
- Three-dimensional consistency: side, front/top, and three-quarter views depict the same construction and state. A matching front silhouette with wrong depth fails.
- Geometric surface treatment: bevel scale, curvature and modeled detail density follow the reference. Inspect in neutral clay; colors and material response are outside this skill.

Treat a noticeable deviation in an identity-defining feature as a failure. Do not use an aggregate score to hide a failed criterion. For less consequential details, explain the remaining difference rather than claiming pixel-perfect fidelity. Hand-painted lighting in the reference is not automatically modeled relief or baked shadow.

## Efficient, intentional geometry

Allocate edges where they affect silhouette, curvature, readable form, deformation if required, or shading. Use fewer edges on flat/hidden surfaces only when the reduction preserves needed thickness, structure, and visibility from gameplay angles. Preserve real openings and substantial profile features as geometry; note small appearance-only detail for the later texturing handoff.

- Avoid accidental holes, duplicate/internal faces, zero-area geometry, unintended intersections, and flipped normals. Open boundaries and separate intersecting parts can be intentional; judge them against the asset's construction rather than welding everything indiscriminately.
- Inspect smoothing and edge treatment. No unintended faceting on smooth curves, pinched shading, or washed-out hard edges. Account for the effect of modifiers and final triangulation.
- Keep geometry ready for later unwrapping: stable topology and meaningful part/region names. Do not unwrap or bake here; preserve supplied UV data when editing an existing model.
- Reduce redundant loops, unnecessary subdivisions, and unseen detail deliberately. Keep the prior version and compare before/after renders at the same cameras. Reject reductions that visibly alter the reference match.
- Do not require an all-quad final game mesh or a single welded object when separate parts/triangles fit the design. Deforming assets need topology and rig checks specific to their intended movement; a static-prop checklist is insufficient.

Report measured geometry after relevant modifiers and triangulation, not just the low-count editing cage. State vertices, triangles, mesh count, and material slots. Distinguish authoring vertices from exported mesh vertices and engine vertex counts when available: UV seams, material boundaries, and normal splits can increase runtime vertices. Do not present an unavailable engine count as measured.

If a budget is supplied, meet it while preserving the defining shape. If these requirements conflict, explain the smallest demonstrated compromise and seek the user's choice; do not quietly exceed the budget or degrade the asset. Without a supplied budget, justify the final count against the object's complexity and viewing distance. Optional LODs should have a concrete gameplay need, not be automatic extra work.

## Review log

Keep a compact log alongside the source or delivery report:

| Version | Observed issue | Actual change | Views inspected | Result / remaining issue |
| --- | --- | --- | --- | --- |
| v0 | Reference comparison findings | Initial model | Actual inspected views | Baseline verdict |
| v1...v5 | Specific defect or justified polish | Geometry/normals/topology change | New actual views | Pass or next defect |

Each row is evidence of a real review, not a template to fill with invented success claims. Geometry optimization can satisfy a revision only when the asset changes and visual comparison confirms preserved quality.
