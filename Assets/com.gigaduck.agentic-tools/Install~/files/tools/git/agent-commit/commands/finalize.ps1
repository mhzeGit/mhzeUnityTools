# agent-commit/commands/finalize.ps1
# The finalize command: review, commit and push records non-interactively.
# -Ids picks records; -All (with no -Ids) takes every pending record, which is
# what the one-click "Virtual commit and push" button in T3 Code runs.
# Loaded by agent-commit.ps1.

function Invoke-Finalize($Store) {
    if ($All -and @(Split-ListArgument $Ids).Count -eq 0) {
        # Same order as Resolve-RecordIds: capture order, then id.
        $records = @(Get-PendingRecords $Store | Sort-Object -Property @{ Expression = { $_.updatedAt } }, @{ Expression = { $_.id } })
        if ($records.Count -eq 0) { Write-Out 'No pending records: nothing to commit or push.'; return }
    } else {
        $records = @(Resolve-RecordIds $Store $Ids)
    }
    $problem = Get-RepoProblem
    if ($problem) { Write-Out $problem 'Red'; $script:ExitCode = 1; return }
    $remote = Get-RemoteInfo
    $fetchOk = $true
    if (-not $NoPush -and $remote.Remote) { $fetchOk = Invoke-Fetch $remote; $remote = Get-RemoteInfo }
    $plan = New-CommitPlan -Store $Store -Records $records
    Write-Plan $Store $plan $remote @(Get-PendingRecords $Store) $fetchOk

    $conflicts = @($plan.Steps | Where-Object { $_.Tree.Conflict })
    if ($conflicts.Count -gt 0 -and -not $SkipConflicts) {
        Write-Out '  Nothing was committed: some records conflict. Add -SkipConflicts to commit the others.' 'Red'
        $script:ExitCode = 1
        return
    }
    if (@($plan.Steps | Where-Object { $_.Commit -or $_.Empty }).Count -eq 0) {
        Write-Out '  Nothing to commit.' 'Red'
        $script:ExitCode = 1
        return
    }
    if (-not $NoPush -and (Get-PushBlocker $remote)) {
        Write-Out '  Nothing was committed: the push cannot succeed. Use -NoPush to commit locally only.' 'Red'
        $script:ExitCode = 1
        return
    }
    if ($DryRun) {
        Write-Out '  Dry run: nothing was committed.' 'Cyan'
        return
    }
    if (-not (Invoke-PlanExecution $Store $plan $remote (-not $NoPush))) { $script:ExitCode = 1 }
}
