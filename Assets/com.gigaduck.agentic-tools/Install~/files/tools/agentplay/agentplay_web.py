"""The agentplay dashboard and HTTP API: players, recordings, playtest reports and the knowledge base, on localhost.

  agentplay.py serve [--port 8765] [--open]     run it here (Ctrl+C stops it)
  agentplay.py serve --detach [--open]          run it in the background; `serve --stop` ends it; `serve --status`

API (JSON; same data as the CLI):
  GET  /api/status                              project, build, ffmpeg, knowledge folder
  GET  /api/players                             players and whether they record
  POST /api/players/<name>/send       {cmd}     send one harness command, get its answer
  POST /api/players/<name>/record     {"action":"start|stop|mark","label":..,"text":..}   stop returns the video
  GET  /api/videos, /api/videos/<id>            video meta with ready URLs (videoUrl, captionsUrl, keyframes[].url ...)
  GET  /api/videos/<id>/frame?t=12.5            the picture at that time (nearest keyframe; &exact=1 decodes the MP4)
  GET  /api/knowledge, /api/knowledge/<segment> mastery per segment; one segment's routes, lessons, history, values
  GET  /api/playtests, /api/playtests/<id>      playtest reports
  GET  /media/videos/<id>/<file>                the files (MP4 with byte ranges, so players can seek)
Listens on 127.0.0.1 only and answers only requests addressed to localhost. Standard library only.
Installed from the Agentic Tools package (Install~/files/tools/agentplay): change it there, not in a project.
"""
import json
import mimetypes
import os
import re
import subprocess
import sys
import threading
import time
import urllib.parse
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ap = None
STATE = None  # Builds/AgentPlay/web.json while a detached server runs
TYPES = {".mp4": "video/mp4", ".vtt": "text/vtt; charset=utf-8", ".jpg": "image/jpeg", ".png": "image/png",
         ".json": "application/json", ".html": "text/html; charset=utf-8", ".avi": "video/x-msvideo"}


class ApiError(Exception):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def running(name):
    folder = ap.run_dir(name)
    pid = ap.instance_pid(folder)
    return bool(pid and ap.pid_alive(pid, ap.EXE))


def players():
    out = []
    if ap.RUNS.exists():
        for folder in sorted(ap.RUNS.iterdir(), key=lambda p: p.stat().st_mtime, reverse=True):
            if not folder.is_dir():
                continue
            alive = running(folder.name) if re.fullmatch(r"[A-Za-z0-9_-]+", folder.name) else False
            detail, age = "", None
            beat = folder / "heartbeat.txt"
            if alive and beat.exists():
                try:
                    parts = beat.read_text(encoding="utf-8").split(" ", 1)
                    age = round(time.time() - int(parts[0]), 1)
                    detail = parts[1] if len(parts) > 1 else ""
                except (OSError, ValueError):
                    pass
            rec_root = folder / "recordings"
            recording = alive and rec_root.is_dir() and any(r.is_dir() and not (r / "recording.json").exists() for r in rec_root.iterdir())
            out.append({"name": folder.name, "state": "running" if alive else "stopped", "detail": detail,
                        "heartbeatAge": age, "recording": recording})
    return out


def video_web(meta):
    return ap.video.web_meta(meta, "/media/videos/%s/" % urllib.parse.quote(meta["id"]))


def find_video(vid):
    for meta in ap.video.all_meta():
        if meta["id"] == vid:
            return meta
    raise ApiError(404, "no video " + vid)


def guarded(fn, *args, **kw):
    """Runs a CLI function that may call ap.fail (sys.exit) and turns that into an API error."""
    try:
        return fn(*args, **kw)
    except SystemExit as e:
        raise ApiError(409, "the command failed (%s); see the server console for its message" % (e.code,))


class Handler(BaseHTTPRequestHandler):
    server_version = "agentplay"

    def log_message(self, fmt, *args):  # quiet: one line per error only
        pass

    # ------------------------------------------------------------- plumbing
    def allowed(self):
        host = (self.headers.get("Host") or "").split(":")[0].lower()
        if host not in ("127.0.0.1", "localhost"):
            self.send_error(403, "only localhost")
            return False
        origin = self.headers.get("Origin")
        if self.command == "POST" and origin and urllib.parse.urlparse(origin).hostname not in ("127.0.0.1", "localhost"):
            self.send_error(403, "cross-site request")
            return False
        return True

    def json(self, data, status=200):
        body = json.dumps(data).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(body)

    def body(self):
        length = int(self.headers.get("Content-Length") or 0)
        if length > 1_000_000:
            raise ApiError(413, "body too large")
        try:
            return json.loads(self.rfile.read(length) or b"{}")
        except ValueError:
            raise ApiError(400, "body is not JSON")

    def file(self, path):
        if not path.is_file():
            raise ApiError(404, "no such file")
        size = path.stat().st_size
        ctype = TYPES.get(path.suffix.lower()) or mimetypes.guess_type(str(path))[0] or "application/octet-stream"
        start, end = 0, size - 1
        rng = re.match(r"bytes=(\d*)-(\d*)$", self.headers.get("Range") or "")
        if rng and size:
            if rng.group(1):
                start = int(rng.group(1))
                end = int(rng.group(2)) if rng.group(2) else size - 1
            else:
                start = max(0, size - int(rng.group(2) or 0))
            end = min(end, size - 1)
            if start > end:
                self.send_response(416)
                self.send_header("Content-Range", "bytes */%d" % size)
                self.end_headers()
                return
            self.send_response(206)
            self.send_header("Content-Range", "bytes %d-%d/%d" % (start, end, size))
        else:
            self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        self.end_headers()
        with open(path, "rb") as f:
            f.seek(start)
            left = end - start + 1
            try:
                while left > 0:
                    chunk = f.read(min(1 << 20, left))
                    if not chunk:
                        break
                    self.wfile.write(chunk)
                    left -= len(chunk)
            except (ConnectionError, OSError):
                pass  # the browser stopped reading (seeking)

    def route(self, method):
        if not self.allowed():
            return
        url = urllib.parse.urlparse(self.path)
        parts = [urllib.parse.unquote(p) for p in url.path.strip("/").split("/") if p]
        query = urllib.parse.parse_qs(url.query)
        try:
            self.dispatch(method, parts, query)
        except ApiError as e:
            self.json({"error": str(e)}, e.status)
        except Exception as e:  # never kill the server on one bad request
            self.json({"error": "%s: %s" % (type(e).__name__, e)}, 500)

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        self.route("POST")

    # ------------------------------------------------------------- routes
    def dispatch(self, method, parts, query):
        if method == "GET" and not parts:
            page = Path(__file__).with_name("agentplay_dashboard.html")
            return self.file(page)
        if parts[:1] == ["media"] and method == "GET" and len(parts) >= 4 and parts[1] in ("videos", "playtests"):
            root = (ap.video.VIDEOS if parts[1] == "videos" else ap.learn.PLAYTESTS).resolve()
            target = (root / parts[2] / Path(*parts[3:])).resolve()
            if root not in target.parents:
                raise ApiError(403, "outside the store")
            return self.file(target)
        if parts[:1] != ["api"]:
            raise ApiError(404, "not found")
        what, rest = (parts[1] if len(parts) > 1 else ""), parts[2:]
        if method == "GET" and what == "status":
            build = ap.latest_build()
            info = ap.read_info(build) if build else None
            return self.json({"tool": "agentplay", "project": ap.ROOT.name,
                              "build": {"name": build.name, "builtAt": (info or {}).get("builtAt"), "current": None} if build else None,
                              "knowledgeDir": str(ap.learn.KB.root), "ffmpeg": bool(ap.video.ffmpeg())})
        if what == "players":
            if method == "GET" and not rest:
                return self.json({"players": players()})
            if method == "POST" and len(rest) == 2:
                name, action = rest
                if not re.fullmatch(r"[A-Za-z0-9_-]+", name) or not running(name):
                    raise ApiError(409, "player %s is not running" % name)
                body = self.body()
                if action == "send":
                    if not isinstance(body, dict) or "op" not in body:
                        raise ApiError(400, "send a command object with an 'op'")
                    return self.json(guarded(ap.send, name, body, float(body.get("timeout", 60)) + 30)[1])
                if action == "record":
                    act = body.get("action")
                    if act == "start":
                        return self.json(guarded(ap.video.start, name, body.get("label") or ""))
                    if act == "mark":
                        return self.json(guarded(ap.video.mark, name, body.get("text") or "mark"))
                    if act == "stop":
                        meta = guarded(ap.video.stop, name)
                        return self.json({"ok": True, "video": video_web(meta)})
                    raise ApiError(400, "action must be start, mark or stop")
            raise ApiError(404, "players: GET /api/players, POST /api/players/<name>/send|record")
        if method != "GET":
            raise ApiError(405, "GET only")
        if what == "videos":
            if not rest:
                lite = []
                for meta in ap.video.all_meta():
                    web = video_web(meta)
                    web.pop("events", None)
                    web["keyframes"] = web["keyframes"][:1]
                    lite.append(web)
                return self.json({"videos": lite})
            meta = find_video(rest[0])
            if rest[1:] == ["frame"]:
                at = float((query.get("t") or ["0"])[0])
                exact = (query.get("exact") or ["0"])[0] in ("1", "true")
                return self.file(guarded(ap.video.frame_at, meta, at, exact))
            return self.json(video_web(meta))
        if what == "knowledge":
            learn = ap.learn
            if not rest:
                try:
                    segs = learn.all_segments()
                    rows = learn.table(segs)
                except SystemExit:
                    rows = [learn.seg_summary(r) for r in learn.KB.records()]
                return self.json({"segments": rows, "global": learn.KB.load_global()})
            seg_id = "/".join(rest)
            if not learn.KB.file(seg_id).exists():
                raise ApiError(404, "nothing learned about " + seg_id)
            return self.json(learn.seg_full(learn.KB.load(seg_id)))
        if what == "playtests":
            reports = ap.learn.all_playtests()
            if not rest:
                return self.json({"playtests": [{
                    "id": r["id"], "scope": r.get("scope"), "started": r.get("started"), "duration": r.get("duration"),
                    "segments": len(r.get("segments", [])), "passed": r.get("passed"), "videoId": r.get("videoId"),
                    "verdict": r.get("verdict")} for r in reports]})
            hit = next((r for r in reports if r["id"] == rest[0]), None)
            if hit is None:
                raise ApiError(404, "no playtest " + rest[0])
            hit = dict(hit)
            if hit.get("videoId"):
                try:
                    hit["video"] = video_web(find_video(hit["videoId"]))
                except ApiError:
                    hit["video"] = None
            return self.json(hit)
        raise ApiError(404, "unknown API path")


def serve(port, open_browser):
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    server.daemon_threads = True
    url = "http://127.0.0.1:%d/" % server.server_address[1]
    print("DASHBOARD %s  (API under %sapi/; Ctrl+C stops)" % (url, url), flush=True)
    if open_browser:
        threading.Timer(0.5, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


def cmd_serve(args):
    if args.stop or args.status:
        try:
            state = json.loads(STATE.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            print("no background dashboard")
            return
        alive = ap.pid_alive(state["pid"])
        if args.status:
            print("dashboard %s at %s" % ("running" if alive else "gone", state["url"]))
            return
        if alive:
            subprocess.run(["taskkill", "/PID", str(state["pid"]), "/F"], capture_output=True,
                           creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        STATE.unlink(missing_ok=True)
        print("dashboard stopped")
        return
    if args.detach:
        try:
            state = json.loads(STATE.read_text(encoding="utf-8"))
            if ap.pid_alive(state["pid"]):
                print("DASHBOARD already running: %s" % state["url"])
                if args.open:
                    webbrowser.open(state["url"])
                return
        except (OSError, ValueError, KeyError):
            pass
        exe = Path(sys.executable)
        quiet = exe.with_name("pythonw.exe") if exe.with_name("pythonw.exe").exists() else exe
        log = ap.HOME / "web.log"
        ap.HOME.mkdir(parents=True, exist_ok=True)
        proc = subprocess.Popen([str(quiet), str(Path(ap.__file__).resolve()), "serve", "--port", str(args.port)],
                                stdout=open(log, "w"), stderr=subprocess.STDOUT, cwd=str(ap.ROOT),
                                creationflags=getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))
        url = "http://127.0.0.1:%d/" % args.port
        STATE.write_text(json.dumps({"pid": proc.pid, "url": url, "since": time.time()}), encoding="utf-8")
        print("DASHBOARD %s (background, pid %d; agentplay.py serve --stop ends it)" % (url, proc.pid))
        if args.open:
            time.sleep(1.0)
            webbrowser.open(url)
        return
    serve(args.port, args.open)


def register(sub, ap_module):
    global ap, STATE
    ap = ap_module
    STATE = ap.HOME / "web.json"
    p = sub.add_parser("serve", help="dashboard and HTTP API on localhost: players, videos, playtests, knowledge")
    p.add_argument("--port", type=int, default=8765)
    p.add_argument("--open", action="store_true", help="open it in the browser")
    p.add_argument("--detach", action="store_true", help="run in the background")
    p.add_argument("--stop", action="store_true", help="stop the background dashboard")
    p.add_argument("--status", action="store_true")
    p.set_defaults(func=cmd_serve)
