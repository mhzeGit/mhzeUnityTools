# agent-commit/lib/time.ps1
# ISO-8601 timestamps and the "5m ago" age labels.
# Loaded by agent-commit.ps1.

function Get-IsoNow {
    return [datetime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-IsoTime($Value) {
    if ($null -eq $Value) { return '' }
    if ($Value -is [datetime]) {
        return $Value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.fffZ', [System.Globalization.CultureInfo]::InvariantCulture)
    }
    return [string]$Value
}

function ConvertFrom-IsoTime([string]$Value) {
    $parsed = [datetime]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal
    if ($Value -and [datetime]::TryParse($Value, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) { return $parsed }
    return $null
}

function Format-Age([string]$IsoTime) {
    $time = ConvertFrom-IsoTime $IsoTime
    if ($null -eq $time) { return '' }
    $span = [datetime]::UtcNow - $time
    if ($span.TotalMinutes -lt 1) { return 'just now' }
    if ($span.TotalMinutes -lt 60) { return "$([int][Math]::Floor($span.TotalMinutes))m ago" }
    if ($span.TotalHours -lt 24) { return "$([int][Math]::Floor($span.TotalHours))h ago" }
    return "$([int][Math]::Floor($span.TotalDays))d ago"
}
