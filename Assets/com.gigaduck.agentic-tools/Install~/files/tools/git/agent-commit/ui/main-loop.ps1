# agent-commit/ui/main-loop.ps1
# Key dispatch of the main screen and the picker loop.
# Loaded by agent-commit.ps1.

function Invoke-MainKey($State, [string]$Key) {
    $State.Message = ''
    $State.MessageColor = 'Yellow'
    $count = $State.Records.Count
    $current = $null
    if ($count -gt 0) { $current = $State.Records[$State.Cursor] }

    if ($Key -eq 'up' -or $Key -eq 'k') {
        if ($count -gt 0) { $State.Cursor = ($State.Cursor - 1 + $count) % $count }
    } elseif ($Key -eq 'down' -or $Key -eq 'j') {
        if ($count -gt 0) { $State.Cursor = ($State.Cursor + 1) % $count }
    } elseif ($Key -eq 'home') {
        $State.Cursor = 0
    } elseif ($Key -eq 'end') {
        $State.Cursor = [Math]::Max(0, $count - 1)
    } elseif ($Key -eq 'space') {
        if ($current -and -not $State.Selected.Remove($current.id)) { [void]$State.Selected.Add($current.id) }
    } elseif ($Key -eq 'a') {
        $allOn = $count -gt 0
        foreach ($r in $State.Records) { if (-not $State.Selected.Contains($r.id)) { $allOn = $false } }
        $State.Selected.Clear()
        if (-not $allOn) { foreach ($r in $State.Records) { [void]$State.Selected.Add($r.id) } }
    } elseif ($Key -eq 'enter') {
        Invoke-Review $State
    } elseif ($Key -eq 'u') {
        Show-Unclaimed $State
    } elseif ($Key -eq 'p') {
        Invoke-PushLocalCommits $State
    } elseif ($Key -eq 'r') {
        Update-UiState $State
        $State.Message = 'Reloaded.'
        $State.MessageColor = 'DarkGray'
    } elseif ($current -and @('v', 'd', 'e', 'b', 's', 'x') -contains $Key) {
        $action = $Key
        if ($Key -eq 'v') {
            Show-Details $State $current
            $action = Read-UiKey
        }
        $message = $null
        switch ($action) {
            'd' { $message = Show-RecordDiff $State $current }
            'e' { $message = Edit-RecordMessage $State $current; Update-UiState $State }
            'b' { $message = Step-RecordBump $State $current; Update-UiState $State }
            's' { $message = Invoke-Recapture $State $current; Update-UiState $State }
            'x' { $message = Invoke-Discard $State $current; Update-UiState $State }
        }
        if ($message) { $State.Message = $message; $State.MessageColor = 'Cyan' }
    }
}

function Start-Interactive($Store) {
    $state = New-UiState $Store
    Update-UiState $state
    while ($true) {
        Show-MainScreen $state
        $key = Read-UiKey
        if ($key -eq 'q' -or $key -eq 'esc' -or $key -eq 'eof') { return }
        try {
            Invoke-MainKey $state $key
        } catch {
            Set-CursorVisible $false
            $state.Message = "Error: $($_.Exception.Message)"
            $state.MessageColor = 'Red'
            try { Update-UiState $state } catch { }
        }
    }
}
