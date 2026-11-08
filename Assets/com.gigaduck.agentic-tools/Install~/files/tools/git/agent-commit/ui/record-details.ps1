# agent-commit/ui/record-details.ps1
# The details screen (V) and the diff view (D) of one record.
# Loaded by agent-commit.ps1.

function Show-Details($State, $Record) {
    Clear-Ui
    Write-Parts @("  Record #$($Record.id)", 'Cyan', "   $($Record.agent) / $($Record.task)", 'DarkGray')
    Write-Host ''
    $prepared = ''
    $time = ConvertFrom-IsoTime $Record.updatedAt
    if ($time) { $prepared = $time.ToLocalTime().ToString('yyyy-MM-dd HH:mm') + " ($(Format-Age $Record.updatedAt))" }
    $base = $Record.baseSha
    if ($base.Length -gt 7) { $base = $base.Substring(0, 7) }
    Write-Line "  Agent     $($Record.agent)"
    Write-Line "  Task      $($Record.task)"
    Write-Line "  Bump      $($Record.bump)"
    Write-Line "  Prepared  $prepared   (revision $($Record.revision))"
    Write-Line "  Folder    $(Get-WorktreeLabel $Record)   (branch $($Record.baseBranch), base $base)"
    Write-Host ''
    Write-Line '  Title' 'Cyan'
    Write-Wrapped $Record.title 'White' '    '
    Write-Host ''
    Write-Line '  Description' 'Cyan'
    if ($Record.description) { Write-Wrapped $Record.description '' '    ' } else { Write-Line '    (empty)' 'DarkGray' }
    Write-Host ''
    Write-Line '  Files' 'Cyan'
    $drift = $State.Drift[$Record.id]
    $overlaps = @()
    if ($State.Overlaps.ContainsKey($Record.id)) { $overlaps = @($State.Overlaps[$Record.id]) }
    foreach ($file in @($Record.files)) {
        $color = 'Gray'
        if ($file.status -eq 'A') { $color = 'Green' } elseif ($file.status -eq 'D') { $color = 'Red' }
        Write-Parts @("    $($file.status) ", $color, $file.path, 'Gray', "  $(Get-FileStats $file)", 'DarkGray')
        if ($drift -and $drift.Paths.Contains($file.path)) {
            Write-Line '        ! changed after the agent prepared it (your own test fix, or more agent work?) - S re-captures it' 'Yellow'
        }
        foreach ($overlap in $overlaps) {
            if ($overlap.Paths -contains $file.path) {
                Write-Line "        ! also changed by #$($overlap.Record.id) ($($overlap.Record.agent): $($overlap.Record.title))" 'Yellow'
            }
        }
    }
    if ($drift -and -not $drift.Known) { Write-Line '    ! The folder this record was prepared in no longer exists; it can still be committed.' 'Yellow' }
    Write-Host ''
    Write-Line '  D diff   E edit message   B bump   S re-capture   X discard   Esc/any other key back' 'Cyan'
}

function Show-RecordDiff($State, $Record) {
    $head = Get-HeadCommit
    $tempIndex = New-TempFilePath '.index'
    try {
        $tree = New-RecordTree -Store $State.Store -Record $Record -Parent $head -TempIndex $tempIndex
    } finally {
        Remove-TempIndex $tempIndex
    }
    if ($tree.Conflict) { return "Record #$($Record.id) $($tree.Conflict)." }
    if (-not $tree.Tree) { return "Nothing to commit: record #$($Record.id)'s changes are already in HEAD." }
    Clear-Ui
    Write-Line "  Diff of #$($Record.id) '$($Record.title)' (exactly what would be committed on top of HEAD). Press q to close the pager." 'Cyan'
    Write-Host ''
    Set-CursorVisible $true
    [void](Invoke-GitInteractive -Arguments @('diff', '--stat', '--patch', $head, $tree.Tree))
    Set-CursorVisible $false
    if ($script:OutputIsConsole) { Wait-AnyKey }
    return $null
}
