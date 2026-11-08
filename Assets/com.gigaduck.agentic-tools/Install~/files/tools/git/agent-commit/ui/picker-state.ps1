# agent-commit/ui/picker-state.ps1
# The picker state: loading records, drift, overlaps, remote and version, and
# the one-line summary of a record.
# Loaded by agent-commit.ps1.

function New-UiState($Store) {
    return [pscustomobject]@{
        Store        = $Store
        Records      = @()
        Cursor       = 0
        Selected     = (New-Object 'System.Collections.Generic.HashSet[int]')
        Drift        = @{}
        Overlaps     = @{}
        Unclaimed    = @()
        Remote       = $null
        Head         = $null
        Version      = $null
        Legacy       = 0
        Message      = ''
        MessageColor = 'Yellow'
    }
}

function Update-UiState($State) {
    $script:TreeCache = @{}
    $records = @(Get-PendingRecords $State.Store)
    $State.Records = $records
    $State.Drift = @{}
    $State.Overlaps = @{}
    foreach ($record in $records) {
        $State.Drift[$record.id] = Get-RecordDrift $record
        $State.Overlaps[$record.id] = @(Get-OverlappingRecords $record $records)
    }
    foreach ($id in @($State.Selected)) {
        if (-not ($records | Where-Object { $_.id -eq $id })) { [void]$State.Selected.Remove($id) }
    }
    if ($State.Cursor -ge $records.Count) { $State.Cursor = [Math]::Max(0, $records.Count - 1) }
    $State.Unclaimed = @(Get-UnclaimedChanges $records $script:RepoRoot)
    $State.Remote = Get-RemoteInfo
    $State.Head = Get-HeadCommit
    $State.Version = $null
    if ($State.Head) { $State.Version = Get-VersionFromEntries (Get-TreeEntries $State.Head) }
    $State.Legacy = Get-LegacyChangeSetCount
}

function Get-RecordSummary($State, $Record) {
    # One-line status of a record for the list: text and color.
    $parts = New-StringList
    $parts.Add($Record.agent)
    $parts.Add($Record.task)
    $parts.Add("$(@($Record.files).Count) file(s) $(Get-RecordStats $Record)")
    $parts.Add("bump $($Record.bump)")
    $age = Format-Age $Record.updatedAt
    if ($age) { $parts.Add($age) }
    if (-not (Test-SamePath $Record.worktree $script:RepoRoot)) { $parts.Add("worktree $(Get-WorktreeLabel $Record)") }
    $flags = New-StringList
    $drift = $State.Drift[$Record.id]
    if ($drift -and -not $drift.Known) { $flags.Add('its folder no longer exists') }
    elseif ($drift -and $drift.Paths.Count -gt 0) { $flags.Add("$($drift.Paths.Count) file(s) changed after prepare") }
    $overlaps = @()
    if ($State.Overlaps.ContainsKey($Record.id)) { $overlaps = @($State.Overlaps[$Record.id]) }
    if ($overlaps.Count -gt 0) { $flags.Add('shares files with ' + ((@($overlaps | ForEach-Object { '#' + $_.Record.id })) -join ', ')) }
    $text = $parts -join ' | '
    if ($flags.Count -gt 0) { return @("$text | ! $($flags -join ', ')", 'Yellow') }
    return @($text, 'DarkGray')
}
