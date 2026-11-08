# agent-commit/lib/record-files.ps1
# Reading, listing, ordering and saving record files and their patch paths.
# Loaded by agent-commit.ps1.

function Read-RecordFile([string]$Path) {
    try {
        $record = ConvertTo-Record ([System.IO.File]::ReadAllText($Path, $script:Utf8NoBom) | ConvertFrom-Json)
        if ($record.id -le 0) { return $null }
        return $record
    } catch {
        return $null
    }
}

function Get-RecordFilePath($Store, [int]$Id) {
    return [System.IO.Path]::Combine($Store.Records, ('{0:D4}.json' -f $Id))
}

function Get-PatchFilePath($Store, $Record) {
    if (-not $Record.patch) { return $null }
    return [System.IO.Path]::Combine($Store.Patches, $Record.patch)
}

function Get-AllRecords($Store) {
    if (-not [System.IO.Directory]::Exists($Store.Records)) { return @() }
    $records = @(foreach ($file in [System.IO.Directory]::GetFiles($Store.Records, '*.json')) {
        $record = Read-RecordFile $file
        if ($record) { $record }
    })
    return @($records | Sort-Object -Property id)
}

function Get-PendingRecords($Store) {
    # Pending records in commit order: oldest capture first, so a record whose
    # edits were already on disk when a later record was captured comes first.
    $pending = @(Get-AllRecords $Store | Where-Object { $_.status -eq 'pending' })
    return @($pending | Sort-Object -Property @{ Expression = { $_.updatedAt } }, @{ Expression = { $_.id } })
}

function Get-RecordById($Store, [int]$Id) {
    $path = Get-RecordFilePath $Store $Id
    if (-not [System.IO.File]::Exists($path)) { return $null }
    return Read-RecordFile $path
}

function Save-Record($Store, $Record) {
    [void][System.IO.Directory]::CreateDirectory($Store.Records)
    $dest = Get-RecordFilePath $Store $Record.id
    $temp = [System.IO.Path]::Combine($Store.Records, '.tmp-' + [guid]::NewGuid().ToString('N'))
    [System.IO.File]::WriteAllText($temp, ($Record | ConvertTo-Json -Depth 6), $script:Utf8NoBom)
    try {
        if ([System.IO.File]::Exists($dest)) { [System.IO.File]::Replace($temp, $dest, $null) }
        else { [System.IO.File]::Move($temp, $dest) }
    } catch {
        [System.IO.File]::Copy($temp, $dest, $true)
        Remove-FileQuietly $temp
    }
}

function Get-NextRecordId($Store) {
    # Records are never deleted (discarded ones stay as history), so ids are
    # never reused.
    $max = 0
    if ([System.IO.Directory]::Exists($Store.Records)) {
        foreach ($file in [System.IO.Directory]::GetFiles($Store.Records, '*.json')) {
            $n = 0
            if ([int]::TryParse([System.IO.Path]::GetFileNameWithoutExtension($file), [ref]$n) -and $n -gt $max) { $max = $n }
        }
    }
    return $max + 1
}
