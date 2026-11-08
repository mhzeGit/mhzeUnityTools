"""Which ready task an agent may take, and why the others must wait.

Pure functions over a list of tasks: no I/O, no clock, easy to test and to
extend with new rules. Each rule returns human-readable reasons; a task with
no reasons is claimable. ``RULES`` is the ordered list of checks, so a new
rule (for example "only one task per label") is one function added there.

Queue order: priority (urgent first), then ``rank`` (your manual order on
the board), then id (oldest first).
"""

from __future__ import annotations

from typing import Callable, Dict, Iterable, List, Optional, Tuple

from . import workflow
from .config import Config
from .model import Task, format_task_id
from .paths import first_overlap

Rule = Callable[[Task, List[Task], Config], List[str]]


def _holders(tasks: Iterable[Task], config: Config, exclude: int) -> List[Task]:
    """Tasks currently reserving their files and resources."""
    statuses = set(config.conflict_statuses)
    return [t for t in tasks if t.id != exclude and t.status in statuses]


def _same_project(a: Task, b: Task) -> bool:
    # A task without a project could touch any repository, so it conflicts with all.
    return not a.project or not b.project or a.project.lower() == b.project.lower()


def rule_dependencies(task: Task, tasks: List[Task], config: Config) -> List[str]:
    by_id = {t.id: t for t in tasks}
    reasons = []
    for dep in task.depends_on:
        other = by_id.get(dep)
        if other is None:
            continue  # archived (finished) or deleted: not blocking
        if other.status != workflow.DONE:
            label = workflow.STATUS_BY_KEY[other.status].label.lower()
            reasons.append(f"waits for {format_task_id(dep)} ({label})")
    return reasons


def rule_file_conflicts(task: Task, tasks: List[Task], config: Config) -> List[str]:
    if not task.touches:
        return []
    reasons = []
    for other in _holders(tasks, config, task.id):
        if not other.touches or not _same_project(task, other):
            continue
        hit = first_overlap(task.touches, other.touches)
        if hit:
            who = f", {other.assignee}" if other.assignee else ""
            reasons.append(f"{hit[0]} overlaps {format_task_id(other.id)} ({other.status}{who})")
    return reasons


def rule_resources(task: Task, tasks: List[Task], config: Config) -> List[str]:
    reasons = []
    holders = _holders(tasks, config, task.id)
    for resource in task.needs:
        users = [t for t in holders if resource in t.needs and t.status == workflow.IN_PROGRESS]
        if len(users) >= config.resource_capacity(resource):
            ids = ", ".join(format_task_id(t.id) for t in users)
            reasons.append(f"{resource} is in use by {ids}")
    return reasons


RULES: List[Rule] = [rule_dependencies, rule_file_conflicts, rule_resources]


def blockers(task: Task, tasks: List[Task], config: Config) -> List[str]:
    """Why ``task`` cannot be claimed right now (empty list = claimable)."""
    reasons: List[str] = []
    for rule in RULES:
        reasons.extend(rule(task, tasks, config))
    return reasons


def queue_key(task: Task, config: Config) -> Tuple[int, float, int]:
    priorities = config.priorities
    level = priorities.index(task.priority) if task.priority in priorities else len(priorities)
    return (level, task.rank, task.id)


def project_matches(task: Task, project: Optional[str]) -> bool:
    """None = any project; otherwise the task's project or tasks with no project."""
    if project is None:
        return True
    return not task.project or task.project.lower() == project.lower()


def ordered_queue(tasks: List[Task], config: Config, project: Optional[str] = None) -> List[Task]:
    ready = [t for t in tasks if t.status == workflow.READY and project_matches(t, project)]
    return sorted(ready, key=lambda t: queue_key(t, config))


def pick_next(
    tasks: List[Task], config: Config, project: Optional[str] = None
) -> Tuple[Optional[Task], Dict[int, List[str]]]:
    """The first claimable task in queue order, and the reasons the ones before it wait."""
    skipped: Dict[int, List[str]] = {}
    for task in ordered_queue(tasks, config, project):
        reasons = blockers(task, tasks, config)
        if not reasons:
            return task, skipped
        skipped[task.id] = reasons
    return None, skipped
