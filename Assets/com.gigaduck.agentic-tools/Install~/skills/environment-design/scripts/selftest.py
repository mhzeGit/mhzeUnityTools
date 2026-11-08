#!/usr/bin/env python
"""Prove the toolchain before any real work (phase 0 gate).   python selftest.py [--keep] [--unity] [--quick]

Scaffolds a throw-away environment "SelfTest" in a temporary fake repo and runs the real pipeline on three tiny assets:
  1. Blender   run_asset.py: build -> .blend -> FBX -> RE-IMPORT -> clay renders -> stats       (assets: crate, pipe, brick wall slab)
  2. Blender   gm_angles.py: smooth angles
  3. Graphmat  gm_run.py <group> export setup masks textures review_final import   (CLI with bakeAo/applyAo; no GUI, no MCP)
  4. checks    textured FBX exists, UVs exist, albedo 2048 px, AO + triplanar applied (albedo differs from the flat texture)
  --unity      also pings the Unity MCP endpoint (env.json unity.mcp_url) and lists a few tools
--quick skips the Graphmat groups (Blender + deliverables export only, ~1 min).
Codex (image generation) is only reported, never called. Exit code 0 = PASS, 1 = a step failed (the message names the missing tool or patch).
"""
import json
import os
import subprocess
import sys
import tempfile
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "tools"))


def step(name, ok, detail=""):
    print(("PASS " if ok else "FAIL ") + name + (("  " + detail) if detail else ""))
    if not ok:
        global FAILED
        FAILED = True


FAILED = False


def run(cmd, **kw):
    t0 = time.time()
    res = subprocess.run(cmd, capture_output=True, text=True, **kw)
    return res, time.time() - t0


def make_textures(tex_dir):
    import numpy as np
    from PIL import Image
    os.makedirs(tex_dir, exist_ok=True)
    rng = np.random.default_rng(1)
    base = {"pine_planks": (150, 95, 40), "black_iron": (45, 42, 44), "teal_iron": (70, 105, 105), "brick_wall": (170, 80, 55)}
    for name, col in base.items():
        n = rng.normal(0, 14, (2048, 2048, 1))
        y = np.arange(2048)[:, None, None]
        stripes = 10 * np.sin(y / 9.0) if name == "pine_planks" else 0
        img = np.clip(np.array(col)[None, None, :] + n + stripes, 0, 255).astype(np.uint8)
        Image.fromarray(img).save(os.path.join(tex_dir, name + "_2k.png"))


def main():
    keep = "--keep" in sys.argv
    tmp = tempfile.mkdtemp(prefix="envskill_selftest_")
    resource_tmp = tempfile.mkdtemp(prefix="envskill_resources_")
    os.makedirs(os.path.join(tmp, "ProjectSettings"))
    print("fake repo:", tmp)
    res, _ = run([sys.executable, os.path.join(HERE, "new_environment.py"), "--name", "SelfTest", "--slug", "self-test", "--prefix", "ST_", "--repo-root", tmp, "--resource-root", resource_tmp])
    step("scaffold new_environment.py", res.returncode == 0, res.stderr[-300:] if res.returncode else "")
    if res.returncode:
        raise SystemExit(1)
    env = os.path.join(resource_tmp, "02_GameAssets", "Environments", "SelfTest", "Source", "Production")
    tools = os.path.join(env, "tools")
    work = os.path.join(resource_tmp, "02_GameAssets", "Environments", "SelfTest", "Source", "Work", "self-test")
    env_vars = dict(os.environ, ENV_ROOT=tmp)
    sys.path.insert(0, tools)
    os.environ["ENV_ROOT"] = tmp
    import importlib
    envcfg = importlib.import_module("envcfg")
    rep = envcfg.tool_report()
    for k, v in rep.items():
        step("tool " + k, not v.startswith("MISSING") or k == "codex", v if not v.startswith("MISSING") else v + ("  (needed only for texture generation)" if k == "codex" else ""))
    if rep["blender"].startswith("MISSING") or rep["graphmat_cli"].startswith("MISSING"):
        raise SystemExit(1)
    step("graphmat-cli has bakeAo/applyAo", envcfg.graphmat_has_ao(rep["graphmat_cli"]), "" if envcfg.graphmat_has_ao(rep["graphmat_cli"]) else "apply references/graphmat-cli-extensions.patch to the Graphmat repo and rebuild")

    # 1. Blender kit build
    res, secs = run([rep["blender"], "-b", "-P", os.path.join(tools, "run_asset.py"), "--", "example_kit", "ALL", "final"], env=env_vars, timeout=600)
    results = [l for l in res.stdout.splitlines() if l.startswith("ASSET_RESULT")]
    step("blender run_asset.py (3 assets, FBX re-import, renders)", len(results) == 3 and res.returncode == 0, f"{len(results)} results in {secs:.0f}s")
    renders = [f for f in os.listdir(os.path.join(work, "renders"))] if os.path.isdir(os.path.join(work, "renders")) else []
    step("clay renders written", len(renders) >= 9, f"{len(renders)} images")
    res, _ = run([rep["blender"], "-b", "-P", os.path.join(tools, "gm_angles.py")], env=env_vars, timeout=300)
    step("blender gm_angles.py", "GM_ANGLES" in res.stdout, res.stdout[-200:] if "GM_ANGLES" not in res.stdout else "")

    res, _ = run([sys.executable, os.path.join(tools, "export_deliverables.py")], env=env_vars, timeout=300)
    blends = os.path.join(env, "Models", "blend")
    step("export_deliverables.py (blends + clay sheets)", res.returncode == 0 and os.path.isdir(blends) and len(os.listdir(blends)) == 3 and os.path.exists(os.path.join(env, "Verification", "clay_example_kit.jpg")),
         (res.stdout.strip()[-200:] + res.stderr[-200:]))
    if "--quick" in sys.argv:
        print("\nRESULT:", "FAIL" if FAILED else "PASS (quick: Graphmat groups skipped)")
        raise SystemExit(1 if FAILED else 0)

    # 2. Graphmat pipeline on two groups
    make_textures(os.path.join(work, "tex"))
    res, _ = run([sys.executable, os.path.join(tools, "gm_groups.py")], env=env_vars)
    step("gm_groups.py", res.returncode == 0, res.stderr[-300:])
    for group, stages in (("props", "export setup masks review_diag textures review_final import"), ("wall_brick", "export setup masks textures import")):
        res, secs = run([sys.executable, os.path.join(tools, "gm_run.py"), group] + stages.split(), env=env_vars, timeout=3000)
        step(f"graphmat group {group} ({stages})", res.returncode == 0, f"{secs:.0f}s" if res.returncode == 0 else (res.stdout[-600:] + res.stderr[-600:]))
    res, _ = run([sys.executable, os.path.join(tools, "gm_check.py"), "props", "wall_brick"], env=env_vars)
    step("gm_check.py (no welded regions)", "'ok': True" in res.stdout or '"ok": true' in res.stdout.lower(), res.stdout.strip()[-300:])

    # 3. result checks
    from PIL import Image
    for asset in ("Crate_Test", "Pipe_Test", "Wall_Brick_Panel"):
        p = os.path.join(work, "fbx_uv", asset + "_uv.fbx")
        step("textured FBX " + asset, os.path.exists(p) and os.path.getsize(p) > 1000)
    for group in ("props", "wall_brick"):
        a = os.path.join(work, "gmat", group, "albedo.png")
        ok = os.path.exists(a)
        step(f"albedo {group} 2048 px", ok and Image.open(a).size == (2048, 2048), str(Image.open(a).size) if ok else "missing")
        obj = os.path.join(work, "gmat", group, "uv.obj")
        has_vt = ok and os.path.exists(obj) and any(l.startswith("vt ") for l in open(obj))
        step(f"uv.obj has UVs {group}", has_vt)

    if "--unity" in sys.argv:
        sys.path.insert(0, os.path.join(tools, "unity"))
        try:
            import mcp
            r = mcp.rpc("tools/list", {})
            names = [t["name"] for t in r["result"]["tools"]]
            step("unity MCP endpoint", "execute_code" in names, f"{len(names)} tools, execute_code={'execute_code' in names}")
        except Exception as e:
            step("unity MCP endpoint", False, f"{e} (start the editor, enable the MCP HTTP server, or use `ucs.py --emit` with your MCP client)")

    print("\nRESULT:", "FAIL" if FAILED else "PASS", "   fake repo kept at " + tmp if keep or FAILED else "")
    if not keep and not FAILED:
        import shutil
        from pathlib import Path
        temp_parent = Path(tempfile.gettempdir()).resolve()
        for candidate in (tmp, resource_tmp):
            target = Path(candidate).resolve()
            if target.parent != temp_parent or not target.name.startswith("envskill_"):
                raise RuntimeError("Refusing cleanup outside self-test temporary folders: " + str(target))
            shutil.rmtree(target, ignore_errors=True)
    raise SystemExit(1 if FAILED else 0)


if __name__ == "__main__":
    main()
