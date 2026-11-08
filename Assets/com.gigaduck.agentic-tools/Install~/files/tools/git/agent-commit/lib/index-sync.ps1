# agent-commit/lib/index-sync.ps1
# Refreshing the shared .git/index for committed paths (waits for index.lock,
# repairs a 0-byte index, keeps index.backup).
# Loaded by agent-commit.ps1.

function Wait-IndexUnlocked([int]$Seconds = 20) {
    $lock = [System.IO.Path]::Combine($script:GitDir, 'index.lock')
    $deadline = [datetime]::UtcNow.AddSeconds($Seconds)
    while ([System.IO.File]::Exists($lock)) {
        if ([datetime]::UtcNow -gt $deadline) { return $false }
        Start-Sleep -Milliseconds 250
    }
    return $true
}

function Repair-GitIndexIfNeeded {
    $index = [System.IO.Path]::Combine($script:GitDir, 'index')
    if ([System.IO.File]::Exists($index) -and (New-Object System.IO.FileInfo $index).Length -gt 0) { return }
    $repair = [System.IO.Path]::Combine($script:RepoRoot, 'tools\git\repair-index.ps1')
    if (-not [System.IO.File]::Exists($repair)) { return }
    Push-Location $script:RepoRoot
    try { & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $repair | Out-Host } finally { Pop-Location }
}

function Sync-IndexPaths([string[]]$Paths) {
    # Point the shared index at the new HEAD for the committed paths only, so
    # `git status` stays correct. Staged work on other paths is kept.
    $list = @($Paths | Where-Object { $_ } | Sort-Object -Unique)
    if ($list.Count -eq 0) { return $null }
    if (-not (Wait-IndexUnlocked)) {
        return "Another git process kept .git/index locked. If 'git status' shows the committed files as staged, run 'git reset -q -- <files>'."
    }
    Repair-GitIndexIfNeeded
    $specFile = New-TempFilePath '.paths'
    try {
        [System.IO.File]::WriteAllBytes($specFile, $script:Utf8NoBom.GetBytes(($list -join "`0") + "`0"))
        $r = Invoke-Git -Arguments @('reset', '-q', "--pathspec-from-file=$specFile", '--pathspec-file-nul') -Environment @{ GIT_LITERAL_PATHSPECS = '1' } -AllowFailure
    } finally {
        Remove-FileQuietly $specFile
    }
    if ($r.ExitCode -ne 0) { return "Could not refresh .git/index for the committed files: $($r.Error.Trim())" }
    # Same as the repo's post-commit hook: keep a healthy index backup.
    $index = [System.IO.Path]::Combine($script:GitDir, 'index')
    try { [System.IO.File]::Copy($index, [System.IO.Path]::Combine($script:GitDir, 'index.backup'), $true) } catch { }
    return $null
}
