# agent-commit/ui/main-screen.ps1
# The main list screen and its header.
# Loaded by agent-commit.ps1.

function Write-Header($State) {
    $remote = $State.Remote
    $target = '(no remote)'
    if ($remote.Remote) { $target = "$($remote.Remote)/$($remote.MergeRef -replace '^refs/heads/', '')" }
    $head = ''
    if ($State.Head) { $head = $State.Head.Substring(0, 7) }
    $version = ''
    if ($State.Version) { $version = "  v$($State.Version)" }
    Write-Parts @('  Agent commits', 'Cyan', "   $($remote.Branch) -> $target   HEAD $head$version", 'DarkGray')
    if ($remote.Ahead -gt 0) {
        Write-Line "  $($remote.Ahead) local commit(s) not pushed yet - press P to push them." 'Yellow'
    }
    if ($remote.Behind -gt 0) {
        Write-Line "  $target has $($remote.Behind) commit(s) you do not have (as of the last fetch)." 'Yellow'
    }
    Write-Host ''
}

function Show-MainScreen($State) {
    Clear-Ui
    Write-Header $State
    $count = $State.Records.Count
    if ($count -eq 0) {
        Write-Line '  No records are waiting. Agents register their finished work with:' 'DarkGray'
        Write-Line '    tools/git/agent-commit.ps1 prepare ...   (see AGENTS.md)' 'DarkGray'
    }
    # Scroll the list (two rows per record) so the cursor stays on screen.
    $visible = $count
    if ($script:OutputIsConsole) {
        try { $visible = [Math]::Max(1, [int][Math]::Floor(([Console]::WindowHeight - 14) / 2)) } catch { }
    }
    $start = 0
    if ($count -gt $visible) { $start = [Math]::Min([Math]::Max(0, $State.Cursor - [int][Math]::Floor($visible / 2)), $count - $visible) }
    $end = [Math]::Min($count, $start + $visible)
    if ($start -gt 0) { Write-Line "      ... $start more above" 'DarkGray' }
    for ($i = $start; $i -lt $end; $i++) {
        $record = $State.Records[$i]
        $isCursor = $i -eq $State.Cursor
        $pointer = '   '
        if ($isCursor) { $pointer = ' > ' }
        $box = '[ ]'
        $boxColor = 'Gray'
        if ($State.Selected.Contains($record.id)) { $box = '[x]'; $boxColor = 'Green' }
        $titleColor = 'Gray'
        if ($isCursor) { $titleColor = 'White' }
        Write-Parts @($pointer, 'Cyan', $box, $boxColor, (' {0,4}  ' -f ('#' + $record.id)), 'DarkGray', $record.title, $titleColor)
        $summary = Get-RecordSummary $State $record
        Write-Parts @('            ', '', $summary[0], $summary[1])
    }
    if ($end -lt $count) { Write-Line "      ... $($count - $end) more below" 'DarkGray' }
    Write-Host ''
    if ($State.Unclaimed.Count -gt 0) {
        Write-Line "  $($State.Unclaimed.Count) changed file(s) in this folder are not in any record (U to list)." 'DarkGray'
    }
    if ($State.Legacy -gt 0) {
        Write-Line "  $($State.Legacy) old-style change set(s) in .agent-changes/ are ignored; ask those agents to register with agent-commit.ps1 prepare." 'Yellow'
    }
    if ($State.Unclaimed.Count -gt 0 -or $State.Legacy -gt 0) { Write-Host '' }
    Write-Line '  Up/Down move   Space select   A all/none   Enter review & commit   Q quit' 'Cyan'
    Write-Line '  V details   D diff   E edit message   B bump   S re-capture   X discard   U unlisted files   P push   R reload' 'DarkCyan'
    if ($State.Message) {
        Write-Host ''
        Write-Wrapped $State.Message $State.MessageColor '  '
    }
}
