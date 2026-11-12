# agent-commit/ui/record-actions.ps1
# Re-capturing (S) and discarding (X) a record, and the unlisted files
# screen (U).
# Loaded by agent-commit.ps1.

function Invoke-Recapture($State, $Record) {
    Clear-Ui
    Write-Line "  Re-capture #$($Record.id) '$($Record.title)'?" 'Yellow'
    Write-Host ''
    Write-Wrapped "The record's files are captured again from $(Get-WorktreeLabel $Record) exactly as they are now, including your own test edits and anything another agent has meanwhile written into these same files. Use it after you fixed something while testing." 'Gray' '  '
    Write-Host ''
    Write-Line '  Y re-capture    N/Esc back' 'Cyan'
    if ((Read-Choice @('y', 'n', 'esc', 'q')) -ne 'y') { return '' }
    if (-not [System.IO.Directory]::Exists($Record.worktree)) { return "The folder of #$($Record.id) no longer exists, so it cannot be re-captured." }
    $snapshot = New-Snapshot -Root $Record.worktree -RawPaths @(@($Record.files) | ForEach-Object { $_.path }) -IncludeIgnored
    try {
        if ($snapshot.Problems.Count -gt 0) { return "Not re-captured: $($snapshot.Problems -join ' ')" }
        Enter-StoreLock $State.Store
        try {
            $fresh = Get-RecordById $State.Store $Record.id
            if (-not $fresh -or $fresh.status -ne 'pending' -or $fresh.revision -ne $Record.revision) { return "Record #$($Record.id) was changed meanwhile. Reload and try again." }
            $oldPatch = Set-RecordSnapshot $State.Store $fresh $snapshot
            $fresh.revision++
            Save-Record $State.Store $fresh
            if ($oldPatch -and $oldPatch -ne $fresh.patch) { Remove-FileQuietly ([System.IO.Path]::Combine($State.Store.Patches, $oldPatch)) }
        } finally {
            Exit-StoreLock $State.Store
        }
    } finally {
        Remove-FileQuietly $snapshot.PatchPath
    }
    return "Re-captured #$($Record.id) from the current files."
}

function Invoke-Discard($State, $Record) {
    Clear-Ui
    Write-Line "  Discard record #$($Record.id) '$($Record.title)'?" 'Yellow'
    Write-Line '  Only the record is dropped from the list. The files themselves are not changed.' 'DarkGray'
    Write-Host ''
    Write-Line '  Y discard    N/Esc back' 'Cyan'
    if ((Read-Choice @('y', 'n', 'esc', 'q')) -ne 'y') { return '' }
    $problem = Update-RecordFields -Store $State.Store -Id $Record.id -ExpectedRevision 0 -Change {
        param($r)
        $r.status = 'discarded'
        $r.discardedAt = Get-IsoNow
    }
    if ($problem) { return $problem }
    Remove-RecordPatch $State.Store $Record
    [void]$State.Selected.Remove($Record.id)
    return "Discarded #$($Record.id)."
}

function Show-Unclaimed($State) {
    Clear-Ui
    Write-Line '  Changed files in this folder that no record lists' 'Cyan'
    Write-Line '  (agents still working, or a file an agent forgot to list)' 'DarkGray'
    Write-Host ''
    foreach ($c in $State.Unclaimed) { Write-Parts @("    $($c.Code) ", 'Yellow', $c.Path, 'Gray') }
    if ($State.Unclaimed.Count -eq 0) { Write-Line '    (none)' 'DarkGray' }
    Wait-AnyKey
}
