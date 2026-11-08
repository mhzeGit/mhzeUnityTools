# agent-commit/lib/git-process.ps1
# Running git: argument quoting, captured and console-attached runs, and
# splitting long argument lists.
# Loaded by agent-commit.ps1.

function ConvertTo-NativeArgument([string]$Value) {
    # Quote one argument following the MSVCRT command-line rules git uses.
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.Append('"')
    $slashes = 0
    foreach ($ch in $Value.ToCharArray()) {
        if ($ch -eq [char]'\') { $slashes++; continue }
        if ($ch -eq [char]'"') {
            [void]$sb.Append([char]'\', 2 * $slashes + 1)
            [void]$sb.Append([char]'"')
            $slashes = 0
            continue
        }
        if ($slashes -gt 0) { [void]$sb.Append([char]'\', $slashes); $slashes = 0 }
        [void]$sb.Append($ch)
    }
    if ($slashes -gt 0) { [void]$sb.Append([char]'\', 2 * $slashes) }
    [void]$sb.Append([char]'"')
    return $sb.ToString()
}

function Invoke-Git {
    # Run git with captured output: stdout as raw bytes and as UTF-8 text.
    # Throws on a non-zero exit unless -AllowFailure is given. -InputText is
    # written to stdin as raw UTF-8 bytes (no BOM); otherwise stdin is closed.
    # Input normally travels as arguments or temp files.
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [hashtable]$Environment,
        [string]$WorkDir,
        [string]$InputText,
        [switch]$AllowFailure
    )
    if (-not $WorkDir) { $WorkDir = $script:RepoRoot }
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:GitExe
    $psi.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $psi.WorkingDirectory = $WorkDir
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardErrorEncoding = $script:Utf8NoBom
    $psi.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    if ($Environment) {
        foreach ($key in $Environment.Keys) { $psi.EnvironmentVariables[$key] = [string]$Environment[$key] }
    }
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = New-Object System.IO.MemoryStream
    $copyTask = $proc.StandardOutput.BaseStream.CopyToAsync($stdout)
    $errTask = $proc.StandardError.ReadToEndAsync()
    try {
        if ($null -ne $InputText) {
            $inputBytes = $script:Utf8NoBom.GetBytes($InputText)
            $proc.StandardInput.BaseStream.Write($inputBytes, 0, $inputBytes.Length)
            $proc.StandardInput.BaseStream.Flush()
        }
        $proc.StandardInput.Close()
    } catch { }
    $proc.WaitForExit()
    $copyTask.Wait()
    $bytes = $stdout.ToArray()
    $result = [pscustomobject]@{
        ExitCode = $proc.ExitCode
        Bytes    = $bytes
        Output   = $script:Utf8NoBom.GetString($bytes)
        Error    = $errTask.Result
    }
    $proc.Dispose()
    if ($result.ExitCode -ne 0 -and -not $AllowFailure) {
        $message = $result.Error.Trim()
        if (-not $message) { $message = $result.Output.Trim() }
        throw "git $($Arguments -join ' ') failed (exit $($result.ExitCode)): $message"
    }
    return $result
}

function Invoke-GitInteractive {
    # Run git attached to this console (progress, pager, credential prompts).
    param([Parameter(Mandatory = $true)][string[]]$Arguments)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $script:GitExe
    $psi.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    $psi.WorkingDirectory = $script:RepoRoot
    $psi.UseShellExecute = $false
    $proc = [System.Diagnostics.Process]::Start($psi)
    $proc.WaitForExit()
    $code = $proc.ExitCode
    $proc.Dispose()
    return $code
}

function Split-ArgumentChunks([string[]]$Items, [int]$ExtraPerItem = 0) {
    # Group arguments so each git command line stays far below Windows' limit.
    $chunks = [System.Collections.Generic.List[object]]::new()
    $current = [System.Collections.Generic.List[string]]::new()
    $length = 0
    foreach ($item in @($Items)) {
        if ($null -eq $item) { continue }
        $cost = (ConvertTo-NativeArgument $item).Length + 1 + $ExtraPerItem
        if ($current.Count -gt 0 -and $length + $cost -gt 8000) {
            $chunks.Add($current.ToArray())
            $current = [System.Collections.Generic.List[string]]::new()
            $length = 0
        }
        $current.Add($item)
        $length += $cost
    }
    if ($current.Count -gt 0) { $chunks.Add($current.ToArray()) }
    return , $chunks
}
