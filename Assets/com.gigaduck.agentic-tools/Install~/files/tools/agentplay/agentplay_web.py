"""The agentplay dashboard and HTTP API: players, recordings, playtest reports and the knowledge base, on localhost.

  agentplay.py serve [--port 8765] [--open]     run it here (Ctrl+C stops it)
  agentplay.py serve --detach [--open]          run it in the background; `serve --stop` ends it; `serve --status`
  agentplay.py serve --detach --tunnel          also reachable from anywhere (a phone): a Cloudflare quick tunnel and a
                                                secret link; remote viewers can only look (--allow-control lets them act)

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
Listens on 127.0.0.1 only. Without --share or --tunnel it answers only requests addressed to localhost. With them, a
request from elsewhere (through the tunnel) needs the secret key of the share link (?key=..., then kept in a cookie)
and may only read. Standard library only (the tunnel downloads Cloudflare's cloudflared once, into Builds/AgentPlay/_tools).
Installed from the Agentic Tools package (Install~/files/tools/agentplay): change it there, not in a project.
"""
import json
import mimetypes
import os
import re
import secrets
import shutil
import subprocess
import sys
import threading
import time
import urllib.parse
import urllib.request
import webbrowser
from http.cookies import SimpleCookie
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

ap = None
STATE = None  # Builds/AgentPlay/web.json while a detached server runs
SHARE = {"token": None, "control": False}  # set by --share / --tunnel: the key remote viewers need, and whether they may act
COOKIE = "agentplay_key"
CLOUDFLARED = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe"
FORWARD_HEADERS = ("Cf-Connecting-Ip", "X-Forwarded-For", "Forwarded", "X-Real-Ip")
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
    def is_local(self):
        host = (self.headers.get("Host") or "").split(":")[0].lower()
        return host in ("127.0.0.1", "localhost") and not any(self.headers.get(h) for h in FORWARD_HEADERS)

    def allowed(self):
        if self.is_local():
            origin = self.headers.get("Origin")
            if self.command == "POST" and origin and urllib.parse.urlparse(origin).hostname not in ("127.0.0.1", "localhost"):
                self.send_error(403, "cross-site request")
                return False
            return True
        token = SHARE["token"]
        if not token:
            self.send_error(403, "only localhost (agentplay.py serve --tunnel shares it with a secret link)")
            return False
        url = urllib.parse.urlparse(self.path)
        given = urllib.parse.parse_qs(url.query).get("key", [""])[0]
        if given:
            if not secrets.compare_digest(given, token):
                return self.locked("this link's key is wrong or out of date: ask for a new link")
            # Keep the key in a cookie (the video and API requests then carry it) and take it out of the address bar.
            rest = [(k, v) for k, v in urllib.parse.parse_qsl(url.query) if k != "key"]
            secure = "https" in (self.headers.get("Cf-Visitor") or "") or self.headers.get("X-Forwarded-Proto") == "https"
            self.send_response(302)
            self.send_header("Location", (url.path or "/") + ("?" + urllib.parse.urlencode(rest) if rest else ""))
            self.send_header("Set-Cookie", "%s=%s; Path=/; HttpOnly; SameSite=Lax; Max-Age=2592000%s" % (COOKIE, token, "; Secure" if secure else ""))
            self.send_header("Cache-Control", "no-store")
            self.send_header("Content-Length", "0")
            self.end_headers()
            return False
        jar = SimpleCookie()
        try:
            jar.load(self.headers.get("Cookie") or "")
        except Exception:
            pass
        if COOKIE not in jar or not secrets.compare_digest(jar[COOKIE].value, token):
            return self.locked("open this dashboard with its share link (it ends in ?key=...)")
        if self.command == "POST" and not SHARE["control"]:
            self.json({"error": "view-only link: starting, recording or sending commands works only on the computer itself"}, 403)
            return False
        return True

    def locked(self, why):
        body = ("<!doctype html><meta name=viewport content='width=device-width,initial-scale=1'><title>agentplay</title>"
                "<body style='background:#111;color:#ddd;font:16px system-ui;padding:24px'><h2>agentplay dashboard</h2><p>%s</p>" % why).encode()
        self.send_response(401)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)
        return False

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
        if method == "GET" and (not parts or parts == ["m"]):
            # Phones get the phone app (also at /m); ?desktop=1 asks for the desktop page anyway.
            phone = parts == ["m"] or (not query.get("desktop") and re.search(r"Mobi|Android|iPhone|iPad", self.headers.get("User-Agent") or ""))
            page = Path(__file__).with_name("agentplay_mobile.html" if phone else "agentplay_dashboard.html")
            return self.file(page if page.exists() else Path(__file__).with_name("agentplay_dashboard.html"))
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
            return self.json({"tool": "agentplay", "project": ap.ROOT.name, "viewer": "local" if self.is_local() else "remote",
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
                    "verdict": r.get("verdict"), "perfect": r.get("perfect"), "series": r.get("series"),
                    "kind": r.get("kind", "playtest")} for r in reports]})
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


def serve(port, open_browser, token=None, control=False, tunnel=False):
    """Serves the dashboard here until Ctrl+C. With a token, requests from elsewhere need it; with tunnel, a Cloudflare quick
    tunnel makes it reachable from anywhere and the share link is printed."""
    SHARE["token"], SHARE["control"] = token, control
    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    server.daemon_threads = True
    url = "http://127.0.0.1:%d/" % server.server_address[1]
    print("DASHBOARD %s  (API under %sapi/; Ctrl+C stops)" % (url, url), flush=True)
    tunnel_proc = None
    if tunnel:
        tunnel_proc, public = start_tunnel(server.server_address[1], ap.HOME / "tunnel.log", detached=False)
        print_share(public, token, control)
    elif token:
        print("SHARE KEY %s  (requests from elsewhere need ?key=<it> once; they are %s)" % (token, "allowed to act" if control else "view only"), flush=True)
    if open_browser:
        threading.Timer(0.5, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        if tunnel_proc is not None:
            tunnel_proc.terminate()


def cloudflared():
    """Cloudflare's tunnel client: AGENTPLAY_CLOUDFLARED, PATH, or a copy downloaded once into Builds/AgentPlay/_tools."""
    found = os.environ.get("AGENTPLAY_CLOUDFLARED") or shutil.which("cloudflared")
    if found:
        return found
    local = ap.HOME / "_tools" / "cloudflared.exe"
    if not local.exists():
        local.parent.mkdir(parents=True, exist_ok=True)
        print("downloading cloudflared (Cloudflare's tunnel client) once into %s ..." % local, flush=True)
        tmp = local.with_suffix(".part")
        urllib.request.urlretrieve(CLOUDFLARED, tmp)
        os.replace(tmp, local)
    return str(local)


def start_tunnel(port, log_path, detached=True):
    """Starts a quick tunnel (no account; a random https://*.trycloudflare.com address that lasts while it runs) to the
    local port. Returns (process, public URL)."""
    exe = cloudflared()
    log = open(log_path, "w")
    flags = (getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)) if detached \
        else getattr(subprocess, "CREATE_NO_WINDOW", 0)
    proc = subprocess.Popen([exe, "tunnel", "--no-autoupdate", "--url", "http://127.0.0.1:%d" % port],
                            stdout=log, stderr=subprocess.STDOUT, creationflags=flags)
    until = time.time() + 90
    while time.time() < until:
        time.sleep(0.5)
        text = Path(log_path).read_text(encoding="utf-8", errors="replace")
        m = re.search(r"https://[a-z0-9-]+\.trycloudflare\.com", text)
        if m:
            return proc, m.group(0)
        if proc.poll() is not None:
            break
    proc.kill()
    ap.fail("the tunnel did not start; see %s" % log_path)


def print_share(public, token, control):
    link = "%s/?key=%s" % (public, token)
    print("SHARE LINK %s" % link)
    print("  open it on any device (phone too); it works while this computer is on and the dashboard runs.")
    print("  %s; anyone with the link can %s. agentplay.py serve --stop ends it (the link dies with it)."
          % ("viewers can also control players" if control else "view only", "use it" if control else "look"))
    return link


def read_state():
    try:
        return json.loads(STATE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return None


def kill(pid):
    subprocess.run(["taskkill", "/PID", str(pid), "/T", "/F"], capture_output=True, creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))


def start_detached(port, token=None, control=False):
    """The server as a hidden background process; returns its pid."""
    exe = Path(sys.executable)
    quiet = exe.with_name("pythonw.exe") if exe.with_name("pythonw.exe").exists() else exe
    ap.HOME.mkdir(parents=True, exist_ok=True)
    command = [str(quiet), str(Path(ap.__file__).resolve()), "serve", "--port", str(port)]
    if token:
        command += ["--share-token", token] + (["--allow-control"] if control else [])
    proc = subprocess.Popen(command, stdout=open(ap.HOME / "web.log", "w"), stderr=subprocess.STDOUT, cwd=str(ap.ROOT),
                            creationflags=getattr(subprocess, "DETACHED_PROCESS", 0) | getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))
    return proc.pid


def cmd_serve(args):
    state = read_state()
    if args.stop or args.status:
        if state is None:
            print("no background dashboard")
            return
        alive = ap.pid_alive(state["pid"])
        if args.status:
            print("dashboard %s at %s" % ("running" if alive else "gone", state["url"]))
            if state.get("public"):
                tunnel_alive = ap.pid_alive(state.get("tunnelPid", 0))
                print("share link %s (%s)" % ("%s/?key=%s" % (state["public"], state["token"]), "up" if tunnel_alive else "tunnel gone"))
            return
        for pid in (state.get("tunnelPid"), state["pid"]):
            if pid and ap.pid_alive(pid):
                kill(pid)
        STATE.unlink(missing_ok=True)
        print("dashboard stopped" + (" and its share link closed" if state.get("public") else ""))
        return
    if args.restart:
        if state is None:
            ap.fail("no background dashboard to restart: agentplay.py serve --detach [--tunnel]")
        if ap.pid_alive(state["pid"]):
            kill(state["pid"])
        port = int(state["url"].rsplit(":", 1)[1].strip("/"))
        new = dict(state, pid=start_detached(port, state.get("token"), state.get("control")), since=time.time())
        STATE.write_text(json.dumps(new), encoding="utf-8")
        print("DASHBOARD restarted at %s (pid %d)%s" % (state["url"], new["pid"], "; the share link is unchanged" if state.get("public") else ""))
        return
    sharing = args.tunnel or args.share
    token = args.share_token or (secrets.token_urlsafe(18) if sharing else None)
    if args.detach:
        if state is not None and ap.pid_alive(state["pid"]):
            if sharing and not state.get("public"):
                ap.fail("a dashboard without a share link runs already: agentplay.py serve --stop, then start it again with --tunnel")
            print("DASHBOARD already running: %s" % state["url"])
            if state.get("public"):
                print_share(state["public"], state["token"], state.get("control"))
            if args.open:
                webbrowser.open(state["url"])
            return
        url = "http://127.0.0.1:%d/" % args.port
        new = {"pid": start_detached(args.port, token, args.allow_control), "url": url, "since": time.time()}
        if args.tunnel:
            tunnel, public = start_tunnel(args.port, ap.HOME / "tunnel.log", detached=True)
            new.update(tunnelPid=tunnel.pid, public=public, token=token, control=args.allow_control)
        STATE.write_text(json.dumps(new), encoding="utf-8")
        print("DASHBOARD %s (background, pid %d; agentplay.py serve --stop ends it)" % (url, new["pid"]))
        if args.tunnel:
            print_share(new["public"], token, args.allow_control)
        if args.open:
            time.sleep(1.0)
            webbrowser.open(url)
        return
    serve(args.port, args.open, token=token, control=args.allow_control, tunnel=args.tunnel)


def register(sub, ap_module):
    global ap, STATE
    ap = ap_module
    STATE = ap.HOME / "web.json"
    p = sub.add_parser("serve", help="dashboard and HTTP API: players, videos, playtests, knowledge (--tunnel: a phone link)")
    p.add_argument("--port", type=int, default=8765)
    p.add_argument("--open", action="store_true", help="open it in the browser")
    p.add_argument("--detach", action="store_true", help="run in the background")
    p.add_argument("--stop", action="store_true", help="stop the background dashboard (and its tunnel)")
    p.add_argument("--status", action="store_true", help="is it running; prints the share link again")
    p.add_argument("--restart", action="store_true", help="restart the background server (after a tools update); the share link stays")
    p.add_argument("--tunnel", action="store_true", help="reachable from anywhere: Cloudflare quick tunnel + secret view-only link")
    p.add_argument("--share", action="store_true", help="accept remote requests that carry the secret key (your own tunnel or LAN proxy)")
    p.add_argument("--allow-control", action="store_true", help="remote viewers may also start/stop recordings and send commands")
    p.add_argument("--share-token", help=argparse_suppress())
    p.set_defaults(func=cmd_serve)


def argparse_suppress():
    import argparse
    return argparse.SUPPRESS
