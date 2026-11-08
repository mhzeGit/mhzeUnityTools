<#
Install the unified environment-design skill and archive the retired standalone skill.
Default targets: personal Codex, Claude Code and Kilo Code skill roots.
Use -SkillRoots for explicit targets, -Gemini to include Gemini, or -ProjectRoot
for project Claude/Kilo roots. Replaced installations are retained outside
skill discovery in <agent-config>/skill-backups; no installation is deleted.
#>
param(
    [switch]$Gemini,
    [string]$ProjectRoot = '',
    [string[]]$SkillRoots = @()
)
$ErrorActionPreference = 'Stop'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not (Test-Path -LiteralPath (Join-Path $source 'SKILL.md'))) { throw "No SKILL.md in $source" }
$name = 'environment-design'
$retired = 'concept-art-to-unity-environment'
if ((Split-Path $source -Leaf) -ne $name) { throw "Expected source folder named $name" }
$profileRoot = [Environment]::GetFolderPath('UserProfile')
if ($SkillRoots.Count -eq 0) {
    $codexRoot = if ($env:CODEX_HOME) { Join-Path $env:CODEX_HOME 'skills' } else { Join-Path $profileRoot '.codex/skills' }
    $SkillRoots = @($codexRoot, (Join-Path $profileRoot '.claude/skills'), (Join-Path $profileRoot '.kilo/skills'))
}
if ($Gemini) { $SkillRoots += Join-Path $profileRoot '.gemini/config/skills' }
if ($ProjectRoot) {
    $SkillRoots += Join-Path $ProjectRoot '.claude/skills'
    $SkillRoots += Join-Path $ProjectRoot '.kilo/skills'
}

function Get-Manifest([string]$Folder) {
    $manifest = @{}
    if (Test-Path -LiteralPath $Folder) {
        foreach ($file in Get-ChildItem -LiteralPath $Folder -Recurse -File) {
            if ($file.FullName -match '[\\/]__pycache__[\\/]' -or $file.Extension -eq '.pyc') { continue }
            $rel = [IO.Path]::GetRelativePath($Folder, $file.FullName)
            $manifest[$rel] = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
    return $manifest
}

function Test-SameManifest($First, $Second) {
    if ($First.Count -ne $Second.Count) { return $false }
    foreach ($key in $First.Keys) { if ($First[$key] -ne $Second[$key]) { return $false } }
    return $true
}

$expected = Get-Manifest $source
foreach ($root in ($SkillRoots | Select-Object -Unique)) {
    $rootPath = [IO.Path]::GetFullPath($root)
    $target = [IO.Path]::GetFullPath((Join-Path $rootPath $name))
    $oldTarget = [IO.Path]::GetFullPath((Join-Path $rootPath $retired))
    # Validate exact absolute move targets inside the explicitly selected skill root.
    foreach ($path in @($target, $oldTarget)) {
        if ((Split-Path $path -Parent) -ne $rootPath) { throw "Target escapes skill root: $path" }
        if ($path -eq $source) { throw "Refusing to move the source installation: $path" }
        if ((Test-Path -LiteralPath $path) -and (Get-Item -LiteralPath $path).LinkType) {
            throw "Resolve linked installation explicitly before replacing: $path"
        }
    }
    $same = Test-SameManifest $expected (Get-Manifest $target)
    $archive = Join-Path (Split-Path $rootPath -Parent) ('skill-backups/environment-merge-' + [Guid]::NewGuid().ToString('N'))
    $needsBackup = (-not $same -and (Test-Path -LiteralPath $target)) -or (Test-Path -LiteralPath $oldTarget)
    if ($needsBackup) { New-Item -ItemType Directory -Path $archive -Force | Out-Null }
    if (-not $same) {
        if (Test-Path -LiteralPath $target) {
            Move-Item -LiteralPath $target -Destination (Join-Path $archive $name)
        }
        foreach ($rel in $expected.Keys) {
            $destination = Join-Path $target $rel
            New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $source $rel) -Destination $destination
        }
    }
    if (-not (Test-SameManifest $expected (Get-Manifest $target))) { throw "Installed content verification failed: $target" }
    # Retire the old entry only after the replacement passes content verification.
    if (Test-Path -LiteralPath $oldTarget) {
        Move-Item -LiteralPath $oldTarget -Destination (Join-Path $archive $retired)
    }
    [pscustomobject]@{
        Destination = $target
        Files = $expected.Count
        Updated = -not $same
        Backup = $(if ($needsBackup) { $archive } else { '' })
    }
}
