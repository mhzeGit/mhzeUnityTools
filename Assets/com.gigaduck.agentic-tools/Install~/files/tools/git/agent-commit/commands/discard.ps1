# agent-commit/commands/discard.ps1
# The discard command: drop pending records (files are not changed).
# Loaded by agent-commit.ps1.

function Invoke-DiscardCommand($Store) {
    foreach ($record in @(Resolve-RecordIds $Store $Ids)) {
        $problem = Update-RecordFields -Store $Store -Id $record.id -ExpectedRevision 0 -Change {
            param($r)
            $r.status = 'discarded'
            $r.discardedAt = Get-IsoNow
        }
        if ($problem) { Write-Out $problem 'Red'; $script:ExitCode = 1 }
        else {
            Remove-RecordPatch $Store $record
            Write-Out "Discarded #$($record.id). The files themselves are not changed." 'Green'
        }
    }
}
