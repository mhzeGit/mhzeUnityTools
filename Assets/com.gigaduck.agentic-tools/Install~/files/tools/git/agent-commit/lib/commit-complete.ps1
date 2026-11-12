# agent-commit/lib/commit-complete.ps1
# Completing a reviewed plan: compare-and-swap branch move, marking records
# applied, then index/version/file sync.
# Loaded by agent-commit.ps1.

function Complete-CommitPlan {
    # Move the branch to the planned commits (compare-and-swap: only if HEAD
    # and every record are unchanged since the review), mark the records as
    # committed, then refresh the index, version numbers and files of this
    # folder for the committed paths.
    param($Store, $Plan)
    $result = [pscustomobject]@{ Ok = $false; Error = $null; Notes = (New-StringList); Updated = (New-StringList); Kept = (New-StringList) }
    $done = @($Plan.Steps | Where-Object { $_.Commit -or $_.Empty })
    if ($done.Count -eq 0) { $result.Error = 'Nothing to commit: every selected record conflicts.'; return $result }

    Enter-StoreLock $Store
    try {
        foreach ($step in $done) {
            $fresh = Get-RecordById $Store $step.Record.id
            if (-not $fresh -or $fresh.status -ne 'pending' -or $fresh.revision -ne $step.Revision) {
                $result.Error = "Record #$($step.Record.id) was changed (re-prepared, edited or discarded) after this review, so nothing was committed. Review again."
                return $result
            }
        }
        $problem = Get-RepoProblem
        if ($problem) { $result.Error = $problem; return $result }
        if ($Plan.CommitCount -gt 0) {
            $r = Invoke-Git -Arguments @('update-ref', '-m', "agent-commit: $($Plan.CommitCount) record(s)", 'HEAD', $Plan.Final, $Plan.Head) -AllowFailure
            if ($r.ExitCode -ne 0) {
                $result.Error = "HEAD changed while this tool was running (did something else commit?). Nothing was committed. Review again.`n$($r.Error.Trim())"
                return $result
            }
        }
        $now = Get-IsoNow
        foreach ($step in $done) {
            try {
                $fresh = Get-RecordById $Store $step.Record.id
                $fresh.status = 'applied'
                $fresh.commit = [string]$step.Commit
                $fresh.appliedAt = $now
                $fresh.pushed = $false
                Save-Record $Store $fresh
                Remove-RecordPatch $Store $fresh
            } catch {
                $result.Notes.Add("Could not mark record #$($step.Record.id) as committed: $($_.Exception.Message)")
            }
        }
    } finally {
        Exit-StoreLock $Store
    }
    $result.Ok = $true

    $paths = New-StringList
    foreach ($step in $done) { foreach ($change in @($step.Tree.Changes)) { $paths.Add($change.Path) } }
    if ($paths.Count -gt 0) {
        $indexWarning = Sync-IndexPaths $paths.ToArray()
        if ($indexWarning) { $result.Notes.Add($indexWarning) }
        if ($Plan.EndVersion -and $Plan.EndVersion -ne $Plan.StartVersion) { [void](Sync-WorktreeVersions $Plan.EndVersion) }
        $files = Update-WorktreeFiles $Plan.Head $Plan.Final $paths.ToArray()
        foreach ($p in $files.Updated) { $result.Updated.Add($p) }
        foreach ($p in $files.Kept) { $result.Kept.Add($p) }
    }
    return $result
}
