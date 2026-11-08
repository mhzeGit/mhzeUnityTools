"""Command line front end: `taskboard <command> ...`.

Every command is one handler registered in ``COMMANDS`` with
``@command(name, help, arguments)``, where ``arguments`` is the small function
right above it that declares its argparse options. Adding a command = one
arguments function plus one decorated handler. Commands receive the parsed args
and a ``Context`` (board, actor, project) and return an exit code.

Exit codes: 0 ok, 1 error, 2 nothing to do (``next`` with an empty queue).
"""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Dict, List, Optional

from . import __version__, render, workflow
from .board import Board
from .errors import BoardError
from .guide import AGENT_GUIDE
from .model import ACTOR_AGENT, ACTOR_USER, Actor, Link
from .project import detect_project, repo_root

EXIT_OK, EXIT_ERROR, EXIT_NOTHING = 0, 1, 2


@dataclass
class Context:
    args: argparse.Namespace
    board: Board
    actor: Actor
    project: str
    repo_root: str

    def out(self, payload, text: str) -> None:
        if self.args.json:
            print(json.dumps(payload, indent=2, ensure_ascii=False))
        else:
            print(text)


Handler = Callable[[Context], int]
COMMANDS: Dict[str, "CommandSpec"] = {}


@dataclass
class CommandSpec:
    name: str
    help: str
    handler: Handler
    configure: Callable[[argparse.ArgumentParser], None]
    needs_board: bool = True


def command(name: str, help: str, arguments: Optional[Callable[[argparse.ArgumentParser], None]] = None,
            needs_board: bool = True):
    """Register a command handler; ``arguments`` declares its argparse options."""
    def register(handler: Handler) -> Handler:
        COMMANDS[name] = CommandSpec(name, help, handler, arguments or (lambda p: None), needs_board)
        return handler
    return register


# ------------------------------------------------------------------ arg helpers

def _task_fields(p: argparse.ArgumentParser, creating: bool) -> None:
    if not creating:
        p.add_argument("--title", help="new title")
    p.add_argument("-d", "--description", help="details, context, links (plain text or markdown)")
    p.add_argument("--accept", action="append", metavar="TEXT",
                   help="an acceptance criterion (repeatable)")
    p.add_argument("-p", "--priority", choices=None, help="urgent | high | normal | low")
    p.add_argument("--label", action="append", metavar="NAME", help="label (repeatable or comma list)")
    p.add_argument("--touches", action="append", metavar="PATH",
                   help="file, folder or glob this task edits (repeatable); used to avoid conflicts")
    p.add_argument("--needs", action="append", metavar="RESOURCE",
                   help="exclusive resource such as unity-editor (repeatable)")
    p.add_argument("--depends", action="append", metavar="ID", help="task that must be done first")


def _collect_fields(args: argparse.Namespace) -> Dict:
    def joined(values):
        return None if values is None else ",".join(values)

    return {
        "title": getattr(args, "title", None),
        "description": args.description,
        "acceptance": args.accept,
        "priority": args.priority,
        "project": args.project,  # the shared --project option
        "labels": joined(args.label),
        "touches": joined(args.touches),
        "needs": joined(args.needs),
        "depends_on": joined(args.depends),
    }


_SHA_RE = re.compile(r"^[0-9a-f]{7,40}$", re.IGNORECASE)


def _links(ctx: Context) -> List[Link]:
    links = []
    for value in getattr(ctx.args, "commit", None) or []:
        value = value.strip().lstrip("#")
        if value.isdigit():
            links.append(Link("virtual_commit", value, repo=ctx.repo_root))
        elif _SHA_RE.match(value):
            links.append(Link("commit", value, repo=ctx.repo_root))
        else:
            raise BoardError(f"--commit takes a virtual commit id (37) or a git sha, not {value!r}")
    for value in getattr(ctx.args, "link", None) or []:
        links.append(Link("url" if "://" in value else "file", value))
    return links


def _show(ctx: Context, task, message: str = "") -> int:
    view = ctx.board.view(task)
    ctx.out(view, (message + "\n" if message else "") + render.task_detail(view))
    return EXIT_OK


def _done_message(task) -> str:
    return f"{task.key} → {workflow.STATUS_BY_KEY[task.status].label}"


# --------------------------------------------------------------------- commands

def _add_args(p):
    p.add_argument("title")
    _task_fields(p, creating=True)
    where = p.add_mutually_exclusive_group()
    where.add_argument("--inbox", action="store_const", const=workflow.PROPOSED, dest="status")
    where.add_argument("--queue", action="store_const", const=workflow.READY, dest="status")


@command("add", "create a task (agents: lands in the Inbox)", _add_args)
def cmd_add(ctx: Context) -> int:
    fields = _collect_fields(ctx.args)
    fields["title"] = ctx.args.title
    if fields["project"] is None:
        fields["project"] = ctx.project
    task = ctx.board.create(fields, ctx.actor, status=ctx.args.status)
    ctx.out(ctx.board.view(task), f"Created {task.key} in {workflow.STATUS_BY_KEY[task.status].label}: {task.title}")
    return EXIT_OK


def _list_args(p):
    p.add_argument("-s", "--status", action="append", help="only these statuses (repeatable)")
    p.add_argument("--here", action="store_true", help="only tasks of the current repo (and of any repo)")
    p.add_argument("--mine", action="store_true", help="only tasks assigned to --agent")
    p.add_argument("--flat", action="store_true", help="one line per task, no grouping")


@command("list", "list tasks grouped by column", _list_args)
def cmd_list(ctx: Context) -> int:
    a = ctx.args
    tasks = ctx.board.tasks(a.status, ctx.project if a.here else None, ctx.actor.name if a.mine else None)
    if not a.status:
        tasks = [t for t in tasks if t.status != workflow.CANCELLED]
    order = {s.key: i for i, s in enumerate(workflow.STATUSES)}
    tasks.sort(key=lambda t: (order[t.status], ctx.board.config.priorities.index(t.priority)
                              if t.priority in ctx.board.config.priorities else 9, t.rank, t.id))
    all_tasks = ctx.board.tasks()
    ctx.out([ctx.board.view(t, all_tasks) for t in tasks], render.task_list(tasks, grouped=not a.flat))
    return EXIT_OK


def _show_args(p):
    p.add_argument("id")


@command("show", "show one task with its history", _show_args)
def cmd_show(ctx: Context) -> int:
    return _show(ctx, ctx.board.get(ctx.args.id))


def _next_args(p):
    p.add_argument("--dry-run", action="store_true", help="only say which task would be claimed")
    p.add_argument("--any-project", action="store_true", help="also take tasks of other repositories")


@command("next", "claim the next task of this repo's queue", _next_args)
def cmd_next(ctx: Context) -> int:
    project = None if ctx.args.any_project else ctx.project
    task, skipped = ctx.board.claim_next(ctx.actor, project, ctx.repo_root, dry_run=ctx.args.dry_run)
    if task is None:
        waiting = [{"task": f"T-{i}", "reasons": r} for i, r in skipped.items()]
        lines = ["Nothing to claim" + (f" for {project}" if project else "") + "."]
        lines += [f"  T-{i} waits: {'; '.join(r)}" for i, r in skipped.items()]
        ctx.out({"task": None, "waiting": waiting}, "\n".join(lines))
        return EXIT_NOTHING
    verb = "Would claim" if ctx.args.dry_run else "Claimed"
    return _show(ctx, task, f"{verb} {task.key}.")


def _claim_args(p):
    p.add_argument("id")
    p.add_argument("--force", action="store_true", help="ignore dependencies and file conflicts")


@command("claim", "claim a specific queued task", _claim_args)
def cmd_claim(ctx: Context) -> int:
    task = ctx.board.claim(ctx.args.id, ctx.actor, ctx.repo_root, force=ctx.args.force)
    return _show(ctx, task, f"Claimed {task.key}.")


def _note_args(p):
    p.add_argument("id")
    p.add_argument("text")


@command("note", "add a progress note (renews your lease)", _note_args)
def cmd_note(ctx: Context) -> int:
    task = ctx.board.note(ctx.args.id, ctx.actor, ctx.args.text)
    ctx.out(ctx.board.view(task), f"Noted on {task.key}.")
    return EXIT_OK


def _heartbeat_args(p):
    p.add_argument("id")


@command("heartbeat", "renew your lease without a note", _heartbeat_args)
def cmd_heartbeat(ctx: Context) -> int:
    task = ctx.board.heartbeat(ctx.args.id, ctx.actor)
    ctx.out(ctx.board.view(task), f"{task.key} lease renewed.")
    return EXIT_OK


def _finish_args(p):
    p.add_argument("id")
    p.add_argument("-s", "--summary", default="", help="what you did, for the user")
    p.add_argument("--commit", action="append", metavar="ID|SHA",
                   help="virtual commit id (e.g. 37) or a git sha (repeatable)")
    p.add_argument("--link", action="append", metavar="URL|PATH", help="extra link (repeatable)")


@command("review", "finished: send to Review for the user to test", _finish_args)
def cmd_review(ctx: Context) -> int:
    task = ctx.board.submit_review(ctx.args.id, ctx.actor, ctx.args.summary, _links(ctx))
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


@command("done", "finished and verified (nothing left for the user to test)", _finish_args)
def cmd_done(ctx: Context) -> int:
    task = ctx.board.complete(ctx.args.id, ctx.actor, ctx.args.summary, _links(ctx))
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


def _id_and_text(name: str, required: bool):
    def configure(p):
        p.add_argument("id")
        p.add_argument(name, nargs=None if required else "?", default="")
    return configure


@command("block", "stuck: say what you need from the user", _id_and_text("reason", True))
def cmd_block(ctx: Context) -> int:
    task = ctx.board.block(ctx.args.id, ctx.actor, ctx.args.reason)
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


@command("unblock", "put a blocked task back in the Queue", _id_and_text("note", False))
def cmd_unblock(ctx: Context) -> int:
    task = ctx.board.unblock(ctx.args.id, ctx.actor, ctx.args.note)
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


@command("release", "give an unfinished task back to the Queue", _id_and_text("reason", False))
def cmd_release(ctx: Context) -> int:
    task = ctx.board.release(ctx.args.id, ctx.actor, ctx.args.reason)
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


@command("cancel", "drop a task", _id_and_text("reason", False))
def cmd_cancel(ctx: Context) -> int:
    task = ctx.board.cancel(ctx.args.id, ctx.actor, ctx.args.reason)
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


@command("reopen", "bring a done, cancelled or archived task back to the Queue", _id_and_text("note", False))
def cmd_reopen(ctx: Context) -> int:
    task = ctx.board.reopen(ctx.args.id, ctx.actor, ctx.args.note)
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


def _move_args(p):
    p.add_argument("id")
    p.add_argument("status", help=" | ".join(workflow.STATUS_KEYS) + " (or inbox, queue)")
    p.add_argument("note", nargs="?", default="")


@command("move", "move a task to any column (people only outside the workflow)", _move_args)
def cmd_move(ctx: Context) -> int:
    task = ctx.board.move(ctx.args.id, ctx.args.status, ctx.actor, ctx.args.note)
    ctx.out(ctx.board.view(task), _done_message(task))
    return EXIT_OK


def _edit_args(p):
    p.add_argument("id")
    _task_fields(p, creating=False)


@command("edit", "change task fields (list options replace the whole list)", _edit_args)
def cmd_edit(ctx: Context) -> int:
    task = ctx.board.update(ctx.args.id, _collect_fields(ctx.args), ctx.actor)
    return _show(ctx, task, f"Updated {task.key}.")


def _link_args(p):
    p.add_argument("id")
    p.add_argument("--commit", action="append", metavar="ID|SHA")
    p.add_argument("--link", action="append", metavar="URL|PATH")


@command("link", "attach a virtual commit, git sha, URL or file to a task", _link_args)
def cmd_link(ctx: Context) -> int:
    task = ctx.board.add_links(ctx.args.id, ctx.actor, _links(ctx))
    ctx.out(ctx.board.view(task), f"Linked to {task.key}.")
    return EXIT_OK


@command("sync", "expire stale leases and run plugin sync (virtual commits)", None)
def cmd_sync(ctx: Context) -> int:
    released = ctx.board.sync()
    ctx.out({"released": [t.key for t in released]},
            "Synced." + (f" Lease expired: {', '.join(t.key for t in released)}" if released else ""))
    return EXIT_OK


def _archive_args(p):
    p.add_argument("--days", type=float, default=None, help="older than this many days (default from config)")


@command("archive", "move old done/cancelled tasks out of the board", _archive_args)
def cmd_archive(ctx: Context) -> int:
    archived = ctx.board.archive_finished(ctx.args.days)
    ctx.out({"archived": [t.key for t in archived]}, f"Archived {len(archived)} task(s).")
    return EXIT_OK


@command("stats", "counts and throughput", None)
def cmd_stats(ctx: Context) -> int:
    stats = ctx.board.stats()
    lines = ["By status:"] + [f"  {workflow.STATUS_BY_KEY[k].label:<12} {v}" for k, v in stats["by_status"].items()]
    lines += ["Open by project:"] + [f"  {k:<20} {v}" for k, v in sorted(stats["open_by_project"].items())]
    lines += [f"Done in 24h: {stats['done_last_24h']}   in 7d: {stats['done_last_7d']}",
              f"Median lead time: {stats['median_lead_time_hours'] or '-'} h"]
    ctx.out(stats, "\n".join(lines))
    return EXIT_OK


def _serve_args(p):
    p.add_argument("--port", type=int, default=None, help="default from config (8765)")
    p.add_argument("--open", action="store_true", help="open the board in your browser")


@command("serve", "open the web board (http://127.0.0.1:8765)", _serve_args)
def cmd_serve(ctx: Context) -> int:
    from .server import serve

    port = ctx.args.port if ctx.args.port is not None else int(ctx.board.config.get("port", 8765))
    serve(ctx.board, port=port, open_browser=ctx.args.open)
    return EXIT_OK


@command("info", "where the board lives and what this shell resolves to", None)
def cmd_info(ctx: Context) -> int:
    info = {
        "version": __version__,
        "home": str(ctx.board.config.home),
        "actor": {"name": ctx.actor.name, "kind": ctx.actor.kind},
        "project": ctx.project,
        "repo_root": ctx.repo_root,
        "plugins": ctx.board.plugins,
    }
    ctx.out(info, "\n".join(f"{k:<10} {v}" for k, v in info.items()))
    return EXIT_OK


@command("guide", "print the protocol agents follow", None, needs_board=False)
def cmd_guide(ctx: Optional[Context]) -> int:
    print(AGENT_GUIDE)
    return EXIT_OK


# ----------------------------------------------------------------------- parser

def build_parser() -> argparse.ArgumentParser:
    common = argparse.ArgumentParser(add_help=False)
    common.add_argument("--home", type=Path, default=argparse.SUPPRESS,
                        help="board folder (default: $TASKBOARD_HOME or ~/.taskboard)")
    common.add_argument("-a", "--agent", default=argparse.SUPPRESS,
                        help="act as this agent (default: $TASKBOARD_AGENT; none = you)")
    common.add_argument("--project", dest="project", default=argparse.SUPPRESS,
                        help="repository name: the queue `next` reads, the project `add` sets "
                             "(default: current git repo; '' with add = any project)")
    common.add_argument("--json", action="store_true", default=argparse.SUPPRESS,
                        help="machine-readable output")

    parser = argparse.ArgumentParser(
        prog="taskboard", parents=[common],
        description="Local kanban for you and your AI agents. `taskboard guide` explains the agent protocol.",
    )
    parser.add_argument("--version", action="version", version=f"taskboard {__version__}")
    sub = parser.add_subparsers(dest="command", metavar="<command>")
    for spec in COMMANDS.values():
        p = sub.add_parser(spec.name, help=spec.help, description=spec.help, parents=[common])
        spec.configure(p)
    return parser


def _make_actor(args: argparse.Namespace, board: Board) -> Actor:
    name = (getattr(args, "agent", None) or os.environ.get("TASKBOARD_AGENT", "")).strip()
    if name:
        return Actor(name, ACTOR_AGENT)
    return Actor(str(board.config.get("user_name", "you")), ACTOR_USER)


def main(argv: Optional[List[str]] = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(encoding="utf-8")  # arrows and names on Windows consoles
        except (ValueError, OSError):
            pass
    parser = build_parser()
    args = parser.parse_args(argv)
    for key, default in (("home", None), ("agent", None), ("project", None), ("json", False)):
        if not hasattr(args, key):
            setattr(args, key, default)
    if not args.command:
        parser.print_help()
        return EXIT_OK
    spec = COMMANDS[args.command]
    if not spec.needs_board:
        return spec.handler(None)  # type: ignore[arg-type]
    try:
        board = Board.open(args.home)
        root = repo_root()
        project = args.project if args.project is not None else detect_project()
        ctx = Context(args, board, _make_actor(args, board), project, root)
        return spec.handler(ctx)
    except BoardError as exc:
        if args.json:
            print(json.dumps({"error": str(exc), "type": type(exc).__name__}))
        else:
            print(f"taskboard: {exc}", file=sys.stderr)
        return EXIT_ERROR
    except KeyboardInterrupt:
        return EXIT_ERROR


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
