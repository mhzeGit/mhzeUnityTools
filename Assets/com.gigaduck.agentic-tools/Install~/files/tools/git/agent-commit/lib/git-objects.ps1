# agent-commit/lib/git-objects.ps1
# Reading trees, (temporary) index entries, working-tree blob ids and
# ignored paths.
# Loaded by agent-commit.ps1.

function Get-TreeEntries([string]$Rev) {
    # Map of every file path in a commit or tree (case-insensitive) ->
    # Path/Mode/Type/Oid. Objects are immutable, so results are cached.
    if (-not $Rev) { return , (New-PathMap) }
    if ($script:TreeCache.ContainsKey($Rev)) { return , $script:TreeCache[$Rev] }
    $map = New-PathMap
    $r = Invoke-Git -Arguments @('ls-tree', '-r', '-z', '--full-tree', $Rev)
    foreach ($record in @(Split-Records $r.Output ([char]0))) {
        $tab = $record.IndexOf([char]9)
        if ($tab -lt 0) { continue }
        $meta = $record.Substring(0, $tab).Split([char]' ')
        $path = $record.Substring($tab + 1)
        $map[$path] = [pscustomobject]@{ Path = $path; Mode = $meta[0]; Type = $meta[1]; Oid = $meta[2] }
    }
    $script:TreeCache[$Rev] = $map
    return , $map
}

function Get-IndexEntry([string]$Path, [hashtable]$Environment, [string]$Root) {
    # Mode/Oid of one path in the (temporary) index, or $null.
    $env2 = @{ GIT_LITERAL_PATHSPECS = '1' }
    if ($Environment) { foreach ($k in $Environment.Keys) { $env2[$k] = $Environment[$k] } }
    $r = Invoke-Git -Arguments @('ls-files', '-s', '-z', '--', $Path) -Environment $env2 -WorkDir $Root
    foreach ($record in @(Split-Records $r.Output ([char]0))) {
        $tab = $record.IndexOf([char]9)
        if ($tab -lt 0) { continue }
        $meta = $record.Substring(0, $tab).Split([char]' ')
        return [pscustomobject]@{ Mode = $meta[0]; Oid = $meta[1]; Path = $record.Substring($tab + 1) }
    }
    return $null
}

function Get-WorkingTreeOids([string[]]$Paths, [string]$Root, [switch]$Write) {
    # Hash working-tree files exactly as `git add` would (line-ending filters
    # applied). Returns path -> blob oid. -Write also stores the blobs.
    $map = New-PathMap
    $list = @($Paths | Where-Object { $_ })
    if ($list.Count -eq 0) { return , $map }
    $oids = [System.Collections.Generic.List[string]]::new()
    foreach ($chunk in (Split-ArgumentChunks $list)) {
        $gitArgs = @('hash-object')
        if ($Write) { $gitArgs += '-w' }
        $gitArgs += '--'
        $gitArgs += $chunk
        $r = Invoke-Git -Arguments $gitArgs -WorkDir $Root
        foreach ($oid in @(Split-Records $r.Output ([char]10))) {
            if ($oid.Trim()) { $oids.Add($oid.Trim()) }
        }
    }
    if ($oids.Count -ne $list.Count) { throw "git hash-object returned $($oids.Count) hashes for $($list.Count) files." }
    for ($i = 0; $i -lt $list.Count; $i++) { $map[$list[$i]] = $oids[$i] }
    return , $map
}

function Get-IgnoredPaths([string[]]$Paths, [string]$Root) {
    $set = New-PathSet
    $list = @($Paths | Where-Object { $_ })
    if ($list.Count -eq 0) { return , $set }
    foreach ($chunk in (Split-ArgumentChunks $list)) {
        $r = Invoke-Git -Arguments (@('ls-files', '-z', '--others', '--ignored', '--exclude-standard', '--') + $chunk) -Environment @{ GIT_LITERAL_PATHSPECS = '1' } -WorkDir $Root
        foreach ($p in @(Split-Records $r.Output ([char]0))) { [void]$set.Add($p) }
    }
    return , $set
}
