"""The status workflow: which columns exist and which moves are allowed.

This is the single source of truth for statuses. The web board reads the
same definitions from ``/api/board`` (label, color, hint), so adding or
renaming a column happens here only.

Agents must follow ``TRANSITIONS``. People may move a card anywhere from the
web board; such a move is recorded with ``forced`` in its activity entry.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Dict, FrozenSet, Tuple

from .errors import WorkflowError
from .model import Actor

PROPOSED = "proposed"
READY = "ready"
IN_PROGRESS = "in_progress"
BLOCKED = "blocked"
REVIEW = "review"
DONE = "done"
CANCELLED = "cancelled"


@dataclass(frozen=True)
class StatusDef:
    key: str
    label: str
    hint: str
    color: str
    terminal: bool = False  # finished; archive may sweep it
    hidden_by_default: bool = False


STATUSES: Tuple[StatusDef, ...] = (
    StatusDef(PROPOSED, "Inbox", "Ideas and agent suggestions waiting for your OK", "#8b909a"),
    StatusDef(READY, "Queue", "Approved work, picked top to bottom by agents", "#6ea8fe"),
    StatusDef(IN_PROGRESS, "In progress", "Claimed by an agent, with a live lease", "#f2b65a"),
    StatusDef(BLOCKED, "Blocked", "Needs an answer or a fix before it can go on", "#f07a72"),
    StatusDef(REVIEW, "Review", "Finished by an agent; waiting for you to test and commit", "#b691f5"),
    StatusDef(DONE, "Done", "Verified and committed", "#52c48d", terminal=True),
    StatusDef(CANCELLED, "Cancelled", "Dropped", "#60646c", terminal=True, hidden_by_default=True),
)

STATUS_KEYS = tuple(s.key for s in STATUSES)
STATUS_BY_KEY: Dict[str, StatusDef] = {s.key: s for s in STATUSES}

TRANSITIONS: Dict[str, FrozenSet[str]] = {
    PROPOSED: frozenset({READY, CANCELLED}),
    READY: frozenset({IN_PROGRESS, PROPOSED, CANCELLED}),
    IN_PROGRESS: frozenset({REVIEW, BLOCKED, READY, DONE, CANCELLED}),
    BLOCKED: frozenset({READY, IN_PROGRESS, CANCELLED}),
    REVIEW: frozenset({DONE, IN_PROGRESS, READY, BLOCKED, CANCELLED}),
    DONE: frozenset({READY}),
    CANCELLED: frozenset({READY, PROPOSED}),
}

# Statuses whose tasks keep their ``touches`` and ``needs`` reserved by default
# (overridable with the ``conflict_statuses`` setting). Review is included
# because the files of a task in review are uncommitted edits in the shared tree.
DEFAULT_CONFLICT_STATUSES = (IN_PROGRESS, BLOCKED, REVIEW)


def require_status(key: str) -> str:
    key = str(key).strip().lower().replace("-", "_").replace(" ", "_")
    aliases = {"inbox": PROPOSED, "queue": READY, "todo": READY, "doing": IN_PROGRESS,
               "wip": IN_PROGRESS, "progress": IN_PROGRESS, "canceled": CANCELLED}
    key = aliases.get(key, key)
    if key not in STATUS_BY_KEY:
        raise WorkflowError(f"Unknown status '{key}'. Use one of: {', '.join(STATUS_KEYS)}")
    return key


def is_allowed(current: str, target: str) -> bool:
    return target in TRANSITIONS.get(current, frozenset())


def check_transition(current: str, target: str, actor: Actor) -> bool:
    """Return True when the move is outside the workflow but allowed (forced).

    Raises WorkflowError when an agent attempts a move the workflow forbids.
    """
    if current == target:
        raise WorkflowError(f"The task is already {STATUS_BY_KEY[target].label}")
    if is_allowed(current, target):
        return False
    if actor.is_user:
        return True
    allowed = ", ".join(sorted(TRANSITIONS.get(current, ()))) or "nothing"
    raise WorkflowError(
        f"Cannot move from {current} to {target}; from {current} a task can go to: {allowed}"
    )
