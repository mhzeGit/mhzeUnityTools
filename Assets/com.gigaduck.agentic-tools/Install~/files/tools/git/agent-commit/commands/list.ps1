# agent-commit/commands/list.ps1
# The list command: print pending (or all) records.
# Loaded by agent-commit.ps1.

function Invoke-List($Store) {
    $records = @(Get-PendingRecords $Store)
    if ($All) { $records = @(Get-AllRecords $Store) }
    if ($records.Count -eq 0) {
        if ($All) { Write-Out 'No records.' } else { Write-Out 'No pending records.' }
        return
    }
    foreach ($record in $records) {
        $state = $record.status
        if ($record.status -eq 'applied') {
            $short = 'no commit'
            if ($record.commit) { $short = $record.commit.Substring(0, 7) }
            if ($record.pushed) { $state = "committed $short, pushed" } else { $state = "committed $short, not pushed" }
        }
        Write-Parts @(('  {0,-5} ' -f ('#' + $record.id)), 'Cyan', $record.title, 'White')
        $line = "        $state | $($record.agent) | $($record.task) | $(@($record.files).Count) file(s) $(Get-RecordStats $record) | bump $($record.bump) | $(Format-Age $record.updatedAt)"
        if (-not (Test-SamePath $record.worktree $script:RepoRoot)) { $line += " | worktree $(Get-WorktreeLabel $record)" }
        Write-Parts @($line, 'DarkGray')
    }
}
