param([Parameter(Mandatory=$true)][string]$JobPath)
$ErrorActionPreference = 'Stop'
$job = Get-Content -LiteralPath $JobPath -Raw | ConvertFrom-Json
$source = [IO.Path]::GetFullPath($job.source)
$output = [IO.Path]::GetFullPath($job.output)
# A private document copy keeps export format changes out of the user's open master.
$app = New-Object -ComObject Photoshop.Application
$document = $null
$previous = $app.DisplayDialogs
try {
    $app.DisplayDialogs = 3
    $document = $app.Open($source)
    $copy = $document.Duplicate()
    try {
        $extension = [IO.Path]::GetExtension($output).ToLowerInvariant()
        $kind = switch ($extension) {
            '.png' { 'PNGSaveOptions' }
            '.tga' { 'TargaSaveOptions' }
            '.jpg' { 'JPEGSaveOptions' }
            '.jpeg' { 'JPEGSaveOptions' }
            '.tif' { 'TiffSaveOptions' }
            '.tiff' { 'TiffSaveOptions' }
            default { throw "Unsupported Photoshop output: $extension" }
        }
        $options = New-Object -ComObject "Photoshop.$kind"
        $copy.SaveAs($output, $options, $true, 2)
    } finally { $copy.Close(2) }
} finally { $app.DisplayDialogs = $previous }
