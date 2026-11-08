# agent-commit/lib/record-tree.ps1
# Building the tree of one record on top of a parent commit (patch apply with
# 3-way fallback, then the version bump).
# Loaded by agent-commit.ps1.

function Set-RecordEntriesDirect {
    # git apply refuses a patch of 1 GiB or more ("patch too large"). A record
    # also keeps the exact blob id of every file it changes, so an oversized
    # record sets those blobs in the temporary index directly. A path the
    # parent already changed since the record was captured was captured again
    # by a later record; keep the newer content instead of reverting it.
    param($Record, [string]$Parent, [hashtable]$IndexEnv, $ParentEntries, $Warnings)
    $sameBase = (-not $Record.baseSha) -or ($Record.baseSha -eq $Parent)
    $baseEntries = $ParentEntries
    if (-not $sameBase) { $baseEntries = Get-TreeEntries $Record.baseSha }
    $superseded = New-StringList
    $lines = New-Object System.Text.StringBuilder
    foreach ($file in @($Record.files)) {
        $path = [string]$file.path
        if ([string]::IsNullOrWhiteSpace($path)) { continue }
        $target = [string]$file.oid
        if ($file.status -eq 'D') { $target = 'deleted' }
        $parentOid = 'deleted'
        if ($ParentEntries.ContainsKey($path)) { $parentOid = $ParentEntries[$path].Oid }
        if ($parentOid -eq $target) { continue }
        $baseOid = 'deleted'
        if ($baseEntries.ContainsKey($path)) { $baseOid = $baseEntries[$path].Oid }
        if (-not $sameBase -and $parentOid -ne $baseOid) { $superseded.Add($path); continue }
        if ($target -eq 'deleted') {
            [void]$lines.Append('0 ' + ('0' * 40) + "`t" + $path + [char]0)
        } else {
            $mode = '100644'
            if ($ParentEntries.ContainsKey($path)) { $mode = $ParentEntries[$path].Mode }
            [void]$lines.Append("$mode $target`t$path" + [char]0)
        }
    }
    if ($superseded.Count -gt 0 -and $Warnings) {
        $Warnings.Add("$(($superseded.ToArray()) -join ', ') was captured again by a later record, so this record keeps the newer content.")
    }
    if ($lines.Length -eq 0) { return $null }
    $apply = Invoke-Git -Arguments @('update-index', '-z', '--index-info') -Environment $IndexEnv -InputText $lines.ToString() -AllowFailure
    if ($apply.ExitCode -ne 0) {
        $message = $apply.Error.Trim()
        if (-not $message) { $message = $apply.Output.Trim() }
        return "does not apply on top of the commits before it ($message)"
    }
    return $null
}

function New-RecordTree {
    # Apply one record's patch on top of $Parent in a temporary index (3-way
    # merge when the file moved on since capture), then write the record's
    # version bump into every version file. Nothing outside the object
    # database is written. Tree = $null means nothing is left to commit.
    param($Store, $Record, [string]$Parent, [string]$TempIndex)
    $result = [pscustomobject]@{
        Tree       = $null
        ParentTree = $null
        Conflict   = $null
        Changes    = @()
        OldVersion = $null
        NewVersion = $null
        Warnings   = (New-StringList)
    }
    $indexEnv = @{ GIT_INDEX_FILE = $TempIndex }
    Remove-TempIndex $TempIndex
    [void](Invoke-Git -Arguments @('read-tree', $Parent) -Environment $indexEnv)
    $result.ParentTree = (Invoke-Git -Arguments @('rev-parse', "$Parent^{tree}")).Output.Trim()
    $parentEntries = Get-TreeEntries $Parent
    $result.OldVersion = Get-VersionFromEntries $parentEntries
    $result.NewVersion = $result.OldVersion

    $patch = Get-PatchFilePath $Store $Record
    if (-not $patch -or -not [System.IO.File]::Exists($patch)) {
        $result.Conflict = 'its saved patch file is missing'
        return $result
    }
    $patchSize = (New-Object System.IO.FileInfo $patch).Length
    if ($patchSize -ge 1000000000) {
        $conflict = Set-RecordEntriesDirect -Record $Record -Parent $Parent -IndexEnv $indexEnv -ParentEntries $parentEntries -Warnings $result.Warnings
        if ($conflict) { $result.Conflict = $conflict; return $result }
    } else {
        $apply = Invoke-Git -Arguments @('apply', '--cached', '--whitespace=nowarn', $patch) -Environment $indexEnv -AllowFailure
        if ($apply.ExitCode -ne 0) {
            Remove-TempIndex $TempIndex
            [void](Invoke-Git -Arguments @('read-tree', $Parent) -Environment $indexEnv)
            $threeWay = Invoke-Git -Arguments @('apply', '--cached', '--3way', '--whitespace=nowarn', $patch) -Environment $indexEnv -AllowFailure
            $unmerged = Invoke-Git -Arguments @('ls-files', '-u', '-z') -Environment $indexEnv -AllowFailure
            $conflicts = @(@(Split-Records $unmerged.Output ([char]0)) | ForEach-Object {
                $tab = $_.IndexOf([char]9)
                if ($tab -ge 0) { $_.Substring($tab + 1) }
            } | Sort-Object -Unique)
            if ($conflicts.Count -gt 0) {
                $result.Conflict = 'conflicts with the commits before it in ' + ($conflicts -join ', ')
                return $result
            }
            if ($threeWay.ExitCode -ne 0) {
                $message = ($threeWay.Error.Trim() -split "`n" | Select-Object -First 2) -join ' '
                if (-not $message) { $message = ($apply.Error.Trim() -split "`n" | Select-Object -First 2) -join ' ' }
                $result.Conflict = "does not apply on top of the commits before it ($message)"
                return $result
            }
        }
    }

    $tree = (Invoke-Git -Arguments @('write-tree') -Environment $indexEnv).Output.Trim()
    if ($tree -eq $result.ParentTree) { return $result }

    if ($result.OldVersion -and ($Record.bump -eq 'patch' -or $Record.bump -eq 'minor')) {
        $newVersion = Step-Version $result.OldVersion $Record.bump
        $entry = Get-IndexEntry $script:VersionFileName $indexEnv $script:RepoRoot
        if ($entry) {
            $blob = Get-BlobText $entry.Oid
            if (-not $blob.RoundTrips) {
                $result.Warnings.Add("$($script:VersionFileName) is not plain UTF-8; its version number was not updated.")
            } else {
                $text = Set-VersionInText $blob.Text $newVersion
                if ($text -cne $blob.Text) {
                    $oid = New-BlobFromText $text $blob.Bom
                    [void](Invoke-Git -Arguments @('update-index', '--cacheinfo', "$($entry.Mode),$oid,$($script:VersionFileName)") -Environment $indexEnv)
                }
            }
        }
        $result.NewVersion = $newVersion
        $tree = (Invoke-Git -Arguments @('write-tree') -Environment $indexEnv).Output.Trim()
    }
    $result.Tree = $tree

    $listed = New-PathSet
    foreach ($f in @($Record.files)) { [void]$listed.Add($f.path) }
    $diff = Invoke-Git -Arguments @('diff-tree', '-r', '--no-renames', '--name-status', '-z', $result.ParentTree, $tree)
    $tokens = @(Split-Records $diff.Output ([char]0))
    $changes = for ($i = 0; $i + 1 -lt $tokens.Count; $i += 2) {
        $path = $tokens[$i + 1]
        [pscustomobject]@{ Status = $tokens[$i].Substring(0, 1); Path = $path; FromBump = ((Test-VersionFilePath $path) -and -not $listed.Contains($path)) }
    }
    $result.Changes = @($changes)
    return $result
}
