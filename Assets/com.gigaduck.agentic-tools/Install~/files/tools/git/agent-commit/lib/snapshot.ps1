# agent-commit/lib/snapshot.ps1
# Capturing a snapshot for prepare: the listed files as a binary patch against
# HEAD on a temporary index, with version-number edits normalized.
# Loaded by agent-commit.ps1.

function New-Snapshot {
    # Capture exactly the listed files of the folder $Root (the main folder or
    # a linked worktree) as a binary patch against its HEAD, on a temporary
    # index. The version number in version.json is reset to HEAD's value
    # (the tool writes versions). Returns Problems (nothing captured when not
    # empty), Notes, Files, PatchPath (temp file, caller deletes), BaseSha, Branch.
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [string[]]$RawPaths,
        [string]$CwdBase,
        [switch]$IncludeIgnored
    )
    $result = [pscustomobject]@{
        Problems  = (New-StringList)
        Notes     = (New-StringList)
        Files     = @()
        PatchPath = $null
        BaseSha   = $null
        Branch    = ''
    }
    $head = Get-HeadCommit $Root
    if (-not $head) { $result.Problems.Add('This repository has no commits yet; make the first commit manually.'); return $result }
    $result.BaseSha = $head
    $result.Branch = Get-CurrentBranch $Root
    $entries = Get-TreeEntries $head

    $paths = [System.Collections.Generic.List[string]]::new()
    $seen = New-PathSet
    foreach ($raw in @($RawPaths)) {
        if ([string]::IsNullOrWhiteSpace($raw)) { continue }
        $resolved = Resolve-ListedPath -Raw $raw -Root $Root -CwdBase $CwdBase
        if ($resolved.Error) { $result.Problems.Add("'$raw' $($resolved.Error)."); continue }
        $path = $resolved.Path
        $full = Get-RepoFullPath $path $Root
        if ($entries.ContainsKey($path)) {
            $entry = $entries[$path]
            $path = $entry.Path
            if ($entry.Mode -eq '120000' -or $entry.Mode -eq '160000') { $result.Problems.Add("'$raw' is a symlink or submodule, which records do not support."); continue }
            if ([System.IO.Directory]::Exists($full)) { $result.Problems.Add("'$raw' is a folder; list the individual files instead."); continue }
        } elseif ([System.IO.Directory]::Exists($full)) {
            $result.Problems.Add("'$raw' is a folder; list the individual files instead.")
            continue
        } elseif ([System.IO.File]::Exists($full)) {
            $cased = Get-DiskCasedPath $Root $path
            if ($cased) { $path = $cased }
        } else {
            $result.Problems.Add("'$raw' does not exist and is not tracked by git (typo?).")
            continue
        }
        if ($seen.Add($path)) { $paths.Add($path) }
    }
    if ($paths.Count -eq 0 -and $result.Problems.Count -eq 0) { $result.Problems.Add('No files were listed.') }
    if ($result.Problems.Count -gt 0) { return $result }

    if (-not $IncludeIgnored) {
        $untracked = @($paths | Where-Object { -not $entries.ContainsKey($_) })
        $ignored = Get-IgnoredPaths $untracked $Root
        foreach ($p in $untracked) {
            if ($ignored.Contains($p)) { $result.Problems.Add("$p is ignored by .gitignore. Remove it from -Files, or pass -Force if it really must be committed.") }
        }
        if ($result.Problems.Count -gt 0) { return $result }
    }

    $tempIndex = New-TempFilePath '.index'
    $indexEnv = @{ GIT_INDEX_FILE = $tempIndex; GIT_LITERAL_PATHSPECS = '1' }
    $patchTemp = New-TempFilePath '.patch'
    try {
        [void](Invoke-Git -Arguments @('read-tree', $head) -Environment $indexEnv -WorkDir $Root)
        foreach ($chunk in (Split-ArgumentChunks $paths.ToArray())) {
            $gitArgs = @('add', '-A')
            if ($IncludeIgnored) { $gitArgs += '-f' }
            $gitArgs += '--'
            $gitArgs += $chunk
            $add = Invoke-Git -Arguments $gitArgs -Environment $indexEnv -WorkDir $Root -AllowFailure
            if ($add.ExitCode -ne 0) {
                $message = $add.Error.Trim()
                if (-not $message) { $message = $add.Output.Trim() }
                $result.Problems.Add("git could not read the listed files: $message")
                return $result
            }
        }

        foreach ($path in $paths) {
            if (-not (Test-VersionFilePath $path) -or -not $entries.ContainsKey($path)) { continue }
            $entry = Get-IndexEntry $path $indexEnv $Root
            if (-not $entry -or $entry.Oid -eq $entries[$path].Oid) { continue }
            $old = Get-BlobText $entries[$path].Oid
            $new = Get-BlobText $entry.Oid
            if (-not $old.RoundTrips -or -not $new.RoundTrips) { continue }
            $normalized = Set-VersionInText $new.Text (Get-VersionFromText $old.Text)
            if ($normalized -ceq $old.Text) {
                $result.Problems.Add("$path only has a version-number change. Remove it from -Files and set -Bump instead; the tool writes the version number.")
                continue
            }
            if ($normalized -cne $new.Text) {
                $oid = New-BlobFromText $normalized $new.Bom
                [void](Invoke-Git -Arguments @('update-index', '--cacheinfo', "$($entry.Mode),$oid,$path") -Environment $indexEnv -WorkDir $Root)
                $result.Notes.Add("$path contains a version-number edit; it was left out (the tool writes the version from -Bump).")
            }
        }
        if ($result.Problems.Count -gt 0) { return $result }

        $statusMap = New-PathMap
        $nameStatus = Invoke-Git -Arguments @('diff', '--cached', '--no-renames', '--name-status', '-z', $head) -Environment $indexEnv -WorkDir $Root
        $tokens = @(Split-Records $nameStatus.Output ([char]0))
        for ($i = 0; $i + 1 -lt $tokens.Count; $i += 2) { $statusMap[$tokens[$i + 1]] = $tokens[$i].Substring(0, 1) }
        foreach ($path in $paths) {
            if (-not $statusMap.ContainsKey($path)) { $result.Problems.Add("$path has no changes compared to the last commit. Remove it from -Files.") }
        }
        foreach ($key in @($statusMap.Keys)) {
            if (-not $seen.Contains($key)) { $result.Problems.Add("$key changed but is not listed; list the individual files.") }
        }
        if ($result.Problems.Count -gt 0) { return $result }

        $stats = New-PathMap
        $numstat = Invoke-Git -Arguments @('diff', '--cached', '--no-renames', '--numstat', '-z', $head) -Environment $indexEnv -WorkDir $Root
        foreach ($line in @(Split-Records $numstat.Output ([char]0))) {
            $parts = $line.Split([char]9)
            if ($parts.Count -ge 3) { $stats[$parts[2]] = $parts }
        }
        $indexOids = New-PathMap
        foreach ($chunk in (Split-ArgumentChunks $paths.ToArray())) {
            $ls = Invoke-Git -Arguments (@('ls-files', '-s', '-z', '--') + $chunk) -Environment $indexEnv -WorkDir $Root
            foreach ($record in @(Split-Records $ls.Output ([char]0))) {
                $tab = $record.IndexOf([char]9)
                if ($tab -lt 0) { continue }
                $indexOids[$record.Substring($tab + 1)] = $record.Substring(0, $tab).Split([char]' ')[1]
            }
        }
        $existing = @($paths | Where-Object { [System.IO.File]::Exists((Get-RepoFullPath $_ $Root)) })
        $wtOids = Get-WorkingTreeOids $existing $Root

        [void](Invoke-Git -Arguments @('diff', '--cached', '--binary', '--full-index', '--no-color', '--no-ext-diff', '--no-textconv', '--no-renames', '--src-prefix=a/', '--dst-prefix=b/', "--output=$patchTemp", $head) -Environment $indexEnv -WorkDir $Root)
        if (-not [System.IO.File]::Exists($patchTemp) -or (New-Object System.IO.FileInfo $patchTemp).Length -eq 0) {
            $result.Problems.Add('The captured diff is empty; nothing to register.')
            return $result
        }

        $files = foreach ($path in $paths) {
            $add = $null; $del = $null
            if ($stats.ContainsKey($path)) {
                if ($stats[$path][0] -ne '-') { $add = [int]$stats[$path][0] }
                if ($stats[$path][1] -ne '-') { $del = [int]$stats[$path][1] }
            }
            $oid = 'deleted'
            if ($indexOids.ContainsKey($path)) { $oid = $indexOids[$path] }
            $wtOid = 'deleted'
            if ($wtOids.ContainsKey($path)) { $wtOid = $wtOids[$path] }
            [pscustomobject]@{ path = $path; status = $statusMap[$path]; add = $add; del = $del; oid = $oid; wtOid = $wtOid }
        }
        $result.Files = @($files)
        $result.PatchPath = $patchTemp
        return $result
    } finally {
        Remove-TempIndex $tempIndex
        if (-not $result.PatchPath) { Remove-FileQuietly $patchTemp }
    }
}
