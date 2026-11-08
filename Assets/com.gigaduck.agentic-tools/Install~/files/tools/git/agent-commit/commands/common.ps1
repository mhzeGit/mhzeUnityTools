# agent-commit/commands/common.ps1
# Helpers shared by the commands: plain output, list arguments, agent name
# detection and resolving -Ids.
# Loaded by agent-commit.ps1.

function Write-Out([string]$Text = '', [string]$Color = '') {
    if ($Color) { Write-Host $Text -ForegroundColor $Color } else { Write-Host $Text }
}

function Split-ListArgument([string[]]$Values) {
    # "-Files a,b" arrives as one string through powershell -File; split it.
    foreach ($value in @($Values)) {
        foreach ($part in ([string]$value).Split([char]',')) {
            if ($part.Trim()) { $part.Trim() }
        }
    }
}

function Get-DetectedAgentName {
    if ($env:CLAUDECODE) { return 'Claude Code' }
    return $null
}

function Resolve-RecordIds($Store, [string[]]$Values) {
    $ids = @(Split-ListArgument $Values)
    if ($ids.Count -eq 0) { throw 'Missing -Ids (run "list" to see the record numbers).' }
    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($token in $ids) {
        $t = $token.TrimStart('#')
        $n = 0
        if (-not [int]::TryParse($t, [ref]$n)) { throw "Cannot read record id '$token'." }
        $record = Get-RecordById $Store $n
        if (-not $record) { throw "Record #$n was not found." }
        if ($record.status -ne 'pending') { throw "Record #$n is already $($record.status)." }
        if (-not ($records | Where-Object { $_.id -eq $n })) { $records.Add($record) }
    }
    # Commit in capture order, whatever order they were typed in.
    return @($records | Sort-Object -Property @{ Expression = { $_.updatedAt } }, @{ Expression = { $_.id } })
}
