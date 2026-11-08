# Package rules: reusable code lives in the package repos

Read before changing or adding anything reusable. Entry point: [AGENTS.md]({{RULES_TO_ROOT}}/AGENTS.md).

## Packages: fix at the source, then update this project

This project consumes its reusable systems and tools as standalone Unity packages hosted in separate Git repositories.
**Never fix, extend or vendor a package inside this project**: not in `Library/PackageCache` (that folder is a regenerated
read-only cache), and not by copying package code into `Assets/`. Change the package where it is authored, publish that change,
then update this project's reference to it.

The package repos and where they are checked out are listed in this project's part of this file (below the shared part). Local
paths differ per machine: find a checkout with `git remote -v` in candidate folders instead of assuming a path. If a package is
not listed, read its `Packages/manifest.json` entry to find the owning repo and path. `com.unity.*` and other third-party
packages are not ours: report the problem, do not patch them locally.

Workflow for any package change:

1. Find the owning repo and package folder (from the list, or from the package id in `Packages/manifest.json`).
2. Edit only that package's folder in its source checkout. Keep the package standalone and self-contained; it must not depend on
   this project's `Assets/`.
3. Test the change in this project (compiles with 0 errors; run the relevant tests or play check).
4. Commit and push it in the source repo. **This is the one sanctioned exception to the virtual-commit rule (AGENTS.md, GIT.md): it
   applies only to separate package source repos, never to this project.** Stage only the package files you changed, use an
   imperative commit message matching that repo's `git log --oneline`, and never force-push. If a package's `version` in
   `package.json` is user-visible, bump it (semver) in the same change.
5. Update this project's reference in `Packages/manifest.json` when needed:
   - **branch dependency** (no `#<sha>` suffix): nothing to edit, but Unity must re-resolve.
   - **pinned dependency** (`#<sha>`): replace the old hash with the pushed commit's full SHA from `git rev-parse HEAD` in the
     source repo.
   - **`file:` dependency**: already points at the local checkout; pushing keeps that path valid for everyone else.
6. Let Unity re-resolve so the new package content is actually used (Package Manager refresh, or remove the package's entry from
   `Packages/packages-lock.json` plus its folder under `Library/PackageCache`, then refresh) and confirm the project still compiles.
7. If you cannot push (no credentials, wrong remote or branch, unrelated dirty files), stop and report it with the affected package
   and source path. Never work around it by editing the cached package, pinning a local `file:` path to a version that has not
   been pushed, or copying code into `Assets/`.

Rules:

- One package per change set; do not reformat, re-version or upgrade unrelated packages along the way.
- Keep the package's public API backward compatible where practical; call out any breaking change in the commit description.
- Never edit another project's or another agent's uncommitted files in a source checkout: verify `git status` before staging, and
  list only your own files.
- Documentation for a package belongs in that package's `README.md` in its source repo, not in this project.

### Reusable system code is authored in the package, never only in `Assets/`

The rules above are not only about patching code that already lives in a package. **New or improved behaviour for a system that
has a package is written in that package, in its source repo, from the start.** A script added under this project's `Assets/`
"for now" ends up stranded in the project and has to be migrated out later.

Where does new code go? Ask two questions:

1. Does it extend or integrate a system that has a package (a feature of that system, a component that only makes sense with it,
   a node or condition about that system)?
2. Is it free of this project's content: no references to this project's scenes, characters, dialog, story or other glue types?

- **Yes to both: the package.** Never write it in `Assets/`.
- **Project-specific glue: this project.** Glue talks to the package only through its public API and contracts; it never reaches
  into package internals or re-implements them.
- **Partly generic: split it.** The generic part goes in the package behind a small contract; the project keeps a thin caller.
  Several consumers of the same idea must share one package feature, never one script each.

Prototyping in this project to see something work is fine, but then you **migrate it in the same task, before you register your
virtual commit**:

1. **Move the code** to the matching package folder in its source repo. Use the package's namespace and keep the class and file
   names. A dependency on another package goes in a separate optional asmdef (`defineConstraints` + `versionDefines`), never a
   hard dependency. Use `"expression": ""` for "any installed version"; `"[0.0.0,)"` is rejected by Unity and silently leaves the
   assembly uncompiled.
2. **Preserve the script GUID.** Copy the `.cs.meta` `guid:` into the package `.meta`, otherwise every prefab, scene and asset using
   the script loses it. New package folders and asmdefs also need `.meta` files with unique GUIDs, or Unity ignores them.
3. **Fix references serialized by name.** `[SerializeReference]` types: put `[MovedFrom(true, "<old namespace>", "Assembly-CSharp", null)]`
   on the moved class and update the `type:` lines in the assets that hold it. Assets that store full type names as text (state
   machine graphs and the like) need those names rewritten.
4. **Compile in the source project** before you push (`Unity.exe -batchmode -nographics -quit -projectPath <source repo>`, with a
   deadline); fix every error. Packages not installed there are skipped by their define constraints, so also confirm in this
   project after the update.
5. **Commit and push in the package repo** (the sanctioned exception above), bump `package.json`.
6. **Update this project**: delete the local copies and their `.meta`, move the pin (manifest `#sha`; for an unpinned package edit
   its `hash` in `Packages/packages-lock.json`), re-resolve, compile with 0 errors, run the relevant validation and a play check
   ([`TESTING.md`](TESTING.md)).
7. **Say so in your report and virtual-commit description.** Other agents' pending records may still contain the deleted files and
   would re-create them on finalize; name those records.

The same class must never exist in both places. If you find reusable code in `Assets/` while doing other work, migrate it (or
report it); do not extend it in place.
