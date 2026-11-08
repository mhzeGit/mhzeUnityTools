"""Shared test fixtures: a throwaway board, a controllable clock, a hard deadline."""

from __future__ import annotations

import faulthandler
import functools
import shutil
import sys
import tempfile
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from taskboard.board import Board  # noqa: E402
from taskboard.config import Config  # noqa: E402
from taskboard.model import ACTOR_AGENT, ACTOR_USER, Actor  # noqa: E402
from taskboard.store import FileStore  # noqa: E402

USER = Actor("you", ACTOR_USER)
CLAUDE = Actor("Claude Code", ACTOR_AGENT)
KILO = Actor("Kilo Code", ACTOR_AGENT)


class FakeClock:
    def __init__(self) -> None:
        self.now = datetime(2026, 11, 8, 12, 0, tzinfo=timezone.utc)

    def __call__(self) -> datetime:
        return self.now

    def advance(self, minutes: float = 0, hours: float = 0, days: float = 0) -> None:
        self.now += timedelta(minutes=minutes, hours=hours, days=days)


def deadline(seconds: float):
    """Per-test hard timeout: a hung test dumps all stacks and kills the run."""

    def wrap(fn):
        @functools.wraps(fn)
        def run(*args, **kwargs):
            faulthandler.dump_traceback_later(seconds, exit=True)
            try:
                return fn(*args, **kwargs)
            finally:
                faulthandler.cancel_dump_traceback_later()
        return run
    return wrap


class BoardTestCase:
    """Mixin: self.board on a temp folder with a fake clock; cleaned up after."""

    config_values: dict = {}

    def setUp(self) -> None:  # noqa: N802 - unittest API
        self.home = Path(tempfile.mkdtemp(prefix="taskboard-test-"))
        self.clock = FakeClock()
        config = Config(self.home, dict(self.config_values))
        self.board = Board(FileStore(self.home), config, now=self.clock)

    def tearDown(self) -> None:  # noqa: N802
        shutil.rmtree(self.home, ignore_errors=True)

    def make(self, title: str = "Task", actor: Actor = USER, status: str = "ready", **fields):
        return self.board.create({"title": title, **fields}, actor, status=status)
