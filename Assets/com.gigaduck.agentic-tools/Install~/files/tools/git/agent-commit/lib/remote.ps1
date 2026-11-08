# agent-commit/lib/remote.ps1
# Repository state and the remote: reasons not to commit, upstream and
# ahead/behind, fetch, push, pushed flags.
# Loaded by agent-commit.ps1.

function Get-RepoProblem {
    # A reason the tool must not commit right now, or $null.
    if (-not (Get-CurrentBranch)) { return 'HEAD is detached. Check out a branch first.' }
    foreach ($marker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-merge', 'rebase-apply')) {
        if (Test-Path -LiteralPath ([System.IO.Path]::Combine($script:GitDir, $marker))) {
            return "A merge, rebase, cherry-pick or revert is in progress ($marker). Finish or abort it first."
        }
    }
    return $null
}

function Get-RemoteInfo {
    $branch = Get-CurrentBranch
    $info = [pscustomobject]@{
        Branch = $branch; Remote = $null; MergeRef = $null; TrackingRef = $null
        TrackingExists = $false; HasUpstream = $false; Ahead = 0; Behind = 0
    }
    if (-not $branch) { return $info }
    $remote = (Invoke-Git -Arguments @('config', '--get', "branch.$branch.remote") -AllowFailure).Output.Trim()
    $merge = (Invoke-Git -Arguments @('config', '--get', "branch.$branch.merge") -AllowFailure).Output.Trim()
    $info.HasUpstream = [bool]($remote -and $merge -and $remote -ne '.')
    if (-not $info.HasUpstream) {
        $remotes = @(Split-Records (Invoke-Git -Arguments @('remote')).Output ([char]10) | ForEach-Object { $_.Trim() })
        $remote = $null
        if ($remotes -contains 'origin') { $remote = 'origin' } elseif ($remotes.Count -eq 1) { $remote = $remotes[0] }
        $merge = "refs/heads/$branch"
    }
    if (-not $remote) { return $info }
    $info.Remote = $remote
    $info.MergeRef = $merge
    $info.TrackingRef = "refs/remotes/$remote/" + ($merge -replace '^refs/heads/', '')
    $info.TrackingExists = (Invoke-Git -Arguments @('rev-parse', '-q', '--verify', $info.TrackingRef) -AllowFailure).ExitCode -eq 0
    if ($info.TrackingExists) {
        $counts = (Invoke-Git -Arguments @('rev-list', '--left-right', '--count', "HEAD...$($info.TrackingRef)")).Output.Trim() -split '\s+'
        $info.Ahead = [int]$counts[0]
        $info.Behind = [int]$counts[1]
    }
    return $info
}

function Invoke-Fetch($Remote) {
    if (-not $Remote -or -not $Remote.Remote) { return $false }
    Write-Host "  Fetching $($Remote.Remote)..." -ForegroundColor DarkGray
    return (Invoke-GitInteractive -Arguments @('fetch', '--quiet', $Remote.Remote)) -eq 0
}

function Invoke-Push($Remote) {
    $gitArgs = @('push')
    if (-not $Remote.HasUpstream) { $gitArgs += '--set-upstream' }
    $gitArgs += @($Remote.Remote, "HEAD:$($Remote.MergeRef)")
    Write-Host "  Pushing to $($Remote.Remote)..." -ForegroundColor Cyan
    return (Invoke-GitInteractive -Arguments $gitArgs) -eq 0
}

function Update-PushedFlags($Store) {
    # Mark committed records whose commit is now on the remote branch.
    $remote = Get-RemoteInfo
    if (-not $remote.TrackingExists) { return }
    Enter-StoreLock $Store
    try {
        foreach ($record in @(Get-AllRecords $Store | Where-Object { $_.status -eq 'applied' -and -not $_.pushed -and $_.commit })) {
            $r = Invoke-Git -Arguments @('merge-base', '--is-ancestor', $record.commit, $remote.TrackingRef) -AllowFailure
            if ($r.ExitCode -eq 0) { $record.pushed = $true; Save-Record $Store $record }
        }
    } finally {
        Exit-StoreLock $Store
    }
}
