# agent-commit/lib/snapshot-paths.ps1
# Normalizing -Files entries to repository-relative paths and recovering the
# on-disk casing of a path.
# Loaded by agent-commit.ps1.

function Resolve-ListedPath([string]$Raw, [string]$Root, [string]$CwdBase) {
    # Normalize one -Files entry to a repository-relative path.
    $p = (Remove-Quotes $Raw).Replace('\', '/')
    if ($p -match '^/([A-Za-z])/(.*)$') { $p = $Matches[1] + ':/' + $Matches[2] }
    $rootSlash = $Root.Replace('\', '/').TrimEnd('/') + '/'
    if ($p -match '^[A-Za-z]:/') {
        if (-not $p.StartsWith($rootSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{ Path = $null; Error = 'is outside this repository folder' }
        }
        $p = $p.Substring($rootSlash.Length)
    } elseif ($CwdBase -and -not (Test-SamePath $CwdBase $Root) -and $p -notmatch '(^|/)\.\.(/|$)') {
        # Run from a subfolder: accept paths relative to it when they only exist there.
        $fromRoot = Get-RepoFullPath $p $Root
        $fromCwd = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($CwdBase, $p.Replace('/', '\')))
        $existsAtRoot = [System.IO.File]::Exists($fromRoot) -or [System.IO.Directory]::Exists($fromRoot)
        $existsAtCwd = [System.IO.File]::Exists($fromCwd) -or [System.IO.Directory]::Exists($fromCwd)
        $cwdSlash = $fromCwd.Replace('\', '/')
        if (-not $existsAtRoot -and $existsAtCwd -and $cwdSlash.StartsWith($rootSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
            $p = $cwdSlash.Substring($rootSlash.Length)
        }
    }
    $p = $p -replace '/{2,}', '/'
    while ($p.StartsWith('./')) { $p = $p.Substring(2) }
    if (-not $p) { return [pscustomobject]@{ Path = $null; Error = 'is an empty path' } }
    if ($p.EndsWith('/')) { return [pscustomobject]@{ Path = $null; Error = 'is a folder; list the individual files instead' } }
    foreach ($segment in $p.Split([char]'/')) {
        if ($segment -eq '..' -or $segment -eq '.' -or $segment -eq '') {
            return [pscustomobject]@{ Path = $null; Error = "must be a plain repository-relative path (no '..')" }
        }
    }
    if ($p -eq '.git' -or $p.StartsWith('.git/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return [pscustomobject]@{ Path = $null; Error = 'is inside .git' }
    }
    if ($p.IndexOfAny([char[]]@('*', '?')) -ge 0) {
        return [pscustomobject]@{ Path = $null; Error = 'contains a wildcard; list the individual files instead' }
    }
    return [pscustomobject]@{ Path = $p; Error = $null }
}

function Get-DiskCasedPath([string]$Root, [string]$RelativePath) {
    # Real on-disk casing for a path typed with the wrong case (Windows is
    # case-insensitive, git is not).
    $current = $Root
    $parts = [System.Collections.Generic.List[string]]::new()
    foreach ($segment in $RelativePath.Split([char]'/')) {
        $found = $null
        try { $found = @([System.IO.Directory]::GetFileSystemEntries($current, $segment)) | Select-Object -First 1 } catch { return $null }
        if (-not $found) { return $null }
        $parts.Add([System.IO.Path]::GetFileName($found))
        $current = $found
    }
    return ($parts -join '/')
}
