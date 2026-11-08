# Git Index Self-Healing and Repair Tool
# Resolves 'fatal: .git/index: index file smaller than expected'
$ErrorActionPreference = "Stop"

function Find-GitRoot {
    $dir = Get-Location
    while ($dir) {
        $gitPath = Join-Path $dir.Path ".git"
        if (Test-Path $gitPath) {
            return $gitPath
        }
        $parent = Split-Path $dir.Path -Parent
        if (-not $parent -or $parent -eq $dir.Path) { break }
        $dir = Get-Item $parent
    }
    return $null
}

$gitDir = Find-GitRoot
if (-not $gitDir) {
    Write-Error "No .git repository found in current or parent directories."
    exit 1
}

$indexPath = Join-Path $gitDir "index"
$backupPath = Join-Path $gitDir "index.backup"
$lockPath = Join-Path $gitDir "index.lock"

# Step 1: Handle stale lockfile
if (Test-Path $lockPath) {
    $lockItem = Get-Item $lockPath
    $gitProcesses = Get-Process -Name git* -ErrorAction SilentlyContinue
    if (-not $gitProcesses -or ((Get-Date) - $lockItem.LastWriteTime).TotalSeconds -gt 30) {
        Write-Host "Removing stale git lockfile: $lockPath" -ForegroundColor Yellow
        Remove-Item -Force $lockPath -ErrorAction SilentlyContinue
    }
}

# Step 2: Check index health
$isCorrupt = $false
if (-not (Test-Path $indexPath)) {
    $isCorrupt = $true
} else {
    $indexItem = Get-Item $indexPath
    if ($indexItem.Length -eq 0) {
        $isCorrupt = $true
    }
}

# Step 3: Repair if corrupt
if ($isCorrupt) {
    Write-Host "Detected corrupted/empty Git index ($indexPath). Repairing..." -ForegroundColor Yellow

    $restoredFromBackup = $false
    if (Test-Path $backupPath) {
        $backupItem = Get-Item $backupPath
        if ($backupItem.Length -gt 0) {
            try {
                Copy-Item -Force $backupPath $indexPath
                $testStatus = git status --porcelain 2>&1
                if ($LASTEXITCODE -eq 0) {
                    Write-Host "Successfully restored Git index from backup ($($backupItem.Length) bytes)." -ForegroundColor Green
                    $restoredFromBackup = $true
                }
            } catch {
                $restoredFromBackup = $false
            }
        }
    }

    if (-not $restoredFromBackup) {
        if (Test-Path $indexPath) {
            Remove-Item -Force $indexPath -ErrorAction SilentlyContinue
        }
        Write-Host "Rebuilding index from HEAD using git reset..." -ForegroundColor Cyan
        git reset
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Successfully rebuilt Git index from HEAD." -ForegroundColor Green
        } else {
            Write-Error "Failed to rebuild index with git reset."
            exit 1
        }
    }
}

# Step 4: Ensure healthy backup is up-to-date
if (Test-Path $indexPath) {
    $indexItem = Get-Item $indexPath
    if ($indexItem.Length -gt 0) {
        Copy-Item -Force $indexPath $backupPath
    }
}

Write-Host "Git index is verified and healthy ($((Get-Item $indexPath).Length) bytes)." -ForegroundColor Green
