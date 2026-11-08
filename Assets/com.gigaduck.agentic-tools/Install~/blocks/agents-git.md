**Git ([`GIT.md`]({{RULES_DIR}}/GIT.md)).** Never commit, push or merge without an explicit user request (cloud agents too); a request authorizes only the Git actions it names. Agents share one working tree: without a request never change Git state (`add commit push stash reset restore clean mv rm merge rebase pull`, checking out a commit, detaching); read-only `status`, `diff`, `log`, `show` are fine, checkouts go in a scratch worktree. Track only your own changes. Never edit `version.json`.

**Finish every task with a virtual commit** (cloud without `pwsh`: GIT.md). List **every** file you created, modified or deleted (both paths of a rename), never another agent's:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/git/agent-commit.ps1 prepare `
  -Agent "<your tool name>" -Task short-task-slug -Bump patch|minor|none `
  -Title "Imperative title, under 72 chars" -Description "What changed and why, behaviour, how verified." `
  -Files path/a,path/b
```

`READY` = registered (note the id); `NOT READY` = nothing registered: fix and rerun. The same `-Agent` + `-Task` replaces your pending record; `-Force` only for a changed, git-ignored file. End your final message with `Virtual commit #<id> ready — <title>`, then ask whether to push it; leave it pending unless the user says yes (GIT.md step 6).
