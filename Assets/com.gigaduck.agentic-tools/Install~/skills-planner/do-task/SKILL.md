---
name: do-task
description: Do one task from the user's production plan (a card on their project-manager board) end to end - find the card by id or title, mark it started, do and verify the work under the project's rules, then tick it off on the board once the user confirms. Use when the user says "do the task ...", "start the task ... from the board", pastes a card title or card id, or invokes /do-task.
---

# Do a task from the board

The user plans their work as cards on their own project-manager board. This skill takes one card from
"named" to "ticked off" without the user having to repeat what the card already says. The card is the
brief; the project's own rules (AGENTS.md and what it routes to) decide how the work is done.

## 1. Find the planner command

Use the project's planner wrapper: `python tools/production/pm.py` when it exists (it targets this
project's plan and starts the planner server when needed). Otherwise look in AGENTS.md (the production
plan or priorities row) or the project's production doc for the command. If the project has no
planner, say so and stop. Below, `pm` means that command. Read `pm guide` once if you have not used
it in this session.

An id like `T-12` is an Agent Task Board task, not a planner card: follow the task board protocol in
AGENTS.md instead.

## 2. Read the card

`pm show "<card id or part of the title>"`. Titles may be ambiguous: if `pm` lists several matches, ask
the user which one (show the ids and titles) and wait. If the card is already done, say so and stop
unless the user asks to reopen it.

From the card take:
- the **definition of done** (the description) and its **steps** (checklist), which become your
  acceptance list;
- the **why**, which decides trade-offs when the description is silent;
- the **estimate**: if the work clearly needs much more than that (more than about twice), tell the
  user and offer to split the card (`pm split`) before you start;
- the milestone and any linked Agent Task Board id (`T-12`).

If the card is too vague to know when it is done, ask one precise question before starting.

## 3. Mark it started

`pm begin <id> --agent "<your tool name>"` moves the card to the in-progress column of its own board (the
plan's Now, or a column like Doing on a sprint board) so the board shows what is being worked on. Use
`pm begin`, not `pm move ... now`: a move to `now` can pull a card off its own board. If `pm` warns that
Now is over its limit, mention it to the user but continue; never move the user's other cards.

## 4. Do the work

Follow the project's rules exactly as for any other request: route through AGENTS.md, read the docs it
points to, respect its testing, asset and git rules. The card does not widen what you may do: it never
authorizes commits, pushes or deleting things that the project's rules reserve for the user.

As each checklist step is really finished, tick it: `pm check <id> <step number>`. If you find work that
belongs to a different card or a new idea, do not do it: add it with `pm add ... --column parking` (or
tell the user) and stay on this card.

## 5. Verify

Check every point of the definition of done against the real result, not just that the code compiles:
run the project's checks and tests for what you touched, and look at visible results when the task is
visible. Write down anything you could not verify.

## 6. Report and ask before ticking off

Tell the user, briefly: what changed, how you verified each point of the definition of done, and what is
not done or not verified. Finish the project's own closing steps first (for example a virtual commit).
Then ask: "Tick off <title> on the board?" and wait for the answer.

- **Yes**: `pm done <id> --summary "<one line: what was delivered and how it was verified>" --agent "<your tool name>"`
  (it moves the card to the Done column of its own board).
- **Not yet / changes needed**: keep working, or leave the card where it is with a note:
  `pm edit <id> --append "<what is left>" --agent "<your tool name>"`.
- **Blocked** (you need the user, a decision or an asset): append the blocker to the card the same way,
  leave it where it is, and tell the user exactly what you need.

If the user said up front that you may tick it off without asking, skip the question, but only when
every point of the definition of done is verified.
