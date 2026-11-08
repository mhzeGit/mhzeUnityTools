"""Which repository (project) a command runs in.

A task's ``project`` is the folder name of its repository root (for example
``Graphmat`` or ``BlahBlahFamilyV6``). ``taskboard next`` only offers tasks of
the agent's own project plus tasks with no project.
"""

from __future__ import annotations

import os
import subprocess
from pathlib import Path
from typing import Optional


def repo_root(start: Optional[Path] = None) -> str:
    """The git top-level folder of ``start`` (default: cwd), or "" outside a repo."""
    start = Path(start or os.getcwd())
    try:
        out = subprocess.run(
            ["git", "-C", str(start), "rev-parse", "--show-toplevel"],
            capture_output=True, text=True, timeout=10,
        )
    except (OSError, subprocess.SubprocessError):
        return ""
    if out.returncode != 0:
        return ""
    return str(Path(out.stdout.strip()))


def detect_project(start: Optional[Path] = None) -> str:
    env = os.environ.get("TASKBOARD_PROJECT", "").strip()
    if env:
        return env
    root = repo_root(start)
    return Path(root).name if root else ""


def git_common_dir(root: str) -> Optional[Path]:
    """``<repo>/.git`` for a main checkout (shared by linked worktrees)."""
    if not root:
        return None
    try:
        out = subprocess.run(
            ["git", "-C", root, "rev-parse", "--git-common-dir"],
            capture_output=True, text=True, timeout=10,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if out.returncode != 0:
        return None
    path = Path(out.stdout.strip())
    return path if path.is_absolute() else (Path(root) / path).resolve()
