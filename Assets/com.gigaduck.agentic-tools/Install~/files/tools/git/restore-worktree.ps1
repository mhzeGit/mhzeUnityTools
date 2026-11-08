# Restore the shared main worktree after a history checkout left it on a
# detached HEAD (Unity then shows old scenes, e.g. an old main menu).
#
# Normal use:
#   pwsh -NoProfile -ExecutionPolicy Bypass -File tools/git/restore-worktree.ps1
#
# It switches the current checkout back to its branch. It refuses to touch a
# checkout with uncommitted changes unless you pass -Force (which discards
# them), and it never runs while a rebase/merge/bisect is in progress.
[CmdletBinding()]
param(
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (& git rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $root) { throw 'Not inside a git repository.' }
$root = $root.Trim()
Push-Location $root
try {
    $branch = (& git symbolic-ref -q --short HEAD 2>$null).Trim()
    $head = (& git rev-parse --short HEAD 2>$null).Trim()
    if ($branch) {
        Write-Host "Already on branch '$branch' at $head - nothing to restore." -ForegroundColor Green
        return
    }

    Write-Host "Detached HEAD at $head in $root." -ForegroundColor Yellow

    foreach ($marker in @('rebase-merge', 'rebase-apply', 'MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'BISECT_LOG')) {
        $path = Join-Path (Join-Path $root '.git') $marker
        if (Test-Path -LiteralPath $path) {
            throw "A git operation is in progress ($marker); finish or abort it before restoring."
        }
    }

    $target = $null
    $origin = (& git rev-parse --abbrev-ref origin/HEAD 2>$null).Trim()
    if ($LASTEXITCODE -eq 0 -and $origin -match '^origin/(.+)$' -and $Matches[1] -ne 'HEAD') { $target = $Matches[1] }
    if (-not $target) {
        foreach ($candidate in @('main', 'master')) {
            & git show-ref --verify --quiet "refs/heads/$candidate"
            if ($LASTEXITCODE -eq 0) { $target = $candidate; break }
        }
    }
    if (-not $target) { throw 'No local branch (main/master) found to switch back to.' }

    $dirty = (& git status --porcelain 2>$null)
    if ($dirty -and -not $Force) {
        throw "The checkout has uncommitted changes; refusing to switch. Keep them, then rerun, or pass -Force to discard them."
    }

    if ($Force) { & git switch --force $target } else { & git switch $target }
    if ($LASTEXITCODE -ne 0) { throw "git switch $target failed." }

    $now = (& git symbolic-ref -q --short HEAD 2>$null).Trim()
    $newHead = (& git rev-parse --short HEAD 2>$null).Trim()
    Write-Host "Restored: now on '$now' at $newHead." -ForegroundColor Green
} finally {
    Pop-Location
}
