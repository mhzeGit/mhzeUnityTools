"""Board location and settings.

The board lives in one folder (the *home*), shared by every repository and
agent on the machine, so one board can hold tasks for several projects:

    1. ``--home`` on the command line
    2. the ``TASKBOARD_HOME`` environment variable
    3. ``~/.taskboard`` (``C:\\Users\\<you>\\.taskboard`` on Windows)

``<home>/config.json`` holds only the settings you changed; everything else
comes from ``DEFAULTS``. Unknown keys are kept, so plugins can read their own
settings from the same file.
"""

from __future__ import annotations

import copy
import json
import os
from pathlib import Path
from typing import Any, Dict, Optional

from . import workflow
from .model import PRIORITIES

DEFAULTS: Dict[str, Any] = {
    # Shown in the web board header.
    "board_name": "Agent Task Board",
    # How people appear in task history (web board and CLI without --agent).
    "user_name": "you",
    # Web board port (127.0.0.1 only).
    "port": 8765,
    # Lease of an in-progress task. Any agent command on the task renews it.
    #   stale_minutes:  the card shows a warning
    #   expire_minutes: the task goes back to the queue for another agent
    "lease": {"stale_minutes": 20, "expire_minutes": 120},
    # When true, agents must send finished work to Review (`done` is refused).
    "require_review": False,
    # Where new tasks start. Agent ideas wait in the Inbox for your OK.
    "agent_tasks_start_as": workflow.PROPOSED,
    "user_tasks_start_as": workflow.READY,
    # Tasks in these statuses keep their files (touches) and resources (needs) reserved.
    "conflict_statuses": list(workflow.DEFAULT_CONFLICT_STATUSES),
    # Exclusive resources and how many tasks may hold each at once.
    # A resource that is not listed here has capacity 1.
    "resources": {"unity-editor": 1, "unity-build": 1, "gpu-benchmark": 1},
    "priorities": list(PRIORITIES),
    # Python modules loaded at startup; each exposes register(context).
    "plugins": ["taskboard.integrations.virtual_commits"],
    # `taskboard archive` with no --days moves done/cancelled tasks older than this.
    "archive_after_days": 14,
    # How often the web server runs lease expiry and plugin sync, in seconds.
    "sync_interval_seconds": 15,
}


def default_home() -> Path:
    env = os.environ.get("TASKBOARD_HOME", "").strip()
    if env:
        return Path(env).expanduser()
    return Path.home() / ".taskboard"


def _merge(base: Dict[str, Any], override: Dict[str, Any]) -> Dict[str, Any]:
    result = copy.deepcopy(base)
    for key, value in override.items():
        if isinstance(value, dict) and isinstance(result.get(key), dict):
            result[key] = _merge(result[key], value)
        else:
            result[key] = value
    return result


class Config:
    """Read-only view over DEFAULTS merged with ``<home>/config.json``."""

    def __init__(self, home: Path, values: Optional[Dict[str, Any]] = None):
        self.home = Path(home)
        self.values = _merge(DEFAULTS, values or {})

    @classmethod
    def load(cls, home: Optional[Path] = None) -> "Config":
        home = Path(home) if home else default_home()
        path = home / "config.json"
        values: Dict[str, Any] = {}
        if path.exists():
            try:
                values = json.loads(path.read_text(encoding="utf-8"))
            except (OSError, ValueError) as exc:
                raise SystemExit(f"taskboard: cannot read {path}: {exc}") from None
        return cls(home, values)

    def get(self, key: str, default: Any = None) -> Any:
        return self.values.get(key, default)

    # Typed accessors for the settings the core uses.

    @property
    def stale_seconds(self) -> float:
        return float(self.values["lease"]["stale_minutes"]) * 60

    @property
    def expire_seconds(self) -> float:
        return float(self.values["lease"]["expire_minutes"]) * 60

    @property
    def conflict_statuses(self) -> tuple:
        return tuple(self.values["conflict_statuses"])

    @property
    def priorities(self) -> tuple:
        return tuple(self.values["priorities"])

    def resource_capacity(self, name: str) -> int:
        return int(self.values.get("resources", {}).get(name, 1))
