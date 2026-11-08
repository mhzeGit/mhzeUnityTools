# Agent Task Board: architecture

How the task board is built and where to change things. User documentation is in [README.md](README.md).

## Goals that shaped the design

1. **Many writers, no server required.** Agents in different terminals and repositories write to the board at the same time, while the web board may or may not be running. So the file system is the source of truth, and every process uses the same lock.
2. **Readable by people and agents.** One JSON file per task, an append-only event log, plain-text CLI output, and `--json` everywhere.
3. **No dependencies.** Python standard library only, and plain browser JavaScript with no build step or CDN. It runs offline on any machine with Python 3.9+.
4. **Easy to change.** Each rule lives in one place: statuses in `workflow.py`, claim rules in `scheduler.py`, the file format in `model.py`, settings in `config.py`. Extension points (plugins, hooks, scheduler rules, commands, routes) need no edits to the core.

## Layers

```
 front ends     cli.py (terminal)          server.py + web/ (browser)
                       \                    /
 application            board.py  (Board: every query and mutation)
                       /     |      \
 rules          workflow.py  scheduler.py  paths.py       hooks.py / plugins.py
 data           model.py     config.py     project.py     integrations/*
 persistence    store.py (TaskStore protocol, FileStore)  locking.py
```

A module only imports from its own row or the rows below it. Front ends never write files themselves; plugins only use the `Board` API.

| Module | Responsibility |
| --- | --- |
| `model.py` | `Task`, `Claim`, `Link`, `Activity`, `Actor`; the only (de)serialization (`to_dict`/`from_dict`); field validation (`normalize_fields`); `SCHEMA_VERSION` + `migrate`; unknown keys kept in `Task.extra`. |
| `workflow.py` | The columns (`STATUSES`: key, label, hint, color) and allowed moves (`TRANSITIONS`). Agents must follow them; people may force a move. |
| `paths.py` | Whether two `touches` entries overlap (folders, files, globs; case- and slash-insensitive). |
| `scheduler.py` | Pure functions: `blockers()` (why a task cannot start), `pick_next()` (queue order + first claimable). Rules are listed in `RULES`. |
| `config.py` | Board folder resolution (`--home`, `TASKBOARD_HOME`, `~/.taskboard`) and `DEFAULTS` merged with `config.json`. |
| `project.py` | Repository root and project name of the current folder; git common dir. |
| `locking.py` | `BoardLock`: cross-process O_EXCL lock file + in-process RLock, re-entrant, stale-lock recovery. |
| `store.py` | `TaskStore` protocol and `FileStore`: atomic task writes, mtime cache, id/event counters, event log with rotation, archive. |
| `board.py` | `Board`: create, update, claim, claim_next, note, block, release, review, complete, cancel, reopen, move, place, links, lease expiry, archive, stats, snapshot. Each mutation is a `_transaction`: lock, load fresh, check, save, log event, release, run hooks. |
| `hooks.py` / `plugins.py` | Event bus (`task.created/updated/moved/note`, `board.sync`) and loading `register(context)` modules from config. |
| `integrations/virtual_commits.py` | Plugin: on `board.sync`, moves Review tasks to Done or Blocked by reading Virtual Commit records. |
| `cli.py` | `taskboard` commands; each is one `@command(name, help, arguments)` handler. |
| `render.py` | Plain-text output of the CLI. |
| `server.py` | Loopback HTTP API, Server-Sent Events stream (`EventHub` tails `events.jsonl`), periodic `Board.sync()`, static files, host/origin checks. |
| `web/` | The board UI (ES modules, see below). |
| `guide.py` | The agent protocol printed by `taskboard guide`. |

## Concurrency model

- **All mutations run under the board lock.** `claim_next` checks the scheduler and claims inside one lock, so ten agents calling `next` at once claim ten different tasks (`tests/test_integration.py::ConcurrencyTests` runs exactly that with real processes).
- **Reads never lock.** Task files are written to a temp file and swapped in with `os.replace`, so a reader sees either the old file or the new one. On Windows, a replace that hits an open reader is retried.
- **The lock** is `<home>/lock`, created with `O_CREAT|O_EXCL`. That works the same on NTFS, ext4 and network drives. A lock older than 20 seconds is considered abandoned; critical sections take milliseconds. Threads of one process (the web server) are serialized by an `RLock` first, and the lock is re-entrant, so a hook may call the board again.
- **Ids never repeat**: the task counter also checks the highest id on disk, including archived tasks.

## Live updates

```
agent CLI ──► Board ──► tasks/*.json + events.jsonl
                                   │ (tail every 0.4 s)
web server: EventHub ──► SSE /api/stream ──► browser: reload /api/board (debounced)
```

Every mutation appends one event `{seq, at, type, task, by, actor, summary, data}`. Heartbeats and pure reordering carry `data.quiet` so the activity feed skips them. The browser reloads the whole board on each event. That is cheap for hundreds of tasks and means the client never has to merge partial updates. After a reconnect it reloads once to catch up.

## Leases

`claim` stores `Claim(agent, claimed_at, heartbeat_at, repo_root, host)`. Any command by that agent on the task (`note`, `heartbeat`, `edit`, `link`) renews `heartbeat_at`. `view()` marks the lease *stale* after `lease.stale_minutes`. `reap_expired()` returns the task to the Queue after `lease.expire_minutes`. That runs on every `next`, every `sync`, and in the web server every `sync_interval_seconds`. Tasks a person started from the board get an assignee but no claim, so they never expire.

## Web UI (`taskboard/web`)

| File | Role |
| --- | --- |
| `index.html` | Static shell: top bar, board, drawer, feed, dialogs. |
| `app.css` | All styling; design tokens on `:root` (colors, radii, fonts, column width). |
| `js/main.js` | Boot, top bar, keyboard shortcuts, live stream wiring, periodic re-render of ages. |
| `js/state.js` | Single client state, `reload()`, filters, per-column sorting, counts. |
| `js/api.js` | Fetch wrappers mirroring the server routes, `connectStream()`. |
| `js/board.js` | Columns and drag and drop (`/place` with the cards above and below). |
| `js/card.js` | One card. |
| `js/drawer.js` | Task detail panel, per-status action buttons, notes composer. |
| `js/editor.js` | Create/edit dialog. |
| `js/feed.js` | Activity panel. |
| `js/ask.js`, `js/toast.js`, `js/util.js` | One-line prompt dialog, toasts, DOM helpers (`h`, `fill`, `icon`, `linkify`, ages, avatars). |

User text is only inserted as text nodes, never as HTML. Column labels, colors and hints come from `/api/board` (`workflow.STATUSES`), so the UI has no hard-coded list of statuses except the empty-column texts.

## How to extend

**Add a scheduling rule** (for example "at most one task per label in progress"): write `def rule_x(task, tasks, config) -> list[str]` in `scheduler.py` and append it to `RULES`. Reasons show up on cards, in the drawer and in `next` output automatically. Add a test in `tests/test_board.py::SchedulingTests`.

**Add a column/status:** add a `StatusDef` to `STATUSES` and its moves to `TRANSITIONS` in `workflow.py`, then add an empty-column text in `web/js/board.js` and buttons in `drawer.js` `actionsFor()`. `test_every_status_has_transitions_and_aliases_resolve` checks the table is complete.

**Add a task field:** add it to `Task` (with a default), to `to_dict`/`from_dict` and `_KNOWN_KEYS`, and, if callers may set it, to `EDITABLE_FIELDS` and `normalize_fields`. Older files simply get the default. If the meaning of an existing field changes, bump `SCHEMA_VERSION` and add a step to `migrate()`.

**Add a CLI command:** write an arguments function and a handler decorated with `@command("name", "help", arguments)` in `cli.py`. Handlers call `ctx.board` and print with `ctx.out(json_payload, text)`.

**Add an API route:** add `(method, regex, handler_name)` to `ROUTES` in `server.py` and a method on `Handler`, then a wrapper in `web/js/api.js`.

**Write a plugin** (notifications, Discord, a sound when a task is blocked, syncing to GitHub Issues...):

```python
# my_board_plugins/notify.py
def register(context):
    def on_moved(board, task, actor, from_status, to_status):
        if to_status == "blocked":
            ...  # task.blocked_reason holds the agent's question
    context.hooks.on("task.moved", on_moved)
```

Then add `"plugin_paths": ["F:/Projects/Tools"]` and `"plugins": ["taskboard.integrations.virtual_commits", "my_board_plugins.notify"]` to `config.json`. Hook handlers run after the change is saved and the lock is released. A failing handler is logged and never breaks the operation.

**Swap the storage** (SQLite, a shared server): implement the `TaskStore` protocol in `store.py` and pass it to `Board(store, config)`. The web server's `EventHub` tails `FileStore.events_path`. Another backend would give it its own event source with the same `subscribe`/`publish` interface.

## Testing

`python -m unittest discover -s tests -t tests` (35 tests, about 2 s). Tests use a temp board folder and a `FakeClock`, so lease expiry and archiving need no sleeping. Every test that waits on a process, socket or thread carries `@deadline(...)`, which dumps all stacks and exits if it hangs. Coverage:

- `test_core.py`: model round-trip and validation, workflow, path overlap.
- `test_board.py`: lifecycle, queue order, dependencies (including cycles), file and resource conflicts, project filter, drag placement, ownership, leases, events, archive, hooks.
- `test_integration.py`: 10 real processes racing for 5 tasks, the CLI, the HTTP API and its security checks, the SSE stream receiving another writer's change, and the virtual-commit plugin against a real git repository.

## Ideas for later

- Per-agent concurrency limit (`max_tasks_per_agent`) as a scheduler rule.
- `taskboard work`: a helper that prints the next task as a ready-made prompt for a fresh agent session.
- Session spawning: a plugin that starts a new cloud or local agent session for each queued task.
- Time-in-column charts on top of `events.jsonl` (the data is already there).
- Import from the `LocalProjectManagmentFiles` boards of the separate project-management app.
- An MCP server wrapping the same `Board` API for agents that prefer tools to shell commands.
