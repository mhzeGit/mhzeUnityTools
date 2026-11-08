"""The agent protocol, printed by `taskboard guide`.

Keep this text short and imperative: agents read it at the start of a task.
The longer human documentation is README.md next to the package.
"""

AGENT_GUIDE = """\
AGENT TASK BOARD - protocol for AI agents
=========================================
The user and other agents share one task board. Keep your task's status true
at all times; the user watches it live. Always pass --agent "<your tool name>"
(or set TASKBOARD_AGENT). Commands run from the repository you work in, which
sets the project. Add --json to any command for machine-readable output.

Working a task
  taskboard next --agent "Claude Code"        claim the top queued task of this repo
  taskboard claim T-12 --agent "Claude Code"  claim a task the user named
  taskboard show T-12                         read it: description, acceptance, touches
  taskboard note T-12 "what you just did"     at each milestone (also renews your lease)
  taskboard review T-12 --commit 37 -s "..."  finished; 37 = your virtual commit id
  taskboard done T-12 -s "..."                finished and nothing for the user to test
  taskboard block T-12 "question for user"    stuck: say exactly what you need, then stop
  taskboard release T-12 "why"                giving it back to the queue unfinished

Rules
  1. One task at a time. Claim before you start; never work a task you did not claim.
  2. Meet every acceptance criterion, or say in your summary which one you did not.
  3. Stay inside the task's `touches` paths; if you must edit more, add a note.
  4. Note progress at least every 15 minutes of work; a silent lease expires
     after 2 hours and the task returns to the queue.
  5. Blocked = you need the user. Do not guess an answer to block on; ask it.
  6. Ideas or follow-up work you notice: `taskboard add "title" -d "..."`;
     they land in the Inbox for the user to approve. Use --queue only when the
     user asked you to queue work.
  7. Working the queue: repeat `taskboard next` until it exits with code 2
     (nothing claimable). Stop at the first failure you cannot fix and block it.

Exit codes: 0 ok, 1 error (read the message), 2 nothing to do.
"""
