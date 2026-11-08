#!/usr/bin/env python
"""Scaffold the working folder of a new environment (phase 0 of the skill).

python new_environment.py --name WineCellar [options]

  --name        CamelCase environment name; becomes the folder <art-dir>/<Name>, the scene name and the Unity asset folder names
  --slug        kebab-case name of the external Source/Work/<slug> area         (default: derived from --name)
  --prefix      prefix of prefabs and materials in Unity, e.g. WC_              (default: initials of the name + "_")
  --repo-root   Unity project root (default: the nearest parent of the current folder with ProjectSettings/ or .git)
  --art-dir     optional external art parent; never inside the Unity project
  --resource-root external resource library override (default: shared resource resolver)
  --unity-base  Unity asset root of the game, e.g. Assets/_MyGame                (default: Assets)
  --player-scene   scene that contains the first-person player rig to copy (for EnvOps4.Player)   [optional]
  --base-profile   existing global Volume profile asset the room profile starts from              [optional]
  --outline-base   existing outline settings asset (project specific renderer feature)             [optional]
  --renderer       renderer asset that owns the outline feature, e.g. Assets/Settings/PC_Renderer.asset   [optional]
  --force       overwrite an existing environment folder's tools/ (never touches assets/, layout/, textures/, env.json)

Creates   <art-dir>/<Name>/{env.json, README.md, PROGRESS.md, reference/, assets/example_kit.py, layout/, textures/materials.json, unity/{colliders,materials}.json,
          tools/ (all python tools, envcfg.py, texture_groups.py, tools/unity/ with the C# ops)}
Then run  python <art-dir>/<Name>/tools/envcfg.py   (tool discovery)   and   python <skill>/scripts/selftest.py   (proves the toolchain).
"""
import argparse
import json
import os
import re
import shutil
import sys
from pathlib import Path

SKILL = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
SCRIPTS = os.path.join(SKILL, "scripts")
TEMPLATES = os.path.join(SKILL, "templates")


def find_root(start):
    p = os.path.abspath(start)
    while True:
        if os.path.isdir(os.path.join(p, "ProjectSettings")) or os.path.isdir(os.path.join(p, ".git")):
            return p
        parent = os.path.dirname(p)
        if parent == p:
            raise SystemExit("no ProjectSettings/ or .git above " + start + " (use --repo-root)")
        p = parent


def copy_tree(src, dst, skip=("__pycache__",)):
    n = 0
    for dirpath, dirnames, filenames in os.walk(src):
        dirnames[:] = [d for d in dirnames if d not in skip]
        rel = os.path.relpath(dirpath, src)
        os.makedirs(os.path.join(dst, rel), exist_ok=True)
        for f in filenames:
            if f.endswith(".pyc"):
                continue
            shutil.copy2(os.path.join(dirpath, f), os.path.join(dst, rel, f))
            n += 1
    return n


def fill(text, vals):
    for k, v in vals.items():
        text = text.replace("@" + k + "@", v)
    return text


def put_if_missing(src, dst, vals=None):
    if os.path.exists(dst):
        return False
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    text = open(src, encoding="utf-8").read()
    open(dst, "w", encoding="utf-8").write(fill(text, vals) if vals else text)
    return True


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--name", required=True)
    ap.add_argument("--slug")
    ap.add_argument("--prefix")
    ap.add_argument("--repo-root")
    ap.add_argument("--art-dir", help="Explicit external art parent (never inside the Unity project)")
    ap.add_argument("--resource-root", help="External project resource library override")
    ap.add_argument("--unity-base", default="Assets")
    ap.add_argument("--player-scene", default="")
    ap.add_argument("--base-profile", default="")
    ap.add_argument("--outline-base", default="")
    ap.add_argument("--renderer", default="")
    ap.add_argument("--force", action="store_true")
    a = ap.parse_args()

    if not re.fullmatch(r"[A-Z][A-Za-z0-9]*", a.name):
        raise SystemExit("--name must be CamelCase letters/digits, e.g. WineCellar")
    slug = a.slug or re.sub(r"(?<!^)(?=[A-Z])", "-", a.name).lower()
    prefix = a.prefix or ("".join(c for c in a.name if c.isupper())[:3] + "_")
    root = os.path.abspath(a.repo_root) if a.repo_root else find_root(os.getcwd())
    # The project's resource library helper (tools/resources/resource_library.py): found from the project this skill is
    # installed in (.claude/skills, .agents/skills, ...), else from --repo-root.
    candidates = [Path(find_root(SKILL)) / "tools" / "resources", Path(root) / "tools" / "resources", Path(SKILL).parents[1] / "resources"]
    library = next((c for c in candidates if (c / "resource_library.py").is_file()), None)
    if library is None:
        raise SystemExit("tools/resources/resource_library.py not found in this project; pass --art-dir for the art sources")
    sys.path.insert(0, str(library))
    import resource_library
    env = os.path.abspath(os.path.join(a.art_dir, a.name)) if a.art_dir else str(resource_library.art(a.name, a.resource_root))
    if Path(env).is_relative_to(Path(root)):
        raise SystemExit("Art sources must be outside the Unity project; use --resource-root or an external --art-dir")
    work = str(Path(env).parent / "Work" / slug)
    if os.path.exists(os.path.join(env, "env.json")) and not a.force:
        raise SystemExit(f"{env} already exists (use --force to refresh tools/ only)")

    vals = {"NAME": a.name, "SLUG": slug, "PREFIX": prefix, "UNITY_BASE": a.unity_base}
    os.makedirs(env, exist_ok=True)
    n_tools = copy_tree(os.path.join(SCRIPTS, "tools"), os.path.join(env, "tools"))
    n_unity = copy_tree(os.path.join(SCRIPTS, "unity"), os.path.join(env, "tools", "unity"))
    created = []
    for src, dst in (("env.json", "env.json"), ("README.md", "README.md"), ("PROGRESS.md", "PROGRESS.md"), ("asset_module.py", "assets/example_kit.py"),
                     ("materials.json", "textures/materials.json"), ("layout_blockout.py", "layout/layout_blockout.py"), ("layout_room.py", "layout/layout_room.py"),
                     ("texture_groups.py", "tools/texture_groups.py"), ("unity/colliders.json", "unity/colliders.json"), ("unity/materials.json", "unity/materials.json")):
        if put_if_missing(os.path.join(TEMPLATES, src), os.path.join(env, dst), vals):
            created.append(dst)
    # env.json: fill in the optional Unity paths given on the command line
    cfg_path = os.path.join(env, "env.json")
    cfg = json.load(open(cfg_path, encoding="utf-8"))
    cfg["project_root"] = root
    cfg["work_root"] = work
    u = cfg["unity"]
    for key, val in (("player_source_scene", a.player_scene), ("base_profile", a.base_profile), ("outline_base", a.outline_base), ("renderer_asset", a.renderer)):
        if val:
            u[key] = val
    json.dump(cfg, open(cfg_path, "w", encoding="utf-8"), indent=2)
    for d in ("reference", "reference/crops", "groups", "Verification"):
        os.makedirs(os.path.join(env, d), exist_ok=True)
    os.makedirs(work, exist_ok=True)

    print(f"environment  : {env}")
    print(f"slug / prefix: {slug} / {prefix}     work area: {work}")
    print(f"tools copied : {n_tools} python + {n_unity} unity files; created from templates: {', '.join(created) if created else 'nothing (kept existing files)'}")
    print("next         : 1) put the concept images in " + os.path.join(env, "reference") + " (and set env.json references)")
    print("               2) python " + os.path.join(env, "tools", "envcfg.py") + "   (tool discovery)      3) python " + os.path.join(SCRIPTS, "selftest.py"))
    print("               4) follow SKILL.md phase 1 onwards")


if __name__ == "__main__":
    main()
