"""Close the loop with the Virtual Commit tool (tools/git/agent-commit.ps1).

An agent that finishes a task registers a virtual commit and runs
``taskboard review T-12 --commit 37``. That stores a ``virtual_commit`` link
(value = record id, repo = the repository root). On every ``board.sync`` this
plugin reads the record file

    <git-common-dir>/agent-commits/records/0037.json

and, once you commit the record with virtual-commit.bat (status
``applied``), moves the task from Review to Done with the commit sha. If you
discard the record instead, the task goes to Blocked so you can decide what
happens next. Records it cannot find are left alone.
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Optional

from .. import workflow
from ..model import ACTOR_SYSTEM, Actor, Link
from ..project import git_common_dir

ACTOR = Actor("virtual-commit", ACTOR_SYSTEM)
LINK_KIND = "virtual_commit"


def read_record(repo_root: str, record_id: str, cache: Dict[str, Optional[Path]]) -> Optional[dict]:
    if repo_root not in cache:
        cache[repo_root] = git_common_dir(repo_root)
    common = cache[repo_root]
    if common is None:
        return None
    try:
        number = int(record_id)
    except ValueError:
        return None
    path = common / "agent-commits" / "records" / f"{number:04d}.json"
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError):
        return None


def sync(board) -> None:
    cache: Dict[str, Optional[Path]] = {}
    for task in board.tasks([workflow.REVIEW]):
        records = [l for l in task.links if l.kind == LINK_KIND and l.repo]
        if not records:
            continue
        states = []
        for link in records:
            record = read_record(link.repo, link.value, cache)
            states.append((link, record.get("status") if record else None, record or {}))
        discarded = [link for link, status, _ in states if status == "discarded"]
        if discarded:
            ids = ", ".join(f"#{l.value}" for l in discarded)
            board.block(task.id, ACTOR, f"Virtual commit {ids} was discarded; decide whether to redo the task")
            continue
        if states and all(status == "applied" for _, status, _ in states):
            links = [Link("commit", rec["commit"], repo=link.repo)
                     for link, _, rec in states if rec.get("commit")]
            text = ("Committed " + ", ".join(l.value[:9] for l in links)) if links else "Committed"
            board.complete(task.id, ACTOR, text, links)


def register(context) -> None:
    context.hooks.on("board.sync", sync)
