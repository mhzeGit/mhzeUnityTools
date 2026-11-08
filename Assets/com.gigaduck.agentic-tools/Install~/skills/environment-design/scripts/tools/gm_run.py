"""Run stages of the Graphmat texturing pipeline for one group (see gm_groups.py for the groups).

python gm_run.py <group> [stage ...]
stages: export        Blender: build the group's assets, lay them out, write model.obj + sidecar.json
        setup         Graphmat CLI: embed textures, Auto UV 2048, position/normal/AO bakes
        masks         Graphmat CLI: one masked folder per region with diagnostic colours, diag_*.png renders
        review_diag   Blender: unlit per-asset views of the UV mesh with the diagnostic albedo
        textures      Graphmat CLI: assign textures (triplanar), multiply AO, export albedo.png + uv.obj, final_*.png renders
        review_final  Blender: unlit per-asset views of the UV mesh with the final albedo
        import        Blender: rebuild per-asset objects from uv.obj, write .utmp/<slug>/fbx_uv/<Asset>_uv.fbx
default stages: export setup masks review_diag
"""
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402
import gm_pipeline  # noqa: E402

GROUPS = envcfg.GROUPS_DIR


def blender(script, *args):
    res = subprocess.run([envcfg.blender(), "-b", "-P", os.path.join(HERE, script), "--", *args], capture_output=True, text=True, timeout=3000)
    lines = [l for l in res.stdout.splitlines() if l.startswith("GM_") or "Error" in l or "Traceback" in l]
    print("\n".join(lines[-12:]))
    if res.returncode != 0:
        print(res.stdout[-3000:])
        print(res.stderr[-2000:])
        raise SystemExit(f"blender {script} failed ({res.returncode})")


def main():
    group = sys.argv[1]
    stages = sys.argv[2:] or ["export", "setup", "masks", "review_diag"]
    spec_path = os.path.abspath(os.path.join(GROUPS, group + ".json"))
    spec = gm_pipeline.load(spec_path)
    for st in stages:
        t0 = time.time()
        print(f"== {group}: {st}")
        if st == "export":
            blender("gm_export.py", "export", spec_path)
        elif st == "setup":
            gm_pipeline.do_setup(spec)
        elif st == "masks":
            gm_pipeline.do_masks(spec)
        elif st == "review_diag":
            blender("gm_review.py", spec_path, "diag_albedo.png", "diag")
        elif st == "textures":
            gm_pipeline.do_textures(spec)
        elif st == "review_final":
            blender("gm_review.py", spec_path, "albedo.png", "final")
        elif st == "import":
            blender("gm_export.py", "import", spec_path)
        else:
            raise SystemExit("unknown stage " + st)
        print(f"   {st} done in {time.time() - t0:.0f}s")


if __name__ == "__main__":
    main()
