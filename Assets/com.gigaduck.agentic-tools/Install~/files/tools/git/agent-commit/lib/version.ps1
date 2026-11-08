# agent-commit/lib/version.ps1
# The tool-owned version file: the one small JSON file in the repository root
# whose "version" field this tool bumps for every record that declares
# -Bump patch|minor. The developer's build reads that file; the tool never
# writes version numbers into any other project file.
# Loaded by agent-commit.ps1.

# The version file is relative to the repository root. It is the only file
# whose version number the tool owns; agents must never edit it (they declare
# -Bump instead). A repository without this file simply gets no version bump.
$script:VersionFileName = 'version.json'
$script:VersionKey = 'version'

function Test-VersionFilePath([string]$Path) {
    # True when a repository-relative path is the tool-owned version file.
    if (-not $Path) { return $false }
    return [string]::Equals(($Path -replace '\\', '/'), $script:VersionFileName, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-VersionFromText([string]$Text) {
    # The "version" value of the version JSON, or $null when absent.
    if (-not $Text) { return $null }
    $m = [regex]::Match($Text, '"' + [regex]::Escape($script:VersionKey) + '"\s*:\s*"([^"]+)"')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Set-VersionInText([string]$Text, [string]$Version) {
    # Replace the "version" value and keep the rest of the document byte for
    # byte. An empty document becomes a minimal one; a document without a
    # "version" key is returned unchanged (nothing safe to rewrite).
    if (-not $Version) { return $Text }
    $pattern = '("' + [regex]::Escape($script:VersionKey) + '"\s*:\s*")([^"]*)(")'
    if ([regex]::IsMatch($Text, $pattern)) {
        return [regex]::Replace($Text, $pattern, '${1}' + $Version + '${3}', 1)
    }
    if ([string]::IsNullOrWhiteSpace($Text)) { return "{`n  `"$($script:VersionKey)`": `"$Version`"`n}`n" }
    return $Text
}

function Get-VersionFromEntries($Entries) {
    # The version recorded in the version file of a commit tree, or $null.
    if (-not $Entries) { return $null }
    foreach ($key in @($Entries.Keys)) {
        if (Test-VersionFilePath $key) { return Get-VersionFromText (Get-BlobText $Entries[$key].Oid).Text }
    }
    return $null
}

function Step-Version([string]$Version, [string]$Bump) {
    if (-not $Version) { return $Version }
    $p = @($Version.Split([char]'.') | ForEach-Object { [int]$_ })
    switch ($Bump) {
        'minor' { if ($p.Count -ge 2) { return "$($p[0]).$($p[1] + 1).0" } }
        'patch' { if ($p.Count -ge 3) { return "$($p[0]).$($p[1]).$($p[2] + 1)" } }
    }
    return $Version
}

function Test-VersionOnlyChange([string]$Path, [string]$OldOid, [string]$NewOid) {
    # True when two versions of the version file differ only in the version.
    if (-not (Test-VersionFilePath $Path) -or -not $OldOid -or -not $NewOid -or $OldOid -eq $NewOid) { return $false }
    $old = Get-BlobText $OldOid
    $new = Get-BlobText $NewOid
    if (-not $old.RoundTrips -or -not $new.RoundTrips) { return $false }
    return (Set-VersionInText $new.Text (Get-VersionFromText $old.Text)) -ceq $old.Text
}
