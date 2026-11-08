"""A tiny in-process event bus that plugins subscribe to.

Hook names and their keyword arguments:

    task.created   board, task, actor
    task.updated   board, task, actor, changes (dict of field -> new value)
    task.moved     board, task, actor, from_status, to_status
    task.note      board, task, actor, text
    board.sync     board            (periodic: the web server every
                                     sync_interval_seconds, and `taskboard sync`)

Handlers run after the change is saved and the lock is released, so a
handler may call board operations itself. A failing handler is reported on
stderr and never breaks the operation that triggered it.
"""

from __future__ import annotations

import sys
import traceback
from collections import defaultdict
from typing import Any, Callable, DefaultDict, List

Handler = Callable[..., Any]

HOOK_NAMES = ("task.created", "task.updated", "task.moved", "task.note", "board.sync")


class HookBus:
    def __init__(self) -> None:
        self._handlers: DefaultDict[str, List[Handler]] = defaultdict(list)

    def on(self, name: str, handler: Handler) -> Handler:
        if name not in HOOK_NAMES:
            raise ValueError(f"Unknown hook '{name}'. Known hooks: {', '.join(HOOK_NAMES)}")
        self._handlers[name].append(handler)
        return handler

    def emit(self, name: str, **kwargs: Any) -> None:
        for handler in list(self._handlers.get(name, ())):
            try:
                handler(**kwargs)
            except Exception:  # noqa: BLE001 - a plugin must never break the board
                print(f"taskboard: hook {name} handler {handler!r} failed:", file=sys.stderr)
                traceback.print_exc()
