# agent-commit/ui/console.ps1
# Console primitives: width-clipped colored rows, word wrap, clearing, key
# tokens (also from redirected input) and the cursor.
# Loaded by agent-commit.ps1.

$script:InputIsConsole = -not [Console]::IsInputRedirected
$script:OutputIsConsole = -not [Console]::IsOutputRedirected

function Get-UiWidth {
    try { $w = [Console]::WindowWidth; if ($w -ge 40) { return $w } } catch { }
    return 120
}

function Write-Parts {
    # Write-Parts @('text', 'Color', 'more text', 'Color', ...) on one line,
    # cut at the window width so rows never wrap.
    param([object[]]$Parts)
    $room = (Get-UiWidth) - 1
    for ($i = 0; $i -lt $Parts.Count; $i += 2) {
        if ($room -le 0) { break }
        $text = [string]$Parts[$i]
        $color = ''
        if ($i + 1 -lt $Parts.Count) { $color = [string]$Parts[$i + 1] }
        if ($text.Length -gt $room) { $text = $text.Substring(0, [Math]::Max(0, $room - 3)) + '...' }
        if ($color) { Write-Host $text -ForegroundColor $color -NoNewline } else { Write-Host $text -NoNewline }
        $room -= $text.Length
    }
    Write-Host ''
}

function Write-Line([string]$Text = '', [string]$Color = '') {
    Write-Parts @($Text, $Color)
}

function Write-Wrapped([string]$Text, [string]$Color = '', [string]$Indent = '') {
    # Word-wrapped output for long messages (warnings, descriptions).
    $width = [Math]::Max(30, (Get-UiWidth) - 1 - $Indent.Length)
    foreach ($paragraph in ($Text -split "`r?`n")) {
        $line = ''
        foreach ($word in ($paragraph -split ' ')) {
            if ($line.Length -gt 0 -and ($line.Length + 1 + $word.Length) -gt $width) {
                if ($Color) { Write-Host ($Indent + $line) -ForegroundColor $Color } else { Write-Host ($Indent + $line) }
                $line = $word
            } elseif ($line.Length -gt 0) {
                $line += ' ' + $word
            } else {
                $line = $word
            }
        }
        if ($Color) { Write-Host ($Indent + $line) -ForegroundColor $Color } else { Write-Host ($Indent + $line) }
    }
}

function Clear-Ui {
    if ($script:OutputIsConsole) {
        try { [Console]::Clear(); return } catch { }
    }
    Write-Host ''
    Write-Host ('=' * 60)
}

function Read-UiKey {
    # One action token per key press. With redirected input (scripted runs)
    # each input line is one token, e.g. "down", "space", "enter", "y".
    if ($script:InputIsConsole) {
        $key = [Console]::ReadKey($true)
        switch ($key.Key) {
            'UpArrow' { return 'up' }
            'DownArrow' { return 'down' }
            'Spacebar' { return 'space' }
            'Enter' { return 'enter' }
            'Escape' { return 'esc' }
            'Home' { return 'home' }
            'End' { return 'end' }
        }
        return ([string]$key.KeyChar).ToLowerInvariant()
    }
    $line = [Console]::In.ReadLine()
    if ($null -eq $line) { return 'eof' }
    return $line.Trim().ToLowerInvariant()
}

function Read-Choice([string[]]$Allowed) {
    while ($true) {
        $key = Read-UiKey
        if ($key -eq 'eof') { return 'eof' }
        if ($Allowed -contains $key) { return $key }
    }
}

function Wait-AnyKey([string]$Prompt = 'Press any key to continue...') {
    Write-Host ''
    Write-Host $Prompt -ForegroundColor DarkGray
    [void](Read-UiKey)
}

function Set-CursorVisible([bool]$Visible) {
    if (-not $script:OutputIsConsole) { return }
    try { [Console]::CursorVisible = $Visible } catch { }
}
