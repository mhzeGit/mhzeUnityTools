# agent-commit/lib/working-tree.ps1
# Changed files in a folder, the ones no record claims, worktree labels and
# the legacy .agent-changes count.
# Loaded by agent-commit.ps1.

function Get-WorkingTreeChanges([string]$Root) {
    $r = Invoke-Git -Arguments @('--no-optional-locks', 'status', '--porcelain=v1', '-z', '--untracked-files=all', '--no-renames') -WorkDir $Root
    foreach ($record in @(Split-Records $r.Output ([char]0))) {
        if ($record.Length -lt 4) { continue }
        [pscustomobject]@{ Code = $record.Substring(0, 2); Path = $record.Substring(3) }
    }
}

function Get-UnclaimedChanges([object[]]$Records, [string]$Root) {
    # Changed files in $Root that no pending record of that folder lists
    # (version.json counts only when it changed beyond the version number).
    if (-not $Root) { $Root = $script:RepoRoot }
    $claimed = New-PathSet
    foreach ($record in @($Records)) {
        if ($null -eq $record -or -not (Test-SamePath $record.worktree $Root)) { continue }
        foreach ($f in @($record.files)) { [void]$claimed.Add($f.path) }
    }
    $entries = Get-TreeEntries (Get-HeadCommit $Root)
    $result = [System.Collections.Generic.List[object]]::new()
    $targets = [System.Collections.Generic.List[object]]::new()
    foreach ($change in @(Get-WorkingTreeChanges $Root)) {
        if ($claimed.Contains($change.Path)) { continue }
        if ((Test-VersionFilePath $change.Path) -and $entries.ContainsKey($change.Path) -and [System.IO.File]::Exists((Get-RepoFullPath $change.Path $Root))) {
            $targets.Add($change)
            continue
        }
        $result.Add($change)
    }
    if ($targets.Count -gt 0) {
        $oids = Get-WorkingTreeOids @($targets | ForEach-Object { $_.Path }) $Root -Write
        foreach ($change in $targets) {
            $headOid = $entries[$change.Path].Oid
            $wtOid = $oids[$change.Path]
            if ($headOid -eq $wtOid -or (Test-VersionOnlyChange $change.Path $headOid $wtOid)) { continue }
            $result.Add($change)
        }
    }
    return $result.ToArray()
}

function Get-WorktreeLabel($Record) {
    if (-not $Record.worktree -or (Test-SamePath $Record.worktree $script:RepoRoot)) { return 'this folder' }
    $root = $script:RepoRoot.TrimEnd('\') + '\'
    $full = [System.IO.Path]::GetFullPath($Record.worktree)
    if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { return $full.Substring($root.Length).Replace('\', '/') }
    return $full
}

function Get-LegacyChangeSetCount {
    # Change-set files of the previous workflow (.agent-changes/*.md).
    $dir = [System.IO.Path]::Combine($script:RepoRoot, '.agent-changes')
    if (-not [System.IO.Directory]::Exists($dir)) { return 0 }
    return @([System.IO.Directory]::GetFiles($dir, '*.md')).Count
}
