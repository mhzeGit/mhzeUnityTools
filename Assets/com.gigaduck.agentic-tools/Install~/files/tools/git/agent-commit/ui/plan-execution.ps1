# agent-commit/ui/plan-execution.ps1
# Committing (and pushing) a reviewed plan and printing the outcome.
# Loaded by agent-commit.ps1.

function Invoke-PlanExecution($Store, $Plan, $Remote, [bool]$Push) {
    # Commit (and push) a reviewed plan. Returns $true when all went well.
    Write-Host ''
    Write-Line '  Committing...' 'Cyan'
    $result = Complete-CommitPlan -Store $Store -Plan $Plan
    if (-not $result.Ok) {
        Write-Wrapped $result.Error 'Red' '  '
        return $false
    }
    foreach ($step in $Plan.Steps) {
        $record = $step.Record
        if ($step.Commit) {
            Write-Parts @('    ', '', $step.Commit.Substring(0, 7), 'Green', "  #$($record.id)  $($record.title)", 'Gray')
        } elseif ($step.Empty) {
            Write-Parts @('    done     ', 'DarkGray', "#$($record.id)  $($record.title)", 'DarkGray', '  (nothing left to commit)', 'DarkGray')
        } else {
            Write-Parts @('    skipped  ', 'Yellow', "#$($record.id)  $($record.title)", 'Gray', '  (conflict, still pending)', 'Yellow')
        }
    }
    if ($result.Updated.Count -gt 0) {
        Write-Wrapped ('Updated in this folder to match the new commits: ' + ($result.Updated -join ', ')) 'DarkGray' '  '
    }
    foreach ($p in $result.Kept) {
        Write-Wrapped "! $p has newer uncommitted edits in this folder; they were kept and stay uncommitted." 'Yellow' '  '
    }
    foreach ($note in $result.Notes) { Write-Wrapped "! $note" 'Yellow' '  ' }
    if ($Plan.EndVersion -and $Plan.EndVersion -ne $Plan.StartVersion) {
        Write-Line "  Version is now $($Plan.EndVersion)." 'Gray'
    }
    if (-not $Push) {
        Write-Line '  Committed locally (not pushed).' 'Green'
        return $true
    }
    if ($Plan.CommitCount -eq 0 -and $Remote.Ahead -eq 0) { return $true }
    if (Invoke-Push $Remote) {
        Update-PushedFlags $Store
        Write-Line '  Pushed.' 'Green'
        return $true
    }
    Write-Wrapped 'The push failed. Your commits are saved locally; fix the problem shown above, then press P in this tool (or run git push).' 'Red' '  '
    return $false
}
