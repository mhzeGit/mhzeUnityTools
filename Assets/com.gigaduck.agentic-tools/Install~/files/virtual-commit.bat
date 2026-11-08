@echo off
rem Virtual Commit: review the work AI agents registered with
rem tools\git\agent-commit.ps1 and commit + push the records you select (one
rem commit per record, only that record's changes, with the agent's title,
rem description and version bump).
rem Double-click it, or run it from a terminal. Arguments are passed through:
rem   virtual-commit.bat list [-All]
rem   virtual-commit.bat finalize -Ids 3,5 [-DryRun] [-NoPush] [-SkipConflicts]
rem   virtual-commit.bat discard -Ids 4
setlocal
cd /d "%~dp0"
set "PS=powershell"
where pwsh >nul 2>nul && set "PS=pwsh"
if "%~1"=="" (
    "%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\git\agent-commit.ps1" menu -PauseOnExit
) else (
    "%PS%" -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\git\agent-commit.ps1" %*
)
exit /b %errorlevel%
