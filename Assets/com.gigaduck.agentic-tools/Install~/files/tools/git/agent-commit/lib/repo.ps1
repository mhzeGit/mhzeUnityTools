# agent-commit/lib/repo.ps1
# Locating the repository (root, git dir, common dir) and reading HEAD and
# the current branch.
# Loaded by agent-commit.ps1.

function Initialize-AgentCommitRepo([string]$StartDirectory) {
    # Returns $false when $StartDirectory is not inside a git repository.
    if (-not $script:GitExe) {
        $git = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if (-not $git) { throw 'git was not found on PATH.' }
        $script:GitExe = $git.Path
    }
    if (-not $StartDirectory -or -not [System.IO.Directory]::Exists($StartDirectory)) { return $false }
    $top = Invoke-Git -Arguments @('rev-parse', '--show-toplevel') -WorkDir $StartDirectory -AllowFailure
    if ($top.ExitCode -ne 0 -or -not $top.Output.Trim()) { return $false }
    $script:RepoRoot = [System.IO.Path]::GetFullPath($top.Output.Trim())
    $script:GitDir = [System.IO.Path]::GetFullPath((Invoke-Git -Arguments @('rev-parse', '--absolute-git-dir')).Output.Trim())
    $common = (Invoke-Git -Arguments @('rev-parse', '--git-common-dir')).Output.Trim()
    if (-not [System.IO.Path]::IsPathRooted($common)) { $common = [System.IO.Path]::Combine($script:RepoRoot, $common) }
    $script:CommonDir = [System.IO.Path]::GetFullPath($common)
    return $true
}

function Get-RepoFullPath([string]$RelativePath, [string]$Root) {
    if (-not $Root) { $Root = $script:RepoRoot }
    # The platform's separator: Windows takes either, other systems only '/'.
    return [System.IO.Path]::Combine($Root, $RelativePath.Replace('/', [string][System.IO.Path]::DirectorySeparatorChar))
}

function Get-HeadCommit([string]$Root) {
    $r = Invoke-Git -Arguments @('rev-parse', '-q', '--verify', 'HEAD^{commit}') -WorkDir $Root -AllowFailure
    if ($r.ExitCode -ne 0) { return $null }
    return $r.Output.Trim()
}

function Get-CurrentBranch([string]$Root) {
    return (Invoke-Git -Arguments @('symbolic-ref', '-q', '--short', 'HEAD') -WorkDir $Root -AllowFailure).Output.Trim()
}
