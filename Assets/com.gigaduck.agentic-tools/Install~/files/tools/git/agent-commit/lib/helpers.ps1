# agent-commit/lib/helpers.ps1
# Small helpers: case-insensitive path sets/maps, temp files, record
# splitting, unquoting and path comparison.
# Loaded by agent-commit.ps1.

function New-PathSet {
    return , (New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase))
}

function New-PathMap {
    return , (New-Object 'System.Collections.Generic.Dictionary[string,object]' ([System.StringComparer]::OrdinalIgnoreCase))
}

function New-StringList {
    return , ([System.Collections.Generic.List[string]]::new())
}

function New-TempFilePath([string]$Extension) {
    return [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), 'agent-commit-' + [guid]::NewGuid().ToString('N') + $Extension)
}

function Remove-FileQuietly([string]$Path) {
    if (-not $Path) { return }
    try { if ([System.IO.File]::Exists($Path)) { [System.IO.File]::Delete($Path) } } catch { }
}

function Remove-TempIndex([string]$Path) {
    Remove-FileQuietly $Path
    Remove-FileQuietly ($Path + '.lock')
}

function Split-Records([string]$Text, [char]$Separator) {
    if ([string]::IsNullOrEmpty($Text)) { return @() }
    return $Text.Split([char[]]@($Separator), [System.StringSplitOptions]::RemoveEmptyEntries)
}

function Remove-Quotes([string]$Value) {
    $v = $Value.Trim()
    if ($v.Length -ge 2) {
        $q = $v[0]
        if (($q -eq [char]'"' -or $q -eq [char]"'") -and $v[$v.Length - 1] -eq $q) { return $v.Substring(1, $v.Length - 2) }
    }
    return $v
}

function Test-SamePath([string]$A, [string]$B) {
    if (-not $A -or -not $B) { return $false }
    try {
        $x = [System.IO.Path]::GetFullPath($A).TrimEnd('\', '/')
        $y = [System.IO.Path]::GetFullPath($B).TrimEnd('\', '/')
    } catch { return $false }
    return [string]::Equals($x, $y, [System.StringComparison]::OrdinalIgnoreCase)
}
