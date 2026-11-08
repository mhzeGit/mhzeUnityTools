"""Error types. The CLI maps them to exit code 1 and the server to HTTP codes."""

from __future__ import annotations


class BoardError(Exception):
    """Base class for every expected, user-facing failure."""

    http_status = 400


class ValidationError(BoardError):
    """A field value is missing or malformed."""


class NotFoundError(BoardError):
    http_status = 404


class WorkflowError(BoardError):
    """A status change the workflow does not allow (see workflow.TRANSITIONS)."""

    http_status = 409


class OwnershipError(BoardError):
    """An agent tried to change a task another agent has claimed."""

    http_status = 409


class NotClaimableError(BoardError):
    """The task cannot be claimed now (dependencies, file conflicts, resources)."""

    http_status = 409


class LockTimeoutError(BoardError):
    """The board lock could not be taken in time."""

    http_status = 503
