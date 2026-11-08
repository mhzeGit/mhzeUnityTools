"""The application service: every read and every change to the board.

The CLI, the web server and plugins all call this class; none of them write
task files themselves. Each mutation:

    1. takes the board lock (store.lock()),
    2. loads fresh task files, checks workflow / ownership / scheduler rules,
    3. saves the task and appends an event to events.jsonl,
    4. releases the lock, then runs plugin hooks.

Agents (Actor.kind == "agent") are held to the workflow and may only change
tasks they own while those are in progress. People can do anything; moves
outside the workflow are recorded as forced.
"""

from __future__ import annotations

import socket
from contextlib import contextmanager
from datetime import timedelta
from pathlib import Path
from statistics import median
from typing import Any, Dict, Iterable, Iterator, List, Optional, Tuple

from . import __version__, scheduler, workflow
from .clock import Clock, age_seconds, from_iso, to_iso, utc_now
from .config import Config
from .errors import NotClaimableError, NotFoundError, OwnershipError, ValidationError, WorkflowError
from .hooks import HookBus
from .model import (
    SYSTEM, Activity, Actor, Claim, Link, Task, format_task_id, normalize_fields, parse_task_id,
)
from .store import FileStore, TaskStore


class _Transaction:
    """Collects events and hooks of one locked operation."""

    def __init__(self, board: "Board"):
        self.board = board
        self.hooks: List[Tuple[str, Dict[str, Any]]] = []

    def save(self, task: Task, event: str, actor: Actor, summary: str, **data: Any) -> None:
        task.updated_at = self.board.now_iso()
        self.board.store.save(task)
        self.board.store.append_event(
            {"type": event, "at": task.updated_at, "task": task.id, "by": actor.name,
             "actor": actor.kind, "summary": summary, "data": data}
        )

    def hook(self, name: str, **kwargs: Any) -> None:
        self.hooks.append((name, kwargs))


class Board:
    def __init__(
        self,
        store: TaskStore,
        config: Config,
        now: Clock = utc_now,
        hooks: Optional[HookBus] = None,
    ):
        self.store = store
        self.config = config
        self._now = now
        self.hooks = hooks or HookBus()
        self.plugins: List[str] = []

    @classmethod
    def open(cls, home: Optional[Path] = None, plugins: bool = True, now: Clock = utc_now) -> "Board":
        config = Config.load(home)
        board = cls(FileStore(config.home), config, now=now)
        if plugins:
            from .plugins import PluginContext, load_plugins

            board.plugins = load_plugins(PluginContext(board, board.hooks, config))
        return board

    # ------------------------------------------------------------------ helpers

    def now(self):
        return self._now()

    def now_iso(self) -> str:
        return to_iso(self._now())

    @contextmanager
    def _transaction(self) -> Iterator[_Transaction]:
        tx = _Transaction(self)
        with self.store.lock():
            yield tx
        for name, kwargs in tx.hooks:
            self.hooks.emit(name, board=self, **kwargs)

    def _require(self, task_id: Any) -> Task:
        task_id = parse_task_id(task_id)
        task = self.store.load(task_id)
        if task is not None:
            return task
        if self.store.load_archived(task_id):
            raise NotFoundError(f"{format_task_id(task_id)} is archived; `reopen` brings it back")
        raise NotFoundError(f"No task {format_task_id(task_id)}")

    def _check_owner(self, task: Task, actor: Actor, force: bool = False) -> None:
        """Agents may only change an in-progress task they hold."""
        if not actor.is_agent or force or task.status != workflow.IN_PROGRESS:
            return
        owner = task.claim.agent if task.claim else task.assignee
        if owner and owner != actor.name:
            raise OwnershipError(
                f"{task.key} is being worked on by {owner}. Pick another task, or ask the user."
            )

    def _activity(self, task: Task, actor: Actor, kind: str, text: str = "",
                  from_status: str = "", to_status: str = "") -> None:
        task.add_activity(Activity(self.now_iso(), actor.name, kind, text, from_status, to_status))

    def _refresh_lease(self, task: Task, actor: Actor) -> None:
        if task.claim and task.claim.agent == actor.name:
            task.claim.heartbeat_at = self.now_iso()

    def _transition(self, tx: _Transaction, task: Task, target: str, actor: Actor,
                    text: str = "", force: bool = False, claim: Optional[Claim] = None) -> None:
        """Change status and the fields that belong to it; save and log."""
        target = workflow.require_status(target)
        current = task.status
        forced = workflow.check_transition(current, target, actor) or force
        task.status = target
        if target == workflow.IN_PROGRESS:
            task.claim = claim
            task.assignee = claim.agent if claim else actor.name
        else:
            task.claim = None
            if target in (workflow.READY, workflow.PROPOSED):
                task.assignee = ""
        if current == workflow.BLOCKED and target != workflow.BLOCKED:
            task.blocked_reason = ""
        note = text + (" (moved outside the workflow)" if forced and actor.is_user else "")
        self._activity(task, actor, "status", note.strip(), current, target)
        label = workflow.STATUS_BY_KEY[target].label
        summary = f"{task.key} → {label}" + (f": {text}" if text else "")
        tx.save(task, "task.moved", actor, summary, **{"from": current, "to": target, "forced": forced})
        tx.hook("task.moved", task=task, actor=actor, from_status=current, to_status=target)

    def _check_dependencies(self, task_id: int, depends_on: List[int]) -> None:
        known = {t.id: t for t in self.store.load_all(include_archive=True)}
        for dep in depends_on:
            if dep == task_id:
                raise ValidationError("A task cannot depend on itself")
            if dep not in known:
                raise ValidationError(f"Dependency {format_task_id(dep)} does not exist")
        # Reject cycles: walk the dependency graph from each new dependency.
        graph = {i: list(t.depends_on) for i, t in known.items()}
        graph[task_id] = list(depends_on)
        stack, seen = list(depends_on), set()
        while stack:
            current = stack.pop()
            if current == task_id:
                raise ValidationError("These dependencies would form a cycle")
            if current in seen:
                continue
            seen.add(current)
            stack.extend(graph.get(current, []))

    # ------------------------------------------------------------------ queries

    def get(self, task_id: Any) -> Task:
        task_id = parse_task_id(task_id)
        task = self.store.load(task_id)
        if task is None:
            task = self.store.load_archived(task_id)
        if task is None:
            raise NotFoundError(f"No task {format_task_id(task_id)}")
        return task

    def tasks(self, statuses: Optional[Iterable[str]] = None, project: Optional[str] = None,
              assignee: Optional[str] = None) -> List[Task]:
        wanted = {workflow.require_status(s) for s in statuses} if statuses else None
        result = []
        for task in self.store.load_all():
            if wanted and task.status not in wanted:
                continue
            if project is not None and not scheduler.project_matches(task, project):
                continue
            if assignee and task.assignee != assignee:
                continue
            result.append(task)
        return result

    def view(self, task: Task, all_tasks: Optional[List[Task]] = None) -> Dict[str, Any]:
        """Task dict plus computed fields (blockers, lease) for the UI and --json."""
        data = task.to_dict()
        data["key"] = task.key
        if task.status == workflow.READY:
            data["blockers"] = scheduler.blockers(task, all_tasks or self.store.load_all(), self.config)
        else:
            data["blockers"] = []
        data["lease"] = None
        if task.claim:
            age = age_seconds(task.claim.heartbeat_at, self.now()) or 0.0
            data["lease"] = {
                "age_seconds": round(age),
                "stale": age > self.config.stale_seconds,
                "expires_in_seconds": round(self.config.expire_seconds - age),
            }
        return data

    def snapshot(self) -> Dict[str, Any]:
        """Everything the web board needs in one response."""
        tasks = self.store.load_all()
        projects = sorted({t.project for t in tasks if t.project} | set(self.config.get("projects", [])),
                          key=str.lower)
        last_seq = self.store.last_event_seq()
        return {
            "board": {
                "name": self.config.get("board_name"),
                "user_name": self.config.get("user_name"),
                "version": __version__,
                "home": str(self.config.home),
                "plugins": self.plugins,
                "lease": self.config.get("lease"),
            },
            "statuses": [vars(s) for s in workflow.STATUSES],
            "transitions": {k: sorted(v) for k, v in workflow.TRANSITIONS.items()},
            "priorities": list(self.config.priorities),
            "resources": self.config.get("resources", {}),
            "projects": projects,
            "tasks": [self.view(t, tasks) for t in tasks],
            "now": self.now_iso(),
            "last_event_seq": last_seq,
        }

    def stats(self) -> Dict[str, Any]:
        tasks = self.store.load_all(include_archive=True)
        now = self.now()
        by_status: Dict[str, int] = {s.key: 0 for s in workflow.STATUSES}
        by_project: Dict[str, int] = {}
        lead_times, done_24h, done_7d = [], 0, 0
        for task in tasks:
            by_status[task.status] = by_status.get(task.status, 0) + 1
            if task.status not in (workflow.DONE, workflow.CANCELLED):
                key = task.project or "(any)"
                by_project[key] = by_project.get(key, 0) + 1
            if task.status == workflow.DONE:
                finished = next((from_iso(a.at) for a in reversed(task.activity)
                                 if a.kind == "status" and a.to_status == workflow.DONE), None)
                created = from_iso(task.created_at)
                if finished and created:
                    lead_times.append((finished - created).total_seconds())
                    if now - finished < timedelta(days=1):
                        done_24h += 1
                    if now - finished < timedelta(days=7):
                        done_7d += 1
        return {
            "by_status": by_status,
            "open_by_project": by_project,
            "done_last_24h": done_24h,
            "done_last_7d": done_7d,
            "median_lead_time_hours": round(median(lead_times) / 3600, 1) if lead_times else None,
        }

    # ----------------------------------------------------------------- creating

    def create(self, fields: Dict[str, Any], actor: Actor, status: Optional[str] = None,
               links: Iterable[Link] = ()) -> Task:
        clean = normalize_fields(fields, self.config.priorities)
        if not clean.get("title"):
            raise ValidationError("A task needs a title")
        if status is None:
            status = self.config.get("agent_tasks_start_as" if actor.is_agent else "user_tasks_start_as")
        status = workflow.require_status(status)
        if status not in (workflow.PROPOSED, workflow.READY):
            raise WorkflowError("New tasks start in the Inbox (proposed) or the Queue (ready)")
        with self._transaction() as tx:
            task_id = self.store.next_task_id()
            if clean.get("depends_on"):
                self._check_dependencies(task_id, clean["depends_on"])
            now = self.now_iso()
            if "rank" not in clean:
                ranks = [t.rank for t in self.store.load_all() if t.status == status]
                clean["rank"] = (max(ranks) + 1.0) if ranks else 1.0
            task = Task(id=task_id, status=status, created_at=now, created_by=actor.name,
                        updated_at=now, links=list(links), **clean)
            self._activity(task, actor, "created", f"Created in {workflow.STATUS_BY_KEY[status].label}",
                           to_status=status)
            tx.save(task, "task.created", actor, f"{task.key} created: {task.title}", status=status)
            tx.hook("task.created", task=task, actor=actor)
        return task

    def update(self, task_id: Any, fields: Dict[str, Any], actor: Actor, force: bool = False) -> Task:
        clean = normalize_fields(fields, self.config.priorities)
        with self._transaction() as tx:
            task = self._require(task_id)
            self._check_owner(task, actor, force)
            if "depends_on" in clean:
                self._check_dependencies(task.id, clean["depends_on"])
            changes = {k: v for k, v in clean.items() if getattr(task, k) != v}
            if not changes:
                return task
            for key, value in changes.items():
                setattr(task, key, value)
            self._refresh_lease(task, actor)
            visible = [k for k in changes if k != "rank"]
            if visible:
                self._activity(task, actor, "edit", "Changed " + ", ".join(visible))
            tx.save(task, "task.updated", actor, f"{task.key} edited: {', '.join(visible) or 'order'}",
                    fields=sorted(changes), quiet=not visible)
            tx.hook("task.updated", task=task, actor=actor, changes=changes)
        return task

    # ------------------------------------------------------------ agent workflow

    def claim(self, task_id: Any, actor: Actor, repo_root: str = "", force: bool = False) -> Task:
        with self._transaction() as tx:
            task = self._require(task_id)
            return self._claim_locked(tx, task, actor, repo_root, force)

    def _claim_locked(self, tx: _Transaction, task: Task, actor: Actor, repo_root: str,
                      force: bool) -> Task:
        if task.status != workflow.READY:
            label = workflow.STATUS_BY_KEY[task.status].label
            raise NotClaimableError(f"{task.key} is in {label}, not in the Queue")
        if not force:
            reasons = scheduler.blockers(task, self.store.load_all(), self.config)
            if reasons:
                raise NotClaimableError(f"{task.key} cannot start yet: " + "; ".join(reasons))
        now = self.now_iso()
        claim = Claim(actor.name, now, now, repo_root, socket.gethostname())
        self._transition(tx, task, workflow.IN_PROGRESS, actor, f"Claimed by {actor.name}", claim=claim)
        return task

    def claim_next(self, actor: Actor, project: Optional[str] = None, repo_root: str = "",
                   dry_run: bool = False) -> Tuple[Optional[Task], Dict[int, List[str]]]:
        """Atomically claim the first claimable queued task of ``project``."""
        self.reap_expired()
        with self._transaction() as tx:
            task, skipped = scheduler.pick_next(self.store.load_all(), self.config, project)
            if task is None or dry_run:
                return task, skipped
            self._claim_locked(tx, task, actor, repo_root, force=False)
            return task, skipped

    def heartbeat(self, task_id: Any, actor: Actor) -> Task:
        with self._transaction() as tx:
            task = self._require(task_id)
            if task.status != workflow.IN_PROGRESS or not task.claim:
                raise WorkflowError(f"{task.key} is not in progress")
            self._check_owner(task, actor)
            task.claim.heartbeat_at = self.now_iso()
            tx.save(task, "task.heartbeat", actor, f"{task.key} heartbeat", quiet=True)
        return task

    def note(self, task_id: Any, actor: Actor, text: str) -> Task:
        text = str(text or "").strip()
        if not text:
            raise ValidationError("The note is empty")
        with self._transaction() as tx:
            task = self._require(task_id)
            self._refresh_lease(task, actor)
            self._activity(task, actor, "note", text)
            tx.save(task, "task.note", actor, f"{task.key}: {text}")
            tx.hook("task.note", task=task, actor=actor, text=text)
        return task

    def block(self, task_id: Any, actor: Actor, reason: str) -> Task:
        reason = str(reason or "").strip()
        if not reason:
            raise ValidationError("Say why the task is blocked (the user reads this)")
        with self._transaction() as tx:
            task = self._require(task_id)
            self._check_owner(task, actor)
            task.blocked_reason = reason
            self._transition(tx, task, workflow.BLOCKED, actor, reason)
        return task

    def unblock(self, task_id: Any, actor: Actor, note: str = "") -> Task:
        """Back to the Queue so any agent can pick it up again."""
        with self._transaction() as tx:
            task = self._require(task_id)
            if task.status != workflow.BLOCKED:
                raise WorkflowError(f"{task.key} is not blocked")
            self._transition(tx, task, workflow.READY, actor, note or "Unblocked")
        return task

    def release(self, task_id: Any, actor: Actor, reason: str = "") -> Task:
        """Give an in-progress task back to the Queue."""
        with self._transaction() as tx:
            task = self._require(task_id)
            self._check_owner(task, actor)
            self._transition(tx, task, workflow.READY, actor, reason or "Released")
        return task

    def _finish(self, task_id: Any, actor: Actor, target: str, summary: str,
                links: Iterable[Link]) -> Task:
        with self._transaction() as tx:
            task = self._require(task_id)
            self._check_owner(task, actor)
            if summary and actor.kind != SYSTEM.kind:
                task.result = summary.strip()
            for link in links:
                if all((l.kind, l.value) != (link.kind, link.value) for l in task.links):
                    task.links.append(link)
            self._transition(tx, task, target, actor, summary.strip())
        return task

    def submit_review(self, task_id: Any, actor: Actor, summary: str = "",
                      links: Iterable[Link] = ()) -> Task:
        return self._finish(task_id, actor, workflow.REVIEW, summary, links)

    def complete(self, task_id: Any, actor: Actor, summary: str = "",
                 links: Iterable[Link] = ()) -> Task:
        if actor.is_agent and self.config.get("require_review"):
            raise WorkflowError("This board requires review: use `review` instead of `done`")
        return self._finish(task_id, actor, workflow.DONE, summary, links)

    def cancel(self, task_id: Any, actor: Actor, reason: str = "") -> Task:
        with self._transaction() as tx:
            task = self._require(task_id)
            self._check_owner(task, actor)
            self._transition(tx, task, workflow.CANCELLED, actor, reason)
        return task

    def reopen(self, task_id: Any, actor: Actor, note: str = "") -> Task:
        task_id = parse_task_id(task_id)
        with self._transaction() as tx:
            task = self.store.load(task_id)
            if task is None:
                archived = self.store.load_archived(task_id)
                if archived:
                    self.store.unarchive(archived)
                    task = archived
            if task is None:
                raise NotFoundError(f"No task {format_task_id(task_id)}")
            self._transition(tx, task, workflow.READY, actor, note or "Reopened")
        return task

    def move(self, task_id: Any, status: str, actor: Actor, text: str = "") -> Task:
        """Generic status change (the web board's drag and drop)."""
        status = workflow.require_status(status)
        if status == workflow.IN_PROGRESS and actor.is_agent:
            return self.claim(task_id, actor)
        if status == workflow.BLOCKED:
            return self.block(task_id, actor, text or "Blocked from the board")
        with self._transaction() as tx:
            task = self._require(task_id)
            self._check_owner(task, actor)
            self._transition(tx, task, status, actor, text)
        return task

    def place(self, task_id: Any, actor: Actor, status: Optional[str] = None,
              above: Optional[Any] = None, below: Optional[Any] = None) -> Task:
        """Drop a card: optionally change column, then sit between two neighbors.

        ``above`` is the card that ends up directly above, ``below`` the one
        directly below. The queue sorts by priority first, so dropping next to
        cards of another priority adopts their priority.
        """
        task = self._require(task_id)
        if status and workflow.require_status(status) != task.status:
            task = self.move(task.id, status, actor)
        upper = self._require(above) if above not in (None, "") else None
        lower = self._require(below) if below not in (None, "") else None
        target_priority = (upper or lower or task).priority
        upper = upper if upper and upper.priority == target_priority else None
        lower = lower if lower and lower.priority == target_priority else None
        if upper and lower:
            rank = (upper.rank + lower.rank) / 2
        elif upper:
            rank = upper.rank + 1.0
        elif lower:
            rank = lower.rank - 1.0
        else:
            rank = task.rank
        return self.update(task.id, {"rank": rank, "priority": target_priority}, actor, force=True)

    def add_links(self, task_id: Any, actor: Actor, links: Iterable[Link]) -> Task:
        with self._transaction() as tx:
            task = self._require(task_id)
            added = [l for l in links if all((x.kind, x.value) != (l.kind, l.value) for x in task.links)]
            if not added:
                return task
            task.links.extend(added)
            self._refresh_lease(task, actor)
            text = ", ".join(f"{l.kind} {l.value}" for l in added)
            self._activity(task, actor, "edit", f"Linked {text}")
            tx.save(task, "task.updated", actor, f"{task.key} linked {text}", fields=["links"])
            tx.hook("task.updated", task=task, actor=actor, changes={"links": added})
        return task

    # -------------------------------------------------------------- maintenance

    def reap_expired(self) -> List[Task]:
        """Return in-progress tasks whose lease expired to the Queue."""
        released = []
        with self._transaction() as tx:
            for task in self.store.load_all():
                if task.status != workflow.IN_PROGRESS or not task.claim:
                    continue
                age = age_seconds(task.claim.heartbeat_at, self.now())
                if age is None or age <= self.config.expire_seconds:
                    continue
                agent = task.claim.agent
                minutes = int(age // 60)
                self._transition(tx, task, workflow.READY, SYSTEM,
                                 f"{agent}'s lease expired after {minutes} min without activity")
                released.append(task)
        return released

    def archive_finished(self, days: Optional[float] = None) -> List[Task]:
        days = self.config.get("archive_after_days", 14) if days is None else days
        cutoff = self.now() - timedelta(days=float(days))
        archived = []
        with self._transaction():
            for task in self.store.load_all():
                if not workflow.STATUS_BY_KEY[task.status].terminal:
                    continue
                updated = from_iso(task.updated_at)
                if updated and updated <= cutoff:
                    self.store.archive(task)
                    self.store.append_event({"type": "task.archived", "at": self.now_iso(),
                                             "task": task.id, "by": SYSTEM.name,
                                             "actor": SYSTEM.kind, "summary": f"{task.key} archived",
                                             "data": {}})
                    archived.append(task)
        return archived

    def sync(self) -> List[Task]:
        """Periodic upkeep: expire leases, then let plugins sync (virtual commits, ...)."""
        released = self.reap_expired()
        self.hooks.emit("board.sync", board=self)
        return released
