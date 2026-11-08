# agent-commit/ui/record-text.ps1
# Short +add/-del statistics text for a record and for one file.
# Loaded by agent-commit.ps1.

function Get-RecordStats($Record) {
    $add = 0; $del = 0; $binary = 0
    foreach ($f in @($Record.files)) {
        if ($null -ne $f.add) { $add += $f.add } elseif ($f.status -ne 'D') { $binary++ }
        if ($null -ne $f.del) { $del += $f.del }
    }
    $text = "+$add/-$del"
    if ($binary -gt 0) { $text += " ($binary binary)" }
    return $text
}

function Get-FileStats($File) {
    if ($null -ne $File.add -or $null -ne $File.del) { return "+$($File.add)/-$($File.del)" }
    if ($File.status -eq 'D') { return '' }
    return 'binary'
}
