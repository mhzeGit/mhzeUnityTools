# Prompt Construction

Keep the prompt concise and separate these concerns:

1. Item identity, recognizable silhouette, materials, period and requested state. Ground unfamiliar forms in a real or user-supplied reference.
2. User-selected rendering treatment and the role of each image: style, shape or edit target.
3. One-design consistency across panels: proportions, parts, state and important marks. Use exact counts only when useful and supported.
4. Camera/layout instructions from [view-sheet-template.md](view-sheet-template.md).
5. Background, light, labels and appropriate detail. Add only relevant exclusions; do not blacklist the style or features the user requested.

Use supported tool inputs. Requested size is not saved size. Preserve the literal native generation/edit prompt, not just an expanded parent brief.

## Named defects

**Wrong identity:** stop polishing; inspect shape references and restore the defining features. Do not turn the item into an easier ornament.

**Wrong style:** compare to the user's choice. Preserve flatter cartoons when selected; add painted volume only when requested. Flatness is not automatically a failure.

**Wrong projection:** change the camera, not the object. Preserve correct panels in an edit and check that the pixels changed appropriately, not only the label.

**Drifting parts:** use a reliable geometry reference and show that same design from the remaining views. Simplify incidental clutter only when identity and user intent survive; do not keep adding arbitrary counts.

## Exploration and stopping

When comparison is requested, vary approaches while holding item, state and layout comparable. Inspect every real output; failed trials cannot be scored. For ordinary requests, do not impose three variants automatically.

After two unsuccessful corrections of the same issue, reassess the reference/approach or request a user reference. Stop when asked to wrap up. Respect safety blocks; do not disguise the same request or substitute a materially different item without agreement. Report quota/permission limits and follow [hosts.md](hosts.md).
