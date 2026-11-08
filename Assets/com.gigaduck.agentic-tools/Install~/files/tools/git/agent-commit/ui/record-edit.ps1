# agent-commit/ui/record-edit.ps1
# Editing a record: title/description in Notepad (E) and cycling the bump (B).
# Loaded by agent-commit.ps1.

function ConvertFrom-MessageText([string]$Text) {
    # First non-empty line = title, everything after it = description.
    $lines = ($Text -replace "`r`n?", "`n").Split([char]10)
    $i = 0
    while ($i -lt $lines.Length -and -not $lines[$i].Trim()) { $i++ }
    if ($i -ge $lines.Length) { return $null }
    $description = ''
    if ($i + 1 -lt $lines.Length) { $description = (@($lines[($i + 1)..($lines.Length - 1)] | ForEach-Object { $_.TrimEnd() }) -join "`n").Trim() }
    return [pscustomobject]@{ Title = $lines[$i].Trim(); Description = $description }
}

function Edit-RecordMessage($State, $Record) {
    $temp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "agent-commit-record-$($Record.id).txt")
    $text = $Record.title + "`r`n`r`n" + ($Record.description -replace "`r?`n", "`r`n") + "`r`n"
    [System.IO.File]::WriteAllText($temp, $text, $script:Utf8NoBom)
    try {
        if ($script:InputIsConsole) { Start-Process -FilePath 'notepad.exe' -ArgumentList (ConvertTo-NativeArgument $temp) }
        Write-Host ''
        Write-Line "  Opened the message of #$($Record.id) in Notepad: first line = title, the rest = description." 'Cyan'
        Wait-AnyKey 'Save the file in Notepad, then press any key here...'
        $parsed = ConvertFrom-MessageText ([System.IO.File]::ReadAllText($temp, $script:Utf8NoBom))
    } finally {
        Remove-FileQuietly $temp
    }
    if (-not $parsed -or -not $parsed.Title) { return 'Not saved: the title (first line) was empty.' }
    if ($parsed.Title -ceq $Record.title -and $parsed.Description -ceq $Record.description.Trim()) { return 'The message was not changed.' }
    $newTitle = $parsed.Title
    $newDescription = $parsed.Description
    $problem = Update-RecordFields -Store $State.Store -Id $Record.id -ExpectedRevision $Record.revision -Change {
        param($r)
        $r.title = $newTitle
        $r.description = $newDescription
    }
    if ($problem) { return $problem }
    return "Saved the new message of #$($Record.id)."
}

function Step-RecordBump($State, $Record) {
    $next = @{ none = 'patch'; patch = 'minor'; minor = 'none' }[$Record.bump]
    if (-not $next) { $next = 'none' }
    $problem = Update-RecordFields -Store $State.Store -Id $Record.id -ExpectedRevision $Record.revision -Change {
        param($r)
        $r.bump = $next
    }
    if ($problem) { return $problem }
    return "#$($Record.id) bump is now $next."
}
