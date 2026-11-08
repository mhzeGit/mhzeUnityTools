#!/usr/bin/env python
"""Textured assets into Unity (C# in cs/EnvTex.cs): albedo PNGs, URP/Lit materials, textured FBX models, prefab rebuild and material assignment.

  python tex.py all <group> [<group> ...]            textures + materials + models + prefab materials for those groups (the usual call)
  python tex.py tex|mats|models|prefabs|apply <group> ...    a single stage

Inputs   .utmp/<slug>/gmat/<group>/albedo.png        the Graphmat albedo of the group (AO already multiplied in)
         .utmp/<slug>/fbx_uv/<Asset>_uv.fbx          textured FBX written by `gm_run.py <group> import`
         <env>/unity/materials.json                  look-dev per group: tint, smooth, glow (see templates/unity/materials.json)
Outputs  <textures dir>/<prefix><group>_Albedo.png   <materials dir>/<prefix><group>.mat (+ _Glow.mat)   <models dir>/<Asset>.fbx   prefabs rebuilt
Prefabs keep their names and GUIDs' consumers: the scene instances update in place, no scene is regenerated.
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import ucs  # noqa: E402

SRC = os.path.join(HERE, "cs", "EnvTex.cs")
P = envcfg.unity_paths()
PFX = envcfg.PREFIX
LOOK = os.path.join(envcfg.ENV_DIR, "unity", "materials.json")
look = json.load(open(LOOK, encoding="utf-8")) if os.path.exists(LOOK) else {"glow_groups": [], "groups": {}}
GLOW_GROUPS = set(look.get("glow_groups", []))
MAT = look.get("groups", {})


def call(cmd):
    return ucs.run(SRC, "ClaudeTmp.EnvTex.Run", json.dumps(cmd))


def spec(group):
    return json.load(open(os.path.join(envcfg.GROUPS_DIR, group + ".json")))


def listfile(name, lines):
    d = os.path.join(envcfg.WORK, "unity")
    os.makedirs(d, exist_ok=True)
    path = os.path.join(d, name)
    open(path, "w").write("\n".join(lines))
    return path


def do(stage, groups):
    gm = envcfg.GMAT_WORK.replace("\\", "/")
    fbx = envcfg.FBX_UV.replace("\\", "/")
    if stage == "tex":
        lines = [f"{gm}/{g}/albedo.png|{P['textures']}/{PFX}{g}_Albedo.png" for g in groups]
        print(call({"cmd": "tex", "list": listfile("tex_list.txt", lines)}))
    elif stage == "mats":
        lines = []
        for g in groups:
            m = MAT.get(g, {})
            lines.append(f"{g}|{P['textures']}/{PFX}{g}_Albedo.png|{1 if g in GLOW_GROUPS else 0}|{m.get('tint', '')}|{m.get('smooth', '')}|{m.get('glow', '')}")
        print(call({"cmd": "mats", "list": listfile("mats_list.txt", lines), "matDir": P["materials"]}))
    elif stage == "models":
        lines = []
        for g in groups:
            for a in spec(g)["assets"]:
                lines.append(f"{fbx}/{a['name']}_uv.fbx|{P['models']}/{a['name']}.fbx")
        print(call({"cmd": "models", "list": listfile("models_list.txt", lines)}))
    elif stage == "prefabs":
        names = []
        for g in groups:
            names += [a["name"] for a in spec(g)["assets"]]
        print(call({"cmd": "prefabs", "list": listfile("prefabs_list.txt", names), "models": P["models"], "prefabs": P["prefabs"]}))
    elif stage == "apply":
        lines = []
        for g in groups:
            for a in spec(g)["assets"]:
                lines.append(f"{a['name']}|{g}")
        print(call({"cmd": "apply", "list": listfile("apply_list.txt", lines), "prefabs": P["prefabs"], "matDir": P["materials"]}))
    else:
        print(__doc__)


def main():
    if len(sys.argv) < 3:
        print(__doc__)
        return
    stage, groups = sys.argv[1], sys.argv[2:]
    if stage == "all":
        for st in ("tex", "mats", "models", "apply"):
            print("--", st)
            do(st, groups)
    else:
        do(stage, groups)


if __name__ == "__main__":
    main()
