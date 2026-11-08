"""The board-wide write lock, safe across processes and threads.

Every mutation (create, claim, move, note, ...) runs inside this lock, so a
read-check-write such as "claim the next free task" is atomic even when many
agents call ``taskboard next`` at the same moment. Critical sections last
milliseconds; readers never take the lock (task files are replaced atomically).

The lock is a file created with O_EXCL, which works the same on Windows and
POSIX and on network drives. A lock file older than ``stale_after`` seconds is
treated as left behind by a crashed process and removed. The lock is
re-entrant within a thread, and threads of one process (the web server) are
serialized by an in-process RLock first.
"""

from __future__ import annotations

import json
import os
import socket
import threading
import time
from pathlib import Path

from .errors import LockTimeoutError


class BoardLock:
    def __init__(self, path: Path, timeout: float = 30.0, stale_after: float = 20.0):
        self.path = Path(path)
        self.timeout = timeout
        self.stale_after = stale_after
        self._thread_lock = threading.RLock()
        self._depth = 0

    def __enter__(self) -> "BoardLock":
        if not self._thread_lock.acquire(timeout=self.timeout):
            raise LockTimeoutError("Timed out waiting for the board lock (another thread holds it)")
        try:
            if self._depth == 0:
                self._acquire_file()
            self._depth += 1
        except BaseException:
            self._thread_lock.release()
            raise
        return self

    def __exit__(self, *exc) -> None:
        try:
            self._depth -= 1
            if self._depth == 0:
                self._release_file()
        finally:
            self._thread_lock.release()

    # ------------------------------------------------------------------ file lock

    def _acquire_file(self) -> None:
        self.path.parent.mkdir(parents=True, exist_ok=True)
        deadline = time.monotonic() + self.timeout
        delay = 0.005
        payload = json.dumps(
            {"pid": os.getpid(), "host": socket.gethostname(), "at": time.time()}
        ).encode("utf-8")
        while True:
            try:
                fd = os.open(str(self.path), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            except FileExistsError:
                self._break_if_stale()
            except PermissionError:
                # Windows reports a file that is being deleted as access denied.
                pass
            else:
                try:
                    os.write(fd, payload)
                finally:
                    os.close(fd)
                return
            if time.monotonic() > deadline:
                raise LockTimeoutError(
                    f"Timed out waiting for the board lock {self.path}. If no taskboard "
                    f"process is running, delete that file."
                )
            time.sleep(delay)
            delay = min(delay * 2, 0.1)

    def _break_if_stale(self) -> None:
        try:
            age = time.time() - self.path.stat().st_mtime
        except OSError:
            return
        if age > self.stale_after:
            try:
                self.path.unlink()
            except OSError:
                pass

    def _release_file(self) -> None:
        for _ in range(20):
            try:
                self.path.unlink()
                return
            except FileNotFoundError:
                return
            except PermissionError:
                time.sleep(0.01)
