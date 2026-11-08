"""Persistence: one JSON file per task plus an append-only event log.

Folder layout (``<home>`` = the board folder, see config.py)::

    <home>/
      config.json          your settings (optional)
      counters.json        last task id and last event sequence number
      lock                 present only while a write is in progress
      tasks/00012.json     one file per live task
      archive/00007.json   finished tasks moved out by `taskboard archive`
      events.jsonl         one JSON line per change; the web board tails it
      events-<stamp>.jsonl rotated logs

``TaskStore`` is the interface the rest of the code uses. ``FileStore`` is the
implementation; a different backend (SQLite, a server) only has to provide
the same methods.
"""

from __future__ import annotations

import json
import os
import time
from pathlib import Path
from typing import Any, Dict, Iterator, List, Optional, Protocol, Tuple

from .clock import to_iso, utc_now
from .locking import BoardLock
from .model import Task

EVENT_LOG_MAX_BYTES = 4 * 1024 * 1024


class TaskStore(Protocol):
    """What the board needs from a storage backend."""

    def lock(self) -> BoardLock: ...
    def load_all(self, include_archive: bool = False) -> List[Task]: ...
    def load(self, task_id: int) -> Optional[Task]: ...
    def load_archived(self, task_id: int) -> Optional[Task]: ...
    def save(self, task: Task) -> None: ...
    def archive(self, task: Task) -> None: ...
    def unarchive(self, task: Task) -> None: ...
    def next_task_id(self) -> int: ...
    def append_event(self, event: Dict[str, Any]) -> Dict[str, Any]: ...
    def read_events(self, after_seq: int = 0, limit: int = 200) -> List[Dict[str, Any]]: ...
    def last_event_seq(self) -> int: ...


def write_json_atomic(path: Path, data: Any) -> None:
    """Write to a temp file, then replace, so readers never see half a file."""
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(f".{path.name}.{os.getpid()}.tmp")
    text = json.dumps(data, indent=2, ensure_ascii=False) + "\n"
    with open(tmp, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(text)
        handle.flush()
        os.fsync(handle.fileno())
    # On Windows a reader holding the target open makes replace fail briefly.
    for attempt in range(50):
        try:
            os.replace(tmp, path)
            return
        except PermissionError:
            if attempt == 49:
                raise
            time.sleep(0.02)


def _read_json(path: Path) -> Optional[Dict[str, Any]]:
    for attempt in range(5):
        try:
            with open(path, "r", encoding="utf-8") as handle:
                return json.load(handle)
        except FileNotFoundError:
            return None
        except (PermissionError, ValueError):
            # Being replaced right now (Windows) or hand-edited badly: retry, then skip.
            if attempt == 4:
                return None
            time.sleep(0.02)
    return None


class FileStore:
    def __init__(self, home: Path):
        self.home = Path(home)
        self.tasks_dir = self.home / "tasks"
        self.archive_dir = self.home / "archive"
        self.events_path = self.home / "events.jsonl"
        self.counters_path = self.home / "counters.json"
        self._lock = BoardLock(self.home / "lock")
        # path -> (mtime_ns, size, Task); avoids re-parsing unchanged files.
        self._cache: Dict[str, Tuple[int, int, Task]] = {}

    # ----------------------------------------------------------------- locking

    def lock(self) -> BoardLock:
        return self._lock

    # ------------------------------------------------------------------- tasks

    def _task_path(self, task_id: int, folder: Optional[Path] = None) -> Path:
        return (folder or self.tasks_dir) / f"{task_id:05d}.json"

    def _iter_task_files(self, folder: Path) -> Iterator[Path]:
        if not folder.is_dir():
            return iter(())
        return (p for p in sorted(folder.glob("*.json")) if not p.name.startswith("."))

    def _load_file(self, path: Path) -> Optional[Task]:
        try:
            stat = path.stat()
        except FileNotFoundError:
            self._cache.pop(str(path), None)
            return None
        cached = self._cache.get(str(path))
        if cached and cached[0] == stat.st_mtime_ns and cached[1] == stat.st_size:
            return Task.from_dict(cached[2].to_dict())  # callers may mutate; hand out a copy
        raw = _read_json(path)
        if not raw or "id" not in raw:
            return None
        task = Task.from_dict(raw)
        self._cache[str(path)] = (stat.st_mtime_ns, stat.st_size, task)
        return Task.from_dict(task.to_dict())

    def load_all(self, include_archive: bool = False) -> List[Task]:
        folders = [self.tasks_dir] + ([self.archive_dir] if include_archive else [])
        tasks = []
        for folder in folders:
            for path in self._iter_task_files(folder):
                task = self._load_file(path)
                if task is not None:
                    tasks.append(task)
        return tasks

    def load(self, task_id: int) -> Optional[Task]:
        return self._load_file(self._task_path(task_id))

    def load_archived(self, task_id: int) -> Optional[Task]:
        return self._load_file(self._task_path(task_id, self.archive_dir))

    def save(self, task: Task) -> None:
        write_json_atomic(self._task_path(task.id), task.to_dict())

    def archive(self, task: Task) -> None:
        write_json_atomic(self._task_path(task.id, self.archive_dir), task.to_dict())
        try:
            self._task_path(task.id).unlink()
        except FileNotFoundError:
            pass

    def unarchive(self, task: Task) -> None:
        self.save(task)
        try:
            self._task_path(task.id, self.archive_dir).unlink()
        except FileNotFoundError:
            pass

    # ---------------------------------------------------------------- counters

    def _bump_counter(self, name: str) -> int:
        """Increment a counter. Call only while holding the lock."""
        counters = _read_json(self.counters_path) or {}
        value = int(counters.get(name, 0)) + 1
        if name == "task":
            # Never reuse an id, even if counters.json was lost or edited.
            existing = [t.id for t in self.load_all(include_archive=True)]
            value = max([value] + [i + 1 for i in existing])
        counters[name] = value
        write_json_atomic(self.counters_path, counters)
        return value

    def next_task_id(self) -> int:
        return self._bump_counter("task")

    # ------------------------------------------------------------------ events

    def append_event(self, event: Dict[str, Any]) -> Dict[str, Any]:
        """Append one event line. Call only while holding the lock."""
        event = dict(event)
        event["seq"] = self._bump_counter("event")
        event.setdefault("at", to_iso(utc_now()))
        self._rotate_if_large()
        line = json.dumps(event, ensure_ascii=False) + "\n"
        with open(self.events_path, "a", encoding="utf-8", newline="\n") as handle:
            handle.write(line)
        return event

    def _rotate_if_large(self) -> None:
        try:
            if self.events_path.stat().st_size < EVENT_LOG_MAX_BYTES:
                return
        except FileNotFoundError:
            return
        stamp = time.strftime("%Y%m%d-%H%M%S")
        try:
            os.replace(self.events_path, self.home / f"events-{stamp}.jsonl")
        except OSError:
            pass

    def last_event_seq(self) -> int:
        return int((_read_json(self.counters_path) or {}).get("event", 0))

    def read_events(self, after_seq: int = 0, limit: int = 200) -> List[Dict[str, Any]]:
        """The newest ``limit`` events with seq > after_seq, oldest first."""
        if not self.events_path.exists():
            return []
        events = []
        with open(self.events_path, "r", encoding="utf-8") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    event = json.loads(line)
                except ValueError:
                    continue
                if int(event.get("seq", 0)) > after_seq:
                    events.append(event)
        return events[-limit:]
