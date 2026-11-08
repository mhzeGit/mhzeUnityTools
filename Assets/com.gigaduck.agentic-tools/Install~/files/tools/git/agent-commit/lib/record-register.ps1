# agent-commit/lib/record-register.ps1
# Changing records under the store lock: guarded field updates, storing a
# new snapshot, and registering (or replacing) a pending record.
# Loaded by agent-commit.ps1.

function Update-RecordFields {
    # Change a pending record under the store lock, only if nobody changed it
    # since $ExpectedRevision. Returns an error message or $null.
    param($Store, [int]$Id, [int]$ExpectedRevision, [scriptblock]$Change)
    Enter-StoreLock $Store
    try {
        $fresh = Get-RecordById $Store $Id
        if (-not $fresh -or $fresh.status -ne 'pending') { return "Record #$Id is no longer pending." }
        if ($ExpectedRevision -gt 0 -and $fresh.revision -ne $ExpectedRevision) {
            return "Record #$Id was changed meanwhile (an agent re-prepared it?). Reload and try again."
        }
        & $Change $fresh
        $fresh.revision++
        Save-Record $Store $fresh
        return $null
    } finally {
        Exit-StoreLock $Store
    }
}

function Set-RecordSnapshot($Store, $Record, $Snapshot) {
    # Store a captured snapshot as the record's new patch (caller holds lock).
    [void][System.IO.Directory]::CreateDirectory($Store.Patches)
    $oldPatch = $Record.patch
    $patchName = '{0:D4}-r{1}.patch' -f $Record.id, ($Record.revision + 1)
    [System.IO.File]::Copy($Snapshot.PatchPath, [System.IO.Path]::Combine($Store.Patches, $patchName), $true)
    $Record.patch = $patchName
    $Record.files = @($Snapshot.Files)
    $Record.baseSha = $Snapshot.BaseSha
    $Record.baseBranch = $Snapshot.Branch
    $Record.updatedAt = Get-IsoNow
    return $oldPatch
}

function Register-Record {
    # Save a snapshot as a pending record. A pending record with the same
    # agent + task + folder (or -Replace id) is replaced and keeps its id.
    param($Store, $Snapshot, [string]$Agent, [string]$Task, [string]$Title, [string]$Description, [string]$Bump, [string]$Root, [int]$ReplaceId)
    Enter-StoreLock $Store
    try {
        $all = @(Get-AllRecords $Store)
        $existing = $null
        if ($ReplaceId -gt 0) {
            $existing = $all | Where-Object { $_.id -eq $ReplaceId } | Select-Object -First 1
            if (-not $existing) { throw "Record #$ReplaceId was not found." }
            if ($existing.status -ne 'pending') { throw "Record #$ReplaceId is already $($existing.status); register a new record instead (drop -Replace)." }
        } else {
            $existing = $all | Where-Object {
                $_.status -eq 'pending' -and
                [string]::Equals($_.agent, $Agent, [System.StringComparison]::OrdinalIgnoreCase) -and
                [string]::Equals($_.task, $Task, [System.StringComparison]::OrdinalIgnoreCase) -and
                (Test-SamePath $_.worktree $Root)
            } | Select-Object -First 1
        }
        if ($existing) {
            $record = $existing
        } else {
            $now = Get-IsoNow
            $record = ConvertTo-Record ([pscustomobject]@{ id = (Get-NextRecordId $Store); revision = 0; createdAt = $now; updatedAt = $now; status = 'pending' })
        }
        $record.agent = $Agent
        $record.task = $Task
        $record.title = $Title
        $record.description = $Description
        $record.bump = $Bump
        $record.worktree = $Root
        $oldPatch = Set-RecordSnapshot $Store $record $Snapshot
        $record.revision++
        Save-Record $Store $record
        if ($oldPatch -and $oldPatch -ne $record.patch) { Remove-FileQuietly ([System.IO.Path]::Combine($Store.Patches, $oldPatch)) }
        return [pscustomobject]@{ Record = $record; Replaced = [bool]$existing }
    } finally {
        Exit-StoreLock $Store
    }
}
