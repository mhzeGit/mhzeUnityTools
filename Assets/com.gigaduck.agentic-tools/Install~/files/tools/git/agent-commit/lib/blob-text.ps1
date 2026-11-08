# agent-commit/lib/blob-text.ps1
# UTF-8 blob text: byte-exact decode/encode (BOM aware), cached blob reads
# and writing new blobs.
# Loaded by agent-commit.ps1.

function ConvertTo-Utf8Bytes([string]$Text, [bool]$Bom) {
    $body = $script:Utf8NoBom.GetBytes($Text)
    if (-not $Bom) { return , $body }
    $bytes = New-Object byte[] ($body.Length + 3)
    $bytes[0] = 0xEF; $bytes[1] = 0xBB; $bytes[2] = 0xBF
    [System.Array]::Copy($body, 0, $bytes, 3, $body.Length)
    return , $bytes
}

function ConvertFrom-Utf8Bytes([byte[]]$Bytes) {
    # Decode UTF-8, remembering a BOM and whether re-encoding gives back the
    # exact same bytes (files that do not are never rewritten).
    if ($null -eq $Bytes) { $Bytes = New-Object byte[] 0 }
    $bom = $Bytes.Length -ge 3 -and $Bytes[0] -eq 0xEF -and $Bytes[1] -eq 0xBB -and $Bytes[2] -eq 0xBF
    $offset = 0
    if ($bom) { $offset = 3 }
    $text = $script:Utf8NoBom.GetString($Bytes, $offset, $Bytes.Length - $offset)
    $again = ConvertTo-Utf8Bytes $text $bom
    $same = $again.Length -eq $Bytes.Length
    if ($same) {
        for ($i = 0; $i -lt $Bytes.Length; $i++) { if ($again[$i] -ne $Bytes[$i]) { $same = $false; break } }
    }
    return [pscustomobject]@{ Text = $text; Bom = $bom; RoundTrips = $same }
}

function Get-BlobText([string]$Oid) {
    if ($script:BlobTextCache.ContainsKey($Oid)) { return $script:BlobTextCache[$Oid] }
    $r = Invoke-Git -Arguments @('cat-file', 'blob', $Oid)
    $decoded = ConvertFrom-Utf8Bytes $r.Bytes
    $script:BlobTextCache[$Oid] = $decoded
    return $decoded
}

function New-BlobFromText([string]$Text, [bool]$Bom) {
    # Store content that is already in git's normalized form (no filters).
    $temp = New-TempFilePath '.blob'
    try {
        [System.IO.File]::WriteAllBytes($temp, (ConvertTo-Utf8Bytes $Text $Bom))
        $r = Invoke-Git -Arguments @('hash-object', '-w', '--no-filters', '--', $temp)
    } finally {
        Remove-FileQuietly $temp
    }
    return $r.Output.Trim()
}
