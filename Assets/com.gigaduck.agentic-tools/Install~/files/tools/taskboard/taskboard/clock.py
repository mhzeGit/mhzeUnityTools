"""Time helpers. Every timestamp on the board is an ISO-8601 UTC string.

The board takes a ``now`` callable so tests can move time forward without
sleeping; production code uses :func:`utc_now`.
"""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Callable, Optional

Clock = Callable[[], datetime]


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def to_iso(moment: datetime) -> str:
    return moment.astimezone(timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def from_iso(text: Optional[str]) -> Optional[datetime]:
    if not text:
        return None
    try:
        value = datetime.fromisoformat(text.replace("Z", "+00:00"))
    except ValueError:
        return None
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)
    return value


def age_seconds(text: Optional[str], now: datetime) -> Optional[float]:
    moment = from_iso(text)
    if moment is None:
        return None
    return (now - moment).total_seconds()


def format_age(seconds: Optional[float]) -> str:
    """Short human age: 45s, 12m, 3h, 2d."""
    if seconds is None:
        return "?"
    seconds = max(0, int(seconds))
    if seconds < 60:
        return f"{seconds}s"
    if seconds < 3600:
        return f"{seconds // 60}m"
    if seconds < 86400:
        return f"{seconds // 3600}h"
    return f"{seconds // 86400}d"
