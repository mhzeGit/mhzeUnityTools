"""Shared configuration for every script in an environment's tools/ folder (stdlib only, so Blender's Python can import it too).

Layout (created by scripts/new_environment.py):
    <library>/02_GameAssets/Environments/<Name>/Source/Production/tools/envcfg.py
    <library>/02_GameAssets/Environments/<Name>/Source/Production/env.json
    <library>/02_GameAssets/Environments/<Name>/Source/Work/<slug>/
The Unity root comes from env.json project_root, ENV_ROOT, or ancestor discovery.
env.json work_root points to the external intermediate files, renders, logs and Graphmat projects.

Machine tools are discovered, never hard-coded. Order: environment variable, env.json "tools", well-known install folders, PATH.
    BLENDER_EXE   GRAPHMAT_CLI   CODEX_EXE
"""
import glob
import json
import os
import re
import shutil
import subprocess
from pathlib import Path

HERE = os.path.dirname(os.path.abspath(__file__))
ENV_DIR = os.path.abspath(os.path.join(HERE, ".."))
NAME = os.path.basename(ENV_DIR)


def _find_root():
    cfg_path = os.path.join(ENV_DIR, "env.json")
    if os.path.isfile(cfg_path):
        configured = json.load(open(cfg_path, encoding="utf-8")).get("project_root")
        if configured and os.path.isdir(os.path.join(configured, "ProjectSettings")):
            return os.path.abspath(configured)
    p = ENV_DIR
    while True:
        if os.path.isdir(os.path.join(p, "ProjectSettings")) or os.path.isdir(os.path.join(p, ".git")):
            return p
        parent = os.path.dirname(p)
        if parent == p:
            raise RuntimeError("envcfg: no ProjectSettings/ or .git above " + ENV_DIR + " (set ENV_ROOT)")
        p = parent


ROOT = os.path.abspath(os.environ.get("ENV_ROOT") or _find_root())


def _load_cfg():
    path = os.path.join(ENV_DIR, "env.json")
    cfg = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else {}
    cfg.setdefault("name", NAME)
    cfg.setdefault("slug", re.sub(r"(?<!^)(?=[A-Z])", "-", NAME).lower())
    cfg.setdefault("prefix", "ENV_")
    cfg.setdefault("texture_budget", 16)
    cfg.setdefault("tools", {})
    u = cfg.setdefault("unity", {})
    u.setdefault("base", "Assets")
    u.setdefault("env_folder", "Environments/" + cfg["name"])
    u.setdefault("materials_folder", "Materials/" + cfg["name"])
    u.setdefault("mcp_url", "http://127.0.0.1:8080/mcp")
    return cfg


CFG = _load_cfg()
SLUG = CFG["slug"]
PREFIX = CFG["prefix"]

# ---- folders ----------------------------------------------------------------------------------------------------------------------
WORK = os.path.abspath(CFG.get("work_root") or os.path.join(ENV_DIR, "..", "Work", SLUG))
if Path(WORK).is_relative_to(Path(ROOT)):
    raise RuntimeError("Authoring work must be outside Unity; set env.json work_root")
ASSETS_DIR = os.path.join(ENV_DIR, "assets")                # one python module per asset family
GROUPS_DIR = os.path.join(ENV_DIR, "groups")                # texture group specs written by gm_groups.py
REF_DIR = os.path.join(ENV_DIR, "reference")                # concept art + crops
LAYOUT_DIR = os.path.join(ENV_DIR, "layout")
TEXT_DIR = os.path.join(ENV_DIR, "textures")                # committed texture bookkeeping (materials.json, selection.json, logs)
VERIF_DIR = os.path.join(ENV_DIR, "Verification")
BLEND_DIR = os.path.join(ENV_DIR, "Models", "blend")
TEX_WORK = os.path.join(WORK, "tex")                        # raw / cands / <material>_2k.png
GMAT_WORK = os.path.join(WORK, "gmat")                      # one folder per texture group
FBX_UV = os.path.join(WORK, "fbx_uv")                       # textured <Asset>_uv.fbx written by gm_run.py import
SHOTS = os.path.join(WORK, "shots")                         # Unity captures


def work(*parts):
    p = os.path.join(WORK, *parts)
    os.makedirs(p if not os.path.splitext(p)[1] else os.path.dirname(p), exist_ok=True)
    return p


def asset_modules():
    """Names of the asset-family modules (assets/*.py without __init__ and private files)."""
    return sorted(os.path.basename(p)[:-3] for p in glob.glob(os.path.join(ASSETS_DIR, "*.py")) if not os.path.basename(p).startswith("_"))


# ---- Unity asset paths (derived from env.json) -----------------------------------------------------------------------------------
def unity_paths():
    u = CFG["unity"]
    base, env = u["base"], u["env_folder"]
    root = f"{base}/Content/{env}"
    d = {
        "base": base,
        "models": f"{root}/Models",
        "textures": f"{root}/Textures",
        "collision": f"{root}/Collision",
        "prefabs": f"{root}/Prefabs",
        "materials": f"{base}/{u['materials_folder']}",
        "scene_dir": f"{base}/Scenes/{env}",
        "scene": u.get("scene") or f"{base}/Scenes/{env}/{CFG['name']}.unity",
        "blockout_scene": f"{base}/Scenes/{env}/{CFG['name']}_Blockout.unity",
        "base_profile": u.get("base_profile", ""),
        "room_profile": u.get("room_profile", f"{base}/GlobalVolumes/{CFG['name']}GlobalVolumeProfile.asset"),
        "player_source_scene": u.get("player_source_scene", ""),
        "outline_base": u.get("outline_base", ""),
        "outline_asset": u.get("outline_asset", f"{base}/{u['materials_folder']}/{PREFIX}Outline.asset"),
        "renderer_asset": u.get("renderer_asset", ""),
        "env_asset_dir": root,
        "mcp_url": u["mcp_url"],
    }
    return d


def unity_tokens():
    """@@TOKEN@@ -> value for the C# sources in unity/cs (filled by ucs.py just before the in-editor compile)."""
    p = unity_paths()
    return {
        "@@SCENE@@": p["scene"], "@@BLOCKOUT_SCENE@@": p["blockout_scene"], "@@PREFAB_DIR@@": p["prefabs"], "@@MODELS_DIR@@": p["models"],
        "@@TEXTURES_DIR@@": p["textures"], "@@COLLISION_DIR@@": p["collision"], "@@ENV_ASSET_DIR@@": p["env_asset_dir"], "@@MATERIAL_DIR@@": p["materials"],
        "@@PREFIX@@": PREFIX, "@@NAME@@": CFG["name"], "@@BASE_PROFILE@@": p["base_profile"], "@@ROOM_PROFILE@@": p["room_profile"],
        "@@PLAYER_SOURCE_SCENE@@": p["player_source_scene"], "@@OUTLINE_BASE@@": p["outline_base"], "@@OUTLINE_ASSET@@": p["outline_asset"],
        "@@RENDERER_ASSET@@": p["renderer_asset"],
    }


# ---- machine tools ----------------------------------------------------------------------------------------------------------------
def _exists(p):
    return bool(p) and os.path.exists(p)


def _first(*cands):
    for c in cands:
        if _exists(c):
            return c
    return None


def blender():
    """Path of blender(.exe). Blender 4.2+ works; the kit was built and verified on 5.2."""
    p = os.environ.get("BLENDER_EXE") or CFG["tools"].get("blender")
    if _exists(p):
        return p
    pats = []
    for pf in (os.environ.get("ProgramFiles"), os.environ.get("ProgramFiles(x86)"), "C:/Program Files", "C:/Program Files (x86)"):
        if pf:
            pats += [pf + "/Blender Foundation/Blender */blender.exe", pf + "/Steam/steamapps/common/Blender/blender.exe"]
    pats += ["/Applications/Blender.app/Contents/MacOS/Blender", "/usr/bin/blender", "/snap/bin/blender"]
    found = []
    for pat in pats:
        found += glob.glob(pat)
    found = sorted(set(found), reverse=True)
    if found:
        return found[0]
    w = shutil.which("blender")
    if w:
        return w
    raise SystemExit("Blender not found. Install it or set BLENDER_EXE / env.json tools.blender")


def graphmat_cli():
    """Path of graphmat-cli(.exe), preferring a build whose author command knows bakeAo/applyAo (see references/graphmat-cli-extensions.patch)."""
    p = os.environ.get("GRAPHMAT_CLI") or CFG["tools"].get("graphmat_cli")
    if _exists(p):
        return p
    exe = "graphmat-cli.exe" if os.name == "nt" else "graphmat-cli"
    roots = [os.environ.get("GRAPHMAT_HOME"), os.path.expanduser("~/Projects/Softwares/Graphmat"), os.path.expanduser("~/Graphmat"), "C:/Graphmat"]
    found = []
    for r in roots:
        if r and os.path.isdir(r):
            found += glob.glob(os.path.join(r, "tools", "GraphMat.Cli", "bin", "**", exe), recursive=True)
    found = sorted(set(found), key=os.path.getmtime, reverse=True)
    for f in found:                                    # newest build that has the AO operations
        if graphmat_has_ao(f):
            return f
    if found:
        return found[0]
    w = shutil.which("graphmat-cli")
    if w:
        return w
    raise SystemExit("graphmat-cli not found. Build tools/GraphMat.Cli or set GRAPHMAT_CLI / env.json tools.graphmat_cli")


def graphmat_has_ao(cli):
    try:
        out = subprocess.run([cli, "help", "author"], capture_output=True, text=True, timeout=60).stdout
        return "bakeAo" in out and "applyAo" in out
    except Exception:
        return False


def codex():
    """Path of the Codex CLI (image generation through its built-in image tool)."""
    p = os.environ.get("CODEX_EXE") or CFG["tools"].get("codex")
    if _exists(p):
        return p
    p = _first(os.path.expandvars(r"%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe"), os.path.expanduser("~/.local/bin/codex"), "/usr/local/bin/codex")
    if p:
        return p
    w = shutil.which("codex")
    if w:
        return w
    raise SystemExit("Codex CLI not found. Set CODEX_EXE / env.json tools.codex, or generate the textures with another reference-capable image tool")


def tool_report():
    """One line per machine tool: found path or MISSING (used by selftest.py)."""
    rep = {}
    for name, fn in (("blender", blender), ("graphmat_cli", graphmat_cli), ("codex", codex)):
        try:
            rep[name] = fn()
        except SystemExit as e:
            rep[name] = "MISSING: " + str(e)
    return rep


if __name__ == "__main__":
    print("ROOT    ", ROOT)
    print("ENV_DIR ", ENV_DIR)
    print("WORK    ", WORK)
    print("NAME/SLUG/PREFIX", NAME, SLUG, PREFIX)
    for k, v in tool_report().items():
        print(f"{k:14s}", v)
    print("graphmat AO ops:", graphmat_has_ao(graphmat_cli()) if not tool_report()["graphmat_cli"].startswith("MISSING") else "n/a")
