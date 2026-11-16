#!/usr/bin/env python3
"""Content Studio: agents make marketing video from the game, as recipes that can be rendered again at any time.

  studio.py doctor                                    what is set up (ffmpeg, Blender, build, folders) and what is missing
  studio.py init                                      write the project's studio.json and content folders (safe to rerun)
  studio.py shot new NAME [--idea TEXT] | ls | show NAME
  studio.py render SHOT [--format vertical] [--fps 30] [--scale 1] [--realtime] [--no-ui] [--no-audio] [--stems voice,music]
  studio.py still SHOT [--format wide] [--at S]       one frame as PNG layers (posters, thumbnails, image posts)
  studio.py takes [SHOT] | take TAKE                  rendered takes (newest first); TAKE = id, SHOT (its newest) or a path
  studio.py sheet TAKE                                contact sheet of the finished picture: how agents look at a take
  studio.py export TAKE [--ui on|off] [--audio master|none|voice+sfx] [--outro NAME|none] [--platform tiktok] [--in S --out S]
  studio.py piece ls | new NAME --role outro | show NAME | render NAME --format vertical
  studio.py edit new NAME --take TAKE [...] | ls | show NAME | build NAME [--force] | render NAME [--platform P] | open NAME
  studio.py publish EDIT [--platform tiktok,shorts]   final files per platform: video, thumbnail, captions, post text, checks

A shot recipe (Marketing/Shots/NAME.shot.json) says where the game starts, which camera films it, what happens and for how long.
`render` starts an agent player, plays the recipe and renders a take on fixed time steps (any size and aspect ratio) as
layers: clean picture, UI with alpha, game audio, optional audio stems, events. Layers are combined later, so UI and sound
are switches, never a new take. `edit build` turns an edit plan (takes + modular pieces such as the outro) into a Blender
video-editing project you can open and change by hand; `edit render` renders that project, including your changes.
Docs: Docs/CONTENT_STUDIO.md. Installed from the Agentic Tools package (Install~/files/tools/studio): change it there.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent / "agentplay"))
try:
    import agentplay as ap  # the agent player: builds, players, commands
except ImportError:  # the agent-play module is not installed
    ap = None

ROOT = ap.ROOT if ap else HERE.parents[1]
CONFIG_FILE = HERE / "studio.json"
BLENDER_SCRIPT = HERE / "studio_blender.py"

FORMATS = {
    "vertical": [1080, 1920], "wide": [1920, 1080], "square": [1080, 1080], "portrait": [1080, 1350],
}
PLATFORMS = {
    "tiktok": {"format": "vertical", "maxSeconds": 600, "bestSeconds": [6, 34], "fps": 30, "safe": [0.10, 0.20, 0.06, 0.16]},
    "shorts": {"format": "vertical", "maxSeconds": 180, "bestSeconds": [6, 45], "fps": 30, "safe": [0.10, 0.18, 0.06, 0.12]},
    "reels": {"format": "vertical", "maxSeconds": 180, "bestSeconds": [6, 30], "fps": 30, "safe": [0.12, 0.22, 0.06, 0.12]},
    "x": {"format": "wide", "maxSeconds": 140, "bestSeconds": [6, 45], "fps": 30, "safe": [0.05, 0.05, 0.05, 0.05]},
    "bluesky": {"format": "wide", "maxSeconds": 180, "bestSeconds": [6, 45], "fps": 30, "safe": [0.05, 0.05, 0.05, 0.05]},
    "youtube": {"format": "wide", "maxSeconds": 3600, "bestSeconds": [30, 180], "fps": 60, "safe": [0.05, 0.05, 0.05, 0.05]},
    "steam": {"format": "wide", "maxSeconds": 600, "bestSeconds": [30, 120], "fps": 30, "safe": [0.05, 0.05, 0.05, 0.05]},
}
# safe = [top, bottom, left, right] fractions of the frame that platform buttons and captions cover

DEFAULT_CONFIG = {
    "outputDir": "",
    "contentDir": "Marketing",
    "brand": {"gameName": "", "cta": "Wishlist now on Steam", "font": "", "background": "#000000", "foreground": "#ffffff",
              "accent": "#ffcc33"},
    "defaults": {"format": "vertical", "fps": 30, "outro": "", "player": "studio", "scale": 1},
    "hide": [], "hideUi": [], "stemRules": {},
    "formats": {}, "platforms": {},
    "blender": "", "ffmpeg": "",
}


def fail(message, code=1):
    print("ERROR: " + message, file=sys.stderr)
    sys.exit(code)


def load_config():
    cfg = json.loads(json.dumps(DEFAULT_CONFIG))
    if CONFIG_FILE.exists():
        try:
            own = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
        except ValueError as e:
            fail("%s is not valid JSON: %s" % (CONFIG_FILE, e))
        for k, v in own.items():
            if isinstance(v, dict) and isinstance(cfg.get(k), dict):
                cfg[k].update(v)
            else:
                cfg[k] = v
    return cfg


CFG = load_config()
CONTENT = ROOT / CFG["contentDir"]
SHOTS, PIECES, EDITS = CONTENT / "Shots", CONTENT / "Pieces", CONTENT / "Edits"


def out_dir(required=True):
    raw = os.environ.get("STUDIO_OUTPUT") or CFG.get("outputDir") or ""
    if not raw:
        if required:
            fail("no outputDir in %s (where takes, Blender projects and exports go, outside Git): set it, see studio.py init" % CONFIG_FILE)
        return None
    p = Path(os.path.expandvars(os.path.expanduser(raw)))
    if not p.is_absolute():
        p = ROOT / p
    return p


def formats():
    f = dict(FORMATS)
    f.update(CFG.get("formats") or {})
    return f


def platforms():
    p = json.loads(json.dumps(PLATFORMS))
    for k, v in (CFG.get("platforms") or {}).items():
        p.setdefault(k, {}).update(v)
    return p


def size_of(fmt, scale=1.0):
    if re.fullmatch(r"\d+x\d+", fmt or ""):
        w, h = [int(x) for x in fmt.split("x")]
    else:
        if fmt not in formats():
            fail("unknown format '%s': %s or WxH" % (fmt, ", ".join(formats())))
        w, h = formats()[fmt]
    return int(w * scale) & ~1, int(h * scale) & ~1


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-") or "x"


def now_id():
    return datetime.datetime.now().strftime("%Y%m%d-%H%M%S")


def read_json(path):
    try:
        return json.loads(Path(path).read_text(encoding="utf-8"))
    except FileNotFoundError:
        fail("not found: %s" % path, 2)
    except ValueError as e:
        fail("%s is not valid JSON: %s" % (path, e))


def write_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(data, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    os.replace(tmp, path)


def say(obj):
    print(json.dumps(obj, indent=1, ensure_ascii=False) if not isinstance(obj, str) else obj)


# ------------------------------------------------------------------ tools: ffmpeg and Blender

_FF = "unset"


def ffmpeg(required=True):
    global _FF
    if _FF == "unset":
        found = os.environ.get("STUDIO_FFMPEG") or CFG.get("ffmpeg") or os.environ.get("AGENTPLAY_FFMPEG") or shutil.which("ffmpeg")
        if not found:
            try:
                import imageio_ffmpeg
                found = imageio_ffmpeg.get_ffmpeg_exe()
            except Exception:
                found = None
        _FF = found if found and Path(found).is_file() else None
    if required and not _FF:
        fail("ffmpeg not found: python -m pip install imageio-ffmpeg (bundles one), or set \"ffmpeg\" in studio.json")
    return _FF


def blender(required=True):
    cands = [os.environ.get("BLENDER_EXE"), CFG.get("blender"), shutil.which("blender")]
    for base in (r"C:\Program Files (x86)\Steam\steamapps\common\Blender", r"C:\Program Files\Steam\steamapps\common\Blender"):
        cands.append(os.path.join(base, "blender.exe"))
    for pf in (r"C:\Program Files\Blender Foundation",):
        if os.path.isdir(pf):
            for d in sorted(os.listdir(pf), reverse=True):
                cands.append(os.path.join(pf, d, "blender.exe"))
    for c in cands:
        if c and Path(c).is_file():
            return c
    if required:
        fail("Blender not found: set \"blender\" in studio.json or BLENDER_EXE")
    return None


def run(cmd, timeout=1800, quiet=True):
    try:
        p = subprocess.run([str(c) for c in cmd], capture_output=True, text=True, timeout=timeout, encoding="utf-8", errors="replace")
    except subprocess.TimeoutExpired:
        fail("timed out after %ds: %s" % (timeout, " ".join(str(c) for c in cmd[:3])))
    if p.returncode != 0:
        tail = (p.stderr or p.stdout or "")[-3000:]
        fail("%s failed (exit %d):\n%s" % (Path(str(cmd[0])).name, p.returncode, tail))
    return p


def run_blender(mode, job, timeout=1800):
    """Runs studio_blender.py in background Blender with a job file; returns its JSON answer."""
    out = out_dir()
    jobs = out / "_jobs"
    jobs.mkdir(parents=True, exist_ok=True)
    jf = jobs / ("%s_%s.json" % (mode, now_id()))
    job = dict(job, mode=mode, answer=str(jf.with_suffix(".answer.json")))
    write_json(jf, job)
    p = run([blender(), "--background", "--factory-startup", "--python", BLENDER_SCRIPT, "--", str(jf)], timeout=timeout)
    ans = Path(job["answer"])
    if not ans.exists():
        fail("Blender gave no answer; its output:\n" + (p.stdout or "")[-3000:])
    a = json.loads(ans.read_text(encoding="utf-8"))
    if not a.get("ok"):
        fail("Blender: " + a.get("error", "failed") + "\n" + a.get("trace", ""))
    return a


def media_info(path):
    """Duration, size, fps and whether there is audio, read from ffmpeg's banner (no ffprobe needed)."""
    p = subprocess.run([ffmpeg(), "-hide_banner", "-i", str(path)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    text = p.stderr
    info = {"audio": "Audio:" in text}
    m = re.search(r"Duration: (\d+):(\d+):([\d.]+)", text)
    if m:
        info["duration"] = int(m.group(1)) * 3600 + int(m.group(2)) * 60 + float(m.group(3))
    m = re.search(r"Video:.*?(\d{2,5})x(\d{2,5})", text)
    if m:
        info["size"] = [int(m.group(1)), int(m.group(2))]
    m = re.search(r"([\d.]+) fps", text)
    if m:
        info["fps"] = float(m.group(1))
    return info


# ------------------------------------------------------------------ doctor and init

def cmd_doctor(args):
    out = out_dir(required=False)
    rows = [
        ("config", CONFIG_FILE.exists(), str(CONFIG_FILE) if CONFIG_FILE.exists() else "missing: studio.py init"),
        ("outputDir", bool(out), str(out) if out else "set outputDir in studio.json (outside Git)"),
        ("content", CONTENT.exists(), str(CONTENT)),
        ("ffmpeg", bool(ffmpeg(False)), ffmpeg(False) or "python -m pip install imageio-ffmpeg"),
        ("blender", bool(blender(False)), blender(False) or "install Blender 4.4+ or set \"blender\""),
        ("agentplay", ap is not None, "tools/agentplay (renders need the agent player)" if ap else "agent-play module not installed"),
    ]
    if ap:
        b = ap.latest_build()
        rows.append(("build", b is not None, (b.name + (" (current)" if b.name.endswith("-" + ap.source_stamp()) else " (STALE: agentplay.py build)")) if b else "agentplay.py build"))
    if CFG["brand"].get("font"):
        f = ROOT / CFG["brand"]["font"]
        rows.append(("font", f.exists(), str(f)))
    for name, ok, text in rows:
        print("%-10s %s  %s" % (name, "ok " if ok else "-- ", text))
    for name, ok, _ in rows:
        if not ok and name in ("outputDir", "ffmpeg"):
            sys.exit(1)


def cmd_init(args):
    if not CONFIG_FILE.exists():
        cfg = {k: v for k, v in DEFAULT_CONFIG.items() if k in ("outputDir", "contentDir", "brand", "defaults", "hide", "hideUi", "stemRules")}
        if args.output:
            cfg["outputDir"] = args.output
        write_json(CONFIG_FILE, cfg)
        print("wrote", CONFIG_FILE)
    for d in (SHOTS, PIECES, EDITS):
        d.mkdir(parents=True, exist_ok=True)
    out = out_dir(required=False)
    if out:
        for d in ("takes", "edits", "exports", "pieces", "published"):
            (out / d).mkdir(parents=True, exist_ok=True)
    print("content:", CONTENT)
    print("outputs:", out or "(set outputDir in studio.json)")


# ------------------------------------------------------------------ shots

SHOT_TEMPLATE = {
    "name": "",
    "idea": "",
    "notes": "What this shot is for and what must be in frame.",
    "setup": [{"op": "attention", "mode": "off"}],
    "camera": {"mode": "player"},
    "length": 6,
    "actions": [],
    "formats": ["vertical"],
    "fps": 30,
    "ui": True,
    "audio": True,
    "stems": [],
    "hide": [],
    "hideUi": [],
}


def shot_path(name):
    p = Path(name)
    if p.suffix == ".json" and p.exists():
        return p
    return SHOTS / (slug(name) + ".shot.json")


def load_shot(name):
    p = shot_path(name)
    if not p.exists():
        fail("no shot '%s' (%s): studio.py shot new %s" % (name, p, name), 2)
    s = read_json(p)
    s.setdefault("name", p.name.replace(".shot.json", ""))
    return s, p


def cmd_shot(args):
    if args.action == "ls":
        for p in sorted(SHOTS.glob("*.shot.json")):
            s = read_json(p)
            print("%-28s %5ss  %-18s %s" % (p.name.replace(".shot.json", ""), s.get("length", "?"), ",".join(s.get("formats", [])), s.get("idea", "")))
    elif args.action == "new":
        if not args.name:
            fail("shot new NAME")
        p = shot_path(args.name)
        if p.exists():
            fail("%s exists" % p)
        s = dict(SHOT_TEMPLATE, name=slug(args.name), idea=args.idea or "")
        write_json(p, s)
        print("wrote", p)
    elif args.action == "show":
        s, p = load_shot(args.name)
        print(p)
        say(s)


# ------------------------------------------------------------------ render: play a recipe in an agent player

def need_player_tools():
    if ap is None:
        fail("rendering needs the agent player (tools/agentplay): enable the agent-play module")


def send(player, command, timeout=180):
    _, answer = ap.send(player, command, timeout)
    if not answer.get("ok"):
        raise RuntimeError("%s -> %s" % (json.dumps(command)[:200], answer.get("error") or answer))
    return answer


def run_steps(player, steps, in_take):
    """Runs recipe steps: agentplay ops, plus {"wait": s}, {"at": s, ...}, {"mark": text}, {"camera": {...}}, {"hide": [...]}."""
    for step in steps or []:
        step = dict(step)
        if "at" in step and in_take:
            at = float(step.pop("at"))
            st = send(player, {"op": "studio", "action": "status"})
            gap = at - float(st.get("t", 0))
            if gap > 0:
                send(player, {"op": "studio", "action": "wait", "s": gap}, timeout=120 + gap * 30)
        if "wait" in step and len(step) == 1:
            s = float(step["wait"])
            if in_take:
                send(player, {"op": "studio", "action": "wait", "s": s}, timeout=120 + s * 30)
            else:
                send(player, {"op": "wait", "s": s}, timeout=60 + s * 2)
            continue
        if "mark" in step and len(step) == 1:
            if in_take:
                send(player, {"op": "studio", "action": "mark", "text": str(step["mark"])})
            continue
        if "camera" in step and len(step) == 1:
            send(player, {"op": "studio", "action": "camera", "camera": step["camera"]})
            continue
        if "hide" in step and len(step) == 1:
            send(player, {"op": "studio", "action": "hide", "names": step["hide"]})
            continue
        if "op" not in step:
            raise RuntimeError("a step needs op (or wait/at/mark/camera/hide): %s" % json.dumps(step))
        timeout = float(step.pop("timeout", 240))
        send(player, step, timeout=timeout)


def start_player(name, keep):
    need_player_tools()
    b = ap.latest_build()
    if b is None:
        fail("no agent build: python tools/agentplay/agentplay.py info, then build when it says STALE", 2)
    folder = ap.run_dir(name)
    pid = ap.instance_pid(folder)
    if pid and ap.pid_alive(pid, ap.EXE):
        if keep:
            return False
        fail("player '%s' is running: stop it or use --player" % name)
    rec = ap.start_player(name, res="640x360", window=True)
    if not rec["boot"].get("ok"):
        fail("player did not boot: %s" % rec["boot"])
    probe = ap.send(name, {"op": "studio", "action": "status"}, 30)[1]
    if not probe.get("ok"):
        ap.stop_one(name)
        fail("this build has no studio op (%s): the agent build predates Content Studio; agentplay.py info / build" % probe.get("error"))
    return True


def take_options(shot, folder, w, h, fps, args, solo=None):
    o = {
        "op": "studio", "action": "start", "folder": str(folder), "ffmpeg": ffmpeg(), "id": folder.name,
        "w": w, "h": h, "fps": fps, "mode": "realtime" if args.realtime else "offline",
        "ui": (not args.no_ui) and shot.get("ui", True) and solo is None,
        "audio": (not args.no_audio) and shot.get("audio", True),
        "camera": shot.get("camera") or {"mode": "player"},
        "hide": list(CFG.get("hide") or []) + list(shot.get("hide") or []),
        "hideUi": list(CFG.get("hideUi") or []) + list(shot.get("hideUi") or []),
        "stemRules": CFG.get("stemRules") or {},
        "max": float(shot.get("length") or 60) + 30, "label": shot.get("idea") or shot["name"],
    }
    if solo:
        o["solo"] = solo
    return o


def play_take(player, shot, folder, w, h, fps, args, solo=None):
    run_steps(player, shot.get("setup"), in_take=False)
    send(player, take_options(shot, folder, w, h, fps, args, solo), timeout=120)
    try:
        run_steps(player, shot.get("actions"), in_take=True)
        length = shot.get("length")
        if length:
            st = send(player, {"op": "studio", "action": "status"})
            gap = float(length) - float(st.get("t", 0))
            if gap > 0:
                send(player, {"op": "studio", "action": "wait", "s": gap}, timeout=120 + gap * 30)
    finally:
        stop = ap.send(player, {"op": "studio", "action": "stop"}, 900)[1]
    if not stop.get("ok"):
        raise RuntimeError("stop failed: %s" % stop.get("error"))
    return stop


def cmd_render(args):
    shot, shot_file = load_shot(args.shot)
    out = out_dir()
    fmts = [args.format] if args.format else (shot.get("formats") or [CFG["defaults"]["format"]])
    fps = args.fps or shot.get("fps") or CFG["defaults"]["fps"]
    stems = [s for s in (args.stems.split(",") if args.stems else shot.get("stems") or []) if s]
    player = args.player or "%s-%s" % (CFG["defaults"].get("player", "studio"), slug(shot["name"])[:20])
    started = start_player(player, args.keep_player)
    made = []
    try:
        for fmt in fmts:
            w, h = size_of(fmt, args.scale or CFG["defaults"].get("scale", 1))
            tid = "%s_%s_%s" % (slug(shot["name"]), fmt if fmt in formats() else "%dx%d" % (w, h), now_id())
            folder = out / "takes" / slug(shot["name"]) / tid
            folder.mkdir(parents=True, exist_ok=True)
            print("rendering %s %dx%d @%d -> %s" % (shot["name"], w, h, fps, folder), file=sys.stderr)
            t0 = time.time()
            meta = play_take(player, shot, folder, w, h, fps, args)
            for stem in stems:
                print("  stem pass: %s" % stem, file=sys.stderr)
                sf = folder / "_pass" / stem
                sf.mkdir(parents=True, exist_ok=True)
                play_take(player, shot, sf, 256, 144, fps, args, solo=stem)
                (folder / "stems").mkdir(exist_ok=True)
                if (sf / "audio.wav").exists():
                    shutil.move(str(sf / "audio.wav"), str(folder / "stems" / (stem + ".wav")))
                shutil.rmtree(folder / "_pass", ignore_errors=True)
            take = read_json(folder / "take.json")
            take.update({"shot": shot["name"], "format": fmt, "recipe": shot, "recipeFile": str(shot_file.relative_to(ROOT)) if shot_file.is_relative_to(ROOT) else str(shot_file),
                         "git": git_head(), "renderSeconds": round(time.time() - t0, 1),
                         "stemFiles": sorted("stems/" + p.name for p in (folder / "stems").glob("*.wav")) if (folder / "stems").exists() else []})
            write_json(folder / "take.json", take)
            preview(folder, take)
            sheet(folder, take)
            made.append({"take": tid, "folder": str(folder), "frames": take.get("frames"), "duration": take.get("duration"),
                         "preview": str(folder / "preview.mp4"), "sheet": str(folder / "sheet.jpg"), "error": take.get("error")})
    finally:
        if started and not args.keep_player:
            print(ap.stop_one(player), file=sys.stderr)
    say(made)


def git_head():
    try:
        return subprocess.run(["git", "-C", str(ROOT), "rev-parse", "--short", "HEAD"], capture_output=True, text=True, timeout=20).stdout.strip()
    except Exception:
        return ""


def cmd_still(args):
    shot, _ = load_shot(args.shot)
    out = out_dir()
    fmt = args.format or (shot.get("formats") or ["wide"])[0]
    w, h = size_of(fmt, args.scale or 1)
    player = args.player or "%s-%s" % (CFG["defaults"].get("player", "studio"), slug(shot["name"])[:20])
    started = start_player(player, args.keep_player)
    try:
        folder = out / "stills" / slug(shot["name"])
        folder.mkdir(parents=True, exist_ok=True)
        name = "%s_%s_%s" % (slug(shot["name"]), fmt, now_id())
        run_steps(player, shot.get("setup"), in_take=False)
        if args.at:
            send(player, {"op": "wait", "s": args.at}, timeout=60 + args.at * 2)
        r = send(player, {"op": "studio", "action": "still", "folder": str(folder), "name": name, "w": w, "h": h,
                          "camera": shot.get("camera") or {"mode": "player"},
                          "hide": list(CFG.get("hide") or []) + list(shot.get("hide") or []),
                          "hideUi": list(CFG.get("hideUi") or []) + list(shot.get("hideUi") or [])})
        comp = folder / (name + "_full.png")
        if r.get("ui"):
            run([ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-i", r["clean"], "-i", r["ui"], "-filter_complex", "overlay=0:0", str(comp)])
        say({"clean": r.get("clean"), "ui": r.get("ui"), "full": str(comp) if r.get("ui") else r.get("clean")})
    finally:
        if started and not args.keep_player:
            print(ap.stop_one(player), file=sys.stderr)


# ------------------------------------------------------------------ takes

def all_takes():
    out = out_dir()
    found = []
    for tj in (out / "takes").glob("*/*/take.json"):
        try:
            found.append((tj.stat().st_mtime, tj.parent))
        except OSError:
            pass
    return [p for _, p in sorted(found, reverse=True)]


def resolve_take(ref):
    """A take folder from: a path, a take id, 'SHOT' or 'latest:SHOT' (its newest), 'SHOT:FORMAT' (newest in that format)."""
    if not ref:
        fail("name a take")
    p = Path(ref)
    if p.is_dir() and (p / "take.json").exists():
        return p
    ref = ref.replace("latest:", "")
    shot, _, fmt = ref.partition(":")
    for folder in all_takes():
        if folder.name == ref:
            return folder
    for folder in all_takes():
        if folder.parent.name == slug(shot) and (not fmt or folder.name.split("_")[-2] == fmt):
            return folder
    fail("no take '%s' (studio.py takes)" % ref, 2)


def load_take(ref):
    folder = resolve_take(ref)
    t = read_json(folder / "take.json")
    t["folder"] = str(folder)
    return folder, t


def cmd_takes(args):
    for folder in all_takes():
        if args.shot and folder.parent.name != slug(args.shot):
            continue
        try:
            t = read_json(folder / "take.json")
        except SystemExit:
            continue
        print("%-48s %4dx%-4d %5.1fs  %s%s" % (folder.name, t.get("width", 0), t.get("height", 0), t.get("duration", 0),
                                              "ui " if t.get("ui") else "", ("audio " if t.get("audio") else "") + " ".join(t.get("stemFiles") or [])))


def cmd_take(args):
    folder, t = load_take(args.take)
    t.pop("recipe", None)
    say(t)


def layer_inputs(folder, t, ui=True, audio="master"):
    """ffmpeg inputs and filter parts for a take's layers: video label [v], audio label [a] or None."""
    inputs = ["-i", str(folder / "clean.mp4")]
    parts = []
    n = 1
    v = "[0:v]"
    if ui and t.get("ui") and (folder / "ui").exists():
        inputs += ["-framerate", str(t["fps"]), "-start_number", "0", "-i", str(folder / "ui" / "ui_%05d.png")]
        parts.append("%s[%d:v]overlay=0:0:format=auto[vui]" % (v, n))
        v = "[vui]"
        n += 1
    a = None
    files = []
    if audio and audio != "none":
        if audio == "master":
            if t.get("audio") and (folder / "audio.wav").exists():
                files = [folder / "audio.wav"]
        else:
            for stem in audio.split("+"):
                f = folder / "stems" / (stem + ".wav")
                if not f.exists():
                    fail("take has no '%s' stem (render with --stems %s)" % (stem, stem))
                files.append(f)
    for f in files:
        inputs += ["-i", str(f)]
    if len(files) == 1:
        a = "[%d:a]" % n
    elif len(files) > 1:
        parts.append("".join("[%d:a]" % (n + i) for i in range(len(files))) + "amix=inputs=%d:normalize=0[amix]" % len(files))
        a = "[amix]"
    return inputs, parts, v, a, n + len(files)


def preview(folder, t):
    """preview.mp4: UI and sound included, small, for people to watch."""
    inputs, parts, v, a, _ = layer_inputs(folder, t)
    parts.append("%sscale=-2:'min(960,ih)'[vp]" % v)
    cmd = [ffmpeg(), "-hide_banner", "-loglevel", "error", "-y"] + inputs + ["-filter_complex", ";".join(parts), "-map", "[vp]"]
    if a:
        cmd += ["-map", a, "-c:a", "aac", "-b:a", "160k"]
    cmd += ["-c:v", "libx264", "-preset", "veryfast", "-crf", "23", "-pix_fmt", "yuv420p", "-shortest", str(folder / "preview.mp4")]
    run(cmd)


def sheet(folder, t, cols=6):
    """sheet.jpg: up to 24 frames of the finished picture (UI on), so an agent can check a take without watching it."""
    dur = max(0.5, float(t.get("duration") or 1))
    count = min(24, max(6, int(dur * 2)))
    rows = (count + cols - 1) // cols
    inputs, parts, v, _, _ = layer_inputs(folder, t, audio="none")
    w = 240 if t.get("height", 1) > t.get("width", 1) else 320
    parts.append("%sfps=%f,scale=%d:-2,drawtext=text='%%{pts\\:hms}':x=4:y=4:fontsize=14:fontcolor=white:box=1:boxcolor=black@0.5,tile=%dx%d[s]"
                 % (v, count / dur, w, cols, rows))
    cmd = [ffmpeg(), "-hide_banner", "-loglevel", "error", "-y"] + inputs + ["-filter_complex", ";".join(parts), "-map", "[s]",
                                                                         "-frames:v", "1", "-q:v", "3", str(folder / "sheet.jpg")]
    try:
        run(cmd, timeout=300)
    except SystemExit:  # no drawtext (no font support in this ffmpeg): plain tiles
        parts[-1] = "%sfps=%f,scale=%d:-2,tile=%dx%d[s]" % (v, count / dur, w, cols, rows)
        run([ffmpeg(), "-hide_banner", "-loglevel", "error", "-y"] + inputs + ["-filter_complex", ";".join(parts), "-map", "[s]",
                                                                              "-frames:v", "1", "-q:v", "3", str(folder / "sheet.jpg")], timeout=300)
    return folder / "sheet.jpg"


def cmd_sheet(args):
    folder, t = load_take(args.take)
    print(sheet(folder, t))


# ------------------------------------------------------------------ pieces: modular reusable parts (outro, sting, intro, lower third)

PIECE_TEMPLATE = {
    "name": "", "role": "outro", "kind": "generated", "duration": 3.0,
    "place": {"at": "end", "fadeIn": 0.25, "fadeOut": 0.0},
    "background": "{background}",
    "elements": [
        {"type": "text", "text": "{gameName}", "size": 0.085, "y": 0.52, "color": "{foreground}", "bold": True, "anim": "pop", "start": 0.0},
        {"type": "text", "text": "{cta}", "size": 0.05, "y": 0.42, "color": "{accent}", "anim": "fade", "start": 0.35},
    ],
    "audio": "",
    "volume": 1.0,
}
ROLES = ("intro", "outro", "sting", "overlay", "lowerthird", "transition")


def piece_dir(name):
    return PIECES / slug(name)


def load_piece(name):
    p = piece_dir(name) / "piece.json"
    if not p.exists():
        fail("no piece '%s' (%s): studio.py piece new %s --role outro" % (name, p, name), 2)
    d = read_json(p)
    d.setdefault("name", slug(name))
    d["_dir"] = str(p.parent)
    return d


def piece_vars():
    b = CFG.get("brand") or {}
    v = {k: str(val) for k, val in b.items() if isinstance(val, (str, int, float))}
    if b.get("font"):
        v["font"] = str((ROOT / b["font"]).resolve())
    return v


def expand(value, variables):
    if isinstance(value, str):
        return re.sub(r"\{(\w+)\}", lambda m: variables.get(m.group(1), m.group(0)), value)
    if isinstance(value, list):
        return [expand(x, variables) for x in value]
    if isinstance(value, dict):
        return {k: expand(x, variables) for k, x in value.items()}
    return value


def piece_job(piece, w, h, fps):
    """The piece, with brand variables filled in and file paths made absolute, as Blender reads it."""
    d = expand({k: v for k, v in piece.items() if not k.startswith("_")}, piece_vars())
    base = Path(piece["_dir"])
    for el in d.get("elements") or []:
        if el.get("src"):
            el["src"] = str((base / el["src"]).resolve())
    for key in ("audio", "blend"):
        if d.get(key):
            d[key] = str((base / d[key]).resolve())
    if d.get("files"):
        d["files"] = {k: str((base / v).resolve()) for k, v in d["files"].items()}
    d["w"], d["h"], d["fps"] = w, h, fps
    d["vars"] = piece_vars()
    d["formatName"] = next((k for k, v in formats().items() if list(v) == [w, h]), "%dx%d" % (w, h))
    d["font"] = d.get("font") or piece_vars().get("font", "")
    return d


def cmd_piece(args):
    if args.action == "ls":
        for p in sorted(PIECES.glob("*/piece.json")):
            d = read_json(p)
            print("%-24s %-10s %-9s %4.1fs  %s" % (p.parent.name, d.get("role"), d.get("kind"), float(d.get("duration", 0)), d.get("place", {}).get("at", "")))
    elif args.action == "new":
        if not args.name:
            fail("piece new NAME --role outro")
        d = piece_dir(args.name)
        if (d / "piece.json").exists():
            fail("%s exists" % d)
        if args.role not in ROLES:
            fail("role is one of " + ", ".join(ROLES))
        data = dict(PIECE_TEMPLATE, name=slug(args.name), role=args.role)
        data["place"] = {"outro": {"at": "end", "fadeIn": 0.25}, "intro": {"at": "start", "fadeOut": 0.25},
                         "sting": {"at": "end", "fadeIn": 0.0}, "overlay": {"at": 0.5, "channel": "top"},
                         "lowerthird": {"at": 0.5, "channel": "top"}, "transition": {"at": "between"}}[args.role]
        write_json(d / "piece.json", data)
        print("wrote", d / "piece.json")
    elif args.action == "show":
        say(load_piece(args.name))
    elif args.action == "render":
        print(render_piece(args.name, args.format or CFG["defaults"]["format"], args.fps or CFG["defaults"]["fps"], args.force))


def render_piece(name, fmt, fps, force=False):
    """The piece as a video file at this format (cached by its content; Blender renders it)."""
    piece = load_piece(name)
    w, h = size_of(fmt)
    if piece.get("kind") == "video":
        f = (piece.get("files") or {}).get(fmt)
        if not f:
            fail("piece %s has no video for format %s" % (name, fmt))
        return Path(piece["_dir"]) / f
    job = piece_job(piece, w, h, fps)
    digest = hashlib.sha1(json.dumps(job, sort_keys=True).encode()).hexdigest()[:10]
    for f in [job.get("audio"), job.get("blend"), job.get("font")] + [e.get("src") for e in job.get("elements") or []]:
        if f and Path(f).exists():
            digest += "%x" % int(Path(f).stat().st_mtime)
    target = out_dir() / "pieces" / slug(name) / ("%s_%dx%d_%d_%s.mp4" % (slug(name), w, h, fps, digest[:16]))
    if target.exists() and not force:
        return target
    target.parent.mkdir(parents=True, exist_ok=True)
    run_blender("piece", {"piece": job, "output": str(target)})
    return target


# ------------------------------------------------------------------ export: fast composite with ffmpeg (no Blender)

def cmd_export(args):
    folder, t = load_take(args.take)
    plat = platforms().get(args.platform) if args.platform else None
    if args.platform and not plat:
        fail("unknown platform: " + ", ".join(platforms()))
    fps = t["fps"]
    tw, th = size_of(plat["format"]) if plat else (t["width"], t["height"])
    ui = args.ui != "off"
    audio = args.audio or ("master" if t.get("audio") else "none")
    inputs, parts, v, a, n = layer_inputs(folder, t, ui=ui, audio=audio)
    start = args.start or 0.0
    end = args.end if args.end else float(t.get("duration") or 0)
    if end <= start:
        fail("--out must be after --in")
    parts.append("%strim=start=%f:end=%f,setpts=PTS-STARTPTS,scale=%d:%d:force_original_aspect_ratio=increase,crop=%d:%d,setsar=1,fps=%d[vm]"
                 % (v, start, end, tw, th, tw, th, fps))
    v, length = "[vm]", end - start
    if a:
        parts.append("%satrim=start=%f:end=%f,asetpts=PTS-STARTPTS,aresample=48000[am]" % (a, start, end))
        a = "[am]"
    outro = None if args.outro == "none" else (args.outro or CFG["defaults"].get("outro") or None)
    if outro:
        piece = load_piece(outro)
        pf = render_piece(outro, plat["format"] if plat else "%dx%d" % (tw, th), fps)
        pinfo = media_info(pf)
        inputs += ["-i", str(pf)]
        fade = float((piece.get("place") or {}).get("fadeIn", 0.25))
        pv = "[%d:v]scale=%d:%d,setsar=1,fps=%d[po]" % (n, tw, th, fps)
        parts.append(pv)
        if fade > 0:
            parts.append("%s[po]xfade=transition=fade:duration=%f:offset=%f[vx]" % (v, fade, max(0.0, length - fade)))
        else:
            parts.append("%s[po]concat=n=2:v=1:a=0[vx]" % v)
        v = "[vx]"
        pa = "[%d:a]aresample=48000[pa]" % n if pinfo.get("audio") else "anullsrc=r=48000:cl=stereo,atrim=duration=%f[pa]" % pinfo.get("duration", 3)
        parts.append(pa)
        if a:
            if fade > 0:
                parts.append("%s[pa]acrossfade=d=%f[ax]" % (a, fade))
            else:
                parts.append("%s[pa]concat=n=2:v=0:a=1[ax]" % a)
            a = "[ax]"
        n += 1
    out = Path(args.output) if args.output else out_dir() / "exports" / ("%s_%s%s%s.mp4" % (
        folder.name, args.platform or "%dx%d" % (tw, th), "" if ui else "_noui", "" if audio != "none" else "_silent"))
    out.parent.mkdir(parents=True, exist_ok=True)
    cmd = [ffmpeg(), "-hide_banner", "-loglevel", "error", "-y"] + inputs + ["-filter_complex", ";".join(parts), "-map", v]
    if a:
        cmd += ["-map", a, "-c:a", "aac", "-b:a", "192k"]
    cmd += ["-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(out)]
    run(cmd)
    info = media_info(out)
    res = {"export": str(out), "duration": info.get("duration"), "size": info.get("size"), "audio": info.get("audio"), "ui": ui, "outro": outro}
    if plat and info.get("duration", 0) > plat["maxSeconds"]:
        res["warning"] = "longer than %s allows (%ss)" % (args.platform, plat["maxSeconds"])
    say(res)


# ------------------------------------------------------------------ edits: plan -> Blender video-editing project

EDIT_TEMPLATE = {
    "name": "", "format": "vertical", "fps": 30, "platforms": ["tiktok", "shorts", "reels"],
    "timeline": [],
    "pieces": [],
    "captions": {"from": "events", "kinds": ["text"], "show": False},
    "music": None,
    "post": {"caption": "", "hashtags": []},
}


def edit_path(name):
    return EDITS / (slug(name) + ".edit.json")


def load_edit(name):
    p = edit_path(name)
    if not p.exists():
        fail("no edit '%s' (%s): studio.py edit new %s --take TAKE" % (name, p, name), 2)
    d = read_json(p)
    d.setdefault("name", slug(name))
    return d, p


def edit_folder(name):
    return out_dir() / "edits" / slug(name)


def plan_timeline(edit):
    """Resolves takes and places the pieces by their role: intro first, clips in order, sting then outro last,
    overlays and lower thirds on top at their time. Returns the list Blender builds the project from."""
    fps = int(edit.get("fps") or CFG["defaults"]["fps"])
    w, h = size_of(edit.get("format") or CFG["defaults"]["format"])
    clips, overlays, intros, endings = [], [], [], []
    for item in edit.get("timeline") or []:
        if "take" in item:
            folder, t = load_take(item["take"])
            dur = float(t.get("duration") or 0)
            cin = float(item.get("in", 0))
            cout = float(item.get("out", dur)) if item.get("out") is not None else dur
            clips.append({"kind": "take", "folder": str(folder), "take": t, "in": cin, "out": min(cout, dur),
                          "ui": item.get("ui", True), "audio": item.get("audio", "master"), "volume": item.get("volume", 1.0),
                          "fadeIn": item.get("fadeIn", 0), "fadeOut": item.get("fadeOut", 0)})
        elif "piece" in item:
            clips.append({"kind": "piece", "piece": item["piece"], "inline": True})
        elif "video" in item:
            clips.append({"kind": "video", "file": str((ROOT / item["video"]).resolve()) if not Path(item["video"]).is_absolute() else item["video"],
                          "in": float(item.get("in", 0)), "out": item.get("out")})
    names = list(edit.get("pieces") or [])
    for name in names:
        piece = load_piece(name)
        at = (piece.get("place") or {}).get("at", "end")
        entry = {"kind": "piece", "piece": name, "role": piece.get("role")}
        if piece.get("role") in ("overlay", "lowerthird"):
            entry["at"] = float(at) if not isinstance(at, str) else 0.0
            overlays.append(entry)
        elif at == "start":
            intros.append(entry)
        else:
            endings.append(entry)
    endings.sort(key=lambda e: 1 if e["role"] == "outro" else 0)  # a sting before the outro; the outro is always last
    seq = intros + clips + endings
    resolved = []
    for e in seq + overlays:
        if e["kind"] == "piece":
            piece = load_piece(e["piece"])
            e = dict(e, job=piece_job(piece, w, h, fps), duration=float(piece.get("duration", 3)), place=piece.get("place") or {})
        resolved.append(e)
    return {"w": w, "h": h, "fps": fps, "sequence": resolved[:len(seq)], "overlays": resolved[len(seq):]}


def cmd_edit(args):
    if args.action == "ls":
        for p in sorted(EDITS.glob("*.edit.json")):
            d = read_json(p)
            blend = edit_folder(p.name.replace(".edit.json", "")) / (p.name.replace(".edit.json", "") + ".blend") if out_dir(False) else None
            print("%-28s %-9s %-24s %s" % (p.name.replace(".edit.json", ""), d.get("format"), ",".join(d.get("platforms") or []),
                                           "blend" if blend and blend.exists() else "-"))
    elif args.action == "new":
        if not args.name or not args.take:
            fail("edit new NAME --take TAKE [--take TAKE2] [--format vertical] [--outro NAME]")
        p = edit_path(args.name)
        if p.exists():
            fail("%s exists" % p)
        first = load_take(args.take[0])[1]
        fmt = args.format or first.get("format") or CFG["defaults"]["format"]
        d = json.loads(json.dumps(EDIT_TEMPLATE))
        d.update(name=slug(args.name), format=fmt, fps=first.get("fps", 30))
        d["platforms"] = [k for k, v in platforms().items() if v["format"] == fmt][:3] or []
        d["timeline"] = [{"take": load_take(t)[0].name, "in": 0, "ui": True, "audio": "master"} for t in args.take]
        outro = None if args.outro == "none" else (args.outro or CFG["defaults"].get("outro"))
        d["pieces"] = [outro] if outro else []
        write_json(p, d)
        print("wrote", p)
    elif args.action == "show":
        d, p = load_edit(args.name)
        print(p)
        say(d)
    elif args.action == "build":
        build_edit(args.name, args.force)
    elif args.action == "render":
        out = render_edit(args.name)
        say({"render": str(out), "seconds": media_info(out).get("duration")})
    elif args.action == "open":
        d, _ = load_edit(args.name)
        blend_file = edit_folder(d["name"]) / (d["name"] + ".blend")
        if not blend_file.exists():
            fail("not built yet: studio.py edit build " + d["name"])
        subprocess.Popen([blender(), str(blend_file)])
        print("opened", blend_file)


def build_edit(name, force=False, quiet=False):
    d, _ = load_edit(name)
    folder = edit_folder(d["name"])
    folder.mkdir(parents=True, exist_ok=True)
    blend_file = folder / (d["name"] + ".blend")
    stamp = folder / "built.json"
    if blend_file.exists():
        built = read_json(stamp) if stamp.exists() else {}
        hand_edited = blend_file.stat().st_mtime > built.get("mtime", 0) + 2
        if hand_edited and not force:
            fail("%s was changed after the last build (hand-edited?). Rebuilding replaces it; the old file is kept as a backup. "
                 "Pass --force to rebuild, or edit render to render it as it is." % blend_file)
        backup = folder / "history" / ("%s_%s.blend" % (d["name"], now_id()))
        backup.parent.mkdir(exist_ok=True)
        shutil.copy2(blend_file, backup)
    plan = plan_timeline(d)
    captions = d.get("captions") or {}
    a = run_blender("assemble", {"edit": d, "plan": plan, "blend": str(blend_file), "captions": captions,
                                 "renders": str(folder / "renders"), "font": piece_vars().get("font", "")})
    write_json(stamp, {"mtime": blend_file.stat().st_mtime, "built": now_id(), "plan": d})
    if quiet:
        return blend_file
    say({"blend": str(blend_file), "frames": a.get("frames"), "seconds": a.get("seconds"), "strips": a.get("strips"),
         "open": "studio.py edit open %s" % d["name"]})


def render_edit(name):
    d, _ = load_edit(name)
    folder = edit_folder(d["name"])
    blend_file = folder / (d["name"] + ".blend")
    if not blend_file.exists():
        build_edit(name, quiet=True)
    out = folder / "renders" / ("%s.mp4" % d["name"])
    out.parent.mkdir(parents=True, exist_ok=True)
    a = run_blender("render", {"blend": str(blend_file), "output": str(out)}, timeout=3600)
    return Path(a.get("output"))


# ------------------------------------------------------------------ publish: final files per platform

def cmd_publish(args):
    d, _ = load_edit(args.edit)
    src = render_edit(args.edit)
    plats = args.platform.split(",") if args.platform else (d.get("platforms") or [])
    pubs = []
    info = media_info(src)
    stamp = now_id()
    base = out_dir() / "published" / ("%s_%s" % (d["name"], stamp))
    base.mkdir(parents=True, exist_ok=True)
    thumb = base / "thumbnail.jpg"
    at = float((d.get("post") or {}).get("thumbnailAt", min(1.0, info.get("duration", 2) / 3)))
    run([ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-ss", str(at), "-i", str(src), "-frames:v", "1", "-q:v", "2", str(thumb)])
    srt = captions_srt(d, base / "captions.srt")
    for pl in plats:
        spec = platforms().get(pl)
        if not spec:
            fail("unknown platform " + pl)
        w, h = size_of(spec["format"])
        target = base / ("%s_%s.mp4" % (d["name"], pl))
        cmd = [ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-i", str(src), "-t", str(spec["maxSeconds"]),
               "-vf", "scale=%d:%d:force_original_aspect_ratio=increase,crop=%d:%d,setsar=1,fps=%d" % (w, h, w, h, spec.get("fps", 30)),
               "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "192k",
               "-movflags", "+faststart", str(target)]
        run(cmd)
        warn = []
        dur = info.get("duration", 0)
        if dur > spec["maxSeconds"]:
            warn.append("cut to %ss" % spec["maxSeconds"])
        lo, hi = spec.get("bestSeconds", [0, 9999])
        if not lo <= dur <= hi:
            warn.append("%.1fs is outside the usual %s-%ss for %s" % (dur, lo, hi, pl))
        if (w, h) != tuple(info.get("size") or (w, h)):
            warn.append("edit is %s, cropped to %dx%d" % ("x".join(map(str, info.get("size", []))), w, h))
        pubs.append({"platform": pl, "file": str(target), "warnings": warn})
    post = d.get("post") or {}
    text = (post.get("caption") or "") + ("\n\n" + " ".join(post.get("hashtags") or []) if post.get("hashtags") else "")
    (base / "post.txt").write_text(text.strip() + "\n", encoding="utf-8")
    md = ["# %s" % d["name"], "", "Rendered from `%s` on %s." % (src.name, stamp), "", "## Files", ""]
    md += ["- **%s**: `%s`%s" % (p["platform"], Path(p["file"]).name, (" — " + "; ".join(p["warnings"])) if p["warnings"] else "") for p in pubs]
    md += ["", "Thumbnail: `thumbnail.jpg`. Captions: `captions.srt`." if srt else "", "", "## Post text", "", text or "(none: set post.caption in the edit)"]
    (base / "README.md").write_text("\n".join(md) + "\n", encoding="utf-8")
    say({"folder": str(base), "files": pubs, "thumbnail": str(thumb), "captions": str(srt) if srt else None, "post": str(base / "post.txt")})


def captions_srt(edit, target):
    """Subtitles from the takes' on-screen text events, timed on the edit's timeline (the outro adds no captions)."""
    plan = plan_timeline(edit)
    kinds = set((edit.get("captions") or {}).get("kinds") or ["text"])
    offset, lines = 0.0, []
    for e in plan["sequence"]:
        if e["kind"] == "take":
            evf = Path(e["folder"]) / "events.jsonl"
            events = []
            if evf.exists():
                for line in evf.read_text(encoding="utf-8").splitlines():
                    try:
                        events.append(json.loads(line))
                    except ValueError:
                        pass
            texts = [ev for ev in events if ev.get("kind") in kinds]
            for i, ev in enumerate(texts):
                t0 = float(ev["t"])
                t1 = float(texts[i + 1]["t"]) if i + 1 < len(texts) else e["out"]
                if ev.get("text") in ("", "(none)") or t1 <= e["in"] or t0 >= e["out"]:
                    continue
                a, b = max(t0, e["in"]) - e["in"] + offset, min(t1, e["out"]) - e["in"] + offset
                lines.append((a, b, ev["text"]))
            offset += e["out"] - e["in"]
        elif e["kind"] == "piece":
            offset += e.get("duration", 0)
    if not lines:
        return None

    def ts(s):
        ms = int(round(s * 1000))
        return "%02d:%02d:%02d,%03d" % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, ms % 1000)

    target.write_text("\n".join("%d\n%s --> %s\n%s\n" % (i + 1, ts(a), ts(b), t) for i, (a, b, t) in enumerate(lines)), encoding="utf-8")
    return target


# ------------------------------------------------------------------ main

def main(argv=None):
    p = argparse.ArgumentParser(prog="studio.py", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)
    sub.add_parser("doctor").set_defaults(func=cmd_doctor)
    s = sub.add_parser("init")
    s.add_argument("--output", help="outputDir: takes, Blender projects and exports (outside Git)")
    s.set_defaults(func=cmd_init)
    s = sub.add_parser("shot")
    s.add_argument("action", choices=["new", "ls", "show"])
    s.add_argument("name", nargs="?")
    s.add_argument("--idea", default="")
    s.set_defaults(func=cmd_shot)
    for name in ("render", "still"):
        s = sub.add_parser(name)
        s.add_argument("shot")
        s.add_argument("--format", help="vertical, wide, square, portrait or WxH (default: the shot's formats)")
        s.add_argument("--scale", type=float, help="multiply the size (2 = 4K from a 1080p format)")
        s.add_argument("--player", help="agent player name (default studio-<shot>)")
        s.add_argument("--keep-player", action="store_true", help="reuse a running player and leave it running")
        if name == "render":
            s.add_argument("--fps", type=int)
            s.add_argument("--realtime", action="store_true", help="record in real time instead of fixed time steps")
            s.add_argument("--no-ui", action="store_true", help="skip the UI layer")
            s.add_argument("--no-audio", action="store_true")
            s.add_argument("--stems", help="extra audio passes, e.g. voice,music,ambience,sfx")
            s.set_defaults(func=cmd_render)
        else:
            s.add_argument("--at", type=float, help="seconds after setup")
            s.set_defaults(func=cmd_still)
    s = sub.add_parser("takes")
    s.add_argument("shot", nargs="?")
    s.set_defaults(func=cmd_takes)
    s = sub.add_parser("take")
    s.add_argument("take")
    s.set_defaults(func=cmd_take)
    s = sub.add_parser("sheet")
    s.add_argument("take")
    s.set_defaults(func=cmd_sheet)
    s = sub.add_parser("export")
    s.add_argument("take")
    s.add_argument("--ui", choices=["on", "off"], default="on")
    s.add_argument("--audio", help="master (default), none, or stems joined by + (voice+sfx)")
    s.add_argument("--outro", help="piece to end with (default: defaults.outro), none for no outro")
    s.add_argument("--platform")
    s.add_argument("--in", dest="start", type=float)
    s.add_argument("--out", dest="end", type=float)
    s.add_argument("-o", "--output")
    s.set_defaults(func=cmd_export)
    s = sub.add_parser("piece")
    s.add_argument("action", choices=["ls", "new", "show", "render"])
    s.add_argument("name", nargs="?")
    s.add_argument("--role", default="outro")
    s.add_argument("--format")
    s.add_argument("--fps", type=int)
    s.add_argument("--force", action="store_true")
    s.set_defaults(func=cmd_piece)
    s = sub.add_parser("edit")
    s.add_argument("action", choices=["new", "ls", "show", "build", "render", "open"])
    s.add_argument("name", nargs="?")
    s.add_argument("--take", action="append")
    s.add_argument("--format")
    s.add_argument("--outro")
    s.add_argument("--platform")
    s.add_argument("--force", action="store_true")
    s.set_defaults(func=cmd_edit)
    s = sub.add_parser("publish")
    s.add_argument("edit")
    s.add_argument("--platform")
    s.set_defaults(func=cmd_publish)
    args = p.parse_args(argv)
    try:
        args.func(args)
    except RuntimeError as e:
        fail(str(e))


if __name__ == "__main__":
    main()
