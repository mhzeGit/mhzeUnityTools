"""Gameplay video for agentplay.py: record what a player sees, turn it into a watchable MP4 with captions, and let agents
"watch" it as keyframe contact sheets. Standard library only (Pillow optional for sheets, ffmpeg optional for MP4).

  agentplay.py record NAME start [--label TEXT] [--fps 15] [--res 960x540]   start recording player NAME
  agentplay.py record NAME mark "text"                                         add a caption/event at this moment
  agentplay.py record NAME stop                                                stop, encode, store in Builds/AgentPlay/videos/<id>
  agentplay.py video list | info ID | sheet ID [--from S --to S] | frame ID --at S | open ID | prune --keep N

The player side is the Agentic Tools package's AgentPlayRecorder (op `record`): it renders the game camera, reads it back
from the GPU without a stall and streams frames into ffmpeg (or writes JPEG frames when there is no ffmpeg), plus a JPEG
keyframe every 2 s and an event log (the ops the agent sent, their results, marks, stage labels, on-screen text).
Installed from the Agentic Tools package (Install~/files/tools/agentplay): change it there, not in a project.
"""
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time
from pathlib import Path

ap = None  # the agentplay module, set by register()
VIDEOS = None
_FFMPEG = "unset"

KIND_TAG = {"op": "", "result": "", "mark": "* ", "segment": "## ", "label": "", "text": "“", "finding": "! ", "look": "\u25c9 "}


# ------------------------------------------------------------------ ffmpeg

def ffmpeg():
    """Path to an ffmpeg executable or None: AGENTPLAY_FFMPEG, agentplay.json "ffmpeg", PATH, then the imageio-ffmpeg
    package (`python -m pip install imageio-ffmpeg` bundles one)."""
    global _FFMPEG
    if _FFMPEG != "unset":
        return _FFMPEG
    found = os.environ.get("AGENTPLAY_FFMPEG") or (ap.CONFIG.get("ffmpeg") if ap else None) or shutil.which("ffmpeg")
    if not found:
        try:
            import imageio_ffmpeg
            found = imageio_ffmpeg.get_ffmpeg_exe()
        except Exception:
            found = None
    _FFMPEG = found if found and Path(found).is_file() else None
    return _FFMPEG


def encoder():
    """The H.264 encoder ffmpeg has ("libx264"), else "mpeg4"; cached beside the builds."""
    ff = ffmpeg()
    if not ff:
        return None
    cache = ap.HOME / "_tools" / "ffmpeg.json"
    try:
        known = json.loads(cache.read_text(encoding="utf-8"))
        if known.get("path") == ff:
            return known["codec"]
    except (OSError, ValueError, KeyError):
        pass
    out = run([ff, "-hide_banner", "-encoders"], 60).stdout
    codec = "libx264" if " libx264 " in out else "mpeg4"
    cache.parent.mkdir(parents=True, exist_ok=True)
    cache.write_text(json.dumps({"path": ff, "codec": codec}), encoding="utf-8")
    return codec


def run(cmd, timeout=600):
    return subprocess.run([str(c) for c in cmd], capture_output=True, text=True, errors="replace", timeout=timeout,
                          creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))


# ------------------------------------------------------------------ recording control

def slug(text, limit=40):
    return re.sub(r"[^A-Za-z0-9_-]+", "-", text or "").strip("-")[:limit]


def start(name, label="", fps=15, res="960x540", quality=72, crf=26, max_minutes=60, key_every=2.0, rec_id=None,
          use_ffmpeg=True, source="screen"):
    """Starts recording player NAME. Returns the player's answer (with "id")."""
    width, height = (int(v) for v in res.lower().split("x"))
    rec_id = rec_id or "%s-%s%s" % (time.strftime("%Y%m%d-%H%M%S"), name, "-" + slug(label) if label else "")
    command = {"op": "record", "action": "start", "id": rec_id, "label": label or name, "fps": fps, "w": width, "h": height,
               "quality": quality, "crf": crf, "max": max_minutes * 60, "key_every": key_every, "source": source}
    if use_ffmpeg and ffmpeg():
        command["ffmpeg"] = ffmpeg()
        command["codec"] = encoder()
    return ap.send(name, command, 60)[1]


def mark(name, text, kind="mark"):
    return ap.send(name, {"op": "record", "action": "mark", "kind": kind, "text": text}, 30)[1]


def elapsed(name):
    """Seconds into the running recording (None when not recording): the exact video time of 'now'."""
    answer = ap.send(name, {"op": "record", "action": "status"}, 30)[1]
    return answer.get("elapsed") if answer.get("ok") and answer.get("recording") else None


def stop(name, extra=None, keep_frames=False):
    """Stops recording player NAME and stores the video. Returns its meta (dict) or raises SystemExit via ap.fail."""
    folder, answer = ap.send(name, {"op": "record", "action": "stop"}, 240)
    if not answer.get("ok"):
        ap.fail("record stop failed: %s" % answer.get("error"))
    return finalize(folder / "recordings" / answer["id"], player=name, extra=extra, keep_frames=keep_frames)


def rescue(run_folder):
    """Before `start` wipes a run folder: keep recordings a crashed or killed player left behind."""
    rec_root = Path(run_folder) / "recordings"
    if not rec_root.is_dir():
        return
    for rec in rec_root.iterdir():
        if rec.is_dir() and not (VIDEOS / rec.name).exists():
            try:
                finalize(rec, player=Path(run_folder).name, extra={"rescued": True})
                print("kept unfinished recording %s" % rec.name, file=sys.stderr)
            except Exception as error:  # never block a start
                print("could not keep recording %s: %s" % (rec.name, error), file=sys.stderr)


# ------------------------------------------------------------------ finalize

def read_events(folder):
    events = []
    path = folder / "events.jsonl"
    if path.exists():
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            try:
                events.append(json.loads(line))
            except ValueError:
                pass
    return events


def clean_events(events):
    """Without the learner's own zero-wait checks (`until ... timeout=0`, and their results): they are bookkeeping, not play."""
    out, skip_result = [], False
    for e in events:
        text = e.get("text", "")
        if e.get("kind") == "op" and text.startswith("until ") and re.search(r"\btimeout=0(\.0)?\b", text):
            skip_result = True
            continue
        if e.get("kind") == "result" and skip_result and text.startswith("until"):
            skip_result = False
            continue
        skip_result = False
        out.append(e)
    return out


def vtt_time(t):
    t = max(0.0, t)
    return "%02d:%02d:%02d.%03d" % (int(t // 3600), int(t % 3600 // 60), int(t % 60), int(round(t % 1 * 1000)) % 1000)


def captions(events, duration):
    """WebVTT: the stage label stays at the top until it changes; ops, results and marks at the bottom; on-screen text
    in the middle. Each cue lasts until the next one in its lane, at most 4 s (labels: until the next label)."""
    lanes = {"label": [], "text": [], "act": []}
    for e in events:
        kind = e.get("kind", "mark")
        lane = "label" if kind == "label" else "text" if kind == "text" else "act"
        text = e.get("text", "").replace("-->", "->").strip()
        if not text:
            continue
        if kind == "result":
            text = ("✖ " if " FAILED" in text else "✔ ") + text
        elif kind == "op":
            text = "▶ " + text
        else:
            text = KIND_TAG.get(kind, "") + text + ("”" if kind == "text" else "")
        lanes[lane].append((float(e.get("t", 0)), text))
    settings = {"label": " line:4% align:start position:2%", "text": " line:62%", "act": " line:88%"}
    cues = []
    for lane, items in lanes.items():
        for i, (t, text) in enumerate(items):
            nxt = items[i + 1][0] if i + 1 < len(items) else duration
            end = nxt if lane == "label" else min(nxt, t + 4.0)
            cues.append((t, max(end, t + 0.6), text, settings[lane]))
    cues.sort(key=lambda c: c[0])
    lines = ["WEBVTT", ""]
    for n, (t, end, text, setting) in enumerate(cues, 1):
        lines += [str(n), "%s --> %s%s" % (vtt_time(t), vtt_time(end), setting), text, ""]
    return "\n".join(lines)


def finalize(src, player="", extra=None, keep_frames=False):
    """Moves a player's recording folder into the video store and makes it watchable: fast-start MP4 with a captions
    track (or MP4 from JPEG frames, or MJPEG AVI without ffmpeg), WebVTT captions, chapters, a keyframe contact sheet."""
    src = Path(src)
    VIDEOS.mkdir(parents=True, exist_ok=True)
    dst = VIDEOS / src.name
    n = 2
    while dst.exists():
        dst = VIDEOS / ("%s-%d" % (src.name, n))
        n += 1
    for attempt in range(20):  # the player may still be closing files for a moment
        try:
            shutil.move(str(src), str(dst))
            break
        except OSError:
            time.sleep(0.5)
    else:
        ap.fail("could not move %s (still open?)" % src)

    rec = {}
    try:
        rec = json.loads((dst / "recording.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        pass
    events = clean_events(read_events(dst))
    keyframes = rec.get("keyframes") or [{"t": int(p.stem[2:]) / 1000.0, "file": "keyframes/" + p.name}
                                         for p in sorted((dst / "keyframes").glob("k_*.jpg"))]
    keyframes.sort(key=lambda k: k["t"])
    duration = float(rec.get("duration") or (max([e["t"] for e in events] + [k["t"] for k in keyframes] + [0.0])))
    fps = int(rec.get("fps") or 15)
    (dst / "captions.vtt").write_text(captions(events, duration), encoding="utf-8")

    video, kind, notes = None, "frames", []
    raw = dst / "video.mp4"
    ff = ffmpeg()
    if raw.exists() and raw.stat().st_size > 0:
        video, kind = "video.mp4", "mp4"
        if ff:  # fast start (plays while loading); video only: the events are shown beside the video, never over it
            tmp = dst / "video.tmp.mp4"
            proc = run([ff, "-hide_banner", "-loglevel", "error", "-y", "-i", raw, "-map", "0:v", "-c:v", "copy",
                        "-movflags", "+faststart", tmp], 900)
            if proc.returncode == 0 and tmp.exists() and tmp.stat().st_size > 0:
                os.replace(tmp, raw)
            else:
                tmp.unlink(missing_ok=True)
                notes.append("remux failed (the raw fragmented MP4 is kept): " + proc.stderr.strip()[-300:])
    elif (dst / "frames").is_dir():
        frames = read_frame_times(dst)
        if frames and ff:
            video, kind = encode_frames(dst, frames, fps, ff, notes)
        elif frames:
            write_avi(dst / "video.avi", [dst / "frames" / f for f, _ in frames], [t for _, t in frames], fps,
                      rec.get("width", 960), rec.get("height", 540))
            video, kind = "video.avi", "avi"
            notes.append("no ffmpeg: MJPEG AVI (plays in VLC and Windows Media Player). For MP4: python -m pip install imageio-ffmpeg")
        if video and not keep_frames:
            shutil.rmtree(dst / "frames", ignore_errors=True)
    if rec.get("sinkError"):
        notes.append("recorder: " + rec["sinkError"])

    chapters = []
    for e in events:
        if e.get("kind") in ("label", "segment") and (not chapters or chapters[-1]["title"] != e.get("text")):
            chapters.append({"t": e.get("t", 0), "title": e.get("text", "")})
    meta = {
        "id": dst.name, "player": player, "label": rec.get("label") or player,
        "created": rec.get("started") or time.strftime("%Y-%m-%d %H:%M:%S"), "createdEpoch": time.time() - duration,
        "duration": round(duration, 2), "fps": fps, "width": rec.get("width"), "height": rec.get("height"),
        "frames": rec.get("frames", 0), "captured": rec.get("captured", 0), "dropped": rec.get("dropped", 0),
        "video": video, "videoKind": kind, "captions": "captions.vtt", "keyframes": keyframes, "events": events,
        "chapters": chapters, "sink": rec.get("sink"), "readback": rec.get("readback"), "notes": notes,
        "playtest": None, "segment": None,
    }
    meta.update(extra or {})
    meta["sizeBytes"] = folder_size(dst)
    meta["sheets"] = make_sheets(dst, keyframes, overview=True)
    write_meta(dst, meta)
    return meta


def read_frame_times(folder):
    frames = []
    path = folder / "frames.jsonl"
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            try:
                item = json.loads(line)
                frames.append(("f_%06d.jpg" % item["i"], float(item["t"])))
            except (ValueError, KeyError):
                pass
    return [(f, t) for f, t in frames if (folder / "frames" / f).exists()]


def encode_frames(dst, frames, fps, ff, notes):
    """JPEG frames with their capture times -> constant-rate MP4 with captions (ffmpeg concat with per-frame durations)."""
    listing = ["ffconcat version 1.0"]
    for i, (name, t) in enumerate(frames):
        nxt = frames[i + 1][1] if i + 1 < len(frames) else t + 1.0 / fps
        listing += ["file 'frames/%s'" % name, "duration %.4f" % max(0.001, nxt - t)]
    listing.append("file 'frames/%s'" % frames[-1][0])
    (dst / "frames.ffconcat").write_text("\n".join(listing) + "\n", encoding="utf-8")
    codec = encoder() or "mpeg4"
    quality = ["-preset", "veryfast", "-crf", "26"] if codec == "libx264" else ["-q:v", "5"]
    proc = run([ff, "-hide_banner", "-loglevel", "error", "-y", "-f", "concat", "-safe", "0", "-i", dst / "frames.ffconcat",
                "-map", "0:v", "-fps_mode", "cfr", "-r", str(fps), "-c:v", codec,
                *quality, "-pix_fmt", "yuv420p", "-movflags", "+faststart", dst / "video.mp4"], 3600)
    (dst / "frames.ffconcat").unlink(missing_ok=True)
    if proc.returncode != 0:
        notes.append("ffmpeg could not encode the frames: " + proc.stderr.strip()[-300:])
        return None, "frames"
    return "video.mp4", "mp4"


def folder_size(folder):
    return sum(p.stat().st_size for p in Path(folder).rglob("*") if p.is_file())


def write_meta(folder, meta):
    tmp = Path(folder) / "meta.json.tmp"
    tmp.write_text(json.dumps(meta, indent=1), encoding="utf-8")
    os.replace(tmp, Path(folder) / "meta.json")


# ------------------------------------------------------------------ sheets (what agents look at instead of the video)

def make_sheets(folder, keyframes, overview=False, start=None, end=None, count=24, cols=4, name=None, events=None):
    """Contact sheet(s) of keyframes with their times (and the caption at that time when events are given).
    overview: one sheet of `count` frames spread over the whole video. Returns the sheet file names (Pillow needed)."""
    try:
        from PIL import Image, ImageDraw
    except ImportError:
        return []
    picked = [k for k in keyframes if (start is None or k["t"] >= start) and (end is None or k["t"] <= end)]
    if not picked:
        return []
    if len(picked) > count:
        step = (len(picked) - 1) / float(count - 1)
        picked = [picked[int(round(i * step))] for i in range(count)]
    width = 400
    with Image.open(Path(folder) / picked[0]["file"]) as first:
        height = int(width * first.height / max(1, first.width))
    rows = (len(picked) + cols - 1) // cols
    label_h = 30 if events else 16
    sheet = Image.new("RGB", (cols * width, rows * (height + label_h)), (16, 16, 18))
    draw = ImageDraw.Draw(sheet)
    for i, k in enumerate(picked):
        x, y = (i % cols) * width, (i // cols) * (height + label_h)
        try:
            with Image.open(Path(folder) / k["file"]) as im:
                sheet.paste(im.convert("RGB").resize((width, height)), (x, y + label_h))
        except OSError:
            continue
        draw.text((x + 4, y + 2), "%s" % clock(k["t"]), fill=(255, 210, 0))
        if events:
            near = [e for e in events if e.get("t", 0) <= k["t"] and e.get("kind") in ("op", "result", "mark", "segment")]
            if near:
                draw.text((x + 4, y + 15), near[-1].get("text", "")[:62], fill=(200, 200, 200))
    target = name or "sheet_overview.png"
    sheet.save(Path(folder) / target)
    return [target]


def clock(t):
    return "%d:%04.1f" % (int(t // 60), t % 60)


# ------------------------------------------------------------------ MJPEG AVI (no ffmpeg)

def write_avi(path, jpegs, times, fps, width, height):
    """Constant-rate MJPEG AVI from JPEG files and their capture times (a hitch repeats a frame). Standard library only."""
    order, emitted = [], 0
    for i, t in enumerate(times):
        target = int(t * fps) + 1
        for _ in range(max(0, min(target - emitted, fps * 30))):
            order.append(i)
        emitted = max(emitted, target)
    blobs = {}
    chunks = []
    for i in order:
        if i not in blobs:
            blobs[i] = Path(jpegs[i]).read_bytes()
        chunks.append(blobs[i])
    movi = bytearray(b"movi")
    index = bytearray()
    for data in chunks:
        offset = len(movi)
        pad = len(data) & 1
        movi += b"00dc" + struct.pack("<I", len(data)) + data + (b"\0" if pad else b"")
        index += b"00dc" + struct.pack("<III", 0x10, offset, len(data))
    biggest = max((len(c) for c in chunks), default=0)
    avih = struct.pack("<IIIIIIIIII4I", int(1e6 / fps), biggest * fps, 0, 0x10, len(chunks), 0, 1, biggest,
                       width, height, 0, 0, 0, 0)
    strh = b"vids" + b"MJPG" + struct.pack("<IHHIIIIIIIIhhhh", 0, 0, 0, 0, 1, fps, 0, len(chunks), biggest, 0xFFFFFFFF, 0,
                                          0, 0, width, height)
    strf = struct.pack("<IiiHH4sIiiII", 40, width, height, 1, 24, b"MJPG", width * height * 3, 0, 0, 0, 0)

    def chunk(fourcc, data):
        return fourcc + struct.pack("<I", len(data)) + data + (b"\0" if len(data) & 1 else b"")

    def lst(kind, data):
        return b"LIST" + struct.pack("<I", len(data) + 4) + kind + data

    strl = lst(b"strl", chunk(b"strh", strh) + chunk(b"strf", strf))
    hdrl = lst(b"hdrl", chunk(b"avih", avih) + strl)
    body = b"AVI " + hdrl + b"LIST" + struct.pack("<I", len(movi)) + bytes(movi) + chunk(b"idx1", bytes(index))
    Path(path).write_bytes(b"RIFF" + struct.pack("<I", len(body)) + body)


def recaption(meta):
    """Rebuilds a stored video's captions.vtt from its event log, and leaves the MP4 with the video track only (videos
    made before 1.6 carried the captions inside, which some players draw over the picture)."""
    folder = folder_of(meta)
    events = clean_events(read_events(folder))
    vtt = folder / "captions.vtt"
    vtt.write_text(captions(events, float(meta.get("duration") or 0)), encoding="utf-8")
    if meta.get("videoKind") == "mp4" and ffmpeg():
        tmp = folder / "video.tmp.mp4"
        proc = run([ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-i", folder / meta["video"], "-map", "0:v",
                    "-c:v", "copy", "-movflags", "+faststart", tmp], 900)
        if proc.returncode == 0 and tmp.exists() and tmp.stat().st_size > 0:
            os.replace(tmp, folder / meta["video"])
        else:
            tmp.unlink(missing_ok=True)
            ap.fail("ffmpeg could not swap the captions: " + proc.stderr[-300:])
    meta["events"] = events
    meta["sizeBytes"] = folder_size(folder)
    write_meta(folder, meta)
    return meta


def web_meta(meta, base):
    """The meta with ready URLs under `base` ("/media/videos/<id>/" for the server, "../../videos/<id>/" for a report),
    so the pages never build paths themselves. A browser cannot play AVI: then only keyframes play (a slideshow)."""
    if not meta:
        return None
    m = dict(meta)
    m["videoUrl"] = base + meta["video"] if meta.get("video") and meta.get("videoKind") == "mp4" else None
    m["captionsUrl"] = base + meta["captions"] if meta.get("captions") else None
    m["sheetUrls"] = [base + s for s in meta.get("sheets") or []]
    m["keyframes"] = [{"t": k["t"], "url": base + k["file"]} for k in meta.get("keyframes") or []]
    return m


# ------------------------------------------------------------------ the store

def all_meta():
    if VIDEOS is None or not VIDEOS.exists():
        return []
    metas = []
    for meta_file in VIDEOS.glob("*/meta.json"):
        try:
            metas.append(json.loads(meta_file.read_text(encoding="utf-8")))
        except (OSError, ValueError):
            pass
    metas.sort(key=lambda m: m.get("createdEpoch", 0), reverse=True)
    return metas


def find(ref):
    """A video by id, unique id fragment, or 'last'."""
    metas = all_meta()
    if not metas:
        ap.fail("no videos yet: agentplay.py record NAME start", 2)
    if ref in ("last", "latest"):
        return metas[0]
    exact = [m for m in metas if m["id"] == ref]
    hits = exact or [m for m in metas if ref in m["id"]]
    if len(hits) != 1:
        ap.fail("video '%s' matches %d videos%s" % (ref, len(hits), ": " + ", ".join(m["id"] for m in hits[:8]) if hits else ""))
    return hits[0]


def folder_of(meta):
    return VIDEOS / meta["id"]


def frame_at(meta, at, exact=False):
    """A picture at time `at`: the nearest keyframe, or (exact, with ffmpeg and an MP4) the exact frame. Returns a path."""
    folder = folder_of(meta)
    if exact and meta.get("videoKind") == "mp4" and ffmpeg():
        out = folder / "at" / ("t_%07.2f.png" % at)
        out.parent.mkdir(exist_ok=True)
        if not out.exists():
            proc = run([ffmpeg(), "-hide_banner", "-loglevel", "error", "-y", "-ss", "%.3f" % at, "-i", folder / meta["video"],
                        "-frames:v", "1", out], 120)
            if proc.returncode != 0:
                ap.fail("ffmpeg could not extract a frame: " + proc.stderr[-300:])
        return out
    keys = meta.get("keyframes") or []
    if not keys:
        ap.fail("this video has no keyframes")
    return folder / min(keys, key=lambda k: abs(k["t"] - at))["file"]


# ------------------------------------------------------------------ commands

def cmd_record(args):
    if args.action == "start":
        answer = start(args.name, args.label or "", args.fps, args.res, args.quality, args.crf, args.max_minutes,
                       args.key_every, args.id, not args.no_ffmpeg, args.source)
        ap.finish(answer)
        print("RECORDING %s (%s); stop with: agentplay.py record %s stop" % (answer.get("id"), answer.get("sink"), args.name))
    elif args.action == "mark":
        ap.finish(mark(args.name, " ".join(args.text) or "mark"))
    elif args.action == "snap":
        ap.finish(ap.send(args.name, {"op": "record", "action": "snap"}, 30)[1])
    elif args.action == "status":
        ap.finish(ap.send(args.name, {"op": "record", "action": "status"}, 30)[1])
    else:
        meta = stop(args.name, keep_frames=args.keep_frames)
        print_meta(meta)


def print_meta(meta, events=False):
    folder = folder_of(meta)
    short = {k: v for k, v in meta.items() if k not in ("events", "keyframes")}
    short["keyframes"] = len(meta.get("keyframes") or [])
    short["eventCount"] = len(meta.get("events") or [])
    short["folder"] = str(folder)
    if meta.get("video"):
        short["path"] = str(folder / meta["video"])
    short["sheetPaths"] = [str(folder / s) for s in meta.get("sheets") or []]
    if events:
        short["events"] = meta.get("events")
    print(json.dumps(short, indent=1))
    if meta.get("video"):
        print("VIDEO %s (%.0f s, %s). Agents: look at the sheet, not the video: agentplay.py video sheet %s [--from S --to S]"
              % (folder / meta["video"], meta.get("duration", 0), meta.get("videoKind"), meta["id"]))


def cmd_video(args):
    if args.action == "list":
        metas = all_meta()[:args.limit]
        if not metas:
            print("no videos yet: agentplay.py record NAME start")
        for m in metas:
            print("%-48s %6.0fs %7.1f MB  %-5s %s" % (m["id"], m.get("duration", 0), m.get("sizeBytes", 0) / 1e6,
                                                     m.get("videoKind"), m.get("label", "")))
        return
    if args.action == "prune":
        metas = all_meta()
        keep = {m["id"] for m in metas[:args.keep]}
        for m in metas:
            if m["id"] not in keep and (args.days is None or time.time() - m.get("createdEpoch", 0) > args.days * 86400):
                shutil.rmtree(folder_of(m), ignore_errors=True)
                print("removed " + m["id"])
        return
    if not args.ref:
        ap.fail("name a video (id, part of an id, or 'last')")
    meta = find(args.ref)
    folder = folder_of(meta)
    if args.action == "info":
        print_meta(meta, events=args.events)
    elif args.action == "recaption":
        meta = recaption(meta)
        print("captions rebuilt: %d events in %s" % (len(meta["events"]), folder / "captions.vtt"))
    elif args.action == "path":
        print(folder / (meta.get("video") or ""))
    elif args.action == "open":
        target = folder / (meta.get("video") or "")
        os.startfile(str(target))  # noqa: the user's default player
        print(target)
    elif args.action == "sheet":
        name = "sheet_%s_%s.png" % (int(args.start or 0), int(args.end if args.end is not None else meta.get("duration", 0)))
        made = make_sheets(folder, meta.get("keyframes") or [], start=args.start, end=args.end, count=args.count,
                           cols=args.cols, name=name, events=meta.get("events"))
        if not made:
            ap.fail("no sheet: no keyframes in that range, or Pillow is missing (python -m pip install pillow)")
        print(folder / made[0])
    elif args.action == "frame":
        if args.at is None:
            ap.fail("--at SECONDS is required")
        print(frame_at(meta, args.at, args.exact))
    elif args.action == "events":
        for e in meta.get("events") or []:
            if args.kind and e.get("kind") != args.kind:
                continue
            print("%8s  %-7s %s" % (clock(e.get("t", 0)), e.get("kind"), e.get("text")))


def register(sub, ap_module):
    global ap, VIDEOS
    ap = ap_module
    VIDEOS = ap.HOME / "videos"
    ap.BEFORE_CLEAN.append(rescue)

    p = sub.add_parser("record", help="record what player NAME sees: start, mark, snap, status, stop")
    p.add_argument("name")
    p.add_argument("action", choices=("start", "stop", "mark", "snap", "status"))
    p.add_argument("text", nargs="*", help="mark: the caption text")
    p.add_argument("--label", help="start: what this recording shows (kept in the video's name and meta)")
    p.add_argument("--id", help="start: the video id (default: time-player-label)")
    p.add_argument("--fps", type=int, default=15)
    p.add_argument("--res", default="960x540")
    p.add_argument("--quality", type=int, default=72, help="JPEG quality of keyframes (and frames without ffmpeg)")
    p.add_argument("--crf", type=int, default=26, help="H.264 quality: lower is better and bigger")
    p.add_argument("--max-minutes", type=float, default=60, help="the recording stops by itself after this")
    p.add_argument("--key-every", type=float, default=2.0, help="seconds between keyframes (the pictures agents look at)")
    p.add_argument("--no-ffmpeg", action="store_true", help="write JPEG frames instead of streaming into ffmpeg")
    p.add_argument("--source", choices=("screen", "camera"), default="screen",
                   help="start: the finished screen as the player sees it (default), or one camera re-rendered (no overlay UI)")
    p.add_argument("--keep-frames", action="store_true", help="stop: keep JPEG frames after encoding")
    p.set_defaults(func=cmd_record)

    p = sub.add_parser("video", help="recorded videos: list, info, sheet, frame, events, path, open, prune, recaption")
    p.add_argument("action", choices=("list", "info", "sheet", "frame", "events", "path", "open", "prune", "recaption"))
    p.add_argument("ref", nargs="?", help="video id, part of it, or 'last'")
    p.add_argument("--limit", type=int, default=20)
    p.add_argument("--from", dest="start", type=float)
    p.add_argument("--to", dest="end", type=float)
    p.add_argument("--count", type=int, default=12)
    p.add_argument("--cols", type=int, default=4)
    p.add_argument("--at", type=float)
    p.add_argument("--exact", action="store_true", help="frame: the exact frame from the MP4 (ffmpeg), not the nearest keyframe")
    p.add_argument("--events", action="store_true", help="info: include every event")
    p.add_argument("--kind", help="events: only this kind (op, result, mark, segment, label, text)")
    p.add_argument("--keep", type=int, default=30, help="prune: keep the newest N")
    p.add_argument("--days", type=float, help="prune: only remove videos older than this")
    p.set_defaults(func=cmd_video)
