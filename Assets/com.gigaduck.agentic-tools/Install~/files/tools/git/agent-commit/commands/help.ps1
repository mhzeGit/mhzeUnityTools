# agent-commit/commands/help.ps1
# The help text.
# Loaded by agent-commit.ps1.

function Show-Help {
    Write-Out @'
Agent commit workflow - virtual commits for AI agents sharing this repository

Agents (never commit, stage or push):
  prepare  -Agent "<tool name>"   Claude Code, Kilo Code, Antigravity, ...
           -Task <slug>           short task id; the same -Task updates your pending record
           -Bump patch|minor|none bug fix / new feature / docs-refactor only
           -Title "<line>"        commit title, imperative, under 72 characters
           -Description "<text>"  what changed, why, how it was verified
           -DescriptionFile <f>   ...or read the description from a file
           -Files a.cs,b.cs       exactly the files you created, edited or deleted
           -Force                 include files that .gitignore would skip
           -Replace <id>          update a specific pending record

Developer (virtual-commit.bat):
  (no command)                    interactive picker: review, edit, commit, push
  list [-All]                     print pending (or all) records
  finalize -Ids 3,5               commit the records and push
  finalize -All                   ...every pending record (the T3 Code button)
           -DryRun -NoPush -SkipConflicts
  discard -Ids 4                  drop pending records (files are not changed)

Records live in <git-common-dir>/agent-commits and are shared by all worktrees.
The tool-owned version number lives in version.json in the repository root.
'@
}
