"""The local web board: a JSON API, a live event stream and the static UI.

Standard library only (http.server), bound to 127.0.0.1. Three background
pieces run next to the request threads:

    EventHub      tails events.jsonl and pushes each new event to every open
                  browser over Server-Sent Events (/api/stream). Changes made
                  by agents in other processes therefore appear within ~0.5 s.
    maintenance   every sync_interval_seconds runs Board.sync(): expires
                  stale leases and lets plugins sync (virtual commits).
    handler       routes in ROUTES; one small method per endpoint.

Security: the server only listens on loopback, rejects requests whose Host
header is not 127.0.0.1/localhost (DNS rebinding), and only accepts writes
sent as application/json from its own origin (cross-site form posts cannot
set that content type without a CORS preflight, which is never granted).

API (all JSON; errors are {"error": message} with a 4xx status):

    GET    /api/board                  statuses, priorities, projects, tasks
    GET    /api/tasks/<id>             one task (also archived ones)
    GET    /api/events?limit=60        recent events, oldest first
    GET    /api/stats                  counts and throughput
    GET    /api/stream                 Server-Sent Events: "change" per event
    POST   /api/tasks                  create  {title, description, ..., status}
    PATCH  /api/tasks/<id>             edit fields
    POST   /api/tasks/<id>/move        {status, note}
    POST   /api/tasks/<id>/place       {status?, above?, below?}  (drag and drop)
    POST   /api/tasks/<id>/notes       {text}
    POST   /api/tasks/<id>/reopen      {note?}
    POST   /api/sync                   run Board.sync() now
"""

from __future__ import annotations

import json
import mimetypes
import queue
import re
import sys
import threading
import time
import webbrowser
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any, Callable, Dict, List, Optional, Tuple
from urllib.parse import parse_qs, urlparse

from .board import Board
from .errors import BoardError, ValidationError
from .model import ACTOR_USER, Actor

WEB_ROOT = Path(__file__).resolve().parent / "web"
MAX_BODY_BYTES = 1024 * 1024


# --------------------------------------------------------------------- events

class EventHub:
    """Tails events.jsonl and fans each new line out to subscriber queues."""

    def __init__(self, path: Path, poll_seconds: float = 0.4):
        self.path = Path(path)
        self.poll_seconds = poll_seconds
        self._subscribers: List[queue.Queue] = []
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._offset = self.path.stat().st_size if self.path.exists() else 0
        self._thread = threading.Thread(target=self._run, name="taskboard-events", daemon=True)

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._thread.join(timeout=2)

    @property
    def stopped(self) -> bool:
        return self._stop.is_set()

    def subscribe(self) -> queue.Queue:
        q: queue.Queue = queue.Queue(maxsize=500)
        with self._lock:
            self._subscribers.append(q)
        return q

    def unsubscribe(self, q: queue.Queue) -> None:
        with self._lock:
            if q in self._subscribers:
                self._subscribers.remove(q)

    def publish(self, event: Dict[str, Any]) -> None:
        with self._lock:
            subscribers = list(self._subscribers)
        for q in subscribers:
            try:
                q.put_nowait(event)
            except queue.Full:
                pass  # a stalled browser tab; it resyncs on reconnect

    def _run(self) -> None:
        while not self._stop.wait(self.poll_seconds):
            try:
                self._poll()
            except Exception as exc:  # noqa: BLE001 - keep the stream alive
                print(f"taskboard: event tail error: {exc}", file=sys.stderr)

    def _poll(self) -> None:
        try:
            size = self.path.stat().st_size
        except FileNotFoundError:
            self._offset = 0
            return
        if size < self._offset:  # rotated
            self._offset = 0
        if size == self._offset:
            return
        with open(self.path, "rb") as handle:
            handle.seek(self._offset)
            chunk = handle.read(size - self._offset)
        # Only consume complete lines; a writer may be mid-append.
        end = chunk.rfind(b"\n")
        if end < 0:
            return
        self._offset += end + 1
        for line in chunk[: end + 1].splitlines():
            try:
                self.publish(json.loads(line.decode("utf-8")))
            except ValueError:
                continue


# -------------------------------------------------------------------- routing

Route = Tuple[str, "re.Pattern[str]", str]
ROUTES: List[Route] = [
    ("GET", re.compile(r"^/api/board$"), "get_board"),
    ("GET", re.compile(r"^/api/tasks/(?P<id>\d+)$"), "get_task"),
    ("GET", re.compile(r"^/api/events$"), "get_events"),
    ("GET", re.compile(r"^/api/stats$"), "get_stats"),
    ("GET", re.compile(r"^/api/stream$"), "get_stream"),
    ("POST", re.compile(r"^/api/tasks$"), "create_task"),
    ("PATCH", re.compile(r"^/api/tasks/(?P<id>\d+)$"), "update_task"),
    ("POST", re.compile(r"^/api/tasks/(?P<id>\d+)/move$"), "move_task"),
    ("POST", re.compile(r"^/api/tasks/(?P<id>\d+)/place$"), "place_task"),
    ("POST", re.compile(r"^/api/tasks/(?P<id>\d+)/notes$"), "add_note"),
    ("POST", re.compile(r"^/api/tasks/(?P<id>\d+)/reopen$"), "reopen_task"),
    ("POST", re.compile(r"^/api/sync$"), "run_sync"),
]


class BoardServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = False  # never share the port with a second board

    def __init__(self, board: Board, port: int):
        self.board = board
        self.actor = Actor(str(board.config.get("user_name", "you")), ACTOR_USER)
        self.hub = EventHub(board.store.events_path)  # type: ignore[attr-defined]
        self._stop = threading.Event()
        super().__init__(("127.0.0.1", port), Handler)
        self.port = self.server_address[1]
        self.allowed_hosts = {f"127.0.0.1:{self.port}", f"localhost:{self.port}"}
        self._maintenance = threading.Thread(target=self._maintain, name="taskboard-sync", daemon=True)

    @property
    def url(self) -> str:
        return f"http://127.0.0.1:{self.port}/"

    def start_background(self) -> threading.Thread:
        """Start serving on a thread (tests and embedding)."""
        self.hub.start()
        self._maintenance.start()
        thread = threading.Thread(target=self.serve_forever, name="taskboard-http", daemon=True)
        thread.start()
        return thread

    def run(self) -> None:
        self.hub.start()
        self._maintenance.start()
        self.serve_forever()

    def close(self) -> None:
        self._stop.set()
        self.hub.stop()
        self.shutdown()
        self.server_close()

    def _maintain(self) -> None:
        interval = float(self.board.config.get("sync_interval_seconds", 15))
        while not self._stop.wait(interval):
            try:
                self.board.sync()
            except Exception as exc:  # noqa: BLE001
                print(f"taskboard: sync failed: {exc}", file=sys.stderr)


class Handler(BaseHTTPRequestHandler):
    server: BoardServer
    protocol_version = "HTTP/1.1"
    server_version = "taskboard"

    # Quiet console: one line per write, nothing for polling or assets.
    def log_message(self, fmt: str, *args: Any) -> None:
        if self.command not in ("GET", "HEAD"):
            sys.stderr.write(f"  {self.command} {self.path} -> {args[1] if len(args) > 1 else ''}\n")

    # ------------------------------------------------------------- plumbing

    def do_GET(self) -> None:
        self._dispatch("GET")

    def do_POST(self) -> None:
        self._dispatch("POST")

    def do_PATCH(self) -> None:
        self._dispatch("PATCH")

    def _dispatch(self, method: str) -> None:
        self._body_consumed = method == "GET"
        try:
            if not self._host_ok():
                return self._json({"error": "Unknown host"}, HTTPStatus.FORBIDDEN)
            url = urlparse(self.path)
            if method == "GET" and not url.path.startswith("/api/"):
                return self._static(url.path)
            if method != "GET" and not self._write_ok():
                return self._json({"error": "Writes must be JSON from the board page"}, HTTPStatus.FORBIDDEN)
            for verb, pattern, name in ROUTES:
                match = pattern.match(url.path)
                if verb == method and match:
                    self.query = parse_qs(url.query)
                    return getattr(self, name)(**match.groupdict())
            self._json({"error": "Not found"}, HTTPStatus.NOT_FOUND)
        except BoardError as exc:
            self._json({"error": str(exc), "type": type(exc).__name__}, exc.http_status)
        except (BrokenPipeError, ConnectionResetError):
            pass
        except Exception as exc:  # noqa: BLE001 - report, keep serving
            print(f"taskboard: {method} {self.path} failed: {exc!r}", file=sys.stderr)
            self._json({"error": f"Internal error: {exc}"}, HTTPStatus.INTERNAL_SERVER_ERROR)

    def _host_ok(self) -> bool:
        return self.headers.get("Host", "") in self.server.allowed_hosts

    def _write_ok(self) -> bool:
        if not self.headers.get("Content-Type", "").startswith("application/json"):
            return False
        origin = self.headers.get("Origin")
        return origin is None or origin.split("://", 1)[-1] in self.server.allowed_hosts

    def _body(self) -> Dict[str, Any]:
        length = int(self.headers.get("Content-Length") or 0)
        if length > MAX_BODY_BYTES:
            raise ValidationError("Request too large")
        raw = self.rfile.read(length) if length else b"{}"
        self._body_consumed = True
        try:
            data = json.loads(raw.decode("utf-8") or "{}")
        except ValueError:
            raise ValidationError("Body is not valid JSON") from None
        if not isinstance(data, dict):
            raise ValidationError("Body must be a JSON object")
        return data

    def _json(self, payload: Any, status: int = HTTPStatus.OK) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        if not getattr(self, "_body_consumed", True):
            # A rejected write left its body unread; never parse it as the next request.
            self.send_header("Connection", "close")
            self.close_connection = True
        self.end_headers()
        self.wfile.write(body)

    def _static(self, path: str) -> None:
        relative = "index.html" if path in ("", "/") else path.lstrip("/")
        target = (WEB_ROOT / relative).resolve()
        if WEB_ROOT not in target.parents or not target.is_file():  # no path traversal
            return self._json({"error": "Not found"}, HTTPStatus.NOT_FOUND)
        body = target.read_bytes()
        kind = mimetypes.guess_type(str(target))[0] or "application/octet-stream"
        if target.suffix == ".js":
            kind = "text/javascript"
        self.send_response(HTTPStatus.OK)
        self.send_header("Content-Type", f"{kind}; charset=utf-8" if kind.startswith("text") else kind)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-cache")
        self.end_headers()
        self.wfile.write(body)

    # ------------------------------------------------------------ endpoints

    @property
    def board(self) -> Board:
        return self.server.board

    @property
    def actor(self) -> Actor:
        return self.server.actor

    def _task(self, task) -> None:
        self._json(self.board.view(task))

    def get_board(self) -> None:
        self._json(self.board.snapshot())

    def get_task(self, id: str) -> None:
        self._task(self.board.get(id))

    def get_events(self) -> None:
        limit = int(self.query.get("limit", ["60"])[0])
        events = [e for e in self.board.store.read_events(limit=limit * 3) if not e.get("data", {}).get("quiet")]
        self._json(events[-limit:])

    def get_stats(self) -> None:
        self._json(self.board.stats())

    def create_task(self) -> None:
        data = self._body()
        status = data.pop("status", None)
        self._json(self.board.view(self.board.create(data, self.actor, status=status)), HTTPStatus.CREATED)

    def update_task(self, id: str) -> None:
        self._task(self.board.update(id, self._body(), self.actor))

    def move_task(self, id: str) -> None:
        data = self._body()
        self._task(self.board.move(id, str(data.get("status", "")), self.actor, str(data.get("note", ""))))

    def place_task(self, id: str) -> None:
        data = self._body()
        self._task(self.board.place(id, self.actor, data.get("status"), data.get("above"), data.get("below")))

    def add_note(self, id: str) -> None:
        self._task(self.board.note(id, self.actor, str(self._body().get("text", ""))))

    def reopen_task(self, id: str) -> None:
        self._task(self.board.reopen(id, self.actor, str(self._body().get("note", ""))))

    def run_sync(self) -> None:
        self._body()
        released = self.board.sync()
        self._json({"released": [t.key for t in released]})

    def get_stream(self) -> None:
        """Server-Sent Events: one "change" message per board event."""
        hub = self.server.hub
        q = hub.subscribe()
        try:
            self.send_response(HTTPStatus.OK)
            self.send_header("Content-Type", "text/event-stream")
            self.send_header("Cache-Control", "no-store")
            self.send_header("Connection", "keep-alive")
            self.end_headers()
            self.wfile.write(b"retry: 2000\n\n")
            self.wfile.flush()
            last_ping = time.monotonic()
            while not hub.stopped:
                try:
                    event = q.get(timeout=1.0)
                except queue.Empty:
                    if time.monotonic() - last_ping > 15:
                        self.wfile.write(b": ping\n\n")
                        self.wfile.flush()
                        last_ping = time.monotonic()
                    continue
                payload = json.dumps(event, ensure_ascii=False)
                self.wfile.write(f"id: {event.get('seq', '')}\nevent: change\ndata: {payload}\n\n".encode("utf-8"))
                self.wfile.flush()
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass
        finally:
            hub.unsubscribe(q)
            self.close_connection = True


def make_server(board: Board, port: int) -> BoardServer:
    try:
        return BoardServer(board, port)
    except OSError as exc:
        raise BoardError(
            f"Cannot listen on 127.0.0.1:{port} ({exc.strerror or exc}). Is the board already open? "
            f"Try `taskboard serve --port {port + 1}`."
        ) from None


def serve(board: Board, port: int = 8765, open_browser: bool = False,
          on_ready: Optional[Callable[[BoardServer], None]] = None) -> None:
    server = make_server(board, port)
    print(f"Task board: {server.url}   (board folder: {board.config.home})")
    print("Press Ctrl+C to stop.")
    if on_ready:
        on_ready(server)
    if open_browser:
        threading.Timer(0.4, lambda: webbrowser.open(server.url)).start()
    try:
        server.run()
    except KeyboardInterrupt:
        print("\nStopped.")
    finally:
        server.hub.stop()
        server.server_close()
