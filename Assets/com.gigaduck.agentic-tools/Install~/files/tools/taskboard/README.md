# Agent Task Board

A local kanban board that you and your AI agents share. You queue tasks, agents claim them one at a time and keep their status up to date, and the board in your browser updates live while they work.

- **One board for all your repositories.** It lives in `~/.taskboard` and tasks carry their project (repository) name, so the agents of every repository use the same board.
- **Safe for parallel agents.** Claiming is atomic, so two agents never take the same task. A task waits while another one edits the same files (`touches`), uses the same exclusive resource (`needs`, such as `unity-editor`), or depends on an unfinished task.
- **Self-healing.** An agent that goes quiet gets a warning on its card after 20 minutes. After 2 hours the task returns to the Queue for another agent.
- **Closes the loop with virtual commits.** An agent that finishes registers its virtual commit and sends the task to Review. When you commit that record with `virtual-commit.bat`, the task moves to Done by itself.
- **No setup.** It needs Python 3.9 or newer and nothing else: no packages, no build step, no internet.

## Quick start

1. **Open the board.** In Unity: **Tools ▸ Agent Task Board ▸ Open Board** (starts the server in the background if needed; **Stop Board Server** in the same menu stops it). Or double-click `task-board.bat` in the repository root, or run:

   ```
   python tools/taskboard/taskboard.py serve --open
   ```

   It opens http://127.0.0.1:8765. Keep the window open while you work.
2. **Add tasks.** Press **N** on the board, fill in the title and what "done" means, and save it to the Queue.
3. **Let an agent work the queue.** Tell an agent in the repository:

   > Work the task board queue: run `python tools/taskboard/taskboard.py guide`, then keep claiming tasks with `taskboard next --agent "Claude Code"` until it says there is nothing left.

   Watch the cards move as the agent claims, notes and finishes them.

To use `taskboard` from any folder, including the other repositories, install it once:

```
pip install -e tools/taskboard
```

After that, `taskboard next --agent "Kilo Code"` works from any folder. Without installing, call the script by its full path, for example `python F:\Projects\MyGame\tools\taskboard\taskboard.py ...`.

## The columns

| Column | Meaning | Who moves cards in |
| --- | --- | --- |
| **Inbox** (`proposed`) | Ideas, and tasks an agent suggested. Nothing here gets worked on. | Agents (`add`), you |
| **Queue** (`ready`) | Approved work. Agents take it from the top. | You (approve, drag) |
| **In progress** | Claimed by an agent, which holds a live lease. | `next` / `claim` |
| **Blocked** | The agent needs something from you. The card shows its question. | Agent (`block`), you |
| **Review** | The agent finished; you test it and commit the virtual commit. | Agent (`review`) |
| **Done** | Verified and committed. | You, or the virtual-commit sync |
| **Cancelled** | Dropped. Hidden unless you toggle **Cancelled** in the top bar. | You |

The top bar counts what is **running**, what is **queued**, and how many cards **need you** (Inbox + Blocked + Review).

### Queue order

Agents take the first Queue task they are allowed to start:

1. **Priority**: urgent, then high, normal, low.
2. **Your order**: drag cards up and down. If you drop a card next to cards of another priority, it takes on their priority.
3. **Age**: older tasks first.

A task is skipped (and its card shows why, under the hourglass) while:

- a task it **depends on** is not Done;
- a task **in progress, blocked or in review** in the same project touches overlapping files. Review counts because those edits are still uncommitted in the shared working tree;
- an exclusive **resource** it needs is at capacity (`unity-editor`, `unity-build` and `gpu-benchmark` allow 1 task at a time).

`next` only offers tasks of the agent's own repository, plus tasks with no project.

## Using the web board

- **Click a card** to open its details: description, acceptance criteria, files, activity, and buttons for the next step (Approve, Unblock, Send back, Approve as Done...).
- **Drag** cards between columns, and up and down within the Inbox and Queue.
- **Notes** you add in the detail panel are what agents read in `taskboard show`. Use them to answer a blocked task.
- **Keys:** `N` new task, `/` search, `E` edit the open task, `A` activity feed, `Esc` close.
- The green **Live** dot shows the board is receiving updates. It reconnects by itself.

## For agents (the protocol)

`taskboard guide` prints the full protocol. In short:

```
taskboard next   --agent "Claude Code"              # claim the top task of this repo
taskboard show   T-12                               # read it
taskboard note   T-12 "what I just did" --agent ... # milestones; renews the lease
taskboard review T-12 --commit 37 -s "summary" --agent ...   # 37 = virtual commit id
taskboard done   T-12 -s "summary" --agent ...      # nothing for the user to test
taskboard block  T-12 "question for the user" --agent ...
taskboard release T-12 "why" --agent ...            # give it back unfinished
taskboard add "Follow-up idea" -d "..." --agent ... # lands in the Inbox
```

`--agent` can be replaced by the `TASKBOARD_AGENT` environment variable. Add `--json` for machine-readable output. Exit codes: `0` ok, `1` error, `2` nothing to do.

Agents may only change a task they hold while it is in progress. They must follow the workflow; you can move any card anywhere.

### Rule text for a repository's AGENTS.md

The Agentic Tools package installs this rule into `AGENTS.md` when its `taskboard` module is enabled; for another repository, paste it into its `AGENTS.md`:

```markdown
## Task board

The user queues work on a shared local task board (Agent Task Board, `tools/taskboard`; `taskboard` once installed). When the user gives you a task id (T-12) or asks you to work the queue:
- read the protocol first: `taskboard guide`;
- claim before you start (`taskboard next --agent "<your tool name>"` or `taskboard claim T-12 ...`), add a `note` at each milestone, and finish with `taskboard review T-12 --commit <virtual commit id> -s "<summary>"`, or `block` with a precise question when you need the user;
- working the queue: repeat `next` until it exits with code 2; stop at a failure you cannot fix and block that task;
- ideas and follow-ups you notice go to the Inbox with `taskboard add`; never queue work the user did not ask for.
```

### A good prompt for a queue worker

> You are the queue worker for this repository. Run `taskboard guide`. Then loop: `taskboard next --agent "Claude Code"`; do the task fully (code, tests, docs, virtual commit); `taskboard review <id> --commit <vc id> -s "<what changed and how you verified it>"`. If anything needs my decision, `block` the task with the exact question and continue with the next one. Stop when `next` exits with code 2.

For long queues, a fresh session per task keeps the agent's context clean. Start the next session when one finishes.

## Command reference

| Command | What it does |
| --- | --- |
| `serve [--port N] [--open]` | Run the web board (127.0.0.1 only). |
| `add TITLE [options] [--inbox\|--queue]` | Create a task. Options: `-d` description, `--accept` (repeat), `-p` priority, `--project`, `--label`, `--touches`, `--needs`, `--depends`. |
| `list [-s STATUS] [--here] [--mine] [--flat]` | Tasks grouped by column. |
| `show ID` | One task with its activity. |
| `next [--dry-run] [--any-project]` | Claim the next task (exit 2 when there is none). |
| `claim ID [--force]` | Claim a specific task. |
| `note ID TEXT` / `heartbeat ID` | Progress note / renew the lease. |
| `review ID` / `done ID` | Finish (`-s` summary, `--commit` virtual commit id or sha, `--link`). |
| `block ID REASON` / `unblock ID [NOTE]` | Stuck / back to the Queue. |
| `release ID [REASON]` | Give an unfinished task back. |
| `cancel ID [REASON]` / `reopen ID [NOTE]` | Drop / bring back (also from the archive). |
| `move ID STATUS [NOTE]` | Any column (people only outside the workflow). |
| `edit ID [options]` | Change fields; list options replace the whole list. |
| `link ID --commit X --link URL` | Attach references. |
| `sync` | Expire stale leases and run plugin sync now. |
| `archive [--days N]` | Move old Done/Cancelled tasks to `archive/`. |
| `stats` | Counts, throughput and median lead time. |
| `info` | Board folder, detected project and agent. |
| `guide` | The agent protocol. |

Global options: `--home DIR` (board folder), `--agent NAME`, `--project NAME`, `--json`. Task ids can be written as `12`, `T-12` or `t12`.

## Settings

Create `~/.taskboard/config.json` with only the values you want to change:

```json
{
  "board_name": "Gigaduck Board",
  "user_name": "mhze",
  "lease": { "stale_minutes": 20, "expire_minutes": 120 },
  "require_review": false,
  "resources": { "unity-editor": 1, "unity-build": 1, "gpu-benchmark": 1 },
  "conflict_statuses": ["in_progress", "blocked", "review"],
  "archive_after_days": 14,
  "port": 8765,
  "plugins": ["taskboard.integrations.virtual_commits"]
}
```

`TASKBOARD_HOME` moves the whole board folder, for example to a synced drive. All settings are documented in `taskboard/config.py`.

## Where the data lives

```
~/.taskboard/
  tasks/00012.json     one readable JSON file per task
  archive/             finished tasks moved out by `archive`
  events.jsonl         every change, one line each (the live feed)
  counters.json        last task id and event number
  config.json          your settings (optional)
```

Back it up by copying the folder. Never edit a file while an agent is working on that task. If a crash leaves a `lock` file behind, it clears itself after 20 seconds.

## Development

```
cd tools/taskboard
timeout 300 python -m unittest discover -s tests -t tests      # 35 tests, ~2 s
python dev/seed_demo.py --home %TEMP%\tb-demo                 # demo data
python taskboard.py --home %TEMP%\tb-demo serve --open
```

How the code is organized, and how to add a column, a scheduling rule, a command, an API route or a plugin, is in [ARCHITECTURE.md](ARCHITECTURE.md).
