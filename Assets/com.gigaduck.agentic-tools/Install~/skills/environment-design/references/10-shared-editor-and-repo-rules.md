# Phase 12 and standing rules - shared editor, repository, deliverables, honest report

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

## A. Shared editor etiquette (the Unity editor is used by other agents AND by the user, live)
1. Before any editor op: `bo.py status`. Play Mode on = someone is testing: do not touch (ops abort by themselves). Compiling/importing = wait.
2. Open your scene **additively**, make it active only while an op runs, close it again; restore the previously active scene. Never swap, save or close a scene you did not open.
3. Preserve the editor baseline. Save only authorized changes to scenes you own; never save, discard or close someone else's unsaved work. Review capture harness save behavior before running against a user-open scene; a dirty flag does not prove content is unchanged.
4. **Do not regenerate or overwrite hand-authored assets** (scenes, prefabs, volume profiles, materials the user touched). Generators may only ADD objects or write NEW paths. Rebuilding a prefab in place from a new model is allowed only for prefabs you created and the user has not edited.
   Check `stat -c %y <file>` against your last write; read the current values before writing; if the user changed it, it is theirs.
5. No editor scripts in `Assets/` that act on their own (`[InitializeOnLoad]`, `delayCall`, post-processors, save hooks). Everything runs from the scratch C# compiled in memory.
6. The legacy `tools.py blockout/assemble` uses `NewScene(Single)`. In a shared editor adapt or replace that path with additive creation; preserve existing open scenes and write a new scene path only.
7. A human may take the controls in Play Mode: check `Mouse.current.lastUpdateTime` before steering the player; use your own capture camera for pictures.
8. Never start a second editor on the project; never kill the user's editor.

## B. Repository rules (read the project's AGENTS.md; for the Blah Blah Family repo these hold)
* **Never** `git add`, `commit`, `push`, `stash`, `reset`, `restore`, `checkout <commit>`, `merge`, `rebase`, `pull`, `clean`, `mv`, `rm`. Read-only git is fine. Never detach the shared worktree.
* At the end register a **virtual commit** with the project's tool, listing exactly the files YOU created/changed/deleted:
  ```
  pwsh -NoProfile -ExecutionPolicy Bypass -File tools/git/agent-commit.ps1 prepare -Agent "<your tool name>" -Task <slug> -Bump minor `
    -Title "Imperative title under 72 chars" -Description "what/why/how verified, budgets, caveats" -Files path1,path2,...
  ```
  `READY` = registered, `NOT READY` = fix what it lists and rerun. End the final message with `Virtual commit #<id> ready - <title>`.
  Changes in a SEPARATE repo (Graphmat) get their own record from that repo's own `tools/git/agent-commit.ps1`. Package repos (`mhzeModularSystems`, ...) are the one place where a normal commit+push is sanctioned (see AGENTS.md "Packages").
* Reusable system code (NPC, doors, interaction, pickup, dialog, first-person controller, state machine) belongs in its package, never in `Assets/`. Environment assets, textures, prefabs, scenes and the art tools in `Docs/Art/<Name>/` are game content and stay in this repo.
* **Index**: after adding/moving/removing scripts or assets run `python tools/index/generate_index.py` (and `--check` must report no problems). New scripts need a class-level `/// <summary>`. New/changed models and textures need look notes:
  `python tools/index/visual_notes.py prepare` -> give each printed job file to an **available lightweight vision model** -> `visual_notes.py merge` -> generate again. Never describe images yourself with the large model.
* **Gameplay modules**: if the environment hosts gameplay, follow `Docs/GAMEPLAY_MODULES.md` (module root, own Spots, no cross references) and run `Tools > BlahBlah Family > Story > Validate Gameplay Modules`.
* `.utmp/` (Graphmat projects, renders, FBX, textures; several GB) is git-ignored: never list it in a record. Commit only the durable sources: `Docs/Art/<Name>/**` (modules, layout, tools, groups, textures bookkeeping, README, PROGRESS, Verification images, `Models/blend/*.blend`),
  the Unity assets (`Content/Environments/<Name>/{Models,Textures,Collision,Prefabs,Materials}`, `Scenes/Environments/<Name>`, volume profile, outline asset), regenerated `Docs/INDEX/**`, and shared files you edited (say so in the description, e.g. the renderer asset).

## C. Deliverables checklist (phase 12)
- [ ] `python tools/export_deliverables.py --unity-shots .utmp/<slug>/shots/final` -> `Models/blend/<Asset>.blend` (49 in the case study), `Verification/clay_<module>.jpg`, `textured_<group>.jpg`, `unity_<name>.jpg`, `assets.json`
- [ ] `final` build of every module was re-run AFTER the last modelling change (the blends must match the Unity models; check timestamps against `bq.py`/module edits)
- [ ] `README.md` (what/where/how to rebuild), `PROGRESS.md` (decisions, numbers, disclosures), the skill's template sections filled
- [ ] stale assets removed (old materials/textures of replaced groups, clay materials, test models, empty folders) and the index regenerated
- [ ] scenes saved and CLEAN; no `__CaptureCam`, no leftover preview lights, no diagnostic fills
- [ ] virtual commit(s) registered; every listed file exists; nothing of anyone else's listed

## D. The final report (template; keep it factual, no hedging on verified items, no claims on unverified ones)
1. **Result**: scene path, prefabs, what it contains (counts: assets, placements, groups, materials, triangles, lights).
2. **How it was verified**: views opened (list), side-by-side with the concept, numbers (stats, audits), WalkTest legs, Play Mode run or not.
3. **Deviations and honest disclosures**: texture images generated vs budget (and why), procedural assembly of the brick tile, assumptions about unseen surfaces, concept inconsistencies and decisions, anything only measured, anything not checked, tools extended (Graphmat patch), shared files touched.
4. **User-owned items**: e.g. the volume profile the user tuned; what you did not touch.
5. Ends with the virtual-commit line required by the repository.
