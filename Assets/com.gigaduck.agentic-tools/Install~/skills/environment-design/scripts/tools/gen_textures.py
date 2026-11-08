"""Generate the flat base-colour textures with the Codex CLI image tool (reference-based), ONE image per material, one at a time.

python gen_textures.py --list                      materials, references, budget used / left
python gen_textures.py --prompt <material>         print the exact prompt (use it with any other reference-capable image tool)
python gen_textures.py <material> [<material> ...] generate; every run is appended to textures/generation-log.json
python gen_textures.py --over-budget <material>    only after the user approved more images (the guard refuses otherwise)

Input   <env>/textures/materials.json   {"style": "...", "common": "...", "materials": {"<name>": {"refs": ["crop.png", ...], "text": "..."}}}
        <env>/reference/crops/<ref>     crops of the concept (see crop.py); the generator must SEE the concept
Output  .utmp/<slug>/tex/raw/<name>_gen.png (+ every retry kept as <name>_gen_<n>.png), log entry with prompt, references, size, seconds

Rules baked in (learned the hard way, see references/04-textures-generation.md)
  * the prompt goes through stdin (`-`): `-i` is variadic and swallows a positional prompt
  * Codex writes the picture to ~/.codex/generated_images/<session>/; the newest new file after the call is the result
  * NEVER run two generator processes at the same time (a lock file refuses it): the first project run generated 21 images against a budget of 16
  * the budget (env.json texture_budget, default 16) counts EVERY call, retries included; the guard stops you before the call that would exceed it
"""
import glob
import json
import os
import shutil
import subprocess
import sys
import time

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import envcfg  # noqa: E402

REF = os.path.join(envcfg.REF_DIR, "crops")
RAW = os.path.join(envcfg.TEX_WORK, "raw")
LOG = os.path.join(envcfg.TEXT_DIR, "generation-log.json")
SPEC = os.path.join(envcfg.TEXT_DIR, "materials.json")
LOCK = os.path.join(envcfg.TEX_WORK, ".gen.lock")
GEN = os.path.expanduser("~/.codex/generated_images")


def load_spec():
    if not os.path.exists(SPEC):
        raise SystemExit("missing " + SPEC + " (copy templates/materials.json and fill it in)")
    return json.load(open(SPEC, encoding="utf-8"))


def load_log():
    return json.load(open(LOG, encoding="utf-8")) if os.path.exists(LOG) else []


def build_prompt(spec, name):
    m = spec["materials"][name]
    return ("Use the built-in image generation tool to create the image. Use case: stylized-concept. Asset: " + m["text"] + " " + spec["style"] + " " + spec["common"] +
            " The attached image(s) are the environment concept reference(s): extract the material from them. Do not write code and do not edit files.")


def pid_alive(pid):
    try:
        if os.name == "nt":
            out = subprocess.run(["tasklist", "/FI", f"PID eq {pid}"], capture_output=True, text=True).stdout
            return str(pid) in out
        os.kill(pid, 0)
        return True
    except Exception:
        return False


def run(name, spec, over_budget):
    m = spec["materials"][name]
    refs = [os.path.join(REF, r) for r in m["refs"]]
    for r in refs:
        if not os.path.exists(r):
            raise SystemExit("missing reference crop " + r)
    used, budget = len(load_log()), envcfg.CFG["texture_budget"]
    if used + 1 > budget and not over_budget:
        raise SystemExit(f"BUDGET: {used} images generated of {budget}. Ask the user before generating more, then rerun with --over-budget")
    prompt = build_prompt(spec, name)
    os.makedirs(RAW, exist_ok=True)
    t0 = time.time()
    before = set(glob.glob(GEN + "/*/*"))
    cmd = [envcfg.codex(), "exec", "--skip-git-repo-check", "-s", "read-only", "-C", os.path.join(envcfg.TEX_WORK, "codex")]
    os.makedirs(os.path.join(envcfg.TEX_WORK, "codex"), exist_ok=True)
    for r in refs:
        cmd += ["-i", r]
    cmd.append("-")
    print("running codex for", name, f"(image {used + 1} of {budget}) ...", flush=True)
    res = subprocess.run(cmd, input=prompt, capture_output=True, text=True, timeout=1500)
    out = (res.stdout or "") + (res.stderr or "")
    new = [f for f in glob.glob(GEN + "/*/*") if f not in before and os.path.getmtime(f) >= t0 - 5 and f.lower().endswith((".png", ".jpg", ".jpeg", ".webp"))]
    entry = {"material": name, "prompt": prompt, "references": m["refs"], "seconds": round(time.time() - t0, 1)}
    if not new:
        entry["result"] = "NO IMAGE PRODUCED"
        log = load_log()
        log.append(entry)                                  # a failed call may still have been billed: it counts
        os.makedirs(os.path.dirname(LOG), exist_ok=True)
        json.dump(log, open(LOG, "w", encoding="utf-8"), indent=1)
        print("NO IMAGE PRODUCED. codex output tail:\n" + out[-1500:])
        return None
    src = max(new, key=os.path.getmtime)
    ext = os.path.splitext(src)[1].lower()
    dst = os.path.join(RAW, name + "_gen" + ext)
    k = 1
    while os.path.exists(dst):                             # keep every retry; the pick happens in selection.json
        k += 1
        dst = os.path.join(RAW, f"{name}_gen_{k}{ext}")
    shutil.copy2(src, dst)
    entry.update({"codex_source": src, "saved": dst, "size": list(Image.open(dst).size)})
    log = load_log()
    log.append(entry)
    os.makedirs(os.path.dirname(LOG), exist_ok=True)
    json.dump(log, open(LOG, "w", encoding="utf-8"), indent=1)
    print("saved", dst, entry["size"], "in", entry["seconds"], "s")
    return dst


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return
    spec = load_spec()
    if args[0] == "--list":
        log = load_log()
        print(f"budget {envcfg.CFG['texture_budget']}, generated {len(log)}")
        for n, m in spec["materials"].items():
            done = [e for e in log if e["material"] == n]
            print(f"  {n:16s} refs={m['refs']}  generated={len(done)}")
        return
    if args[0] == "--prompt":
        print(build_prompt(spec, args[1]))
        return
    over = "--over-budget" in args
    names = [a for a in args if not a.startswith("--")]
    os.makedirs(envcfg.TEX_WORK, exist_ok=True)
    if os.path.exists(LOCK):
        try:
            pid = int(open(LOCK).read().strip())
        except Exception:
            pid = 0
        if pid and pid_alive(pid):
            raise SystemExit(f"another gen_textures.py run (pid {pid}) is active: never run the generator twice at once")
    open(LOCK, "w").write(str(os.getpid()))
    try:
        for n in names:
            if n not in spec["materials"]:
                raise SystemExit("unknown material " + n)
            run(n, spec, over)
    finally:
        try:
            os.remove(LOCK)
        except OSError:
            pass


if __name__ == "__main__":
    main()
