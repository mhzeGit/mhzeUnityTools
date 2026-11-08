#!/usr/bin/env python
"""Run environment-scene operations through the in-memory C# ops (cs/EnvOps*.cs) next to whatever other agents have open in the shared editor.

  python bo.py status                                                     editor state: Play Mode? compiling? which scenes are loaded / dirty
  python bo.py tree [depth]                                               hierarchy of the environment scene
  python bo.py shots <name> "<px py pz tx ty tz fov [w h] [pp]>" ...      screenshots -> .utmp/<slug>/shots/<name>_<n>.png (room coordinates)
  python bo.py op <OpName> [key=value ...]                                any public static op of EnvOps2..EnvOps6 (see references/06 and 07)

The scene is loaded ADDITIVELY, made active only while the op runs and closed again; Play Mode aborts every op except `status`.
Every op receives an OpCmd {op, scene, list, text, json, f0, f1, f2, i0, b0}; `key=value` pairs fill those fields.
`pp` at the end of a shot spec switches post processing on. Shots hide the player rig by default (hide=FPC_Character); pass hide=a,b,c* to hide others.
"""
import json
import os
import re
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, ".."))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import ucs  # noqa: E402

CS = os.path.join(HERE, "cs")
ENV_UNITY = os.path.join(envcfg.ENV_DIR, "unity")          # per-environment data: colliders.json, materials.json


def build_source():
    """ucs.py compiles one file: merge cs/EnvOps*.cs (usings first, once) into .utmp/<slug>/unity/_EnvAll.cs."""
    names = ["EnvOps.cs", "EnvOps2.cs", "EnvOps3.cs", "EnvOps4.cs", "EnvOps5.cs", "EnvOps6.cs"]
    usings, bodies = [], []
    for n in names:
        path = os.path.join(CS, n)
        if not os.path.exists(path):
            continue
        for line in open(path, encoding="utf-8").read().splitlines():
            if line.startswith("using "):
                if line not in usings:
                    usings.append(line)
            else:
                bodies.append(line)
        bodies.append("")
    out_dir = os.path.join(envcfg.WORK, "unity")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.join(out_dir, "_EnvAll.cs")
    open(out, "w", encoding="utf-8").write("\n".join(usings + [""] + bodies))
    return out


SRC = build_source()


def call(cmd):
    if cmd.get("op") == "PrefabColliders" and not cmd.get("json"):
        cmd["json"] = os.path.join(ENV_UNITY, "colliders.json").replace("\\", "/")
    return ucs.run(SRC, "ClaudeTmp.EnvOps.Run", json.dumps(cmd))


def retry(fn, tries=8, wait=8):
    """The editor is shared: it may be busy (Play Mode, compiling, importing). Retry while the answer says so."""
    out = None
    for _ in range(tries):
        out = fn()
        if "FAILED" not in str(out) and "ABORT" not in str(out):
            return out
        time.sleep(wait)
    return out


def shots(name, specs, hide="FPC_Character", profile=""):
    lines, outs = [], []
    os.makedirs(envcfg.SHOTS, exist_ok=True)
    for n, spec in enumerate(specs):
        v = spec.split()
        pp = "0"
        if v and v[-1] == "pp":
            pp = "1"
            v = v[:-1]
        nums = v[:7]
        w, h = (v[7], v[8]) if len(v) >= 9 else ("1672", "941")
        png = os.path.join(envcfg.SHOTS, f"{name}_{n:02d}.png").replace("\\", "/")
        outs.append(png)
        lines.append("|".join([png] + nums + [w, h, pp, hide]))
    lst = os.path.join(envcfg.WORK, "shots_list.txt")
    open(lst, "w").write("\n".join(lines))
    print(call({"op": "shots", "list": lst, "text": ("profile=" + profile) if profile else ""}))
    return outs


def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        return
    if a[0] == "status":
        print(call({"op": "status"}))
    elif a[0] == "tree":
        print(call({"op": "tree", "i0": int(a[1]) if len(a) > 1 else 2}))
    elif a[0] == "shots":
        hide = "FPC_Character"
        specs = []
        for x in a[2:]:
            if x.startswith("hide="):
                hide = x[5:]
            else:
                specs.append(x)
        shots(a[1], specs, hide)
    elif a[0] == "op":
        cmd = {"op": a[1]}
        for kv in a[2:]:
            k, v = kv.split("=", 1)
            cmd[k] = float(v) if k in ("f0", "f1", "f2") else (int(v) if k == "i0" else (v.lower() == "true" if k == "b0" else v))
        print(call(cmd))
    else:
        print(__doc__)


if __name__ == "__main__":
    main()
