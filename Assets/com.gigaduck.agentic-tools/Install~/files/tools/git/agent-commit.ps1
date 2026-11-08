<#
.SYNOPSIS
    Agent commit workflow: AI agents register their finished work as virtual
    commits; the developer tests it and commits + pushes the chosen records.

.DESCRIPTION
    Agents (never commit or push themselves):
      agent-commit.ps1 prepare -Agent <name> -Task <slug> -Bump patch|minor|none
                               -Title "<line>" -Description "<text>" | -DescriptionFile <file>
                               -Files <file>,<file>,... [-VerificationReport <result.json>] [-Force] [-Replace <id>]

    Developer (virtual-commit.bat runs the picker):
      agent-commit.ps1                      interactive picker (menu)
      agent-commit.ps1 list [-All]          print records
      agent-commit.ps1 finalize -Ids 3,5 [-DryRun] [-NoPush] [-SkipConflicts]
      agent-commit.ps1 finalize -All [-DryRun] [-NoPush] [-SkipConflicts]   every pending record
      agent-commit.ps1 discard -Ids 4

    Engine: agent-commit/lib/. Picker: agent-commit/ui/. Commands: agent-commit/commands/.
    The tool-owned version file is version.json in the repository root.
    See AGENTS.md. Exit codes: 0 ok / READY, 1 not ready or refused, 2 error.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)][string]$Command = 'menu',
    [string]$Agent,
    [string]$Task,
    [string]$Title,
    [string]$Description,
    [string]$DescriptionFile,
    [string[]]$VerificationReport = @(),
    [string]$Bump,
    [string[]]$Files = @(),
    [int]$Replace = 0,
    [switch]$Force,
    [string[]]$Ids = @(),
    [switch]$NoPush,
    [switch]$DryRun,
    [switch]$SkipConflicts,
    [switch]$All,
    [switch]$PauseOnExit
)

# ---------------------------------------------------------------------------
# parts
# ---------------------------------------------------------------------------
# The tool lives in agent-commit/, one concern per file:
#   lib/       the engine. Agents register finished work as a *record* (a
#              virtual commit): an exact, binary-safe patch of only the files
#              they list, captured on a temporary index, plus the commit title,
#              description and version bump. Records live in
#              <git-common-dir>/agent-commits, so agents in linked worktrees and
#              in the main folder share one list. The developer later turns the
#              selected records into real commits (one per record, chained on
#              HEAD with git plumbing) and pushes. The working tree, the shared
#              .git/index and other agents' uncommitted files are never touched
#              while a record is captured or a commit is assembled.
#   ui/        the developer's console picker (virtual-commit.bat): lists
#              the records, lets you inspect, fix and select them, then commits
#              each selected record as its own commit and pushes. No AI is
#              involved.
#   commands/  the commands of this script.
# The parts are read and compiled as one script block and dot-sourced into this
# script's scope, in the order below. (Dot-sourcing each file separately costs
# several ms per file in script policy checks, on every run of the tool.)
# Must run on Windows PowerShell 5.1 and PowerShell 7+.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# This script's own folder (tools/git). The parts are compiled into one
# scriptblock and dot-sourced into this scope, so they see this script-scope
# variable (used by the worktree guard to find the committed hook template).
$script:ToolRoot = $PSScriptRoot

$agentCommitParts = @(
    'lib/state',            # script-scope state and constants
    'lib/helpers',          # path sets/maps, temp files, string and path helpers
    'lib/time',             # ISO timestamps and age labels
    'lib/git-process',      # Invoke-Git, Invoke-GitInteractive, argument quoting/chunking
    'lib/repo',             # repository discovery, HEAD, current branch
    'lib/git-objects',      # trees, index entries, working-tree blob ids, ignored paths
    'lib/blob-text',        # byte-exact UTF-8 blob text
    'lib/version',          # version.json: the tool-owned version number
    'lib/store-lock',       # record store paths and lock
    'lib/record-model',     # record JSON normalization
    'lib/record-files',     # read/list/save record files
    'lib/record-register',  # Update-RecordFields, Register-Record
    'lib/snapshot-paths',   # -Files path normalization
    'lib/snapshot',         # New-Snapshot (prepare capture)
    'lib/record-relations', # drift, overlapping records, contained edits
    'lib/record-tree',      # New-RecordTree (patch apply + version bump)
    'lib/commit-plan',      # commit messages, New-CommitPlan
    'lib/index-sync',       # shared .git/index refresh after committing
    'lib/worktree-sync',    # version/file sync of this folder after committing
    'lib/commit-complete',  # Complete-CommitPlan
    'lib/remote',           # repo problems, remote info, fetch/push
    'lib/working-tree',     # changed/unclaimed files, worktree labels
    'lib/worktree-guard',   # keep the main worktree on its branch (hook install + repair)
    'ui/console',           # picker: console output and key input primitives
    'ui/record-text',       # picker: record/file statistics text
    'ui/plan-review',       # picker: plan review screen and push blockers
    'ui/plan-execution',    # picker: commit/push a reviewed plan
    'ui/picker-state',      # picker: state and record summaries
    'ui/main-screen',       # picker: main list screen
    'ui/record-details',    # picker: details (V) and diff (D)
    'ui/record-edit',       # picker: edit message (E), cycle bump (B)
    'ui/record-actions',    # picker: re-capture (S), discard (X), unlisted files (U)
    'ui/review',            # picker: review & commit (Enter), push (P)
    'ui/main-loop',         # picker: key dispatch and loop
    'commands/common',      # Write-Out, list arguments, -Ids
    'commands/prepare',     # prepare (agents)
    'commands/list',        # list
    'commands/finalize',    # finalize
    'commands/discard',     # discard
    'commands/help'         # help text
)
$agentCommitSource = New-Object System.Text.StringBuilder
foreach ($agentCommitPart in $agentCommitParts) {
    [void]$agentCommitSource.Append([System.IO.File]::ReadAllText([System.IO.Path]::Combine($PSScriptRoot, 'agent-commit', "$agentCommitPart.ps1"))).Append("`n")
}
try {
    $agentCommitScript = [scriptblock]::Create($agentCommitSource.ToString())
} catch [System.Management.Automation.ParseException] {
    # Name the part file that has the syntax error.
    foreach ($agentCommitPart in $agentCommitParts) {
        $agentCommitTokens = $null; $agentCommitErrors = $null
        [void][System.Management.Automation.Language.Parser]::ParseFile([System.IO.Path]::Combine($PSScriptRoot, 'agent-commit', "$agentCommitPart.ps1"), [ref]$agentCommitTokens, [ref]$agentCommitErrors)
        if ($agentCommitErrors) { throw "Syntax error in agent-commit/$agentCommitPart.ps1 line $($agentCommitErrors[0].Extent.StartLineNumber): $($agentCommitErrors[0].Message)" }
    }
    throw
}
. $agentCommitScript

$script:ExitCode = 0

# ---------------------------------------------------------------------------
# entry point
# ---------------------------------------------------------------------------

$currentDirectory = (Get-Location).ProviderPath
$pause = $false
$savedEncoding = $null
try {
    try { $savedEncoding = [Console]::OutputEncoding; [Console]::OutputEncoding = $script:Utf8NoBom } catch { }
    $commandName = $Command.ToLowerInvariant()
    if (@('help', '-h', '--help', '/?') -contains $commandName) {
        Show-Help
    } else {
        if (-not (Initialize-AgentCommitRepo $currentDirectory)) {
            if (-not (Initialize-AgentCommitRepo $PSScriptRoot)) { throw "Not inside a git repository: $currentDirectory" }
            $currentDirectory = $script:RepoRoot
        }
        $store = Get-StorePaths
        # Refuse to be fooled by a shared folder that a history checkout left
        # detached: repair it first so records and the picker always see the
        # real branch. Also (re)installs the post-checkout guard hook.
        Invoke-WorktreeGuard
        switch ($commandName) {
            'prepare' { Invoke-Prepare $store $currentDirectory }
            'list' { Invoke-List $store }
            'finalize' { Invoke-Finalize $store }
            'discard' { Invoke-DiscardCommand $store }
            'menu' { Set-CursorVisible $false; Start-Interactive $store }
            default { Show-Help; throw "Unknown command '$Command'." }
        }
    }
} catch {
    Write-Host ''
    Write-Host "ERROR  $($_.Exception.Message)" -ForegroundColor Red
    if ($env:AGENT_COMMIT_DEBUG) { Write-Host $_.ScriptStackTrace }
    $script:ExitCode = 2
    $pause = $PauseOnExit -and $script:InputIsConsole
} finally {
    Set-CursorVisible $true
    if ($savedEncoding) { try { [Console]::OutputEncoding = $savedEncoding } catch { } }
}
if ($pause) { Wait-AnyKey 'Press any key to close...' }
exit $script:ExitCode
