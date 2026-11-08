# agent-commit/lib/commit-plan.ps1
# Commit messages and the commit plan: one commit object per record chained on
# HEAD, without moving any ref.
# Loaded by agent-commit.ps1.

function Get-CommitMessage($Record) {
    $message = $Record.title.Trim()
    $body = $Record.description.Trim()
    if ($body) { $message += "`n`n" + ($body -replace "`r`n?", "`n") }
    if ($Record.agent.Trim()) { $message += "`n`nAgent: " + $Record.agent.Trim() }
    return $message + "`n"
}

function New-CommitPlan {
    # Chain one commit per record (in the given order) on top of HEAD.
    # Records that conflict are skipped and stay pending. The commits exist
    # only as objects until Complete-CommitPlan moves the branch.
    param($Store, [object[]]$Records)
    $head = Get-HeadCommit
    if (-not $head) { throw 'This repository has no commits yet; make the first commit manually.' }
    $plan = [pscustomobject]@{
        Head         = $head
        Final        = $head
        Branch       = (Get-CurrentBranch)
        Steps        = [System.Collections.Generic.List[object]]::new()
        CommitCount  = 0
        StartVersion = (Get-VersionFromEntries (Get-TreeEntries $head))
        EndVersion   = $null
    }
    $tempIndex = New-TempFilePath '.index'
    try {
        $parent = $head
        foreach ($record in @($Records)) {
            $tree = New-RecordTree -Store $Store -Record $record -Parent $parent -TempIndex $tempIndex
            $step = [pscustomobject]@{ Record = $record; Revision = $record.revision; Parent = $parent; Tree = $tree; Commit = $null; Empty = $false }
            if (-not $tree.Conflict) {
                if (-not $tree.Tree) {
                    $step.Empty = $true
                } else {
                    $messageFile = New-TempFilePath '.msg'
                    try {
                        [System.IO.File]::WriteAllBytes($messageFile, $script:Utf8NoBom.GetBytes((Get-CommitMessage $record)))
                        $step.Commit = (Invoke-Git -Arguments @('commit-tree', $tree.Tree, '-p', $parent, '-F', $messageFile)).Output.Trim()
                    } finally {
                        Remove-FileQuietly $messageFile
                    }
                    $parent = $step.Commit
                    $plan.CommitCount++
                }
            }
            $plan.Steps.Add($step)
        }
        $plan.Final = $parent
        $plan.EndVersion = Get-VersionFromEntries (Get-TreeEntries $parent)
    } finally {
        Remove-TempIndex $tempIndex
    }
    return $plan
}
