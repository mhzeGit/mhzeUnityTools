# agent-commit/ui/plan-review.ps1
# The review screen of a commit plan: per-commit files, versions and
# warnings, and whether a push is blocked.
# Loaded by agent-commit.ps1.

function Get-StepWarnings($Store, $Step, [object[]]$AllPending, $SelectedIds) {
    # Plain-language warnings for one planned commit.
    $warnings = New-StringList
    $record = $Step.Record
    $drift = Get-RecordDrift $record
    foreach ($path in $drift.Paths) {
        $warnings.Add("$path changed after the agent prepared this record. The prepared version is committed and the newer edits stay uncommitted. To commit the file as it is now instead, go back and press S on this record.")
    }
    foreach ($overlap in @(Get-OverlappingRecords $record $AllPending)) {
        $other = $overlap.Record
        $paths = $overlap.Paths -join ', '
        $label = "#$($other.id) ($($other.agent): $($other.title))"
        $selected = $SelectedIds -contains $other.id
        if (Test-RecordContains $Store $record $other $overlap.Paths) {
            if ($selected) {
                $warnings.Add("$paths also holds the edits of $label, which is selected too. They land in whichever of the two commits comes first.")
            } else {
                $warnings.Add("$paths also holds the edits of $label, which is NOT selected: they were already in the file when this record was prepared, so they are committed here too.")
            }
        } elseif (-not $selected) {
            $warnings.Add("$label also changes $paths. Its edits are not part of this commit.")
        }
    }
    foreach ($w in $Step.Tree.Warnings) { $warnings.Add($w) }
    return , $warnings
}

function Write-Plan($Store, $Plan, $Remote, [object[]]$AllPending, [bool]$FetchOk) {
    $selectedIds = @($Plan.Steps | ForEach-Object { $_.Record.id })
    $target = '(no remote)'
    if ($Remote.Remote) { $target = "$($Remote.Remote)/$($Remote.MergeRef -replace '^refs/heads/', '')" }
    $skipped = @($Plan.Steps | Where-Object { $_.Tree.Conflict }).Count
    $header = "  Review: $($Plan.CommitCount) commit(s) on $($Plan.Branch) -> $target"
    if ($skipped -gt 0) { $header += "   ($skipped record(s) skipped)" }
    Write-Line $header 'Cyan'
    Write-Host ''

    $n = 0
    $total = @($Plan.Steps | Where-Object { -not $_.Tree.Conflict }).Count
    foreach ($step in $Plan.Steps) {
        $record = $step.Record
        if ($step.Tree.Conflict) {
            Write-Parts @('  skip  ', 'Red', "#$($record.id)  ", 'DarkGray', $record.title, 'Red')
            Write-Wrapped "It $($step.Tree.Conflict), so it stays pending. Usually an older record for the same work is selected too: discard the older one, or commit the others first, then press S on this record to re-capture it." 'Red' '        '
            Write-Host ''
            continue
        }
        $n++
        Write-Parts @("  $n/$total  ", 'Cyan', "#$($record.id)  ", 'DarkGray', $record.title, 'White')
        $versionText = 'no version change'
        if ($step.Tree.OldVersion -and $step.Tree.NewVersion -and $step.Tree.NewVersion -ne $step.Tree.OldVersion) {
            $versionText = "version $($step.Tree.OldVersion) -> $($step.Tree.NewVersion)"
        }
        $where = ''
        if (-not (Test-SamePath $record.worktree $script:RepoRoot)) { $where = " | from worktree $(Get-WorktreeLabel $record)" }
        Write-Line "        $($record.agent) | $($record.task) | bump $($record.bump) | $versionText$where" 'DarkGray'
        if ($step.Empty) {
            Write-Line '        Nothing left to commit (these changes are already in HEAD). The record will be marked as committed.' 'Yellow'
        }
        foreach ($change in @($step.Tree.Changes)) {
            $color = 'Gray'
            if ($change.Status -eq 'A') { $color = 'Green' } elseif ($change.Status -eq 'D') { $color = 'Red' }
            $suffix = ''
            if ($change.FromBump) { $suffix = '  (version number)' }
            Write-Parts @("        $($change.Status) ", $color, $change.Path, 'Gray', $suffix, 'DarkGray')
        }
        foreach ($w in (Get-StepWarnings $Store $step $AllPending $selectedIds)) { Write-Wrapped "! $w" 'Yellow' '        ' }
        Write-Host ''
    }

    if ($Remote.Ahead -gt 0 -and $Remote.TrackingExists) {
        Write-Line "  Also pushed: $($Remote.Ahead) earlier local commit(s) that were never pushed:" 'Yellow'
        $log = Invoke-Git -Arguments @('log', '--format=%h %s', "$($Remote.TrackingRef)..$($Plan.Head)") -AllowFailure
        foreach ($line in @(Split-Records $log.Output ([char]10) | Select-Object -First 10)) { Write-Line "    $line" 'DarkGray' }
        Write-Host ''
    }
    if (-not $Remote.Remote) {
        Write-Line '  No git remote is configured, so the commits can only be made locally.' 'Yellow'
    } elseif (-not $FetchOk) {
        Write-Line "  Could not fetch from $($Remote.Remote); the push may fail." 'Yellow'
    }
    if ($Remote.Behind -gt 0) {
        Write-Wrapped "$target has $($Remote.Behind) commit(s) you do not have, so a push would be rejected. Pull them first (git pull), then run this again. You can still commit locally now." 'Red' '  '
    }
}

function Get-PushBlocker($Remote) {
    if (-not $Remote.Remote) { return 'no remote' }
    if ($Remote.Behind -gt 0) { return 'behind' }
    return $null
}
