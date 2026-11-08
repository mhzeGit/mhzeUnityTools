# agent-commit/lib/worktree-sync.ps1
# After committing: rewriting version numbers in this folder and writing files
# committed from another worktree (never over newer edits).
# Loaded by agent-commit.ps1.

function Sync-WorktreeVersions([string]$Version) {
    # Rewrite only the version number in this folder's version.json; other
    # uncommitted edits in that file are preserved.
    $changed = New-StringList
    if (-not $Version) { return , $changed }
    $full = Get-RepoFullPath $script:VersionFileName
    if (-not [System.IO.File]::Exists($full)) { return , $changed }
    $decoded = ConvertFrom-Utf8Bytes ([System.IO.File]::ReadAllBytes($full))
    if (-not $decoded.RoundTrips) { return , $changed }
    $newText = Set-VersionInText $decoded.Text $Version
    if ($newText -ceq $decoded.Text) { return , $changed }
    [System.IO.File]::WriteAllBytes($full, (ConvertTo-Utf8Bytes $newText $decoded.Bom))
    $changed.Add($script:VersionFileName)
    return , $changed
}

function Update-WorktreeFiles([string]$Base, [string]$Final, [string[]]$Paths) {
    # After committing: files in this folder that still hold the pre-commit
    # content (a record captured in another worktree) get the committed
    # content. Files with other uncommitted edits are never overwritten.
    $result = [pscustomobject]@{ Updated = (New-StringList); Kept = (New-StringList) }
    $unique = @($Paths | Where-Object { $_ } | Sort-Object -Unique)
    if ($unique.Count -eq 0) { return $result }
    $baseEntries = Get-TreeEntries $Base
    $finalEntries = Get-TreeEntries $Final
    $existing = @($unique | Where-Object { [System.IO.File]::Exists((Get-RepoFullPath $_)) })
    $oids = Get-WorkingTreeOids $existing $script:RepoRoot
    foreach ($path in $unique) {
        $currentOid = 'deleted'
        if ($oids.ContainsKey($path)) { $currentOid = $oids[$path] }
        $finalOid = 'deleted'
        if ($finalEntries.ContainsKey($path)) { $finalOid = $finalEntries[$path].Oid }
        $baseOid = 'deleted'
        if ($baseEntries.ContainsKey($path)) { $baseOid = $baseEntries[$path].Oid }
        if ($currentOid -eq $finalOid) { continue }
        if ($currentOid -ne $baseOid) { $result.Kept.Add($path); continue }
        $full = Get-RepoFullPath $path
        try {
            if ($finalOid -eq 'deleted') {
                [System.IO.File]::Delete($full)
            } else {
                $bytes = (Invoke-Git -Arguments @('cat-file', '--filters', "$($Final):$path")).Bytes
                [void][System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full))
                [System.IO.File]::WriteAllBytes($full, $bytes)
            }
            $result.Updated.Add($path)
        } catch {
            $result.Kept.Add($path)
        }
    }
    return $result
}
