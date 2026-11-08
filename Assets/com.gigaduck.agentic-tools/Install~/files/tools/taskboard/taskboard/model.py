"""The task record and its parts.

A task is one JSON file. ``Task.to_dict`` / ``Task.from_dict`` are the only
(de)serialization path, so the on-disk format lives in exactly one place.

Forward compatibility: keys this version does not know are kept in
``Task.extra`` and written back unchanged, so a newer tool (or a plugin) can
add fields without an older tool deleting them. ``SCHEMA_VERSION`` plus
``migrate`` handle real format changes.
"""

from __future__ import annotations

import re
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional

from .errors import ValidationError

SCHEMA_VERSION = 1

PRIORITIES = ("urgent", "high", "normal", "low")
DEFAULT_PRIORITY = "normal"

ACTOR_AGENT = "agent"
ACTOR_USER = "user"
ACTOR_SYSTEM = "system"


@dataclass(frozen=True)
class Actor:
    """Who performs an operation. Agents are held to the workflow; people are not."""

    name: str
    kind: str = ACTOR_USER

    @property
    def is_agent(self) -> bool:
        return self.kind == ACTOR_AGENT

    @property
    def is_user(self) -> bool:
        return self.kind == ACTOR_USER


SYSTEM = Actor("board", ACTOR_SYSTEM)


# --------------------------------------------------------------------------- ids

_ID_RE = re.compile(r"^\s*(?:t-?|#)?(\d+)\s*$", re.IGNORECASE)


def parse_task_id(text: Any) -> int:
    """Accept 12, "12", "T-12", "t12" and "#12"."""
    if isinstance(text, int) and not isinstance(text, bool):
        return text
    match = _ID_RE.match(str(text))
    if not match:
        raise ValidationError(f"Not a task id: {text!r} (use a number such as 12 or T-12)")
    return int(match.group(1))


def format_task_id(task_id: int) -> str:
    return f"T-{task_id}"


# ------------------------------------------------------------------------- parts


@dataclass
class Claim:
    """The live lease of the agent working on a task."""

    agent: str
    claimed_at: str
    heartbeat_at: str
    repo_root: str = ""
    host: str = ""

    def to_dict(self) -> Dict[str, Any]:
        return {
            "agent": self.agent,
            "claimed_at": self.claimed_at,
            "heartbeat_at": self.heartbeat_at,
            "repo_root": self.repo_root,
            "host": self.host,
        }

    @classmethod
    def from_dict(cls, raw: Optional[Dict[str, Any]]) -> Optional["Claim"]:
        if not raw:
            return None
        return cls(
            agent=str(raw.get("agent", "")),
            claimed_at=str(raw.get("claimed_at", "")),
            heartbeat_at=str(raw.get("heartbeat_at", raw.get("claimed_at", ""))),
            repo_root=str(raw.get("repo_root", "")),
            host=str(raw.get("host", "")),
        )


@dataclass
class Link:
    """A reference to something outside the board.

    ``kind`` decides who understands it: ``virtual_commit`` (value = record id,
    ``repo`` = repository root, read by integrations/virtual_commits.py),
    ``commit`` (a git sha), ``url`` or ``file``. Plugins may add kinds.
    """

    kind: str
    value: str
    label: str = ""
    repo: str = ""

    def to_dict(self) -> Dict[str, Any]:
        data = {"kind": self.kind, "value": self.value}
        if self.label:
            data["label"] = self.label
        if self.repo:
            data["repo"] = self.repo
        return data

    @classmethod
    def from_dict(cls, raw: Dict[str, Any]) -> "Link":
        return cls(
            kind=str(raw.get("kind", "url")),
            value=str(raw.get("value", "")),
            label=str(raw.get("label", "")),
            repo=str(raw.get("repo", "")),
        )


@dataclass
class Activity:
    """One entry of a task's own history: a status change, note or edit."""

    at: str
    by: str
    kind: str  # "created" | "status" | "note" | "edit" | "claim" | "system"
    text: str = ""
    from_status: str = ""
    to_status: str = ""

    def to_dict(self) -> Dict[str, Any]:
        data = {"at": self.at, "by": self.by, "kind": self.kind}
        if self.text:
            data["text"] = self.text
        if self.from_status:
            data["from"] = self.from_status
        if self.to_status:
            data["to"] = self.to_status
        return data

    @classmethod
    def from_dict(cls, raw: Dict[str, Any]) -> "Activity":
        return cls(
            at=str(raw.get("at", "")),
            by=str(raw.get("by", "")),
            kind=str(raw.get("kind", "note")),
            text=str(raw.get("text", "")),
            from_status=str(raw.get("from", "")),
            to_status=str(raw.get("to", "")),
        )


# -------------------------------------------------------------------------- task

# Fields a caller may set with create/update. Status, claim, activity and the
# timestamps change only through Board operations.
EDITABLE_FIELDS = (
    "title",
    "description",
    "acceptance",
    "priority",
    "project",
    "labels",
    "touches",
    "needs",
    "depends_on",
    "rank",
)

_KNOWN_KEYS = {
    "schema", "id", "title", "description", "acceptance", "status", "priority",
    "project", "labels", "touches", "needs", "depends_on", "rank", "created_at",
    "created_by", "updated_at", "assignee", "claim", "blocked_reason", "result",
    "links", "activity",
}


@dataclass
class Task:
    id: int
    title: str
    status: str
    created_at: str
    created_by: str
    updated_at: str
    description: str = ""
    acceptance: List[str] = field(default_factory=list)
    priority: str = DEFAULT_PRIORITY
    project: str = ""
    labels: List[str] = field(default_factory=list)
    touches: List[str] = field(default_factory=list)
    needs: List[str] = field(default_factory=list)
    depends_on: List[int] = field(default_factory=list)
    rank: float = 0.0
    assignee: str = ""
    claim: Optional[Claim] = None
    blocked_reason: str = ""
    result: str = ""
    links: List[Link] = field(default_factory=list)
    activity: List[Activity] = field(default_factory=list)
    extra: Dict[str, Any] = field(default_factory=dict)

    @property
    def key(self) -> str:
        return format_task_id(self.id)

    def add_activity(self, entry: Activity) -> None:
        self.activity.append(entry)

    def to_dict(self) -> Dict[str, Any]:
        data: Dict[str, Any] = dict(self.extra)
        data.update(
            {
                "schema": SCHEMA_VERSION,
                "id": self.id,
                "title": self.title,
                "description": self.description,
                "acceptance": list(self.acceptance),
                "status": self.status,
                "priority": self.priority,
                "project": self.project,
                "labels": list(self.labels),
                "touches": list(self.touches),
                "needs": list(self.needs),
                "depends_on": list(self.depends_on),
                "rank": self.rank,
                "created_at": self.created_at,
                "created_by": self.created_by,
                "updated_at": self.updated_at,
                "assignee": self.assignee,
                "claim": self.claim.to_dict() if self.claim else None,
                "blocked_reason": self.blocked_reason,
                "result": self.result,
                "links": [link.to_dict() for link in self.links],
                "activity": [entry.to_dict() for entry in self.activity],
            }
        )
        return data

    @classmethod
    def from_dict(cls, raw: Dict[str, Any]) -> "Task":
        raw = migrate(dict(raw))
        return cls(
            id=int(raw["id"]),
            title=str(raw.get("title", "")),
            status=str(raw.get("status", "ready")),
            created_at=str(raw.get("created_at", "")),
            created_by=str(raw.get("created_by", "")),
            updated_at=str(raw.get("updated_at", raw.get("created_at", ""))),
            description=str(raw.get("description", "")),
            acceptance=[str(x) for x in raw.get("acceptance") or []],
            priority=str(raw.get("priority", DEFAULT_PRIORITY)),
            project=str(raw.get("project", "")),
            labels=[str(x) for x in raw.get("labels") or []],
            touches=[str(x) for x in raw.get("touches") or []],
            needs=[str(x) for x in raw.get("needs") or []],
            depends_on=[int(x) for x in raw.get("depends_on") or []],
            rank=float(raw.get("rank") or 0.0),
            assignee=str(raw.get("assignee", "")),
            claim=Claim.from_dict(raw.get("claim")),
            blocked_reason=str(raw.get("blocked_reason", "")),
            result=str(raw.get("result", "")),
            links=[Link.from_dict(x) for x in raw.get("links") or []],
            activity=[Activity.from_dict(x) for x in raw.get("activity") or []],
            extra={k: v for k, v in raw.items() if k not in _KNOWN_KEYS},
        )


def migrate(raw: Dict[str, Any]) -> Dict[str, Any]:
    """Upgrade an older task dict to SCHEMA_VERSION.

    Add one ``if schema < N`` step per future format change; never edit an
    existing step. Version 1 is the first format, so there is nothing to do yet.
    """
    schema = int(raw.get("schema", 1) or 1)
    if schema > SCHEMA_VERSION:
        # Written by a newer tool: read what we understand, keep the rest in extra.
        return raw
    raw["schema"] = SCHEMA_VERSION
    return raw


# -------------------------------------------------------------------- validation


def clean_list(value: Any) -> List[str]:
    """Normalize a list field from a list, or a comma/newline separated string."""
    if value is None:
        return []
    if isinstance(value, str):
        parts = re.split(r"[\n,]", value)
    else:
        parts = [str(x) for x in value]
    seen: List[str] = []
    for part in parts:
        part = part.strip()
        if part and part not in seen:
            seen.append(part)
    return seen


def clean_lines(value: Any) -> List[str]:
    """Like clean_list but only splits on newlines (acceptance criteria may hold commas)."""
    if value is None:
        return []
    parts = value.split("\n") if isinstance(value, str) else [str(x) for x in value]
    return [p.strip() for p in parts if p.strip()]


def normalize_fields(fields: Dict[str, Any], priorities=PRIORITIES) -> Dict[str, Any]:
    """Validate and normalize editable fields. Unknown keys are rejected."""
    clean: Dict[str, Any] = {}
    for key, value in fields.items():
        if value is None:
            continue
        if key not in EDITABLE_FIELDS:
            raise ValidationError(f"Field '{key}' cannot be set directly")
        if key == "title":
            value = " ".join(str(value).split())
            if not value:
                raise ValidationError("A task needs a title")
            if len(value) > 200:
                raise ValidationError("Keep the title under 200 characters; put details in the description")
        elif key == "description":
            value = str(value).strip()
        elif key == "acceptance":
            value = clean_lines(value)
        elif key == "priority":
            value = str(value).strip().lower()
            if value not in priorities:
                raise ValidationError(f"Priority must be one of: {', '.join(priorities)}")
        elif key == "project":
            value = str(value).strip()
        elif key in ("labels", "needs"):
            value = clean_list([v.lower() for v in clean_list(value)])
        elif key == "touches":
            value = [v.replace("\\", "/") for v in clean_list(value)]
        elif key == "depends_on":
            value = [parse_task_id(v) for v in clean_list(value)]
        elif key == "rank":
            try:
                value = float(value)
            except (TypeError, ValueError):
                raise ValidationError("Rank must be a number") from None
        clean[key] = value
    return clean
