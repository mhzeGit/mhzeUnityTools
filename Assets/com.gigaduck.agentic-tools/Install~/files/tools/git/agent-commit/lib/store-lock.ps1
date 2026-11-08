# agent-commit/lib/store-lock.ps1
# The record store location (<git-common-dir>/agent-commits) and its
# re-entrant cross-process lock with stale-lock detection.
# Loaded by agent-commit.ps1.

function Get-StorePaths {
    $root = [System.IO.Path]::Combine($script:CommonDir, 'agent-commits')
    return [pscustomobject]@{
        Root    = $root
        Records = [System.IO.Path]::Combine($root, 'records')
        Patches = [System.IO.Path]::Combine($root, 'patches')
        Lock    = [System.IO.Path]::Combine($root, 'lock')
    }
}

function Test-StaleLock([string]$LockPath) {
    try {
        if (-not [System.IO.File]::Exists($LockPath)) { return $false }
        $age = [datetime]::UtcNow - [System.IO.File]::GetLastWriteTimeUtc($LockPath)
        if ($age.TotalMinutes -gt 10) { return $true }
        $text = [System.IO.File]::ReadAllText($LockPath)
        $pidMatch = [regex]::Match($text, '"pid"\s*:\s*(\d+)')
        $hostMatch = [regex]::Match($text, '"host"\s*:\s*"([^"]*)"')
        if ($pidMatch.Success -and (-not $hostMatch.Success -or $hostMatch.Groups[1].Value -eq $env:COMPUTERNAME)) {
            $proc = Get-Process -Id ([int]$pidMatch.Groups[1].Value) -ErrorAction SilentlyContinue
            if (-not $proc) { return $true }
        } elseif (-not $pidMatch.Success -and $age.TotalMinutes -gt 1) {
            return $true
        }
    } catch { }
    return $false
}

function Enter-StoreLock($Store) {
    # Serialize record writes between agents and the picker. Re-entrant.
    if ($script:StoreLockDepth -gt 0) { $script:StoreLockDepth++; return }
    [void][System.IO.Directory]::CreateDirectory($Store.Records)
    [void][System.IO.Directory]::CreateDirectory($Store.Patches)
    $deadline = [datetime]::UtcNow.AddSeconds(120)
    $warned = $false
    while ($true) {
        try {
            $stream = [System.IO.File]::Open($Store.Lock, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try {
                $info = '{"pid": ' + $PID + ', "host": "' + $env:COMPUTERNAME + '", "startedAt": "' + (Get-IsoNow) + '"}'
                $bytes = $script:Utf8NoBom.GetBytes($info)
                $stream.Write($bytes, 0, $bytes.Length)
            } finally {
                $stream.Dispose()
            }
            $script:StoreLockDepth = 1
            return
        } catch {
            if (Test-StaleLock $Store.Lock) {
                Remove-FileQuietly $Store.Lock
                continue
            }
            if ([datetime]::UtcNow -gt $deadline) {
                throw "Another agent-commit operation is still running (lock file: $($Store.Lock)). Try again in a moment."
            }
            if (-not $warned) {
                Write-Host 'Waiting for another agent-commit operation to finish...' -ForegroundColor Yellow
                $warned = $true
            }
            Start-Sleep -Milliseconds 250
        }
    }
}

function Exit-StoreLock($Store) {
    if ($script:StoreLockDepth -le 0) { return }
    $script:StoreLockDepth--
    if ($script:StoreLockDepth -eq 0) { Remove-FileQuietly $Store.Lock }
}
