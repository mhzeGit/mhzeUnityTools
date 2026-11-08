# Phases 0 and 1 - orient, prove the tools, write the brief

> Implementation recipe from the original boiler-room pipeline. Follow `../SKILL.md` and [runbook.md](runbook.md) for current scope, stage order and safeguards. Example measurements/settings are illustrative; project rules and the actual brief take precedence.

## 0.1 Read before touching anything (in this order)
1. `AGENTS.md` / `CLAUDE.md` of the repository (rules for commits, shared editor, assets, packages). They override this skill.
2. The docs index (`Docs/INDEX/README.md` in this repo) -> only the area file you need. Find existing player rig, post-processing profiles, renderer asset, outline feature.
3. The sibling skills: `concept-art-to-3d` (geometry rules: reference fidelity, >= 1 revision pass, real FBX export + re-import, >= 3 views),
   `model-to-textured-asset` (generated flat textures, Graphmat CLI only, diagnostic colours gate), the parent skill (geometry vs paint and finished-section gate),
   `unity-gameplay-workflow` when the environment contains gameplay. This skill never replaces their gates; it tells you how they were satisfied.
4. Memory notes of earlier environment work, if the agent has any.

## 0.2 Find the concept and OPEN it
* The user attaches images to the prompt; if the agent cannot see them, search the repo for the concept (`Docs/Art/**`, names the user mentions). Copy them into
  `Docs/Art/<Name>/reference/` as `perspective.png` and `plan.png` (set `references` in env.json to the real file names).
* Typical set: one **perspective hero view** (heights, silhouettes, materials, light) and one **top-down plan** (footprint, placement). Look at both at full size, then at quadrants.
* If an image is missing and the plan/perspective is needed, ask for it; prepare tooling meanwhile.

## 0.3 Tool discovery (commands)
```
python Docs/Art/<Name>/tools/envcfg.py            # ROOT, WORK, Blender, Graphmat CLI (+AO ops?), Codex
python tools/skills/environment-design/scripts/selftest.py --unity
type ProjectSettings\ProjectVersion.txt           # Unity version (the kit was built on 6000.6.4f1, URP)
```
* **Blender**: 4.2+ works, built on 5.2. `bl.py` finds it (Program Files, Steam, PATH). Blender's Python has numpy but **no PIL**: PIL scripts run with system python.
* **Graphmat CLI**: needs the operations `author mesh-info render-model export-model extract` and the two extensions `bakeAo`/`applyAo` (+ `mode`). If
  `graphmat-cli help author` does not list `bakeAo`, first check the current CLI/API and package documentation. Only if an authorized source change is needed, review `references/graphmat-cli-extensions.patch` for compatibility in the Graphmat repo and rebuild
  (`dotnet build tools/GraphMat.Cli/GraphMat.Cli.csproj -c Release -o tools/GraphMat.Cli/bin/Release-ao/`) and set `tools.graphmat_cli` in env.json.
  This is a change in a **separate repository**: register its own virtual commit there (see 10). Never use the Graphmat GUI or MCP.
* **Image generator**: Codex CLI with the `image_generation` feature (`codex features list`). It reads reference images with `-i` and saves pictures under
  `~/.codex/generated_images/<session>/`. If no reference-capable generator exists, prepare prompts (`gen_textures.py --prompt <material>`), stop before phase 6 and report.
* **Unity**: editor with the Unity MCP package (`com.coplaydev.unity-mcp`) -> HTTP endpoint `http://127.0.0.1:8080/mcp`. If the session's own MCP connector says
  "failed", the direct HTTP route (`tools/unity/mcp.py`) still works as soon as the editor is open (editor start takes ~95 s). Start it with the project's run
  script or `Unity.exe -projectPath <repo>`; never start a second editor on the same project.
* **Python packages**: `pip install pillow numpy scipy` (scipy only for `fitcam.py`).

## 0.4 Check the state of the shared world
* `python Docs/Art/<Name>/tools/unity/bo.py status` -> Play Mode? compiling? which scenes are open and dirty?
* `git status --porcelain` and `git log --oneline -5`: other agents' files are in the same tree (never touch them; list only YOUR files later).
* Another agent in Play Mode or a dirty scene you do not own = wait (the ops abort and retry by themselves, see `bo.retry`).
* A human may be working live in the editor (the user tuned the post-processing profile while the agent kept capturing): check file times
  (`stat -c %y <file>`) before you overwrite any asset you created earlier; if it changed and you did not change it, it is the user's now.

## 0.5 Scaffold
```
python tools/skills/environment-design/scripts/new_environment.py --name <CamelName> --unity-base Assets/<GameRoot> \
   [--prefix XX_] [--player-scene Assets/.../HouseScene.unity] [--base-profile Assets/.../GlobalVolumeProfile.asset] \
   [--outline-base Assets/.../OutlineSettings.asset] [--renderer Assets/Settings/PC_Renderer.asset]
```
Creates `Docs/Art/<Name>/` with tools, templates, `env.json` (names, Unity paths, tool overrides), `README.md`, `PROGRESS.md`, folders. `.utmp/<slug>/` is the git-ignored
work area (several GB: Graphmat projects, renders, FBX). Make sure `.utmp` is in `.gitignore`.

## 1.1 The brief (write it into PROGRESS.md)
1. Record the actual user request and split it into numbered requirements. Do not substitute the boiler-room brief.
2. For every adjective write a **measurable assertion** and how you will prove it:
   | words | assertion | proof |
   | --- | --- | --- |
   | "looks exactly like the reference" | at the concept's view the scene matches shapes, palette, light, vibe | inspected side-by-side at a matched camera; statistics are diagnostic |
   | "fully 3D, walkable first person" | every reachable space fits the actual player controller and has geometry + collision | real controller traversal plus route and backside views |
   | "do not miss any 3D models" | each inventory line is modelled or consciously texture-only | inventory table fully ticked |
   | "modular, reuse" | pieces snap on round dimensions and are placed many times | asset count vs placements (e.g. 49 assets -> 235 placements) |
   | "high detail, not simple" | silhouette + bevel + thickness + rivets/chamfers where the concept has them | review sheets vs crops |
   | "hand painted simple stylized" | texture style matches the concept crop | each texture opened beside its crop |
   | "AO over the base colours" | AO multiplied into the albedo of every region | applyAo in every group job, final sheets show contact darkening |
   | "polished lighting + post" | colours and vibe equal the concept | grade numbers + side-by-side |
3. Decide nothing silently: write assumptions (scale, unseen surfaces, budgets) in "Decisions".

## 1.2 Resolve choices once
Infer generator, texture budget and AO from the actual request, current session and texturing skill. Ask only when a missing choice materially affects the outcome. Use an available reference-capable generator; Codex CLI is one optional adapter. Record the agreed or clearly stated provisional generation cap in `env.json`; 16 was the boiler-room budget, not an entitlement or universal requirement. Count retries and failures across all generators. Never infer approval to exceed a cap from silence.

When AO is requested, verify CLI bake/apply support and tune contact shading to the concept. If AO is omitted, adapt the bundled jobs that currently bake/apply it rather than adding it silently. Record scale, hardware and performance assumptions as well.

## GATE 0/1 checklist
- [ ] tool table in PROGRESS.md (paths + versions), `selftest.py` PASS (or each FAIL explained and fixed)
- [ ] editor state + `git status` baseline written down
- [ ] request, measurable assertions, answers, assumptions in PROGRESS.md
