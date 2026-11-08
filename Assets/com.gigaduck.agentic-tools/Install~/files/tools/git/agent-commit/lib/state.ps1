# agent-commit/lib/state.ps1
# Shared engine state: script-scope caches, repository paths and constants.
# Loaded by agent-commit.ps1.

$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$script:RepoRoot = $null
$script:GitDir = $null
$script:CommonDir = $null
$script:GitExe = $null
$script:TreeCache = @{}
$script:BlobTextCache = @{}
$script:StoreLockDepth = 0
$script:RecordSchema = 1
$script:BumpKinds = @('none', 'patch', 'minor')
