# agent-commit/lib/record-relations.ps1
# How records relate to the files on disk and to each other: drift after
# prepare, shared files, and one snapshot already containing another.
# Loaded by agent-commit.ps1.

function Get-RecordDrift($Record) {
    # Files whose content in the record's folder differs from what was
    # captured. Known = $false when that folder no longer exists.
    $result = [pscustomobject]@{ Known = $false; Paths = (New-StringList) }
    $root = $Record.worktree
    if (-not $root -or -not [System.IO.Directory]::Exists($root)) { return $result }
    try {
        $files = @($Record.files)
        $existing = @($files | Where-Object { [System.IO.File]::Exists((Get-RepoFullPath $_.path $root)) } | ForEach-Object { $_.path })
        $oids = Get-WorkingTreeOids $existing $root
        foreach ($file in $files) {
            if (-not $file.wtOid) { continue }
            $current = 'deleted'
            if ($oids.ContainsKey($file.path)) { $current = $oids[$file.path] }
            if ($current -ne $file.wtOid) { $result.Paths.Add($file.path) }
        }
        $result.Known = $true
    } catch { }
    return $result
}

function Get-OverlappingRecords($Record, [object[]]$Records) {
    # Other records that list at least one of this record's files.
    $mine = New-PathSet
    foreach ($f in @($Record.files)) { [void]$mine.Add($f.path) }
    foreach ($other in @($Records)) {
        if ($null -eq $other -or $other.id -eq $Record.id) { continue }
        $shared = @(@($other.files) | Where-Object { $mine.Contains($_.path) } | ForEach-Object { $_.path })
        if ($shared.Count -gt 0) { [pscustomobject]@{ Record = $other; Paths = $shared } }
    }
}

function Test-RecordContains($Store, $Outer, $Inner, [string[]]$Paths) {
    # True when $Inner's changes to $Paths are already inside $Outer's
    # snapshot (they were on disk when $Outer was captured).
    $outerPatch = Get-PatchFilePath $Store $Outer
    $innerPatch = Get-PatchFilePath $Store $Inner
    if (-not $outerPatch -or -not $innerPatch -or -not [System.IO.File]::Exists($outerPatch) -or -not [System.IO.File]::Exists($innerPatch)) { return $false }
    $tempIndex = New-TempFilePath '.index'
    $indexEnv = @{ GIT_INDEX_FILE = $tempIndex }
    try {
        if ((Invoke-Git -Arguments @('read-tree', $Outer.baseSha) -Environment $indexEnv -AllowFailure).ExitCode -ne 0) { return $false }
        if ((Invoke-Git -Arguments @('apply', '--cached', '--whitespace=nowarn', $outerPatch) -Environment $indexEnv -AllowFailure).ExitCode -ne 0) { return $false }
        $gitArgs = @('apply', '--cached', '--reverse', '--check', '--whitespace=nowarn')
        foreach ($p in @($Paths)) { $gitArgs += "--include=$p" }
        $gitArgs += $innerPatch
        return (Invoke-Git -Arguments $gitArgs -Environment $indexEnv -AllowFailure).ExitCode -eq 0
    } finally {
        Remove-TempIndex $tempIndex
    }
}
