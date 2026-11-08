# Git rules: virtual commits and the shared worktree

Read before you finish a task or touch Git in any way. Entry point: [AGENTS.md]({{RULES_TO_ROOT}}/AGENTS.md).

## Cloud sessions: virtual commit, then push and merge only when requested

A cloud session (claude.ai/code or any container started from a task) works in its **own private clone**. Like local agents, it never
commits, pushes or merges automatically. An explicit user request authorizes the requested actions; absent that request, prepare the
virtual commit and leave it pending. When the user explicitly requests commit/push/merge (there is no `virtual-commit.bat` in a cloud
container), carry out the requested actions in order:

1. **Make the virtual commit.** Work on the branch the task assigns (create it if missing). Register and finalize the record exactly as
   the local steps below describe (`agent-commit.ps1 prepare`, then `finalize -Ids <id>`; no *Offer to finalize* question, the yes is
   already given). Where `pwsh` is not installed, commit by hand instead: stage only the files you changed, write the title and the
   description (with a `Bump:` and a `Tests:` line) as the message, and never edit `version.json` yourself (the bump then stays in the message).
2. **Push fully.** `git push -u origin <branch>`; retry only network failures, 2 s, 4 s, 8 s, 16 s. Nothing stays local.
3. **Merge into `main` fully.** `git fetch origin main`; if `main` moved, merge it into your branch, resolve conflicts, rebuild what you
   can and push the branch again. Then fast-forward `main` with `git push origin <branch>:main`. If `main` is protected or the push is
   rejected, open a pull request for the branch and merge it with the GitHub tools. Verify with `git log origin/main` that the commit is there.
4. **Stop and report instead** when a conflict cannot be resolved with certainty, checks fail, or the merge needs a force-push. Never
   rewrite history, never force-push `main`, never touch another branch.
5. Keep the other rules: list only your own changes, run the finish checklist in `AGENTS.md`, and in the final message say the commit
   hash, that it was pushed and merged into `main`, what was tested, what the user tests manually and what is **Not run** (a cloud session
   has no Unity, see `{{RULES_DIR}}/TESTING.md`), because nothing else carries that to the user. The `Virtual commit #<id> ready` line and the
   *commit and push now?* question do not apply: the work is already pushed and merged.

## Git: virtual commits (local agents)

Several agents may share this working tree (and linked worktrees). **Never commit, stage, push, merge or otherwise change Git state automatically.** When the user explicitly asks you to perform one of these actions, that request is authorization to do the requested action; follow the steps below directly without asking for the same authorization again. A request to do one action does not authorize unrelated Git actions. When the user has not explicitly asked for Git operations, finish by registering a *virtual commit* — a record with a binary-safe snapshot of exactly the files you list, plus the commit title, description and version bump — using `tools/git/agent-commit.ps1 prepare`. Records live in `<git-common-dir>/agent-commits`, shared by every worktree. The user can later run `virtual-commit.bat` in the repo root to commit and push the selected records. No AI is involved at push time, so the record must be complete and accurate.

1. **Without an explicit user request, do not run Git commands that change the index, HEAD or files:** this includes `git add`, `git commit`, `git push`, `git stash`, `git reset`, `git restore`, `git checkout -- <file>`, `git clean`, `git mv`, `git rm`, `git merge`, `git rebase`, and `git pull`. Read-only commands (`git status --porcelain`, `git diff`, `git log`, `git show`) are fine. If the user explicitly requests a Git operation, carry it out directly and limit it to the requested scope. For a commit or push request, use the virtual-commit tool below; never use raw `git add/commit/push`.

2. **Keep track of every file you create, modify or delete** while you work. Other agents' uncommitted edits are in the same tree, so `git status` is *not* a list of your changes. Never add a file to `-Files` just because it shows up in `git status`.

3. **When the task is complete** (code, tests and docs all done), register the record:

   ```powershell
   pwsh -NoProfile -ExecutionPolicy Bypass -File tools/git/agent-commit.ps1 prepare `
     -Agent "<your tool name>" `
     -Task short-task-slug `
     -Bump patch `
     -Title "Imperative commit title, under 72 characters" `
     -Description "What changed and why, user-visible behavior, notable details, how it was verified." `
     -Files path/to/file1,path/to/file2
   ```

   - `-Agent` your tool's name (Claude Code, Kilo Code, Antigravity, ...). `-Task` a short slug naming this task; running the same command again with the same `-Agent` and `-Task` replaces your pending record instead of adding a second one.
   - `-Title` the commit title: imperative mood, specific, at most 72 characters, no trailing period, no `feat:`-style prefix. Match the style of `git log --oneline`.
   - `-Description` plain text: what changed and why, user-visible behavior, notable implementation details, and a `Tests:` line (what ran in your player, what the user tests manually, what is Not run).
   - `-Bump` `patch` (bug fix / small improvement), `minor` (new feature or notable change) or `none` (docs, formatting, a refactor with no behavior change). Never edit `version.json` yourself; the tool owns the version number and writes the bump into the commit and into your working tree.
   - `-Files` **every** file you created, modified or deleted in this task, comma-separated, repository-relative (or relative to your working folder). Include deleted files, and both the old and the new path of a renamed file. Never list another agent's files, folders or glob patterns; the tool rejects those.
   - `-VerificationReport` optionally accepts one or more AutoPlaytest `result.json` paths. The record retains each run ID, outcome and coverage in the description.
   - `-Force` only for a file you really changed that `.gitignore` ignores; `-Replace <id>` updates a specific pending record.

4. **Read the output.** `READY` means the record is registered; note its id and the warnings. `NOT READY` means nothing was registered: fix every problem it reports and run the same command again.

5. **End your final message** with one line: `Virtual commit #<id> ready — <title>`.

6. **Ask about pushing every virtual commit.** Immediately after each `READY`, ask the user whether they want that record pushed. If they say yes, finalize it with `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/git/agent-commit.ps1 finalize -Ids <id>`. It commits only that record's files with its version bump, then pushes. Report the resulting commit hash or tool error. If they say no, or have not yet answered, leave it pending; never commit or push automatically. An explicit request to commit or push already authorizes that requested action, so do not ask for duplicate authorization. Never finalize records you did not register. For explicit merge requests, perform only the requested merge, respecting the shared-worktree protections below; stop and report if it cannot be done safely.

7. **Follow-ups.** If you edit a listed file again (before or after the user tested), run the same command again; it replaces your pending record with a fresh snapshot. After the user has committed a record, registering your further work creates a new record (use a new `-Task` slug for a different task).

## One-click push (user, T3 Code)

The user's T3 Code project has a project script, **Virtual commit + push**, that runs
`.\virtual-commit.bat finalize -All -SkipConflicts`: it commits and pushes every pending record (one commit per
record, capture order) and leaves conflicting records pending with the reason. It costs no tokens; agents never run
it themselves. `finalize -All` exists only for `finalize` (`discard` still needs explicit `-Ids`). After a `READY`
the question in step 6 can be answered by pressing that button instead of replying in chat.

## Git: keep the shared worktree on its branch

The main folder is **one shared working tree** that Unity, the user and every agent read at the same
time. Never run a command that detaches, rewinds or rewrites it:

- `git checkout <commit>`, `git checkout --detach`, `git switch --detach` (any history checkout)
- `git reset`, `git rebase`, `git merge`, `git pull`, `git stash`

A plain `git checkout <old-commit>` here leaves HEAD **detached** and silently reverts *every Unity
scene and prefab to that old version* — this is exactly what wiped the main menu and appeared as
"scenes reverting out of nowhere, many commits back". The virtual-commit tool commits onto whatever
HEAD points at, so a detached HEAD also makes records land in the wrong place.

Inspect history **read-only** instead of checking it out:

- `git log`, `git show <sha>`, `git diff <sha> -- <path>`, `git show <sha>:<path>`
- for a real checkout, create a scratch worktree and delete it when done:
  `git worktree add ..\scratch <sha>` then `git worktree remove ..\scratch --force`

Guard rails now enforce this:

- The committed hook `tools/git/hooks/post-checkout` is installed into `.git/hooks/post-checkout`
  (automatically, by `tools/git/agent-commit.ps1`) and switches the main folder straight back to its
  branch when a detached, clean checkout lands there. A rebase/merge/bisect is left alone. To check
  out a commit here anyway, set `$env:KILO_ALLOW_DETACHED='1'` for that single command.
- Every `agent-commit.ps1` run (and `virtual-commit.bat`) also repairs a detached, clean checkout
  before doing anything.
- After an accident, or if the hook is missing: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/git/restore-worktree.ps1`.
