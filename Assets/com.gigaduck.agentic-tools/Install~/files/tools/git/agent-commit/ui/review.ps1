# agent-commit/ui/review.ps1
# Review & commit of the selection (Enter) and pushing existing local commits (P).
# Loaded by agent-commit.ps1.

function Invoke-Review($State) {
    $ids = @($State.Selected)
    if ($ids.Count -eq 0 -and $State.Records.Count -gt 0) { $ids = @($State.Records[$State.Cursor].id) }
    Update-UiState $State
    $chosen = @($State.Records | Where-Object { $ids -contains $_.id })
    if ($chosen.Count -eq 0) {
        $State.Message = 'Nothing to review. Select records with Space (or A for all).'
        return
    }
    $problem = Get-RepoProblem
    if ($problem) { $State.Message = $problem; $State.MessageColor = 'Red'; return }

    Clear-Ui
    $fetchOk = Invoke-Fetch $State.Remote
    $remote = Get-RemoteInfo
    Write-Host '  Building the commits...' -ForegroundColor DarkGray
    $plan = New-CommitPlan -Store $State.Store -Records $chosen
    Clear-Ui
    Write-Plan $State.Store $plan $remote $State.Records $fetchOk

    $committable = @($plan.Steps | Where-Object { $_.Commit -or $_.Empty }).Count
    if ($committable -eq 0) {
        Write-Line '  Nothing can be committed: every selected record conflicts.' 'Red'
        Wait-AnyKey
        $State.Message = 'Nothing was committed.'
        $State.MessageColor = 'DarkGray'
        return
    }
    if (Get-PushBlocker $remote) {
        Write-Line '  C commit locally only    N/Esc back' 'Cyan'
        $choice = Read-Choice @('c', 'n', 'esc', 'q', 'eof')
    } else {
        Write-Line '  Y commit & push    C commit only (no push)    N/Esc back' 'Cyan'
        $choice = Read-Choice @('y', 'c', 'n', 'esc', 'q', 'eof')
    }
    if ($choice -ne 'y' -and $choice -ne 'c') {
        $State.Message = 'Nothing was committed.'
        $State.MessageColor = 'DarkGray'
        return
    }
    $ok = Invoke-PlanExecution $State.Store $plan $remote ($choice -eq 'y')
    $State.Selected.Clear()
    Wait-AnyKey
    Update-UiState $State
    if ($ok) { $State.Message = 'Done.'; $State.MessageColor = 'Green' }
}

function Invoke-PushLocalCommits($State) {
    Clear-Ui
    $fetchOk = Invoke-Fetch $State.Remote
    $remote = Get-RemoteInfo
    if (-not $remote.Remote) { $State.Message = 'No git remote is configured.'; return }
    if ($remote.Ahead -eq 0 -and $remote.TrackingExists) { $State.Message = 'There are no local commits to push.'; $State.MessageColor = 'DarkGray'; return }
    if ($remote.Behind -gt 0) {
        $State.Message = "The remote has $($remote.Behind) commit(s) you do not have. Pull them first (git pull), then push."
        $State.MessageColor = 'Red'
        return
    }
    Clear-Ui
    Write-Line "  Push these local commit(s) to $($remote.Remote)?" 'Cyan'
    if ($remote.TrackingExists) {
        $log = Invoke-Git -Arguments @('log', '--format=%h %s', "$($remote.TrackingRef)..HEAD") -AllowFailure
        foreach ($line in @(Split-Records $log.Output ([char]10) | Select-Object -First 20)) { Write-Line "    $line" 'Gray' }
    }
    if (-not $fetchOk) { Write-Line "  Could not fetch from $($remote.Remote); the push may fail." 'Yellow' }
    Write-Host ''
    Write-Line '  Y push    N/Esc back' 'Cyan'
    if ((Read-Choice @('y', 'n', 'esc', 'q', 'eof')) -ne 'y') { return }
    $ok = Invoke-Push $remote
    if ($ok) { Update-PushedFlags $State.Store }
    Wait-AnyKey
    Update-UiState $State
    if ($ok) { $State.Message = 'Pushed.'; $State.MessageColor = 'Green' }
}
