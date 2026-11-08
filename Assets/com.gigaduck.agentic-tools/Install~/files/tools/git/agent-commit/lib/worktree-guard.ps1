# agent-commit/lib/worktree-guard.ps1
# Keeping the shared main worktree on its branch: installing the committed
# post-checkout guard hook and repairing a detached (history-checkout) HEAD.
# Loaded by agent-commit.ps1.

function Get-WorktreeGuardSourcePath {
    return [System.IO.Path]::Combine($script:ToolRoot, 'hooks', 'post-checkout')
}

function Get-WorktreeGuardHookPath {
    return [System.IO.Path]::Combine($script:CommonDir, 'hooks', 'post-checkout')
}

function Test-GitOperationInProgress {
    # A rebase/merge/bisect/revert legitimately detaches HEAD; never fight it.
    foreach ($marker in @('rebase-merge', 'rebase-apply', 'MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'BISECT_LOG')) {
        $path = [System.IO.Path]::Combine($script:GitDir, $marker)
        if ([System.IO.File]::Exists($path) -or [System.IO.Directory]::Exists($path)) { return $true }
    }
    return $false
}

function Get-DefaultBranchName {
    $origin = Invoke-Git -Arguments @('rev-parse', '--abbrev-ref', 'origin/HEAD') -AllowFailure
    if ($origin.ExitCode -eq 0) {
        $name = $origin.Output.Trim()
        if ($name -match '^origin/(.+)$' -and $Matches[1] -ne 'HEAD') { return $Matches[1] }
    }
    foreach ($candidate in @('main', 'master')) {
        if ((Invoke-Git -Arguments @('show-ref', '--verify', '--quiet', "refs/heads/$candidate") -AllowFailure).ExitCode -eq 0) { return $candidate }
    }
    return $null
}

function Get-MainWorktreeGuardState {
    $state = [pscustomobject]@{
        IsMain              = $false
        Detached            = $false
        Branch              = ''
        Head                = ''
        BranchName          = ''
        Clean               = $true
        OperationInProgress = $false
    }
    if (-not $script:GitDir -or -not $script:CommonDir) { return $state }
    $gitDir = $script:GitDir.TrimEnd([char]'\', [char]'/')
    $commonDir = $script:CommonDir.TrimEnd([char]'\', [char]'/')
    if (-not $gitDir.Equals($commonDir, [System.StringComparison]::OrdinalIgnoreCase)) { return $state }
    $state.IsMain = $true
    $state.Head = Get-HeadCommit $script:RepoRoot
    $state.Branch = Get-CurrentBranch $script:RepoRoot
    $state.Detached = [string]::IsNullOrEmpty($state.Branch)
    $state.OperationInProgress = Test-GitOperationInProgress
    $status = Invoke-Git -Arguments @('status', '--porcelain', '--untracked-files=no') -AllowFailure
    $state.Clean = ($status.ExitCode -eq 0 -and -not $status.Output.Trim())
    if ($state.Detached) { $state.BranchName = Get-DefaultBranchName }
    return $state
}

function Install-WorktreeGuardHook {
    # Keep .git/hooks/post-checkout in sync with the committed template. Hooks
    # are not cloned, so this self-heals after a fresh clone; the template is
    # tracked, so the guard travels with the repository.
    $source = Get-WorktreeGuardSourcePath
    if (-not [System.IO.File]::Exists($source)) { return }
    $target = Get-WorktreeGuardHookPath
    $text = [System.IO.File]::ReadAllText($source) -replace "`r`n", "`n"
    if ([System.IO.File]::Exists($target)) {
        $current = [System.IO.File]::ReadAllText($target) -replace "`r`n", "`n"
        if ($current -ceq $text) { return }
    }
    $dir = [System.IO.Path]::GetDirectoryName($target)
    if (-not [System.IO.Directory]::Exists($dir)) { [void][System.IO.Directory]::CreateDirectory($dir) }
    [System.IO.File]::WriteAllText($target, $text, $script:Utf8NoBom)
}

function Invoke-WorktreeGuard {
    # Called by every command before it does anything. Installs the hook, and
    # when this folder is the shared main worktree parked on a detached HEAD it
    # switches back to the branch (fast, safe and lossless when clean).
    Install-WorktreeGuardHook
    $state = Get-MainWorktreeGuardState
    if (-not $state.IsMain -or -not $state.Detached) { return }
    $short = $state.Head
    if ($short -and $short.Length -gt 8) { $short = $short.Substring(0, 8) }
    Write-Out ''
    Write-Out 'WORKTREE GUARD  This shared checkout is on a DETACHED HEAD, so Unity shows old scenes.' 'Red'
    Write-Out "  HEAD is $short instead of a branch. A history checkout landed here." 'Yellow'
    if ($state.OperationInProgress) {
        Write-Out '  A rebase/merge/bisect is in progress, so it was left untouched.' 'Yellow'
        return
    }
    if (-not $state.BranchName) {
        Write-Out '  No local branch to switch back to; run "git switch <branch>" yourself.' 'Yellow'
        return
    }
    # Try to switch back even with uncommitted changes: git carries them when
    # they do not conflict. This is what stops a rogue checkout from sticking.
    $switch = Invoke-Git -Arguments @('switch', $state.BranchName) -AllowFailure
    if ($switch.ExitCode -eq 0) {
        if ($state.Clean) {
            Write-Out "  It was clean, so it was switched back to '$($state.BranchName)' now." 'Green'
        } else {
            Write-Out "  It was switched back to '$($state.BranchName)' now (uncommitted changes kept)." 'Green'
        }
    } else {
        $message = $switch.Error.Trim()
        if (-not $message) { $message = $switch.Output.Trim() }
        Write-Out "  Switching back to '$($state.BranchName)' failed: $message" 'Red'
        if (-not $state.Clean) {
            Write-Out "  Its uncommitted changes conflict; commit or keep them, then run: git switch $($state.BranchName)" 'Yellow'
        }
    }
}
