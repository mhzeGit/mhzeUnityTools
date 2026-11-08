"""Plain-text output for the CLI (``--json`` bypasses this module)."""

from __future__ import annotations

import textwrap
from typing import Dict, List

from . import workflow
from .clock import format_age
from .model import Task, format_task_id

PRIORITY_MARK = {"urgent": "!!", "high": "! ", "normal": "  ", "low": "- "}


def task_line(task: Task) -> str:
    project = f"[{task.project}] " if task.project else ""
    who = f"  @{task.assignee}" if task.assignee else ""
    mark = PRIORITY_MARK.get(task.priority, "  ")
    return f"{mark}{task.key:>6}  {task.status:<11} {project}{task.title}{who}"


def task_list(tasks: List[Task], grouped: bool = True) -> str:
    if not tasks:
        return "No tasks."
    if not grouped:
        return "\n".join(task_line(t) for t in tasks)
    lines = []
    for status in workflow.STATUSES:
        group = [t for t in tasks if t.status == status.key]
        if not group:
            continue
        lines.append(f"{status.label} ({len(group)})")
        lines.extend("  " + task_line(t) for t in group)
        lines.append("")
    return "\n".join(lines).rstrip()


def _wrap(text: str, indent: str = "  ") -> str:
    return "\n".join(
        textwrap.fill(line, 96, initial_indent=indent, subsequent_indent=indent) if line else ""
        for line in text.splitlines()
    )


def task_detail(view: Dict) -> str:
    status = workflow.STATUS_BY_KEY[view["status"]].label
    out = [f"{view['key']}  {view['title']}", f"  status:    {status}", f"  priority:  {view['priority']}"]
    if view.get("project"):
        out.append(f"  project:   {view['project']}")
    if view.get("assignee"):
        out.append(f"  assignee:  {view['assignee']}")
    lease = view.get("lease")
    if lease:
        flag = "  STALE" if lease["stale"] else ""
        out.append(f"  lease:     last activity {format_age(lease['age_seconds'])} ago{flag}")
    for key, label in (("labels", "labels"), ("needs", "needs"), ("touches", "touches")):
        if view.get(key):
            out.append(f"  {label + ':':<10} {', '.join(view[key])}")
    if view.get("depends_on"):
        out.append(f"  depends:   {', '.join(format_task_id(i) for i in view['depends_on'])}")
    if view.get("blockers"):
        out.append("  waiting:   " + "; ".join(view["blockers"]))
    if view.get("blocked_reason"):
        out.append(f"  blocked:   {view['blocked_reason']}")
    if view.get("description"):
        out += ["", "Description", _wrap(view["description"])]
    if view.get("acceptance"):
        out += ["", "Acceptance criteria"] + [f"  - {item}" for item in view["acceptance"]]
    if view.get("result"):
        out += ["", "Result", _wrap(view["result"])]
    if view.get("links"):
        out += ["", "Links"] + [f"  {l['kind']}: {l['value']}" for l in view["links"]]
    if view.get("activity"):
        out += ["", "Activity"]
        for entry in view["activity"][-12:]:
            move = f"{entry.get('from')} → {entry.get('to')}  " if entry.get("kind") == "status" else ""
            out.append(f"  {entry['at'][:16].replace('T', ' ')}  {entry['by']}: {move}{entry.get('text', '')}")
    return "\n".join(out)
