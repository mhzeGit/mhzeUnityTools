"""Run a script of this tools/ folder in headless Blender (the Blender path is discovered by envcfg.py, never typed by hand).

python bl.py <script.py> [-- arg arg ...]        e.g.   python bl.py run_asset.py -- boilers Boiler_Large v0 wire
                                                        python bl.py gm_angles.py
Relative script names are looked up in this folder. Output is filtered to the lines that matter (ASSET_RESULT, GM_*, errors, tracebacks);
add --raw to see everything. The exit code is Blender's.
"""
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402


def main():
    a = sys.argv[1:]
    if not a:
        print(__doc__)
        raise SystemExit(1)
    raw = "--raw" in a
    a = [x for x in a if x != "--raw"]
    script = a[0] if os.path.isabs(a[0]) or os.path.exists(a[0]) else os.path.join(HERE, a[0])
    rest = a[1:]
    cmd = [envcfg.blender(), "-b", "-P", script] + (["--"] + rest[1:] if rest and rest[0] == "--" else (["--"] + rest if rest else []))
    res = subprocess.run(cmd, capture_output=True, text=True)
    lines = res.stdout.splitlines()
    keep = lines if raw else [l for l in lines if l.startswith(("ASSET_RESULT", "GM_", "TEST", "RESULT")) or "Error" in l or "Traceback" in l or l.startswith("  File ")]
    print("\n".join(keep[-60:]))
    if res.returncode != 0:
        print(res.stdout[-3000:])
        print(res.stderr[-2000:])
    raise SystemExit(res.returncode)


if __name__ == "__main__":
    main()
