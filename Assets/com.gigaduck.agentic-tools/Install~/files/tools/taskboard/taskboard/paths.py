"""Overlap rules for a task's ``touches`` list (files, folders and globs).

Two tasks in the same project conflict when any of their touched paths
overlap: equal, one is a folder containing the other, or a glob matches.
Comparison is case-insensitive (Windows file systems are) and slash-agnostic.
"""

from __future__ import annotations

import fnmatch
from typing import Iterable, Optional, Tuple

_WILDCARDS = "*?["


def normalize(path: str) -> str:
    text = path.strip().replace("\\", "/")
    while text.startswith("./"):
        text = text[2:]
    while "//" in text:
        text = text.replace("//", "/")
    return text.rstrip("/").lower()


def _literal_prefix(path: str) -> str:
    """The folder part of a glob before its first wildcard ("a/b/*.cs" -> "a/b")."""
    cut = min((path.find(c) for c in _WILDCARDS if c in path), default=-1)
    if cut < 0:
        return path
    head = path[:cut]
    return head[: head.rfind("/")] if "/" in head else ""


def _contains(folder: str, path: str) -> bool:
    return folder == "" or path == folder or path.startswith(folder + "/")


def overlap(a: str, b: str) -> bool:
    a, b = normalize(a), normalize(b)
    if not a or not b:
        return False
    if a == b or _contains(a, b) or _contains(b, a):
        return True
    a_glob = any(c in a for c in _WILDCARDS)
    b_glob = any(c in b for c in _WILDCARDS)
    if a_glob and fnmatch.fnmatchcase(b, a):
        return True
    if b_glob and fnmatch.fnmatchcase(a, b):
        return True
    if a_glob and b_glob:
        # Two globs: be conservative and compare their literal folders.
        pa, pb = _literal_prefix(a), _literal_prefix(b)
        return _contains(pa, pb) or _contains(pb, pa)
    if a_glob:
        return False if not _literal_prefix(a) else _contains(b, _literal_prefix(a))
    if b_glob:
        return False if not _literal_prefix(b) else _contains(a, _literal_prefix(b))
    return False


def first_overlap(mine: Iterable[str], theirs: Iterable[str]) -> Optional[Tuple[str, str]]:
    theirs = list(theirs)
    for a in mine:
        for b in theirs:
            if overlap(a, b):
                return a, b
    return None
