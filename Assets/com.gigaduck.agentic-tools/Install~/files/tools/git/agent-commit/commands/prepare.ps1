# agent-commit/commands/prepare.ps1
# The prepare command (agents): validate the arguments, capture and register the
# record, print READY / NOT READY.
# Loaded by agent-commit.ps1.

function Invoke-Prepare($Store, [string]$CurrentDirectory) {
    $problems = New-StringList
    $agentName = ([string]$Agent).Trim()
    if (-not $agentName) { $agentName = Get-DetectedAgentName }
    if (-not $agentName) { $problems.Add('-Agent is missing: your tool''s name, e.g. -Agent "Claude Code" (or "Kilo Code", "Antigravity", ...).') }

    $taskName = ([string]$Task).Trim()
    if (-not $taskName) {
        $problems.Add('-Task is missing: a short slug naming this task, e.g. -Task wire-joint-snapping. Prepare the same work again with the same -Task and it replaces your pending record.')
    } elseif ($taskName -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,59}$') {
        $problems.Add("-Task '$taskName' must be a slug: letters, digits, '.', '_' or '-', at most 60 characters.")
    }

    $titleText = ([string]$Title).Trim()
    if (-not $titleText) { $problems.Add('-Title is missing: the commit title, one line in imperative mood, under 72 characters.') }
    elseif ($titleText -match '[\r\n]') { $problems.Add('-Title must be a single line; put details in -Description.') }
    elseif ($titleText.Length -gt 100) { $problems.Add("-Title is $($titleText.Length) characters; keep it under 72.") }

    $bumpKind = ([string]$Bump).Trim().ToLowerInvariant()
    if (-not $bumpKind) { $problems.Add('-Bump is missing: patch (bug fix / small improvement), minor (new feature) or none (docs, formatting, refactor only).') }
    elseif ($script:BumpKinds -notcontains $bumpKind) { $problems.Add("-Bump '$Bump' must be patch, minor or none.") }

    $body = ''
    if ($DescriptionFile) {
        if ($Description) { $problems.Add('Use -Description or -DescriptionFile, not both.') }
        $descPath = $DescriptionFile
        if (-not [System.IO.Path]::IsPathRooted($descPath)) { $descPath = [System.IO.Path]::Combine($CurrentDirectory, $descPath) }
        if ([System.IO.File]::Exists($descPath)) { $body = [System.IO.File]::ReadAllText($descPath) }
        else { $problems.Add("-DescriptionFile not found: $DescriptionFile") }
    } elseif ($Description) {
        $body = $Description
    }
    $body = ($body -replace "`r`n?", "`n").Trim()
    if (-not $body -and -not ($DescriptionFile -and $problems.Count -gt 0)) {
        $problems.Add('-Description is missing: what changed and why, user-visible behavior, and how you verified it (or -DescriptionFile <file>).')
    }
    $verification = @(Split-ListArgument $VerificationReport)
    if ($verification.Count -gt 0) {
        $summaries = New-StringList
        foreach ($receiptFile in $verification) {
            try {
                $path = if ([System.IO.Path]::IsPathRooted($receiptFile)) { $receiptFile } else { [System.IO.Path]::Combine($CurrentDirectory, $receiptFile) }
                $receipt = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json -ErrorAction Stop
                if ($receipt.runId -notmatch '^[0-9a-f]{32}$' -or $receipt.result -notin @('PASS', 'FAIL', 'ABORTED', 'TIMEOUT') -or
                    $receipt.summary -notmatch '^RESULT: ' -or $receipt.summary -match '[\r\n]' -or $receipt.summary.Length -gt 500) {
                    throw 'Expected a completed AutoPlaytest result.json.'
                }
                $summaries.Add($receipt.summary)
            } catch {
                $problems.Add("Invalid verification report '$receiptFile': $($_.Exception.Message)")
            }
        }
        if ($summaries.Count -gt 0) { $body += "`n`nVerification receipts`n" + ($summaries -join "`n") }
    }

    $listed = @(Split-ListArgument $Files)
    if ($listed.Count -eq 0) { $problems.Add('-Files is missing: every file you created, edited or deleted in this task, comma-separated.') }

    if ($problems.Count -eq 0) {
        $snapshot = New-Snapshot -Root $script:RepoRoot -RawPaths $listed -CwdBase $CurrentDirectory -IncludeIgnored:$Force
        foreach ($p in $snapshot.Problems) { $problems.Add($p) }
    }
    if ($problems.Count -gt 0) {
        Write-Out 'NOT READY  Nothing was registered. Fix these problems, then run the same command again:' 'Red'
        foreach ($p in $problems) { Write-Out "  - $p" 'Yellow' }
        $script:ExitCode = 1
        return
    }

    try {
        $registered = Register-Record -Store $Store -Snapshot $snapshot -Agent $agentName -Task $taskName -Title $titleText -Description $body -Bump $bumpKind -Root $script:RepoRoot -ReplaceId $Replace
    } finally {
        Remove-FileQuietly $snapshot.PatchPath
    }
    $record = $registered.Record

    $headline = "READY  Record #$($record.id) registered"
    if ($registered.Replaced) { $headline = "READY  Record #$($record.id) updated (replaces your earlier version of this task)" }
    Write-Out $headline 'Green'
    Write-Out "  title:  $($record.title)"
    Write-Out "  agent:  $($record.agent)   task: $($record.task)   bump: $($record.bump)"
    Write-Out "  files:  $(@($record.files).Count)  ($(Get-RecordStats $record))"
    foreach ($file in @($record.files)) {
        $stats = Get-FileStats $file
        if ($stats) { $stats = "  ($stats)" }
        Write-Out "    $($file.status) $($file.path)$stats"
    }
    $branchText = $record.baseBranch
    if (-not $branchText) { $branchText = '(detached)' }
    Write-Out "  base:   $branchText @ $($record.baseSha.Substring(0, 8))   folder: $($script:RepoRoot)"

    $notes = New-StringList
    foreach ($n in $snapshot.Notes) { $notes.Add($n) }
    if ($record.title.Length -gt 72) { $notes.Add("The title is $($record.title.Length) characters; under 72 reads best in git log.") }
    $pending = @(Get-PendingRecords $Store)
    foreach ($overlap in @(Get-OverlappingRecords $record $pending)) {
        $other = $overlap.Record
        $notes.Add("$($overlap.Paths -join ', ') is also in pending record #$($other.id) ($($other.agent): $($other.title)). Tell the user both records touch it.")
    }
    if ($notes.Count -gt 0) {
        Write-Out 'Notes:' 'Yellow'
        foreach ($n in $notes) { Write-Out "  - $n" 'Yellow' }
    }

    $unclaimed = @(Get-UnclaimedChanges $pending $script:RepoRoot)
    if ($unclaimed.Count -gt 0) {
        Write-Out ''
        Write-Out 'Other changed files in this folder that no pending record lists (they may belong to other agents).'
        Write-Out 'Add one to -Files ONLY if you created, edited or deleted it during this task:'
        foreach ($c in ($unclaimed | Select-Object -First 25)) { Write-Out "    $($c.Code.Trim()) $($c.Path)" }
        if ($unclaimed.Count -gt 25) { Write-Out "    ... and $($unclaimed.Count - 25) more" }
    }
    Write-Out ''
    Write-Out 'Do not commit, stage or push. The user tests this and commits it with virtual-commit.bat.'
    Write-Out "If you change these files again, run the same command again (same -Agent and -Task); it updates record #$($record.id)."
}
